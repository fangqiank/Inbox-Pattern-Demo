namespace OutboxPatternDemo.Infrastructure
{
    public interface IEventHandler<TEvent>
    {
        Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
        string HandlerName { get; }
    }
}
