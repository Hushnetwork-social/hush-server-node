using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;
using HushNode.Caching;
using HushNode.Identity.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public class FullIdentityTransactionHandler(
    IUnitOfWorkProvider<IdentityDbContext> unitOfWorkProvider,
    IBlockchainCache blockchainCache,
    ILogger<FullIdentityTransactionHandler> logger) 
    : IFullIdentityTransactionHandler
{
    private readonly IUnitOfWorkProvider<IdentityDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ILogger<FullIdentityTransactionHandler> _logger = logger;

    public async Task HandleFullIdentityTransaction(ValidatedTransaction<FullIdentityPayload> transaction)
    {
        // Validate required fields before processing
        if (string.IsNullOrWhiteSpace(transaction.Payload.IdentityAlias))
        {
            this._logger.LogWarning("Rejecting FullIdentity transaction: IdentityAlias is null or empty. Signatory: {Signatory}",
                transaction.UserSignature.Signatory);
            return;
        }

        if (string.IsNullOrWhiteSpace(transaction.Payload.PublicSigningAddress))
        {
            this._logger.LogWarning("Rejecting FullIdentity transaction: PublicSigningAddress is null or empty. Signatory: {Signatory}",
                transaction.UserSignature.Signatory);
            return;
        }

        if (string.IsNullOrWhiteSpace(transaction.Payload.PublicEncryptAddress))
        {
            this._logger.LogWarning("Rejecting FullIdentity transaction: PublicEncryptAddress is null or empty. Signatory: {Signatory}",
                transaction.UserSignature.Signatory);
            return;
        }

        using var readonlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();
        var identityExists = await readonlyUnitOfWork
            .GetRepository<IIdentityRepository>()
            .AnyAsync(transaction.Payload.PublicSigningAddress);

        if(identityExists)
        {
            // Identity already exists - this can happen if the same transaction is processed twice
            // or if the client retries identity creation. Gracefully skip instead of crashing.
            this._logger.LogWarning("Skipping FullIdentity transaction: Identity already exists for address {Address}",
                transaction.Payload.PublicSigningAddress);
            return;
        }

        await this.InsertFullIdentity(transaction);
    }

    private async Task InsertFullIdentity(ValidatedTransaction<FullIdentityPayload> transaction)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        var profile = new Profile(
            transaction.Payload.IdentityAlias,
            string.Empty,
            transaction.Payload.PublicSigningAddress,
            transaction.Payload.PublicEncryptAddress,
            transaction.Payload.IsPublic,
            this._blockchainCache.LastBlockIndex);

        await writableUnitOfWork
            .GetRepository<IIdentityRepository>()
            .AddFullIdentity(profile);

        await writableUnitOfWork.CommitAsync();

        this._logger.LogInformation($"Full identity added: {profile.Alias} | {profile.PublicSigningAddress}");
    }
}
