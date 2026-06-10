using Dapper;
using InboxDemo.Common.Models;
using Npgsql;
using System.Data;

namespace InboxDemo.Processor
{
    public class InboxDatabase(string connectionString)
    {
        private IDbConnection CreateConnection() => new NpgsqlConnection(connectionString);

        // 初始化 Inbox 表
        public async Task InitializeAsync()
        {
            using var connection = CreateConnection();

            var createTableSql = @"
                CREATE TABLE IF NOT EXISTS inbox_messages (
                    id UUID PRIMARY KEY,
                    message_type VARCHAR(500) NOT NULL,
                    payload JSONB NOT NULL,
                    received_on_utc TIMESTAMP WITH TIME ZONE NOT NULL,
                    processed_on_utc TIMESTAMP WITH TIME ZONE,
                    error TEXT,
                    retry_count INT NOT NULL DEFAULT 0
                );

                -- 过滤索引：快速查询未处理消息
                CREATE INDEX IF NOT EXISTS idx_inbox_messages_unprocessed 
                ON inbox_messages (received_on_utc, id) 
                WHERE processed_on_utc IS NULL AND error IS NULL;

                -- 错误消息索引
                CREATE INDEX IF NOT EXISTS idx_inbox_messages_error 
                ON inbox_messages (received_on_utc, id) 
                WHERE error IS NOT NULL AND retry_count < 3;
            ";

            await connection.ExecuteAsync(createTableSql);
        }

        // 使用 ON CONFLICT DO NOTHING 实现幂等插入
        public async Task<bool> InsertMessageAsync(InboxMessage message)
        {
            using var connection = CreateConnection();

            // 关键 SQL：重复消息直接被忽略，无副作用
            var sql = @"
                INSERT INTO inbox_messages (id, message_type, payload, received_on_utc, retry_count)
                VALUES (@Id, @MessageType, @Payload::jsonb, @ReceivedOnUtc, @RetryCount)
                ON CONFLICT (id) DO NOTHING
                RETURNING id;
            ";

            var result = await connection.QueryFirstOrDefaultAsync<Guid?>(sql, message);

            return result.HasValue;
        }

        // 获取未处理的消息（批量拉取）
        public async Task<IEnumerable<InboxMessage>> GetUnprocessedMessagesAsync(
            int batchSize = 10)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT id, message_type AS MessageType, payload::text AS Payload, 
                   received_on_utc AS ReceivedOnUtc, processed_on_utc AS ProcessedOnUtc,
                   error AS Error, retry_count AS RetryCount
                FROM inbox_messages
                WHERE processed_on_utc IS NULL 
                  AND (error IS NULL OR retry_count < 3)
                ORDER BY received_on_utc
                LIMIT @BatchSize
                FOR UPDATE SKIP LOCKED;  -- 避免并发冲突
            ";

            return await connection.QueryAsync<InboxMessage>(sql, new { BatchSize = batchSize });
        }

        // 标记消息为已处理
        public async Task MarkAsProcessedAsync(Guid messageId)
        {
            using var connection = CreateConnection();

            var sql = @"
                UPDATE inbox_messages 
                SET processed_on_utc = @ProcessedOnUtc, 
                    error = NULL
                WHERE id = @Id
            ";

            await connection.ExecuteAsync(sql, new {
                Id = messageId,
                ProcessedOnUtc = DateTimeOffset.UtcNow }
            );
        }

        // 标记消息处理失败
        public async Task MarkAsFailedAsync(Guid messageId, string error)
        {
            using var connection = CreateConnection();

            var sql = @"
            UPDATE inbox_messages 
                SET error = @Error,
                    retry_count = retry_count + 1,
                    processed_on_utc = NULL
                WHERE id = @Id
            ";

            await connection.ExecuteAsync(sql, new { Id = messageId, Error = error });
        }
    }
}
