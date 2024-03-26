using System.Reactive.Subjects;
using System.Threading.Tasks;
using Olimpo;

namespace HushServerNode.Blockchain;

public class BlockchainBootstrapper : IBootstrapper
{
    private readonly IBlockchainService _blockchainService;
    private readonly IMemPoolService _memPoolService;
    private readonly IBlockGeneratorService _blockGeneratorService;

    public int Priority { get; set; } = 10;

    public Subject<bool> BootstrapFinished { get; }

    public BlockchainBootstrapper(
        IBlockchainService blockchainService,
        IMemPoolService memPoolService,
        IBlockGeneratorService blockGeneratorService)
    {
        this._blockchainService = blockchainService;
        this._memPoolService = memPoolService;
        this._blockGeneratorService = blockGeneratorService;

        this.BootstrapFinished = new Subject<bool>();
    }

    public void Shutdown()
    {
    }

    public async Task Startup()
    {
        await this._blockchainService.InitializeBlockchainAsync();
        await this._memPoolService.InitializeMemPool();

        this.BootstrapFinished.OnNext(true);
    }
}
