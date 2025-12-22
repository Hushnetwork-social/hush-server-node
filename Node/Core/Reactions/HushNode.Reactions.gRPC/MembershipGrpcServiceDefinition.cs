using Grpc.Core;
using HushNetwork.proto;
using HushNode.Interfaces;

namespace HushNode.Reactions.gRPC;

public class MembershipGrpcServiceDefinition(HushMembership.HushMembershipBase hushMembershipGrpcService) : IGrpcDefinition
{
    private readonly HushMembership.HushMembershipBase _hushMembershipGrpcService = hushMembershipGrpcService;

    public void AddGrpcService(Server server)
    {
        server.Services.Add(HushMembership.BindService(this._hushMembershipGrpcService));
    }
}
