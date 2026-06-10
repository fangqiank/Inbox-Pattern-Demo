using Dapper;
using InboxDemo.Common.Models;
using Npgsql;
using System.Data;

namespace InboxDemo.Processor
{
    public class InboxDatabase(string connectionString)
    {
        private NpgsqlConnection CreateConnection() => new(connectionString);

        /// <summary>
        /// 创建并开启一个连接+事务，供 InboxBackgroundProcessor 在事务内完成拉取+处理+标记。
        /// </summary>
        public async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> BeginTransactionAsync()
        {
            var connection = CreateConnection();
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            return (connection, transaction);
        }

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

        // 使用 ON CONFLICT DO NOTHING 实现幂等插入（InboxConsumer 调用，无事务）
        public async Task<bool> InsertMessageAsync(InboxMessage message)
        {
            using var connection = CreateConnection();

            var sql = @"
                INSERT INTO inbox_messages (id, message_type, payload, received_on_utc, retry_count)
                VALUES (@Id, @MessageType, @Payload::jsonb, @ReceivedOnUtc, @RetryCount)
                ON CONFLICT (id) DO NOTHING
                RETURNING id;
            ";

            var result = await connection.QueryFirstOrDefaultAsync<Guid?>(sql, message);
            return result.HasValue;
        }

        // 在事务内获取未处理的消息（FOR UPDATE SKIP LOCKED 行级锁持续到事务结束）
        public async Task<IEnumerable<InboxMessage>> GetUnprocessedMessagesAsync(
            NpgsqlTransaction transaction, int batchSize = 10)
        {
            var connection = transaction.Connection!;

            var sql = @"
                SELECT id, message_type AS MessageType, payload::text AS Payload,
                   received_on_utc AS ReceivedOnUtc, processed_on_utc AS ProcessedOnUtc,
                   error AS Error, retry_count AS RetryCount
                FROM inbox_messages
                WHERE processed_on_utc IS NULL
                  AND (error IS NULL OR retry_count < 3)
                  AND (error IS NULL OR received_on_utc < NOW() - (INTERVAL '5 seconds' * LEAST(retry_count, 5)))
                ORDER BY received_on_utc
                LIMIT @BatchSize
                FOR UPDATE SKIP LOCKED;
            ";

            return await connection.QueryAsync<InboxMessage>(sql, new { BatchSize = batchSize }, transaction);
        }

        // 标记消息为已处理（在事务内调用）
        public async Task MarkAsProcessedAsync(Guid messageId, NpgsqlTransaction transaction)
        {
            var connection = transaction.Connection!;

            var sql = @"
                UPDATE inbox_messages
                SET processed_on_utc = @ProcessedOnUtc,
                    error = NULL
                WHERE id = @Id
            ";

            await connection.ExecuteAsync(sql, new {
                Id = messageId,
                ProcessedOnUtc = DateTime.UtcNow
            }, transaction);
        }

        // 标记消息处理失败（在事务内调用）
        public async Task MarkAsFailedAsync(Guid messageId, string error, NpgsqlTransaction transaction)
        {
            var connection = transaction.Connection!;

            var sql = @"
            UPDATE inbox_messages
                SET error = @Error,
                    retry_count = retry_count + 1,
                    processed_on_utc = NULL
                WHERE id = @Id
            ";

            await connection.ExecuteAsync(sql, new { Id = messageId, Error = error }, transaction);
        }
    }
}
