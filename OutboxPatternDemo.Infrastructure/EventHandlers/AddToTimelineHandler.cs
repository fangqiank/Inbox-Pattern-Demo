using Microsoft.Extensions.Logging;
using OutboxPatternDemo.Domain;

namespace OutboxPatternDemo.Infrastructure.EventHandlers
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
    }
}
