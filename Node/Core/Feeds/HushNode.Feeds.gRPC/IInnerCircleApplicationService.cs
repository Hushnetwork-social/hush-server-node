using HushNetwork.proto;

namespace HushNode.Feeds.gRPC;

public interface IInnerCircleApplicationService
{
    Task<GetInnerCircleResponse> GetInnerCircleAsync(string ownerPublicAddress);
    Task<CreateInnerCircleResponse> CreateInnerCircleAsync(string ownerPublicAddress, string requesterPublicAddress);
    Task<AddMembersToInnerCircleResponse> AddMembersToInnerCircleAsync(
        string ownerPublicAddress,
        string requesterPublicAddress,
        IReadOnlyList<InnerCircleMemberProto> members);
}
