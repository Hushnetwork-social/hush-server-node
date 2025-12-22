using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;
using HushNode.Reactions.Crypto;
using HushNode.Reactions.Storage;

namespace HushNode.Reactions;

/// <summary>
/// Service for querying anonymous reactions.
///
/// NOTE: Reaction submission is handled via blockchain transactions.
/// See ReactionTransactionHandler for submission processing.
/// </summary>
public class ReactionService : IReactionService
{
    private readonly IUnitOfWorkProvider<ReactionsDbContext> _unitOfWorkProvider;
    private readonly ILogger<ReactionService> _logger;

    public ReactionService(
        IUnitOfWorkProvider<ReactionsDbContext> unitOfWorkProvider,
        ILogger<ReactionService> logger)
    {
        _unitOfWorkProvider = unitOfWorkProvider;
        _logger = logger;
    }

    public async Task<IEnumerable<MessageReactionTally>> GetTalliesAsync(
        FeedId feedId,
        IEnumerable<FeedMessageId> messageIds)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IReactionsRepository>();
        return await repository.GetTalliesForMessagesAsync(messageIds);
    }

    public async Task<bool> NullifierExistsAsync(byte[] nullifier)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IReactionsRepository>();
        return await repository.NullifierExistsAsync(nullifier);
    }

    public async Task<byte[]?> GetReactionBackupAsync(byte[] nullifier)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IReactionsRepository>();
        var nullifierRecord = await repository.GetNullifierWithBackupAsync(nullifier);
        return nullifierRecord?.EncryptedEmojiBackup;
    }
}

/// <summary>
/// Interface for getting feed information (public key, author commitments).
/// Used by NewReactionContentHandler and ReactionTransactionHandler.
/// </summary>
public interface IFeedInfoProvider
{
    Task<ECPoint?> GetFeedPublicKeyAsync(FeedId feedId);
    Task<byte[]?> GetAuthorCommitmentAsync(FeedMessageId messageId);
}
