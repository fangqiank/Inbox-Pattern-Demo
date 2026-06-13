namespace OutboxPatternDemo.Infrastructure
{
    public class InboxMessage
    {
        public Guid Id { get; init; }
        public Guid MessageId { get; init; }  // 原始消息ID，用于去重
        public string Name { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public DateTimeOffset OccurredOnUtc { get; init; }
        public DateTimeOffset? ProcessedOnUtc { get; set; }
        public string? Error { get; set; }
        public string? HandlerName { get; set; }
    }
}
