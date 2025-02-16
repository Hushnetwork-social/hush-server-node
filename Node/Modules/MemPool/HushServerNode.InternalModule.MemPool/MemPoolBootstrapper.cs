using System.Reactive.Subjects;
using Olimpo;

namespace HushServerNode.InternalModule.MemPool;

public class MemPoolBootstrapper : IBootstrapper
{
    private readonly IMemPoolService _memPoolService;

    public int Priority { get; set; } = 10;

    public Subject<bool> BootstrapFinished { get; }

    public MemPoolBootstrapper(IMemPoolService memPoolService)
    {
        this._memPoolService = memPoolService;
        
        this.BootstrapFinished = new Subject<bool>();
    }

    public void Shutdown()
    {
    }

    public Task Startup()
    {
        this._memPoolService.InitializeMemPool();

        this.BootstrapFinished.OnNext(true);
        return Task.CompletedTask;
    }
}
