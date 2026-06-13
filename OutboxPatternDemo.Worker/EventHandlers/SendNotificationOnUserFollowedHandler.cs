using Microsoft.Extensions.Logging;
using OutboxPatternDemo.Domain;
using OutboxPatternDemo.Infrastructure;

namespace OutboxPatternDemo.Worker.EventHandlers
{
    public class SendNotificationOnUserFollowedHandler(ILogger<SendNotificationOnUserFollowedHandler> logger) : IEventHandler<UserFollowedEvent>
    {
        public string HandlerName => nameof(SendNotificationOnUserFollowedHandler);

        public async Task HandleAsync(UserFollowedEvent @event, CancellationToken cancellationToken)
        {
            logger.LogInformation(
                "[NOTIFICATION] Sending notification: User {FollowerId} started following {FollowedId} at {OccurredOn}",
                @event.FollowerId, @event.FollowedId, @event.OccurredOn);

            await Task.Delay(100, cancellationToken);

            logger.LogInformation(
                "[NOTIFICATION] Notification sent successfully for follow event {EventId}",
                @event.Id);
        }

        async Task IEventHandler.HandleAsync(object domainEvent, CancellationToken cancellationToken)
            => await HandleAsync((UserFollowedEvent)domainEvent, cancellationToken);
    }
}
