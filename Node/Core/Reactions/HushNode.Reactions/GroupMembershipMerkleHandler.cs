using Microsoft.Extensions.Logging;
using Olimpo;
using Olimpo.EntityFramework.Persistency;
using HushNode.Feeds.Events;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Reactions;

/// <summary>
/// Handles Group Feed membership change events for Protocol Omega Merkle tree updates.
/// Registers or revokes member commitments when members join/leave/ban/unban.
/// </summary>
public class GroupMembershipMerkleHandler :
    IHandle<MemberJoinedGroupFeedEvent>,
    IHandle<MemberLeftGroupFeedEvent>,
    IHandle<MemberBannedFromGroupFeedEvent>,
    IHandle<MemberUnbannedFromGroupFeedEvent>
{
    private readonly IUnitOfWorkProvider<FeedsDbContext> _feedsUnitOfWorkProvider;
    private readonly IMembershipService _membershipService;
    private readonly IUserCommitmentService _userCommitmentService;
    private readonly ILogger<GroupMembershipMerkleHandler> _logger;

    public GroupMembershipMerkleHandler(
        IUnitOfWorkProvider<FeedsDbContext> feedsUnitOfWorkProvider,
        IMembershipService membershipService,
        IUserCommitmentService userCommitmentService,
        IEventAggregator eventAggregator,
        ILogger<GroupMembershipMerkleHandler> logger)
    {
        _feedsUnitOfWorkProvider = feedsUnitOfWorkProvider;
        _membershipService = membershipService;
        _userCommitmentService = userCommitmentService;
        _logger = logger;

        // Subscribe to membership events
        eventAggregator.Subscribe(this);

        _logger.LogInformation("[GroupMembershipMerkleHandler] Initialized and subscribed to membership events");
    }

    /// <summary>
    /// Handle member joining a group feed.
    /// Generates and registers their commitment in the Merkle tree.
    /// </summary>
    public async void Handle(MemberJoinedGroupFeedEvent message)
    {
        try
        {
            _logger.LogDebug(
                "[GroupMembershipMerkleHandler] Member joined feed {FeedId}: {Address}",
                message.FeedId, message.MemberPublicAddress[..16]);

            // Generate commitment for the member
            // Note: For full implementation, this would use the member's Poseidon commitment
            // For now, we derive a commitment from the member's public address
            var commitment = _userCommitmentService.DeriveCommitmentFromAddress(message.MemberPublicAddress);

            // Register in GroupFeedMemberCommitment table
            using (var unitOfWork = _feedsUnitOfWorkProvider.CreateWritable())
            {
                var commitmentRepo = unitOfWork.GetRepository<IGroupFeedMemberCommitmentRepository>();
                await commitmentRepo.RegisterCommitmentAsync(
                    message.FeedId,
                    commitment,
                    message.KeyGeneration,
                    message.BlockIndex);
                await unitOfWork.CommitAsync();
            }

            // Register in Merkle tree for ZK proofs
            var result = await _membershipService.RegisterCommitmentAsync(message.FeedId, commitment);

            if (result.Success)
            {
                _logger.LogInformation(
                    "[GroupMembershipMerkleHandler] Registered commitment for member in feed {FeedId}, leaf: {LeafIndex}",
                    message.FeedId, result.LeafIndex);
            }
            else if (!result.AlreadyRegistered)
            {
                _logger.LogWarning(
                    "[GroupMembershipMerkleHandler] Failed to register commitment: {Error}",
                    result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[GroupMembershipMerkleHandler] Error handling MemberJoinedGroupFeedEvent for feed {FeedId}",
                message.FeedId);
        }
    }

    /// <summary>
    /// Handle member leaving a group feed.
    /// Revokes their commitment in the Merkle tree.
    /// </summary>
    public async void Handle(MemberLeftGroupFeedEvent message)
    {
        try
        {
            _logger.LogDebug(
                "[GroupMembershipMerkleHandler] Member left feed {FeedId}: {Address}",
                message.FeedId, message.MemberPublicAddress[..16]);

            // Get member's commitment
            var commitment = _userCommitmentService.DeriveCommitmentFromAddress(message.MemberPublicAddress);

            // Revoke in GroupFeedMemberCommitment table
            using (var unitOfWork = _feedsUnitOfWorkProvider.CreateWritable())
            {
                var commitmentRepo = unitOfWork.GetRepository<IGroupFeedMemberCommitmentRepository>();
                await commitmentRepo.RevokeCommitmentAsync(
                    message.FeedId,
                    commitment,
                    message.BlockIndex);
                await unitOfWork.CommitAsync();
            }

            // Update Merkle root (commitment remains in tree but is marked as revoked)
            await _membershipService.UpdateMerkleRootAsync(message.FeedId, message.BlockIndex.Value);

            _logger.LogInformation(
                "[GroupMembershipMerkleHandler] Revoked commitment for member leaving feed {FeedId}",
                message.FeedId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[GroupMembershipMerkleHandler] Error handling MemberLeftGroupFeedEvent for feed {FeedId}",
                message.FeedId);
        }
    }

    /// <summary>
    /// Handle member being banned from a group feed.
    /// Revokes their commitment in the Merkle tree.
    /// </summary>
    public async void Handle(MemberBannedFromGroupFeedEvent message)
    {
        try
        {
            _logger.LogDebug(
                "[GroupMembershipMerkleHandler] Member banned from feed {FeedId}: {Address}",
                message.FeedId, message.MemberPublicAddress[..16]);

            // Get member's commitment
            var commitment = _userCommitmentService.DeriveCommitmentFromAddress(message.MemberPublicAddress);

            // Revoke in GroupFeedMemberCommitment table
            using (var unitOfWork = _feedsUnitOfWorkProvider.CreateWritable())
            {
                var commitmentRepo = unitOfWork.GetRepository<IGroupFeedMemberCommitmentRepository>();
                await commitmentRepo.RevokeCommitmentAsync(
                    message.FeedId,
                    commitment,
                    message.BlockIndex);
                await unitOfWork.CommitAsync();
            }

            // Update Merkle root
            await _membershipService.UpdateMerkleRootAsync(message.FeedId, message.BlockIndex.Value);

            _logger.LogInformation(
                "[GroupMembershipMerkleHandler] Revoked commitment for banned member in feed {FeedId}",
                message.FeedId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[GroupMembershipMerkleHandler] Error handling MemberBannedFromGroupFeedEvent for feed {FeedId}",
                message.FeedId);
        }
    }

    /// <summary>
    /// Handle member being unbanned from a group feed.
    /// Registers a new commitment in the Merkle tree.
    /// </summary>
    public async void Handle(MemberUnbannedFromGroupFeedEvent message)
    {
        try
        {
            _logger.LogDebug(
                "[GroupMembershipMerkleHandler] Member unbanned from feed {FeedId}: {Address}",
                message.FeedId, message.MemberPublicAddress[..16]);

            // Generate new commitment for the unbanned member
            var commitment = _userCommitmentService.DeriveCommitmentFromAddress(message.MemberPublicAddress);

            // Register new commitment in GroupFeedMemberCommitment table
            using (var unitOfWork = _feedsUnitOfWorkProvider.CreateWritable())
            {
                var commitmentRepo = unitOfWork.GetRepository<IGroupFeedMemberCommitmentRepository>();
                await commitmentRepo.RegisterCommitmentAsync(
                    message.FeedId,
                    commitment,
                    message.KeyGeneration,
                    message.BlockIndex);
                await unitOfWork.CommitAsync();
            }

            // Register in Merkle tree
            var result = await _membershipService.RegisterCommitmentAsync(message.FeedId, commitment);

            if (result.Success)
            {
                _logger.LogInformation(
                    "[GroupMembershipMerkleHandler] Registered commitment for unbanned member in feed {FeedId}, leaf: {LeafIndex}",
                    message.FeedId, result.LeafIndex);
            }
            else if (!result.AlreadyRegistered)
            {
                _logger.LogWarning(
                    "[GroupMembershipMerkleHandler] Failed to register unbanned member commitment: {Error}",
                    result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[GroupMembershipMerkleHandler] Error handling MemberUnbannedFromGroupFeedEvent for feed {FeedId}",
                message.FeedId);
        }
    }
}
