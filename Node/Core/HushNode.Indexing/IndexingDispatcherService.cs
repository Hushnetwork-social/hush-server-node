using HushNode.Events;
using Olimpo;

namespace HushNode.Indexing;

public class IndexingDispatcherService :
    IIndexingDispatcherService,
    IHandleAsync<BlockCreatedEvent>
{
    private readonly IEventAggregator _eventAggregator;

    public IndexingDispatcherService(IEventAggregator eventAggregator)
    {
        this._eventAggregator = eventAggregator;

        this._eventAggregator.Subscribe(this);
    }

    public Task HandleAsync(BlockCreatedEvent message)
    {
        return Task.CompletedTask;
    }
}