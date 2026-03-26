using Grpc.Core;
using HushNetwork.proto;
using HushNode.Interfaces;

namespace HushNode.Elections.gRPC;

public class ElectionsGrpcServiceDefinition(HushElections.HushElectionsBase hushElectionsGrpcService) : IGrpcDefinition
{
    private readonly HushElections.HushElectionsBase _hushElectionsGrpcService = hushElectionsGrpcService;

    public void AddGrpcService(Server server)
    {
        server.Services.Add(HushElections.BindService(_hushElectionsGrpcService));
    }
}
