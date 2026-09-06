using Grpc.Core;
using HushNetwork.proto;
using HushNode.Interfaces;

namespace HushNode.HushVoting.Licence.gRPC;

public sealed class HushVotingLicenceGrpcServiceDefinition(
    HushVotingLicence.HushVotingLicenceBase licenceGrpcService) : IGrpcDefinition
{
    private readonly HushVotingLicence.HushVotingLicenceBase _licenceGrpcService = licenceGrpcService;

    public void AddGrpcService(Server server)
    {
        server.Services.Add(HushVotingLicence.BindService(_licenceGrpcService));
    }
}
