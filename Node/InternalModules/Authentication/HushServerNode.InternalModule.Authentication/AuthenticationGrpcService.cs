using Grpc.Core;
using HushEcosystem.Model.Blockchain;
using HushNetwork.proto;
using HushServerNode.InternalModule.MemPool.Events;
using Olimpo;

namespace HushServerNode.InternalModule.Authentication;

public class AuthenticationGrpcService : HushProfile.HushProfileBase
{
    private readonly IAuthenticationService _authenticationService;

    private readonly IEventAggregator _eventAggregator;

    public AuthenticationGrpcService(IAuthenticationService authenticationService, IEventAggregator eventAggregator)
    {
        this._authenticationService = authenticationService;
        this._eventAggregator = eventAggregator;
    }

    public override async Task<SetProfileReply> SetProfile(SetProfileRequest request, ServerCallContext context)
    {
        var profile = this._authenticationService.GetUserProfile(request.Profile.UserPublicSigningAddress);
        
        if (profile == null)
        {
            // sends the UserProfile to MemPool 
            await this._eventAggregator.PublishAsync(new AddTrasactionToMemPoolEvent(
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

            return new SetProfileReply
            {
                Successfull = true,
                ResultType = 1,
                Message = "User Profile added to blockchain"
            };
        }

        // TODO [AboimPinto] here should be update the ProfileName and IsPublic flag
        return new SetProfileReply
        {
            Successfull = false,
            ResultType = 2,
            Message = "User Profile already in the blockchain"
        };
    }

    public override Task<GetProfileReply> GetProfile(GetProfileRequest request, ServerCallContext context)
    {
        var profile = this._authenticationService.GetUserProfile(request.ProfilePublicSigningAddress);

        if (profile == null)
        {
            return Task.FromResult(new GetProfileReply
            {
                Successfull = false,
                Message = "Could not find the profile in Blockchain"
            });
        }

        return Task.FromResult(new GetProfileReply
        {
            UserName = profile.UserName,
            ProfilePublicSigningAddress = profile.UserPublicSigningAddress,
            ProfilePublicEncryptAddress = profile.UserPublicEncryptAddress,
            Successfull = true,
            Message = string.Empty
        });
    }

    public override Task<ProfileExistsReply> ProfileExists(ProfileExistsRequest request, ServerCallContext context)
    {
        var profile = this._authenticationService.GetUserProfile(request.ProfilePublicKey);

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
        var profile = this._authenticationService.GetUserProfile(request.ProfilePublicKey);

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
                UserPublicSigningAddress = profile.UserPublicSigningAddress,
                UserPublicEncryptAddress = profile.UserPublicEncryptAddress,
                UserName = profile.UserName,
                IsPublic = profile.IsPublic
            }
        });
    }

    public override Task<SearchProfileReply> SearchProfileByPublicKey(SearchProfileRequest request, ServerCallContext context)
    {
        var profile = this._authenticationService.GetUserProfile(request.ProfilePublicKey);

        var reply = new SearchProfileReply();

        reply.SeachedProfiles.Add(new SearchProfileReply.Types.SeachedProfile
        {
            UserName = profile.UserName,
            UserPublicSigningAddress = profile.UserPublicSigningAddress,
            UserPublicEncryptAddress = profile.UserPublicEncryptAddress
        });

        return Task.FromResult(reply);
    }
}
