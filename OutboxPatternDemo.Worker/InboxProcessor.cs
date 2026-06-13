using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OutboxPatternDemo.Domain;
using OutboxPatternDemo.Infrastructure;
using OutboxPatternDemo.Worker.EventHandlers;
using System.Data;
using System.Text.Json;

namespace OutboxPatternDemo.Worker
{
    public class InboxProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<InboxProcessor> logger,
        IConfiguration configuration
        ) : BackgroundService
    {
        private static readonly JsonSerializerOptions jsonSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly string FetchOutboxSql = """
            SELECT * FROM outbox_messages
            WHERE processed_on_utc IS NULL
            AND retry_count < 3
            ORDER BY created_on_utc
            LIMIT 10
            FOR UPDATE SKIP LOCKED
            """;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var pollInterval = configuration.GetValue("PollingIntervalSeconds", 5);
            logger.LogInformation("Inbox Processor started, polling every {Interval}s", pollInterval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessOutboxMessagesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing outbox messages in inbox processor");
                }

                await Task.Delay(TimeSpan.FromSeconds(pollInterval), stoppingToken);
            }
        }

        private async Task ProcessOutboxMessagesAsync(CancellationToken stoppingToken)
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Outer transaction holds FOR UPDATE row locks across all per-message savepoints
            using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, stoppingToken);

            try
            {
                var messages = await dbContext.OutboxMessages
                    .FromSqlRaw(FetchOutboxSql) // Safe: no user-supplied values in the SQL
                    .ToListAsync(stoppingToken);

                if (messages.Count == 0)
                {
                    await transaction.RollbackAsync(stoppingToken);
                    return;
                }

                logger.LogInformation("Processing {Count} outbox messages", messages.Count);

                foreach (var message in messages)
                {
                    await ProcessMessageInSavepointAsync(message, dbContext, scope, transaction, stoppingToken);
                }

                await dbContext.SaveChangesAsync(stoppingToken);
                await transaction.CommitAsync(stoppingToken);

                logger.LogInformation("Successfully processed batch of {Count} messages", messages.Count);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(stoppingToken);
                logger.LogError(ex, "Failed to process message batch");
                throw;
            }
        }

        private async Task ProcessMessageInSavepointAsync(
            OutboxMessage message,
            AppDbContext dbContext,
            IServiceScope scope,
            IDbContextTransaction transaction,
            CancellationToken stoppingToken)
        {
            var dbTransaction = transaction.GetDbTransaction();
            var savepoint = $"msg_{message.Id:N}";
            await dbTransaction.SaveAsync(savepoint, stoppingToken);

            try
            {
                await ProcessMessageWithInboxAsync(message, dbContext, scope, stoppingToken);
                await dbContext.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync(savepoint, stoppingToken);

                // Discard any InboxMessage entities added during this message's handler execution.
                // They were not persisted (rolled back to savepoint) and must be removed from
                // the ChangeTracker so the next SaveChangesAsync only touches retry bookkeeping.
                var addedEntries = dbContext.ChangeTracker.Entries<InboxMessage>()
                    .Where(e => e.State == EntityState.Added).ToList();
                foreach (var entry in addedEntries)
                    entry.State = EntityState.Detached;

                // RetryCount and Error are saved outside the handler savepoint.
                // If retries are exhausted the message is permanently marked processed.
                message.RetryCount++;
                message.Error = ex.ToString();

                if (message.RetryCount >= 3)
                {
                    message.ProcessedOnUtc = DateTime.UtcNow;
                    logger.LogError(ex,
                        "Message {MessageId} failed after {RetryCount} retries, marking as processed with error",
                        message.Id, message.RetryCount);
                }
                else
                {
                    logger.LogWarning(ex,
                        "Message {MessageId} failed (attempt {RetryCount}/3), will retry",
                        message.Id, message.RetryCount);
                }

                await dbContext.SaveChangesAsync(stoppingToken);
            }
        }

        private async Task ProcessMessageWithInboxAsync(
            OutboxMessage message,
            AppDbContext dbContext,
            IServiceScope scope,
            CancellationToken stoppingToken)
        {
            // Resolve handlers for the event type
            var handlers = GetHandlersForEvent(message.Name, scope);

            if (handlers.Count == 0)
            {
                logger.LogWarning("No handlers found for event: {EventName}", message.Name);
                message.ProcessedOnUtc = DateTime.UtcNow;
                message.Error = "No handlers registered";
                return;
            }

            // Deserialize the domain event by Name column
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

            // Execute handlers serially.
            // If any handler fails the exception propagates up to the savepoint catch,
            // which rolls back ALL handler changes for this message (including InboxMessage
            // records from handlers that already succeeded).  RetryCount is then persisted
            // outside the savepoint scope so it survives the rollback.
            foreach (var handler in handlers)
            {
                await ExecuteHandlerWithInboxWriteAsync(handler, domainEvent, message, dbContext, stoppingToken);
            }

            message.ProcessedOnUtc = DateTime.UtcNow;
        }

        private static List<IEventHandler> GetHandlersForEvent(string name, IServiceScope scope)
        {
            return name switch
            {
                nameof(UserFollowedEvent) =>
                [
                    scope.ServiceProvider.GetRequiredService<SendNotificationOnUserFollowedHandler>(),
                    scope.ServiceProvider.GetRequiredService<UpdateFollowStatsHandler>(),
                    scope.ServiceProvider.GetRequiredService<AddToTimelineHandler>()
                ],
                _ => []
            };
        }

        private async Task ExecuteHandlerWithInboxWriteAsync(
            IEventHandler handler,
            object domainEvent,
            OutboxMessage message,
            AppDbContext dbContext,
            CancellationToken stoppingToken)
        {
            logger.LogInformation(
                "Executing handler {HandlerName} for event {EventId}",
                handler.HandlerName, message.MessageId);

            await handler.HandleAsync(domainEvent, stoppingToken);

            var inboxMessage = new InboxMessage
            {
                Id = Guid.NewGuid(),
                MessageId = message.MessageId,
                Name = message.Name,
                Content = message.Content,
                OccurredOnUtc = message.CreatedOnUtc,
                ProcessedOnUtc = DateTime.UtcNow,
                HandlerName = handler.HandlerName
            };

            dbContext.InboxMessages.Add(inboxMessage);

            logger.LogInformation(
                "Handler {HandlerName} completed for event {EventId}",
                handler.HandlerName, message.MessageId);
        }
    }
}
