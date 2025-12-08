using Microsoft.Extensions.Options;
using Olimpo;
using HushNode.Credentials;
using HushNode.Feeds.Events;
using HushNode.Feeds.Storage;
using HushNode.MemPool;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class FeedsInitializationWorkflow(
    IFeedsStorageService feedsStorageService,
    IOptions<CredentialsProfile> credentialsProfileOptions,
    IMemPoolService memPoolService,
    IEventAggregator eventAggregator) : IFeedsInitializationWorkflow
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IOptions<CredentialsProfile> _credentialsProfileOptions = credentialsProfileOptions;
    private readonly IMemPoolService _memPoolService = memPoolService;
    private readonly IEventAggregator _eventAggregator = eventAggregator;

    public async Task Initialize()
    {
        var hasPersonalFeed = await this._feedsStorageService
            .HasPersonalFeed(this._credentialsProfileOptions.Value.PublicSigningAddress);

        if (!hasPersonalFeed)
        {
            this.CretatePersonalFeedForLocalUser();
        }

        await this._eventAggregator.PublishAsync(new FeedsInitializedEvent());
    }

    private void CretatePersonalFeedForLocalUser()
    {
        // Generate AES key for the personal feed and encrypt it with the user's RSA public key
        var feedAesKey = EncryptKeys.GenerateAesKey();
        var encryptedFeedKey = EncryptKeys.Encrypt(
            feedAesKey,
            this._credentialsProfileOptions.Value.PublicEncryptAddress);

        var validatedTransaction = NewPersonalFeedPayloadHandler
            .CreateNewPersonalFeedTransaction(encryptedFeedKey)
            .SignTransactionWithLocalUser()
            .ValidateTransactionWithLocalUser();

        this._memPoolService.AddVerifiedTransaction(validatedTransaction);
    }
}
