using HushNode.Feeds.Events;
using Olimpo;

namespace HushNode.Feeds;

public class FeedsInitializationWorkflow(IEventAggregator eventAggregator) : IFeedsInitializationWorkflow
{
    private readonly IEventAggregator _eventAggregator = eventAggregator;

    public async Task Initialize()
    {
        await this._eventAggregator.PublishAsync(new FeedsInitializedEvent());
    }
}
