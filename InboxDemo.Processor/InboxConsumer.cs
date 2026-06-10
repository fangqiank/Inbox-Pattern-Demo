using InboxDemo.Common.Models;
using MassTransit;
using System.Text.Json;

namespace InboxDemo.Processor
{
    /// <summary>
    /// 通用 Inbox Consumer：所有消息先落入 Inbox 表，不做业务处理
    /// </summary>
    public class InboxConsumer(
        InboxDatabase inboxDatabase, 
        ILogger<InboxConsumer> logger
        ) : IConsumer<OrderCreated>
    {
        public async Task Consume(ConsumeContext<OrderCreated> context)
        {
            var messageId = context.MessageId ?? Guid.NewGuid();
            var payload = JsonSerializer.Serialize(context.Message);

            var inboxMessage = new InboxMessage
            {
                Id = messageId,
                MessageType = typeof(OrderCreated).FullName!,
                Payload = payload,
                ReceivedOnUtc = DateTime.UtcNow,
                RetryCount = 0
            };

            // 尝试插入 Inbox，重复消息会被 ON CONFLICT DO NOTHING 忽略
            var isNewMessage = await inboxDatabase.InsertMessageAsync(inboxMessage);

            if (isNewMessage)
                logger.LogInformation("New message saved to inbox: {MessageId} ({MessageType})",
                    messageId, typeof(OrderCreated).Name);
            else
                logger.LogWarning("Duplicate message ignored: {MessageId}", messageId);

            // 注意：这里只做落库，业务逻辑在 BackgroundProcessor 中处理
        }
    }

    /// <summary>
    /// 泛型 Inbox Consumer：支持任意消息类型，新消息类型无需创建专用 Consumer
    /// </summary>
    public class GenericInboxConsumer<T>(
        InboxDatabase inboxDatabase,
        ILogger<GenericInboxConsumer<T>> logger
        ) : IConsumer<T> where T : class
    {
        public async Task Consume(ConsumeContext<T> context)
        {
            var messageId = context.MessageId ?? Guid.NewGuid();
            var payload = JsonSerializer.Serialize(context.Message);

            var inboxMessage = new InboxMessage
            {
                Id = messageId,
                MessageType = typeof(T).FullName!,
                Payload = payload,
                ReceivedOnUtc = DateTime.UtcNow,
                RetryCount = 0
            };

            var isNewMessage = await inboxDatabase.InsertMessageAsync(inboxMessage);

            if (isNewMessage)
                logger.LogInformation("New message saved to inbox: {MessageId} ({MessageType})",
                    messageId, typeof(T).Name);
            else
                logger.LogWarning("Duplicate message ignored: {MessageId}", messageId);
        }
    }
}
