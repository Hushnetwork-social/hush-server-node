using System.Threading.Tasks;
using HushNode.Credentials;
using HushNode.Feeds.Events;
using HushNode.Feeds.Storage;
using HushNode.MemPool;
using HushShared.Blockchain;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Options;
using Olimpo;
using Org.BouncyCastle.Crypto;

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
        var validatedTransaction = NewPersonalFeedPayloadHandler
            .CreateNewPersonalFeedTransaction()
            .SignTransactionWithLocalUser()
            .ValidateTransactionWithLocalUser();

        this._memPoolService.AddVerifiedTransaction(validatedTransaction);
    }
}
