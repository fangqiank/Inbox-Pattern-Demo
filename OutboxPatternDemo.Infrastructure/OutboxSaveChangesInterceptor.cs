using Microsoft.EntityFrameworkCore.Diagnostics;
using OutboxPatternDemo.Domain;
using System.Text.Json;

namespace OutboxPatternDemo.Infrastructure
{
    public class OutboxSaveChangesInterceptor: SaveChangesInterceptor
    {
        private static readonly JsonSerializerOptions jsonSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
            )
        {
            var dbContext = eventData.Context;
            if (dbContext is null)
                return result;

            var domainEvent = dbContext.ChangeTracker
                .Entries<Entity>()
                .Where(e => e.Entity.DomainEvents.Any())
                .SelectMany(x => x.Entity.DomainEvents)
                .ToList();

            var outboxMessages = domainEvent.Select(ToOutboxMessage).ToList();

            if(outboxMessages.Count > 0)
                dbContext.Set<OutboxMessage>().AddRange(outboxMessages);

            foreach ( var entry in dbContext.ChangeTracker.Entries<Entity>() )
            {
                entry.Entity.ClearDomainEvents();
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }


        private static OutboxMessage ToOutboxMessage(DomainEvent domainEvent)
        {
            return new OutboxMessage
            {
                Id = Guid.NewGuid(),
                MessageId = domainEvent.Id,
                Name = domainEvent.GetType().Name,
                Content = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), jsonSerializerOptions),
                CreatedOnUtc = domainEvent.OccurredOn
            };
        }
    }
}
