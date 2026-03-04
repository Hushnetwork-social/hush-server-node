using HushNetwork.proto;

namespace HushNode.Feeds.gRPC;

public interface IGroupAdministrationApplicationService
{
    Task<BlockMemberResponse> BlockMemberAsync(BlockMemberRequest request);
    Task<UnblockMemberResponse> UnblockMemberAsync(UnblockMemberRequest request);
    Task<BanFromGroupFeedResponse> BanFromGroupFeedAsync(BanFromGroupFeedRequest request);
    Task<UnbanFromGroupFeedResponse> UnbanFromGroupFeedAsync(UnbanFromGroupFeedRequest request);
    Task<PromoteToAdminResponse> PromoteToAdminAsync(PromoteToAdminRequest request);
    Task<UpdateGroupFeedTitleResponse> UpdateGroupFeedTitleAsync(UpdateGroupFeedTitleRequest request);
    Task<UpdateGroupFeedDescriptionResponse> UpdateGroupFeedDescriptionAsync(UpdateGroupFeedDescriptionRequest request);
    Task<UpdateGroupFeedSettingsResponse> UpdateGroupFeedSettingsAsync(UpdateGroupFeedSettingsRequest request);
    Task<DeleteGroupFeedResponse> DeleteGroupFeedAsync(DeleteGroupFeedRequest request);
}
