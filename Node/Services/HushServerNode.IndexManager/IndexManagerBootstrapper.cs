using System.Reactive.Subjects;
using Olimpo;

namespace HushServerNode.IndexManager;

public class IndexManagerBootstrapper : IBootstrapper
{
    public Subject<bool> BootstrapFinished { get; }

    public int Priority { get; set; }

    public IndexManagerBootstrapper()
    {
         this.BootstrapFinished = new Subject<bool>();
    }

    public void Shutdown()
    {
    }

    public Task Startup()
    {
        return Task.CompletedTask;
    }
}
