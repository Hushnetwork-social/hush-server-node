using Grpc.Core;
using HushNetwork.proto;
using HushNode.Interfaces;

namespace HushNode.InternalModules.Identity;

public class IdentityGrpcServiceDefinition(HushProfile.HushProfileBase identityGrpcService) : IGrpcDefinition
{
    private readonly HushProfile.HushProfileBase _identityGrpcService = identityGrpcService;

    public void AddGrpcService(Server server)
    {
        server.Services.Add(HushProfile.BindService(this._identityGrpcService));
    }
}
