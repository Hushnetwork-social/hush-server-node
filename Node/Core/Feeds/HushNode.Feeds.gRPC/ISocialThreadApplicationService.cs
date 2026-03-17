using HushNetwork.proto;

namespace HushNode.Feeds.gRPC;

public interface ISocialThreadApplicationService
{
    Task<GetSocialCommentsPageResponse> GetSocialCommentsPageAsync(GetSocialCommentsPageRequest request);

    Task<GetSocialThreadRepliesResponse> GetSocialThreadRepliesAsync(GetSocialThreadRepliesRequest request);
}
