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
        using var readonlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();
        var identityExists = await readonlyUnitOfWork
            .GetRepository<IIdentityRepository>()
            .AnyAsync(transaction.Payload.PublicSigningAddress);

        if(identityExists)
        {
            // TODO [AboimPinto] should not exist. 
            // If the identity exists, should never receive a transaction with FullIdentity, but, only field updates
            throw new InvalidOperationException("At this point the system should already have a identity in the Index database");
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
