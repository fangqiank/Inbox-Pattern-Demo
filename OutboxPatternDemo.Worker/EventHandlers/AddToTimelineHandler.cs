using Microsoft.Extensions.Logging;
using OutboxPatternDemo.Domain;
using OutboxPatternDemo.Infrastructure;

namespace OutboxPatternDemo.Worker.EventHandlers
{
    public class AddToTimelineHandler(ILogger<AddToTimelineHandler> logger) : IEventHandler<UserFollowedEvent>
    {
        public string HandlerName => nameof(AddToTimelineHandler);

        public async Task HandleAsync(UserFollowedEvent @event, CancellationToken cancellationToken)
        {
            logger.LogInformation(
                "[TIMELINE] Adding to timeline: User {FollowerId} followed {FollowedId}",
                @event.FollowerId, @event.FollowedId);

            await Task.Delay(75, cancellationToken);

            logger.LogInformation(
                "[TIMELINE] Timeline entry added for event {EventId}",
                @event.Id);
        }

        async Task IEventHandler.HandleAsync(object domainEvent, CancellationToken cancellationToken)
            => await HandleAsync((UserFollowedEvent)domainEvent, cancellationToken);
    }
}
