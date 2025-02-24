using System.Reactive.Subjects;
using Olimpo;

namespace HushNode.Indexing;

public class IndexingBootstrapper : IBootstrapper
{
    public Subject<bool> BootstrapFinished => throw new NotImplementedException();

    public int Priority { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public void Shutdown()
    {
        throw new NotImplementedException();
    }

    public Task Startup()
    {
        throw new NotImplementedException();
    }
}
