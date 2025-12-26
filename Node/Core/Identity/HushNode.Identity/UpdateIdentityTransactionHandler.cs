using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;
using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public class UpdateIdentityTransactionHandler(
    IUnitOfWorkProvider<IdentityDbContext> unitOfWorkProvider,
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache,
    ILogger<UpdateIdentityTransactionHandler> logger)
    : IUpdateIdentityTransactionHandler
{
    private readonly IUnitOfWorkProvider<IdentityDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ILogger<UpdateIdentityTransactionHandler> _logger = logger;

    public async Task HandleUpdateIdentityTransaction(ValidatedTransaction<UpdateIdentityPayload> transaction)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(transaction.Payload.NewAlias))
        {
            this._logger.LogWarning("Rejecting UpdateIdentity transaction: NewAlias is null or empty. Signatory: {Signatory}",
                transaction.UserSignature.Signatory);
            return;
        }

        // The signatory is the identity owner - use their public signing address
        var publicSigningAddress = transaction.UserSignature.Signatory;

        // Verify identity exists
        using var readonlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();
        var identityExists = await readonlyUnitOfWork
            .GetRepository<IIdentityRepository>()
            .AnyAsync(publicSigningAddress);

        if (!identityExists)
        {
            this._logger.LogWarning("Rejecting UpdateIdentity transaction: Identity does not exist for address {Address}",
                publicSigningAddress);
            return;
        }

        await this.UpdateIdentityAlias(publicSigningAddress, transaction.Payload.NewAlias);
    }

    private async Task UpdateIdentityAlias(string publicSigningAddress, string newAlias)
    {
        var blockIndex = this._blockchainCache.LastBlockIndex;

        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IIdentityRepository>()
            .UpdateAliasAsync(publicSigningAddress, newAlias, blockIndex);

        await writableUnitOfWork.CommitAsync();

        // Update BlockIndex on all feeds where this user is a participant
        // This ensures other clients can detect the identity change via feed sync
        await this._feedsStorageService.UpdateFeedsBlockIndexForParticipantAsync(publicSigningAddress, blockIndex);

        this._logger.LogInformation("Identity alias updated: {Address} -> {NewAlias} (feeds updated)", publicSigningAddress, newAlias);
    }
}
