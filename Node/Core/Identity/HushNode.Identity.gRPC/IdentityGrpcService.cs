using Grpc.Core;
using HushNetwork.proto;
using HushNode.Identity.Storage;
using HushShared.Identity.Model;

namespace HushNode.Identity.gRPC;

public class IdentityGrpcService(IIdentityService identityService, IIdentityStorageService identityStorageService) : HushIdentity.HushIdentityBase
{
    private readonly IIdentityService _identityService = identityService;
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;

    public override async Task<GetIdentityReply> GetIdentity(GetIdentityRequest request, ServerCallContext context)
    {
        // Use IIdentityService which includes cache-aside pattern (FEAT-048)
        var profileBase = await this._identityService.RetrieveIdentityAsync(request.PublicSigningAddress);
        
        var reply = new GetIdentityReply();
        if (profileBase is NonExistingProfile)
        {
            reply.Successfull = false;
            reply.Message = "Identity not found in the Blockchain";
        }
        else
        {
            var profile = (Profile)profileBase;

            reply.Successfull = true;
            reply.Message = string.Empty;
            reply.PublicSigningAddress = profile.PublicSigningAddress;
            reply.PublicEncryptAddress = profile.PublicEncryptAddress;
            reply.ProfileName = profile.Alias;
            reply.IsPublic = profile.IsPublic;
        }

        return reply;
    }

    public override async Task<SearchByDisplayNameReply> SearchByDisplayName(SearchByDisplayNameRequest request, ServerCallContext context)
    {
        var identitiesFound = await this._identityStorageService.SearchByDisplayNameAsync(request.PartialDisplayName);
        
        var reply = new SearchByDisplayNameReply();

        foreach (var identity in identitiesFound)
        {
            reply.Identities.Add(
                new SearchByDisplayNameReply.Types.Identity
                {
                    DisplayName = identity.Alias,
                    PublicSigningAddress = identity.PublicSigningAddress,
                    PublicEncryptAddress = identity.PublicEncryptAddress 
                });
        }

        return reply;
    }


    // public override Task<GetProfileReply> GetProfile(GetProfileRequest request, ServerCallContext context)
    // {
    //     return base.GetProfile(request, context);
    // }

    // public override Task<LoadProfileReply> LoadProfile(LoadProfileRequest request, ServerCallContext context)
    // {
    //     return base.LoadProfile(request, context);
    // }

    // public override Task<ProfileExistsReply> ProfileExists(ProfileExistsRequest request, ServerCallContext context)
    // {
    //     return base.ProfileExists(request, context);
    // }

    // public override Task<SearchProfileByPublicKeyReply> SearchProfileByPublicKey(SearchProfileByPublicKeyRequest request, ServerCallContext context)
    // {
    //     return base.SearchProfileByPublicKey(request, context);
    // }

    // public override Task<SearchProfileByUserNameReply> SearchProfileByUserName(SearchProfileByUserNameRequest request, ServerCallContext context)
    // {
    //     return base.SearchProfileByUserName(request, context);
    // }

    // public override Task<SetProfileReply> SetProfile(SetProfileRequest request, ServerCallContext context)
    // {
    //     return base.SetProfile(request, context);
    // }
}
