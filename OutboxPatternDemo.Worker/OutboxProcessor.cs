using Microsoft.EntityFrameworkCore;
using OutboxPatternDemo.Domain;
using OutboxPatternDemo.Infrastructure;
using System.Text.Json;

namespace OutboxPatternDemo.Worker
{
    public class OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger)
        : BackgroundService
    {
        private static readonly JsonSerializerOptions JsonSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Outbox Processor started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessOutboxMessagesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing outbox messages");
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        private async Task ProcessOutboxMessagesAsync(CancellationToken stoppingToken)
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var messages = await dbContext.OutboxMessages
                .Where(m => m.ProcessedOnUtc == null)
                .OrderBy(m => m.CreatedOnUtc)
                .Take(10) // 批量处理
                .ToListAsync(stoppingToken);

            foreach (var message in messages)
            {
                try
                {
                    logger.LogInformation("Processing outbox message: {MessageId} - {MessageName}",
                        message.Id, message.Name);

                    // 这里实现你的事件处理逻辑
                    // 例如：通过 MediatR 发布事件、发送邮件、调用外部服务等
                    await HandleEventAsync(message, stoppingToken);

                    message.ProcessedOnUtc = DateTime.UtcNow;

                    logger.LogInformation("Successfully processed outbox message: {MessageId}", message.Id);
                }
                catch (Exception ex)
                {
                    message.Error = ex.ToString();
                    logger.LogError(ex, "Failed to process outbox message: {MessageId}", message.Id);

                    // 可选：实现重试逻辑
                    // message.RetryCount++;
                    // if (message.RetryCount >= 3) message.ProcessedOnUtc = DateTime.UtcNow; // 标记为已处理但失败
                }
            }

            await dbContext.SaveChangesAsync(stoppingToken);
        }

        private Task HandleEventAsync(OutboxMessage message, CancellationToken stoppingToken)
        {
            return message.Name switch
            {
                nameof(Domain.UserFollowedEvent) => HandleUserFollowedEventAsync(message, stoppingToken),
                _ => Task.CompletedTask
            };
        }

        private async Task HandleUserFollowedEventAsync(OutboxMessage message, CancellationToken stoppingToken)
        {
            var userFollowedEvent = JsonSerializer.Deserialize<UserFollowedEvent>(
            message.Content, JsonSerializerOptions);

            if (userFollowedEvent is null)
                throw new InvalidOperationException("Failed to deserialize event");

            logger.LogInformation("User {FollowerId} followed {FollowedId} at {OccurredOn}",
                userFollowedEvent.FollowerId,
                userFollowedEvent.FollowedId,
                userFollowedEvent.OccurredOn);

            await Task.Delay(100, stoppingToken);
        }
    }
}
