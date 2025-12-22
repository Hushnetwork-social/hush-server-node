using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Feeds.Storage;

public class FeedsStorageService(
    IUnitOfWorkProvider<FeedsDbContext> unitOfWorkProvider) 
    : IFeedsStorageService
{
    private readonly IUnitOfWorkProvider<FeedsDbContext> _unitOfWorkProvider = unitOfWorkProvider;

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
}
