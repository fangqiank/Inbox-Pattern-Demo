using InboxDemo.Common.Models;
using System.Text.Json;

namespace InboxDemo.Processor
{
    public interface IMessageHandler
    {
        Task HandleAsync(InboxMessage message, CancellationToken cancellationToken);
    }

    // 订单创建消息的业务处理器
    public class OrderCreatedHandler(ILogger<OrderCreatedHandler> logger) : IMessageHandler
    {
        public async Task HandleAsync(InboxMessage message, CancellationToken cancellationToken)
        {
            var orderCreated = JsonSerializer.Deserialize<OrderCreated>(message.Payload);

            if (orderCreated == null)
                throw new InvalidOperationException("Failed to deserialize message payload");

            logger.LogInformation(
                "Processing order: {OrderId}, Customer: {Customer}, Amount: {Amount:C}",
                orderCreated.OrderId,
                orderCreated.CustomerName,
                orderCreated.Amount
            );

            await Task.Delay(100, cancellationToken);

            // 这里可以执行实际的业务操作：
            // - 发送确认邮件
            // - 更新库存
            // - 生成发票
            // 等等...
        }
    }

    // 消息处理器工厂
    public class MessageHandlerFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Dictionary<string, Type> _handlerMap;

        public MessageHandlerFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _handlerMap = new Dictionary<string, Type>
            {
                // 消息类型 -> 处理器类型的映射
                ["InboxDemo.Common.Models.OrderCreated"] = typeof(OrderCreatedHandler),
                // 在这里添加其他消息类型和处理器的映射
            };
        }

        public IMessageHandler? GetHandler(string messageType)
        {
            if (_handlerMap.TryGetValue(messageType, out var handlerType))
                return (IMessageHandler)_serviceProvider.GetRequiredService(handlerType);
            
            return null;
        }
    }
}
