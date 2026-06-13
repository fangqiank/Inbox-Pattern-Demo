using Microsoft.EntityFrameworkCore;
using OutboxPatternDemo.Domain;
using OutboxPatternDemo.Infrastructure;
using OutboxPatternDemo.Infrastructure.EventHandlers;
using System.Data;
using System.Text.Json;

namespace OutboxPatternDemo.Worker
{
    public class InboxProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<InboxProcessor> logger
        ) : BackgroundService
    {
        private static readonly JsonSerializerOptions jsonSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("📥 Inbox Processor started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessOutboxMessagesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "❌ Error processing outbox messages in inbox processor");
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        private async Task ProcessOutboxMessagesAsync(CancellationToken stoppingToken)
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 使用事务 + 行锁，防止并发处理同一消息
            using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, stoppingToken);

            try
            {
                // 1. 获取未处理的 Outbox 消息（跳过已锁定的行）
                var messages = await dbContext.OutboxMessages
                    .FromSqlRaw(@"
                    SELECT * FROM outbox_messages 
                    WHERE processed_on_utc IS NULL 
                    AND retry_count < 3  -- 最多重试3次
                    ORDER BY created_on_utc 
                    LIMIT 10 
                    FOR UPDATE SKIP LOCKED")
                    .ToListAsync(stoppingToken);

                if (messages.Count == 0)
                {
                    await transaction.RollbackAsync(stoppingToken);
                    return;
                }

                logger.LogInformation("📨 Processing {Count} outbox messages", messages.Count);

                foreach (var message in messages)
                {
                    await ProcessMessageWithInboxAsync(message, dbContext, scope, stoppingToken);
                }

                await dbContext.SaveChangesAsync(stoppingToken);
                await transaction.CommitAsync(stoppingToken);

                logger.LogInformation("✅ Successfully processed batch of {Count} messages", messages.Count);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(stoppingToken);
                logger.LogError(ex, "❌ Failed to process message batch");
                throw;
            }
        }

        private async Task ProcessMessageWithInboxAsync(
            OutboxMessage message, 
            AppDbContext dbContext, 
            IServiceScope scope, 
            CancellationToken stoppingToken)
        {
            try
            {
                // 2. Inbox 幂等性检查 - 核心步骤
                var existingInbox = await dbContext.InboxMessages
                    .FirstOrDefaultAsync(i => i.MessageId == message.MessageId, stoppingToken);

                if (existingInbox is not null)
                {
                    logger.LogWarning(
                        "🔄 Duplicate message detected! MessageId: {MessageId}, Already processed at: {ProcessedAt}",
                        message.MessageId, existingInbox.ProcessedOnUtc);

                    // 标记 Outbox 消息为已处理，避免重复拉取
                    message.ProcessedOnUtc = DateTime.UtcNow;
                    message.Error = $"Duplicate - Already processed at {existingInbox.ProcessedOnUtc}";
                    return;
                }

                // 3. 获取事件的所有处理器
                var handlers = GetHandlersForEvent(message.Name, scope);

                if (handlers.Count == 0)
                {
                    logger.LogWarning("⚠️ No handlers found for event: {EventName}", message.Name);
                    message.ProcessedOnUtc = DateTime.UtcNow;
                    message.Error = "No handlers registered";
                    return;
                }

                // 4. 反序列化事件 - 用 Name 列解析具体类型，无需多态 $type
                var eventType = message.Name switch
                {
                    nameof(UserFollowedEvent) => typeof(UserFollowedEvent),
                    _ => null
                };
                if (eventType is null)
                {
                    throw new InvalidOperationException($"Unknown event type: {message.Name}");
                }

                var domainEvent = JsonSerializer.Deserialize(message.Content, eventType, jsonSerializerOptions);
                if (domainEvent is null)
                {
                    throw new InvalidOperationException($"Failed to deserialize event: {message.Name}");
                }

                // 5. 执行所有处理器
                var handlerTasks = handlers.Select(handler =>
                    ExecuteHandlerWithErrorHandling(handler, domainEvent, message, dbContext, stoppingToken));

                await Task.WhenAll(handlerTasks);

                // 6. 标记 Outbox 消息为已处理
                message.ProcessedOnUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                // 处理失败，记录错误并增加重试计数
                message.RetryCount++;
                message.Error = ex.ToString();

                if (message.RetryCount >= 3)
                {
                    // 超过最大重试次数，标记为已处理（但记录错误）
                    message.ProcessedOnUtc = DateTime.UtcNow;
                    logger.LogError(ex,
                        "❌ Message {MessageId} failed after {RetryCount} retries, marking as processed with error",
                        message.Id, message.RetryCount);
                }
                else
                {
                    logger.LogWarning(ex,
                        "⚠️ Message {MessageId} failed (attempt {RetryCount}/3), will retry",
                        message.Id, message.RetryCount);
                }
            }
        }

        private List<object> GetHandlersForEvent(string name, IServiceScope scope)
        {
            return name switch
            {
                nameof(UserFollowedEvent) => new List<object>
                {
                    scope.ServiceProvider.GetRequiredService<SendNotificationOnUserFollowedHandler>(),
                    scope.ServiceProvider.GetRequiredService<UpdateFollowStatsHandler>(),
                    scope.ServiceProvider.GetRequiredService<AddToTimelineHandler>()
                },
                _ => new List<object>()
            };
        }

        private async Task ExecuteHandlerWithErrorHandling(
            object handler, 
            object domainEvent, 
            OutboxMessage message, 
            AppDbContext dbContext, 
            CancellationToken stoppingToken)
        {
            var handlerName = (string)((dynamic)handler).HandlerName;

            try
            {
                logger.LogInformation(
                    "🔧 Executing handler {HandlerName} for event {EventId}",
                    handlerName, message.MessageId);

                // 执行处理器
                await ((dynamic)handler).HandleAsync((dynamic)domainEvent, stoppingToken);

                // 记录到 Inbox - 每个处理器一条记录
                var inboxMessage = new InboxMessage
                {
                    Id = Guid.NewGuid(),
                    MessageId = message.MessageId,
                    Name = message.Name,
                    Content = message.Content,
                    OccurredOnUtc = message.CreatedOnUtc,
                    ProcessedOnUtc = DateTime.UtcNow,
                    HandlerName = handlerName
                };

                dbContext.InboxMessages.Add(inboxMessage);

                logger.LogInformation(
                    "✅ Handler {HandlerName} completed for event {EventId}",
                    handlerName, message.MessageId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "❌ Handler {HandlerName} failed for event {EventId}",
                    handlerName, message.MessageId);
                throw; // 重新抛出，让外层处理重试逻辑
            }
        }
    }
}
