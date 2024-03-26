using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Grpc.Core;
using HushNetwork;
using HushServerNode.Blockchain;
using Olimpo;

namespace HushServerNode;

public class gRPCServerBootstraper : IBootstrapper
{
    private readonly IBlockchainService _blockchainService;
    private readonly HushProfile.HushProfileBase _hushProfileBase;
    private readonly HushBlockchain.HushBlockchainBase _hushBlockchainBase;

    public Subject<bool> BootstrapFinished { get; }

    public int Priority { get; set; } = 10;
        

    public gRPCServerBootstraper(
        IBlockchainService blockchainService,
        HushProfile.HushProfileBase hushProfileBase,
        HushBlockchain.HushBlockchainBase hushBlockchainBase)
    {
        this._blockchainService = blockchainService;
        this._hushProfileBase = hushProfileBase;
        this._hushBlockchainBase = hushBlockchainBase;

        this.BootstrapFinished = new Subject<bool>();
    }

    public void Shutdown()
    {
    }

    public Task Startup()
    {
        var rcpServer = new Grpc.Core.Server
        {
            Services = 
            { 
                Greeter.BindService(new GreeterService()),
                HushProfile.BindService(this._hushProfileBase),
                HushBlockchain.BindService(this._hushBlockchainBase)
            },
            Ports = {new ServerPort("localhost", 5000, ServerCredentials.Insecure)}
        };

        rcpServer.Start();

        this.BootstrapFinished.OnNext(true);
        return Task.CompletedTask;
    }
}
