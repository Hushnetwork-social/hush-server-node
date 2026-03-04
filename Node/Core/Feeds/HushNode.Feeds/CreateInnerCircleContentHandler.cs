using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushNode.MemPool;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;

namespace HushNode.Feeds;

public class CreateInnerCircleContentHandler(
    ICredentialsProvider credentialProvider,
    IFeedsStorageService feedsStorageService,
    IIdentityStorageService identityStorageService,
    IMemPoolService memPoolService)
    : ITransactionContentHandler, IAsyncTransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;
    private readonly IMemPoolService _memPoolService = memPoolService;

    public bool CanValidate(Guid transactionKind) =>
        CreateInnerCirclePayloadHandler.CreateInnerCirclePayloadKind == transactionKind;

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction) =>
        this.ValidateAndSignAsync(transaction).GetAwaiter().GetResult();

    public async Task<AbstractTransaction?> ValidateAndSignAsync(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<CreateInnerCirclePayload>;
        if (signedTransaction == null)
        {
            return null;
        }

        var payload = signedTransaction.Payload;
        var ownerAddress = payload.OwnerPublicAddress;
        var signatoryAddress = signedTransaction.UserSignature?.Signatory;

        if (string.IsNullOrWhiteSpace(ownerAddress))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(signatoryAddress) || signatoryAddress != ownerAddress)
        {
            return null;
        }

        var ownerIdentity = await this._identityStorageService.RetrieveIdentityAsync(ownerAddress);
        if (ownerIdentity is not Profile ownerProfile || string.IsNullOrWhiteSpace(ownerProfile.PublicEncryptAddress))
        {
            return null;
        }

        var alreadyExists = await this._feedsStorageService.OwnerHasInnerCircleAsync(ownerAddress);
        if (alreadyExists)
        {
            return null;
        }

        // Prevent same-owner duplicate create requests while the first one is still pending in mempool.
        var hasPendingCreateForOwner = (this._memPoolService.PeekPendingValidatedTransactions() ?? Enumerable.Empty<AbstractTransaction>())
            .OfType<SignedTransaction<CreateInnerCirclePayload>>()
            .Any(x => string.Equals(x.Payload.OwnerPublicAddress, ownerAddress, StringComparison.Ordinal));

        if (hasPendingCreateForOwner)
        {
            return null;
        }

        var blockProducerCredentials = this._credentialProvider.GetCredentials();
        return signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);
    }
}
