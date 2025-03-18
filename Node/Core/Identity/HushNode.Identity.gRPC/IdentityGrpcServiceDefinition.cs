using Grpc.Core;
using HushNetwork.proto;
using HushNode.Interfaces;

namespace HushNode.Identity.gRPC;

public class IdentityGrpcServiceDefinition(HushIdentity.HushIdentityBase identityGrpcService) : IGrpcDefinition
{
    private readonly HushIdentity.HushIdentityBase _identityGrpcService = identityGrpcService;

    public void AddGrpcService(Server server)
    {
        server.Services.Add(HushIdentity.BindService(_identityGrpcService));
    }
}
