using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Feeds.Storage;

public class FeedsStorageService(
    IUnitOfWorkProvider<FeedsDbContext> unitOfWorkProvider) 
    : IFeedsStorageService
{
    private readonly IUnitOfWorkProvider<FeedsDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public async Task CreateGroupFeed(GroupFeed groupFeed)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .CreateGroupFeed(groupFeed);

        await writableUnitOfWork.CommitAsync();
    }

    public async Task<bool> HasPersonalFeed(string publicSigningAddress) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .HasPersonalFeed(publicSigningAddress);

    public async Task<bool> IsFeedIsBlockchain(FeedId feedId) => 
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .IsFeedInBlockchain(feedId);

    public async Task CreateFeed(Feed feed)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .CreateFeed(feed);

        await writableUnitOfWork.CommitAsync();
    }

    public async Task<bool> CreatePersonalFeedIfNotExists(Feed feed, string publicSigningAddress)
    {
        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Use serializable isolation to prevent race conditions where two transactions
                // both check HasPersonalFeed=false and then both create a feed
                using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable(
                    System.Data.IsolationLevel.Serializable);
                var repository = writableUnitOfWork.GetRepository<IFeedsRepository>();

                // Check within the same serializable transaction
                var hasPersonalFeed = await repository.HasPersonalFeed(publicSigningAddress);
                if (hasPersonalFeed)
                {
                    return false; // Personal feed already exists
                }

                await repository.CreateFeed(feed);
                await writableUnitOfWork.CommitAsync();
                return true;
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "40001") // Serialization failure
            {
                if (attempt == maxRetries)
                    throw; // Rethrow on final attempt

                // Wait a bit before retrying (exponential backoff)
                await Task.Delay(50 * attempt);
            }
        }

        return false; // Should not reach here
    }

    public async Task<IEnumerable<Feed>> RetrieveFeedsForAddress(string publicSigningAddress, BlockIndex blockIndex) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .RetrieveFeedsForAddress(publicSigningAddress, blockIndex);

    public async Task<Feed?> GetFeedByIdAsync(FeedId feedId) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetFeedByIdAsync(feedId);

    public async Task<IReadOnlyList<FeedId>> GetFeedIdsForUserAsync(string publicAddress) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetFeedIdsForUserAsync(publicAddress);

    public async Task UpdateFeedsBlockIndexForParticipantAsync(string publicSigningAddress, BlockIndex blockIndex)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .UpdateFeedsBlockIndexForParticipantAsync(publicSigningAddress, blockIndex);

        await writableUnitOfWork.CommitAsync();
    }

    // ===== Group Feed Admin Operations (FEAT-009) =====

    public async Task<GroupFeed?> GetGroupFeedAsync(FeedId feedId) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetGroupFeedAsync(feedId);

    public async Task<GroupFeedParticipantEntity?> GetGroupFeedParticipantAsync(FeedId feedId, string publicAddress) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetGroupFeedParticipantAsync(feedId, publicAddress);

    public async Task UpdateParticipantTypeAsync(FeedId feedId, string publicAddress, ParticipantType newType)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .UpdateParticipantTypeAsync(feedId, publicAddress, newType);

        await writableUnitOfWork.CommitAsync();
    }

    public async Task<bool> IsAdminAsync(FeedId feedId, string publicAddress) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .IsAdminAsync(feedId, publicAddress);

    public async Task<int> GetAdminCountAsync(FeedId feedId) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetAdminCountAsync(feedId);

    // ===== Group Feed Metadata Operations (FEAT-009 Phase 4) =====

    public async Task UpdateGroupFeedTitleAsync(FeedId feedId, string newTitle)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .UpdateGroupFeedTitleAsync(feedId, newTitle);

        await writableUnitOfWork.CommitAsync();
    }

    public async Task UpdateGroupFeedDescriptionAsync(FeedId feedId, string newDescription)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .UpdateGroupFeedDescriptionAsync(feedId, newDescription);

        await writableUnitOfWork.CommitAsync();
    }

    public async Task MarkGroupFeedDeletedAsync(FeedId feedId)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .MarkGroupFeedDeletedAsync(feedId);

        await writableUnitOfWork.CommitAsync();
    }

    // ===== Key Rotation Operations (FEAT-010) =====

    public async Task<int?> GetMaxKeyGenerationAsync(FeedId feedId) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetMaxKeyGenerationAsync(feedId);

    public async Task<IReadOnlyList<string>> GetActiveGroupMemberAddressesAsync(FeedId feedId) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetActiveGroupMemberAddressesAsync(feedId);

    public async Task CreateKeyRotationAsync(GroupFeedKeyGenerationEntity keyGeneration)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();
        var repository = writableUnitOfWork.GetRepository<IFeedsRepository>();

        // Create the key generation entity with encrypted keys
        await repository.CreateKeyRotationAsync(keyGeneration);

        // Update the group's CurrentKeyGeneration
        await repository.UpdateCurrentKeyGenerationAsync(keyGeneration.FeedId, keyGeneration.KeyGeneration);

        // Commit atomically
        await writableUnitOfWork.CommitAsync();
    }
}
