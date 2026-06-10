using static MassTransit.Monitoring.Performance.BuiltInCounters;

namespace InboxDemo.Processor
{
    internal class InboxBackgroundProcessor : BackgroundService
    {
        private readonly InboxDatabase _inboxDatabase;
        private readonly MessageHandlerFactory _handlerFactory;
        private readonly ILogger<InboxBackgroundProcessor> _logger;
        private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(2);
        private readonly int _batchSize = 10;

        public InboxBackgroundProcessor(
            InboxDatabase inboxDatabase,
            MessageHandlerFactory handlerFactory,
            ILogger<InboxBackgroundProcessor> logger
            )
        {
            _inboxDatabase = inboxDatabase;
            _handlerFactory = handlerFactory;
            _logger = logger;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Inbox Background Processor started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessBatchAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing inbox messages");
                }

                await Task.Delay(_pollingInterval, stoppingToken);
            }
        }

        private async Task ProcessBatchAsync(CancellationToken stoppingToken)
        {
            // 批量拉取未处理消息
            var messages = await _inboxDatabase.GetUnprocessedMessagesAsync(_batchSize);

            foreach (var message in messages)
            {
                try
                {
                    var handler = _handlerFactory.GetHandler(message.MessageType);

                    if (handler == null)
                    {
                        _logger.LogWarning("No handler found for message type: {MessageType}", message.MessageType);
                        await _inboxDatabase.MarkAsFailedAsync(message.Id, $"Unknown message type: {message.MessageType}");
                        continue;
                    }

                    // 执行真正的业务逻辑
                    await handler.HandleAsync(message, stoppingToken);

                    // 标记为已处理
                    await _inboxDatabase.MarkAsProcessedAsync(message.Id);

                    _logger.LogInformation("Message processed successfully: {MessageId}", message.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process message: {MessageId}", message.Id);

                    // 标记失败，等待重试
                    await _inboxDatabase.MarkAsFailedAsync(message.Id, ex.Message);
                }
            }
        }
    }
}
