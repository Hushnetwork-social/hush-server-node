using System.Reactive.Subjects;
using Olimpo;

namespace HushNode.Indexing;

public class IndexingBootstrapper : IBootstrapper
{
    public Subject<bool> BootstrapFinished { get; } = new Subject<bool>();

    public int Priority { get; set; } = 10;

    public void Shutdown()
    {
    }

    public Task Startup()
    {
        this.BootstrapFinished.OnNext(true);
        return Task.CompletedTask;
    }
}
