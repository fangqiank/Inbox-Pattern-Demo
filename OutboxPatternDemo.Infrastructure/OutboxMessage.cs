namespace OutboxPatternDemo.Infrastructure
{
    public class OutboxMessage
    {
        public Guid Id { get; init; }
        public Guid MessageId { get; init; }  // 幂等键
        public string Name { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public DateTimeOffset CreatedOnUtc { get; init; }
        public DateTimeOffset? ProcessedOnUtc { get; set; }
        public string? Error { get; set; }

        public int RetryCount { get; set; }
    }
}
