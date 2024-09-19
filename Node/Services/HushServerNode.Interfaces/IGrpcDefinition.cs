namespace HushServerNode.Interfaces;

public interface IGrpcDefinition
{
    void AddGrpcService(Grpc.Core.Server server);
}
