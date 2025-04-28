using System.Reactive.Subjects;
using Olimpo;

namespace HushNode.MemPool;

public class MemPoolBoopstrapper(IMemPoolService memPoolService) : IBootstrapper
{
    private readonly IMemPoolService _memPoolService = memPoolService;

    public Subject<string> BootstrapFinished { get; } = new Subject<string>();

    public int Priority { get; set; } = 10;

    public void Shutdown()
    {
    }

    public async Task Startup()
    {
        await this._memPoolService.InitializeMemPoolAsync();
        this.BootstrapFinished.OnNext("Mempool");
    }
}
