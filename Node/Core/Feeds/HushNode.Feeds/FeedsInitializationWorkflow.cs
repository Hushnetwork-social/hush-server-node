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
        // Check if encryption keys are available
        var publicEncryptAddress = this._credentialsProfileOptions.Value.PublicEncryptAddress;
        if (string.IsNullOrEmpty(publicEncryptAddress))
        {
            Console.WriteLine("[Feeds] WARNING: Cannot create personal feed - PublicEncryptAddress is missing from credentials.");
            Console.WriteLine("[Feeds] The stacker credentials file needs to be regenerated with encryption keys.");
            Console.WriteLine("[Feeds] Personal feed creation skipped. Some features may not work correctly.");
            return;
        }

        // Generate AES key for the personal feed and encrypt it with the user's ECIES public key
        var feedAesKey = EncryptKeys.GenerateAesKey();
        var encryptedFeedKey = EncryptKeys.Encrypt(
            feedAesKey,
            publicEncryptAddress);

        var validatedTransaction = NewPersonalFeedPayloadHandler
            .CreateNewPersonalFeedTransaction(encryptedFeedKey)
            .SignTransactionWithLocalUser()
            .ValidateTransactionWithLocalUser();

        this._memPoolService.AddVerifiedTransaction(validatedTransaction);
    }
}
