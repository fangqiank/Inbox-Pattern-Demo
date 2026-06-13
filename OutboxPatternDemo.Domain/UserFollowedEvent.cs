namespace OutboxPatternDemo.Domain
{
    public record UserFollowedEvent(Guid FollowerId, Guid FollowedId) : DomainEvent
    {
    }
}
