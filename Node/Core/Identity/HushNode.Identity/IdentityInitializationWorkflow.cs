using Microsoft.Extensions.Options;
using Olimpo;
using HushNode.Credentials;
using HushNode.Identity.Events;
using HushNode.Identity.Storage;
using HushNode.MemPool;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public class IdentityInitializationWorkflow(
    IIdentityStorageService identityStorageService,
    IOptions<CredentialsProfile> credentialsProfileOptions,
    IMemPoolService memPoolService,
    IEventAggregator eventAggregator) : IIdentityInitializationWorkflow
{
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;
    private readonly IOptions<CredentialsProfile> _credentialsProfileOptions = credentialsProfileOptions;
    private readonly IMemPoolService _memPoolService = memPoolService;
    private readonly IEventAggregator _eventAggregator = eventAggregator;

    public async Task Initialize()
    {
        var profileBase = await this._identityStorageService
            .RetrieveIdentityAsync(this._credentialsProfileOptions.Value.PublicSigningAddress);

        if (profileBase is NonExistingProfile)
        {
            var stakerFullIdentityTransaction = FullIdentityPayloadHandler.CreateNew(
                this._credentialsProfileOptions.Value.ProfileName,
                this._credentialsProfileOptions.Value.PublicSigningAddress,
                this._credentialsProfileOptions.Value.PublicEncryptAddress,
                this._credentialsProfileOptions.Value.IsPublic);

            var stakerFullIdentityTransactionValidated = stakerFullIdentityTransaction
                .SignTransactionWithLocalUser()
                .ValidateTransactionWithLocalUser();

            
            this._memPoolService.AddVerifiedTransaction(stakerFullIdentityTransactionValidated);
        }
        else
        {
            // Identity is already in the Blockchain. DO NOTHING
        }

        await this._eventAggregator.PublishAsync(new IdentityInitializedEvent());
    }
}
