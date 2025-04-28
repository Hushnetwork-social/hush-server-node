using System.Reactive.Subjects;
using Olimpo;

namespace HushServerNode.InternalModule.Blockchain;

public class BlockchainBootstrapper : IBootstrapper
{
    private readonly IBlockchainService _blockchainService;
    private readonly IBlockGeneratorService _blockGeneratorService;

    public int Priority { get; set; } = 10;

    public Subject<string> BootstrapFinished { get; }

    public BlockchainBootstrapper(
        IBlockchainService blockchainService,
        IBlockGeneratorService blockGeneratorService)
    {
        this._blockchainService = blockchainService;
        this._blockGeneratorService = blockGeneratorService;
        
        this.BootstrapFinished = new Subject<string>();
    }

    public void Shutdown()
    {
    }

    public async Task Startup()
    {
        await this._blockchainService.InitializeBlockchainAsync();

        this.BootstrapFinished.OnNext("Blockchain");
    }
}
