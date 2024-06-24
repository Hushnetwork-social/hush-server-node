using System.Reactive.Subjects;
using System.Threading.Tasks;
using Olimpo;

namespace HushServerNode.CacheService;

public class BlockchainCacheBootstrapper : IBootstrapper
{
    private readonly IBlockchainCache _blockchainCache;

    public Subject<bool> BootstrapFinished { get; }

    public int Priority { get; set; } = 5;

    public BlockchainCacheBootstrapper(IBlockchainCache blockchainCache)
    {
        this._blockchainCache = blockchainCache;

        this.BootstrapFinished = new Subject<bool>();
    }

    public void Shutdown()
    {
    }

    public Task Startup()
    {
        this._blockchainCache.ConnectPersistentDatabase();

        this.BootstrapFinished.OnNext(true);

        return Task.CompletedTask;
    }
}
