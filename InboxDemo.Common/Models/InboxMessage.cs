namespace InboxDemo.Common.Models
{
    public class InboxMessage
    {
        public Guid Id { get; set; }                    // MessageId 作为主键
        public string MessageType { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;  // JSON 格式
        public DateTime ReceivedOnUtc { get; set; }
        public DateTime? ProcessedOnUtc { get; set; }
        public string? Error { get; set; }
        public int RetryCount { get; set; }
    }
}
