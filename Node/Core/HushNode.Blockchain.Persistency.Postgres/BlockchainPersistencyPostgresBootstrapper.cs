using System.Reactive.Subjects;
using Olimpo;

namespace HushNode.Blockchain.Persistency.Postgres;

public class BlockchainPersistencyPostgresBootstrapper : IBootstrapper
{
    public Subject<bool> BootstrapFinished { get; } = new Subject<bool>();

    public int Priority { get; set; } = 10;

    public void Shutdown()
    {
        throw new NotImplementedException();
    }

    public Task Startup()
    {
        return Task.CompletedTask;
    }
}
