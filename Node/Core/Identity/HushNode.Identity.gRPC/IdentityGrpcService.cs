using Grpc.Core;
using HushNetwork.proto;
using HushNode.Identity.Model;
using HushNode.Identity.Storage;
using Microsoft.VisualBasic;

namespace HushNode.Identity.gRPC;

public class IdentityGrpcService(IIdentityStorageService identityStorageService) : HushIdentity.HushIdentityBase
{
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;

    public override async Task<GetIdentityReply> GetIdentity(GetIdentityRequest request, ServerCallContext context)
    {
        var profileBase = await this._identityStorageService.RetrieveIdentityAsync(request.PublicSigningAddress);
        
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
            reply.PublicEncryptAddress = profile.PublicEncryptionKey;
            reply.ProfileName = profile.Alias;
            reply.IsPublic = profile.IsPublic;

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
