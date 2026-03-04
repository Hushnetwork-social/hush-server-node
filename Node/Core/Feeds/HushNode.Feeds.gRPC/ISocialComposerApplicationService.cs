using HushNetwork.proto;

namespace HushNode.Feeds.gRPC;

public interface ISocialComposerApplicationService
{
    Task<GetSocialComposerContractResponse> GetSocialComposerContractAsync(GetSocialComposerContractRequest request);
}
