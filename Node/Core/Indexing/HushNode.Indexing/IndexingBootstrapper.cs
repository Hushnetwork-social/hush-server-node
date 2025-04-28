using System.Reactive.Subjects;
using Olimpo;

namespace HushNode.Indexing;

public class IndexingBootstrapper : IBootstrapper
{
    public Subject<string> BootstrapFinished { get; } = new Subject<string>();

    public int Priority { get; set; } = 10;

    public IndexingBootstrapper(IIndexingDispatcherService indexingDispatcherService)
    {
        
    }

    public void Shutdown()
    {
    }

    public Task Startup()
    {
        this.BootstrapFinished.OnNext("Indexing");
        return Task.CompletedTask;
    }
}
