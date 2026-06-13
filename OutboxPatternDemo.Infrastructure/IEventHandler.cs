namespace OutboxPatternDemo.Infrastructure
{
    public interface IEventHandler
    {
        string HandlerName { get; }
        Task HandleAsync(object domainEvent, CancellationToken cancellationToken);
    }

    public interface IEventHandler<TEvent> : IEventHandler
    {
        Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
    }
}
