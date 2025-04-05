using System.Reactive.Subjects;
using Olimpo;
using HushNode.Identity.Events;

namespace HushNode.Identity;

public class IdentityBootstrapper : 
    IBootstrapper,
    IHandle<IdentityInitializedEvent>
{
    private readonly IIdentityInitializationWorkflow _identityInitializationWorkflow;

    public Subject<bool> BootstrapFinished { get; } = new Subject<bool>();

    public int Priority { get; set; } = 7;

    public IdentityBootstrapper(
        IIdentityInitializationWorkflow identityInitializationWorkflow,
        IEventAggregator eventAggregator)
    {
        this._identityInitializationWorkflow = identityInitializationWorkflow;

        eventAggregator.Subscribe(this);
    }

    public void Shutdown()
    {
    }

    public async Task Startup()
    {
        await this._identityInitializationWorkflow.Initialize();
    }

    public void Handle(IdentityInitializedEvent message)
    {
        this.BootstrapFinished.OnNext(true);
    }
}
