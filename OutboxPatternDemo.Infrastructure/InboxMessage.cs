namespace OutboxPatternDemo.Infrastructure
{
    public class InboxMessage
    {
        public Guid Id { get; init; }
        public Guid MessageId { get; init; }  // 原始消息ID，用于去重
        public string Name { get; init; } = string.Empty;
        public string? Content { get; init; }  // 冗余存储，与 OutboxMessage.Content 相同；仅用于查询方便
        public DateTimeOffset OccurredOnUtc { get; init; }
        public DateTimeOffset? ProcessedOnUtc { get; set; }
        public string? Error { get; set; }
        public string? HandlerName { get; set; }
    }
}
