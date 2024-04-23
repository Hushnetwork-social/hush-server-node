using Grpc.Core;
using HushEcosystem.Model.Blockchain;
using HushNetwork.proto;
using HushServerNode.Blockchain;
using HushServerNode.Blockchain.Events;
using Olimpo;

namespace HushServerNode.Services;

public class HushProfileService : HushProfile.HushProfileBase
{
    private readonly IBlockchainIndexDb _blockchainIndexDb;
    private readonly IEventAggregator _eventAggregator;

    public HushProfileService(
        IBlockchainIndexDb blockchainIndexDb,
        IEventAggregator eventAggregator)
    {
        this._blockchainIndexDb = blockchainIndexDb;
        this._eventAggregator = eventAggregator;
    }

    public override Task<SetProfileReply> SetProfile(SetProfileRequest request, ServerCallContext context)
    {
        var profile = this._blockchainIndexDb.Profiles
            .SingleOrDefault(x => x.UserPublicSigningAddress == request.Profile.UserPublicSigningAddress);
        
        if (profile == null)
        {
            // sends the UserProfile to MemPool 
            this._eventAggregator.PublishAsync(new AddTrasactionToMemPoolEvent(
                new HushUserProfile
                {
                    Id = request.Profile.Id,
                    Issuer = request.Profile.Issuer,
                    UserPublicSigningAddress = request.Profile.UserPublicSigningAddress,
                    UserPublicEncryptAddress = request.Profile.UserPublicEncryptAddress,
                    UserName = request.Profile.UserName,
                    IsPublic = request.Profile.IsPublic,
                    Hash = request.Profile.Hash,
                    Signature = request.Profile.Signature
                }
            ));

            return Task.FromResult(new SetProfileReply
            {
                Successfull = true,
                ResultType = 1,
                Message = "User Profile added to blockchain"
            });
        }

        // TODO [AboimPinto] here should be update the ProfileName and IsPublic flag
        return Task.FromResult(new SetProfileReply
        {
            Message = "???"
        });
    }

    public override Task<ProfileExistsReply> ProfileExists(ProfileExistsRequest request, ServerCallContext context)
    {
        var profile = this._blockchainIndexDb.Profiles
            .SingleOrDefault(x => x.UserPublicSigningAddress == request.ProfilePublicKey);

        if (profile == null)
        {
            return Task.FromResult(new ProfileExistsReply
            {
                Exists = false
            });
        }

        return Task.FromResult(new ProfileExistsReply
        {
            Exists = true
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
                Successfull = false,
                Message = "Could not find the profile in Blockchain"
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

    public override Task<SearchProfileReply> SearchProfileByPublicKey(SearchProfileRequest request, ServerCallContext context)
    {
        var profiles = this._blockchainIndexDb.Profiles
            .Where(x => x.UserPublicSigningAddress == request.ProfilePublicKey);

        var reply = new SearchProfileReply();

        foreach (var profile in profiles)
        {
            reply.SeachedProfiles.Add(new SearchProfileReply.Types.SeachedProfile
            {
                UserName = profile.UserName,
                UserPublicSigningAddress = profile.UserPublicSigningAddress,
                UserPublicEncryptAddress = profile.UserPublicEncryptAddress
            });
        }

        return Task.FromResult(reply);
    }
}
