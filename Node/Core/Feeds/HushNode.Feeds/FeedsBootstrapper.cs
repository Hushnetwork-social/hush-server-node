using System.Reactive.Subjects;
using HushNode.Feeds.Events;
using Olimpo;

namespace HushNode.Feeds;

public class FeedsBootstrapper : 
    IBootstrapper,
    IHandle<FeedsInitializedEvent>
{
    private readonly IFeedsInitializationWorkflow _feedInitializationWorkflow;

    public Subject<string> BootstrapFinished { get; } = new Subject<string>();

    public int Priority { get; set; } = 10;

    public FeedsBootstrapper(
        IFeedsInitializationWorkflow feedInitializationWorkflow,
        IEventAggregator eventAggregator)
    {
        this._feedInitializationWorkflow = feedInitializationWorkflow;

        eventAggregator.Subscribe(this);
    }

    public void Shutdown()
    {
    }

    public async Task Startup()
    {
        await this._feedInitializationWorkflow.Initialize();
    }

    public void Handle(FeedsInitializedEvent message)
    {
        this.BootstrapFinished.OnNext("Feeds");
    }
}
