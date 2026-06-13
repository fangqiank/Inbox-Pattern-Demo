using Microsoft.Extensions.Logging;
using OutboxPatternDemo.Domain;

namespace OutboxPatternDemo.Infrastructure.EventHandlers
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
    }
}
