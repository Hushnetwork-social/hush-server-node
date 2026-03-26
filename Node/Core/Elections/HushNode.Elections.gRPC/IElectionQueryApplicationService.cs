using HushNetwork.proto;
using HushShared.Elections.Model;

namespace HushNode.Elections.gRPC;

public interface IElectionQueryApplicationService
{
    Task<GetElectionResponse> GetElectionAsync(ElectionId electionId);

    Task<GetElectionsByOwnerResponse> GetElectionsByOwnerAsync(string ownerPublicAddress);
}
