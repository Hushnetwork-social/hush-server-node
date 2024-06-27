using Grpc.Core;
using HushEcosystem.Model.Blockchain;
using HushNetwork.proto;
using HushServerNode.Blockchain.Events;
using HushServerNode.CacheService;
using Olimpo;

namespace HushServerNode.Services;

public class HushProfileService : HushProfile.HushProfileBase
{
    private readonly IBlockchainCache _blockchainCacheService;
    private readonly IEventAggregator _eventAggregator;

    public HushProfileService(
        IBlockchainCache blockchainCacheService,
        IEventAggregator eventAggregator)
    {
        this._blockchainCacheService = blockchainCacheService;
        this._eventAggregator = eventAggregator;
    }

    public override Task<SetProfileReply> SetProfile(SetProfileRequest request, ServerCallContext context)
    {
        var profile = this._blockchainCacheService.GetProfile(request.Profile.UserPublicSigningAddress);
        
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
            Successfull = false,
            ResultType = 2,
            Message = "User Profile already in the blockchain"
        });
    }

    public override Task<ProfileExistsReply> ProfileExists(ProfileExistsRequest request, ServerCallContext context)
    {
        var profile = this._blockchainCacheService.GetProfile(request.ProfilePublicKey);

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
        var profile = this._blockchainCacheService.GetProfile(request.ProfilePublicKey);

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
                UserPublicSigningAddress = profile.PublicSigningAddress,
                UserPublicEncryptAddress = profile.PublicEncryptAddress,
                UserName = profile.UserName,
                IsPublic = profile.IsPublic
            }
        });
    }

    public override Task<SearchProfileReply> SearchProfileByPublicKey(SearchProfileRequest request, ServerCallContext context)
    {
        var profile = this._blockchainCacheService.GetProfile(request.ProfilePublicKey);

        var reply = new SearchProfileReply();

        reply.SeachedProfiles.Add(new SearchProfileReply.Types.SeachedProfile
        {
            UserName = profile.UserName,
            UserPublicSigningAddress = profile.PublicSigningAddress,
            UserPublicEncryptAddress = profile.PublicEncryptAddress
        });

        return Task.FromResult(reply);
    }
}
