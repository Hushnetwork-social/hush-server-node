using Grpc.Core;
using HushNetwork.proto;
using HushServerNode.Interfaces;

namespace HushServerNode.InternalModule.Authentication;

public class AuthenticationGrpcServiceDefinition : IGrpcDefinition
{
    private readonly HushProfile.HushProfileBase _authenticationGrpcService;

    public AuthenticationGrpcServiceDefinition(HushProfile.HushProfileBase authenticationGrpcService)
    {
        this._authenticationGrpcService = authenticationGrpcService;
    }

    public void AddGrpcService(Server server)
    {
        server.Services.Add(HushProfile.BindService(this._authenticationGrpcService));
    }
}
