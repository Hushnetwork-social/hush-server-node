using System.Reactive.Subjects;
using Grpc.Core;
using HushNetwork.proto;
using HushServerNode.Blockchain;
using Olimpo;

namespace HushServerNode;

public class gRPCServerBootstraper : IBootstrapper
{
    private readonly IBlockchainService _blockchainService;
    private readonly HushProfile.HushProfileBase _hushProfileBase;
    private readonly HushBlockchain.HushBlockchainBase _hushBlockchainBase;
    private readonly HushFeed.HushFeedBase _hushFeedBase;

    public Subject<bool> BootstrapFinished { get; }

    public int Priority { get; set; } = 10;
        

    // public gRPCServerBootstraper(
    //     IBlockchainService blockchainService,
    //     HushProfile.HushProfileBase hushProfileBase,
    //     HushBlockchain.HushBlockchainBase hushBlockchainBase,
    //     HushFeed.HushFeedBase hushFeedBase)
    // {
    //     this._blockchainService = blockchainService;
    //     this._hushProfileBase = hushProfileBase;
    //     this._hushBlockchainBase = hushBlockchainBase;
    //     this._hushFeedBase = hushFeedBase;

    //     this.BootstrapFinished = new Subject<bool>();
    // }

    public gRPCServerBootstraper()
    {
        this.BootstrapFinished = new Subject<bool>();
    }

    public void Shutdown()
    {
    }

    public Task Startup()
    {
        // var rcpServer = new Grpc.Core.Server
        // {
        //     Services = 
        //     { 
        //         Greeter.BindService(new GreeterService()),
        //         HushProfile.BindService(this._hushProfileBase),
        //         HushBlockchain.BindService(this._hushBlockchainBase),
        //         HushFeed.BindService(this._hushFeedBase)
        //     },
        //     Ports = {new ServerPort("localhost", 5000, ServerCredentials.Insecure)}
        // };

        // rcpServer.Start();

        this.BootstrapFinished.OnNext(true);
        return Task.CompletedTask;
    }
}
