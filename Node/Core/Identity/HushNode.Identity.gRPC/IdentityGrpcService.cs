using Grpc.Core;
using HushNetwork.proto;

namespace HushNode.Identity.gRPC;

public class IdentityGrpcService : HushProfile.HushProfileBase
{
    public override Task<GetProfileReply> GetProfile(GetProfileRequest request, ServerCallContext context)
    {
        return base.GetProfile(request, context);
    }

    public override Task<LoadProfileReply> LoadProfile(LoadProfileRequest request, ServerCallContext context)
    {
        return base.LoadProfile(request, context);
    }

    public override Task<ProfileExistsReply> ProfileExists(ProfileExistsRequest request, ServerCallContext context)
    {
        return base.ProfileExists(request, context);
    }

    public override Task<SearchProfileByPublicKeyReply> SearchProfileByPublicKey(SearchProfileByPublicKeyRequest request, ServerCallContext context)
    {
        return base.SearchProfileByPublicKey(request, context);
    }

    public override Task<SearchProfileByUserNameReply> SearchProfileByUserName(SearchProfileByUserNameRequest request, ServerCallContext context)
    {
        return base.SearchProfileByUserName(request, context);
    }

    public override Task<SetProfileReply> SetProfile(SetProfileRequest request, ServerCallContext context)
    {
        return base.SetProfile(request, context);
    }
}
