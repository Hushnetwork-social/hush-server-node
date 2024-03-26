using Grpc.Core;
using HushNetwork;
using HushServerNode.Blockchain;

namespace HushServerNode.Services;

public class HushProfileService : HushProfile.HushProfileBase
{
    private readonly IBlockchainIndexDb _blockchainIndexDb;

    public HushProfileService(IBlockchainIndexDb blockchainIndexDb)
    {
        this._blockchainIndexDb = blockchainIndexDb;
    }

    public override Task<SetProfileReply> SetProfile(SetProfileRequest request, ServerCallContext context)
    {
        return Task.FromResult(new SetProfileReply
        {
            Message = "Hello " //+ request.Name
        });
    }

    public override Task<LoadProfileReply> LoadProfile(LoadProfileRequest request, ServerCallContext context)
    {
        var profile = this._blockchainIndexDb.Profiles
            .SingleOrDefault(x => x.UserPublicSigningAddress == request.ProfilePublicKey);

        if (profile == null)
        {
            return Task.FromResult(new LoadProfileReply
            {
                Profile = null
            });    
        }

        return Task.FromResult(new LoadProfileReply
        {
            Profile = new LoadProfileReply.Types.UserProfile
            {
                UserPublicSigningAddress = profile.UserPublicEncryptAddress,
                UserPublicEncryptAddress = profile.UserPublicEncryptAddress,
                UserName = profile.UserName,
                IsPublic = profile.IsPublic
            }
        });
    }
}
