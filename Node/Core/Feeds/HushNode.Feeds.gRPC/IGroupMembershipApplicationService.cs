using HushNetwork.proto;

namespace HushNode.Feeds.gRPC;

public interface IGroupMembershipApplicationService
{
    Task<JoinGroupFeedResponse> JoinGroupFeedAsync(JoinGroupFeedRequest request);
    Task<LeaveGroupFeedResponse> LeaveGroupFeedAsync(LeaveGroupFeedRequest request);
    Task<AddMemberToGroupFeedResponse> AddMemberToGroupFeedAsync(AddMemberToGroupFeedRequest request);
}
