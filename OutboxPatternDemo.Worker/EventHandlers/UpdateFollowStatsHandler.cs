using Microsoft.Extensions.Logging;
using OutboxPatternDemo.Domain;
using OutboxPatternDemo.Infrastructure;

namespace OutboxPatternDemo.Worker.EventHandlers
{
    public class UpdateFollowStatsHandler(ILogger<UpdateFollowStatsHandler> logger) : IEventHandler<UserFollowedEvent>
    {
        public string HandlerName => nameof(UpdateFollowStatsHandler);

        public async Task HandleAsync(UserFollowedEvent @event, CancellationToken cancellationToken)
        {
            logger.LogInformation(
                "[STATS] Updating follow statistics: Follower={FollowerId}, Followed={FollowedId}",
                @event.FollowerId, @event.FollowedId);

            await Task.Delay(50, cancellationToken);

            logger.LogInformation(
                "[STATS] Statistics updated successfully for event {EventId}",
                @event.Id);
        }

        async Task IEventHandler.HandleAsync(object domainEvent, CancellationToken cancellationToken)
            => await HandleAsync((UserFollowedEvent)domainEvent, cancellationToken);
    }
}
