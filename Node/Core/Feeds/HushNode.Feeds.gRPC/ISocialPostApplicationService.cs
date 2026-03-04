using HushNetwork.proto;

namespace HushNode.Feeds.gRPC;

public interface ISocialPostApplicationService
{
    Task<CreateSocialPostResponse> CreateSocialPostAsync(CreateSocialPostRequest request);

    Task<GetSocialPostPermalinkResponse> GetSocialPostPermalinkAsync(GetSocialPostPermalinkRequest request);
}
