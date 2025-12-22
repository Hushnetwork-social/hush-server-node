using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Olimpo;
using HushNode.Credentials;
using HushNode.Feeds.Events;
using HushNode.Reactions.Storage;
using HushShared.Feeds.Model;

namespace HushNode.Reactions;

/// <summary>
/// Handles FeedCreatedEvent to automatically register the local server user's
/// commitment when they participate in a new feed.
/// This enables the server user (Stacker) to use anonymous reactions.
/// </summary>
public class FeedCreatedCommitmentHandler : IHandle<FeedCreatedEvent>
{
    private readonly IUserCommitmentService _userCommitmentService;
    private readonly IMembershipService _membershipService;
    private readonly string _localUserAddress;
    private readonly ILogger<FeedCreatedCommitmentHandler> _logger;

    public FeedCreatedCommitmentHandler(
        IUserCommitmentService userCommitmentService,
        IMembershipService membershipService,
        IOptions<CredentialsProfile> credentials,
        IEventAggregator eventAggregator,
        ILogger<FeedCreatedCommitmentHandler> logger)
    {
        _userCommitmentService = userCommitmentService;
        _membershipService = membershipService;
        _localUserAddress = credentials.Value.PublicSigningAddress;
        _logger = logger;

        // Subscribe to feed created events
        eventAggregator.Subscribe(this);

        _logger.LogInformation("[FeedCreatedCommitmentHandler] Initialized for user {Address}", _localUserAddress[..16]);
    }

    public async void Handle(FeedCreatedEvent message)
    {
        try
        {
            // Check if the local server user is a participant in this feed
            var isParticipant = message.ParticipantPublicAddresses.Contains(_localUserAddress);

            if (!isParticipant)
            {
                _logger.LogDebug("[FeedCreatedCommitmentHandler] Local user not a participant in feed {FeedId}, skipping", message.FeedId);
                return;
            }

            // Get the local user's commitment and register it
            var commitment = _userCommitmentService.GetLocalUserCommitment();
            var result = await _membershipService.RegisterCommitmentAsync(message.FeedId, commitment);

            if (result.Success)
            {
                _logger.LogInformation(
                    "[FeedCreatedCommitmentHandler] Registered commitment for feed {FeedId}, leaf index: {LeafIndex}",
                    message.FeedId, result.LeafIndex);
            }
            else if (result.AlreadyRegistered)
            {
                _logger.LogDebug(
                    "[FeedCreatedCommitmentHandler] Commitment already registered for feed {FeedId}",
                    message.FeedId);
            }
            else
            {
                _logger.LogWarning(
                    "[FeedCreatedCommitmentHandler] Failed to register commitment for feed {FeedId}: {Error}",
                    message.FeedId, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FeedCreatedCommitmentHandler] Error handling FeedCreatedEvent for feed {FeedId}", message.FeedId);
        }
    }
}
