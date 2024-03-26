using System.Reactive.Subjects;
using System.Threading.Tasks;
using Olimpo;

namespace HushServerNode.RpcManager;

public class RpcBootstrapper : IBootstrapper
{
    private readonly IRpc _rpc;

    public Subject<bool> BootstrapFinished { get; }

    public int Priority { get; set; }

    public RpcBootstrapper(IRpc rpc)
    {
        this._rpc = rpc;

        this.BootstrapFinished = new Subject<bool>();
    }

    public void Shutdown()
    {
    }

    public Task Startup()
    {
        this.BootstrapFinished.OnNext(true);
        return Task.CompletedTask;
    }
}
