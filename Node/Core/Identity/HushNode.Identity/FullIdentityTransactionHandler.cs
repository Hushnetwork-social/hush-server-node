using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Olimpo;
using Olimpo.EntityFramework.Persistency;
using HushNode.Caching;
using HushNode.Events;
using HushNode.Identity.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public class FullIdentityTransactionHandler(
    IUnitOfWorkProvider<IdentityDbContext> unitOfWorkProvider,
    IBlockchainCache blockchainCache,
    IEventAggregator eventAggregator,
    ILogger<FullIdentityTransactionHandler> logger)
    : IFullIdentityTransactionHandler
{
    private readonly IUnitOfWorkProvider<IdentityDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IEventAggregator _eventAggregator = eventAggregator;
    private readonly ILogger<FullIdentityTransactionHandler> _logger = logger;

    public async Task HandleFullIdentityTransaction(ValidatedTransaction<FullIdentityPayload> transaction)
    {
        if (string.IsNullOrWhiteSpace(transaction.Payload.IdentityAlias))
        {
            this._logger.LogWarning("Rejecting FullIdentity transaction: alias is null or empty. Kind: {PayloadKind}",
                transaction.PayloadKind);
            return;
        }

        if (string.IsNullOrWhiteSpace(transaction.Payload.PublicSigningAddress))
        {
            this._logger.LogWarning("Rejecting FullIdentity transaction: signing address is null or empty. Kind: {PayloadKind}",
                transaction.PayloadKind);
            return;
        }

        if (string.IsNullOrWhiteSpace(transaction.Payload.PublicEncryptAddress))
        {
            this._logger.LogWarning("Rejecting FullIdentity transaction: encrypt address is null or empty. Kind: {PayloadKind}",
                transaction.PayloadKind);
            return;
        }

        using var readonlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();
        var identityExists = await readonlyUnitOfWork
            .GetRepository<IIdentityRepository>()
            .AnyAsync(transaction.Payload.PublicSigningAddress);

        if (identityExists)
        {
            this._logger.LogDebug("Skipping FullIdentity transaction: identity already indexed for {TruncatedAddress}",
                TruncateAddress(transaction.Payload.PublicSigningAddress));
            return;
        }

        await this.InsertFullIdentity(transaction);
    }

    private async Task InsertFullIdentity(ValidatedTransaction<FullIdentityPayload> transaction)
    {
        var signingAddress = transaction.Payload.PublicSigningAddress;
        var profile = new Profile(
            transaction.Payload.IdentityAlias,
            string.Empty,
            signingAddress,
            transaction.Payload.PublicEncryptAddress,
            transaction.Payload.IsPublic,
            this._blockchainCache.LastBlockIndex);

        try
        {
            using (var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable())
            {
                await writableUnitOfWork
                    .GetRepository<IIdentityRepository>()
                    .AddFullIdentity(profile);

                await writableUnitOfWork.CommitAsync();
            }

            // FEAT-011: publish cache coherence AFTER the commit so no stale
            // success/absence is ever served post-index (established
            // IdentityUpdatedEvent invalidation pattern).
            await this._eventAggregator.PublishAsync(new IdentityUpdatedEvent(signingAddress));

            this._logger.LogInformation("Full identity indexed. TruncatedAddress: {TruncatedAddress}",
                TruncateAddress(signingAddress));
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Race condition: another request inserted the same identity between
            // our check and insert. Typed unique-violation detection (Postgres
            // SqlState 23505) — never stringly exception inspection. Gracefully
            // converge: the existing profile is authoritative.
            this._logger.LogDebug("FullIdentity duplicate insert converged for {TruncatedAddress}",
                TruncateAddress(signingAddress));
        }
    }

    /// <summary>Typed duplicate detection: Postgres unique_violation (23505).</summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" };

    /// <summary>Non-identifying diagnostics: first 8 characters of the address only.</summary>
    private static string TruncateAddress(string address) =>
        address.Length <= 8 ? address : address[..8];
}
