using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushNode.MemPool;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;

namespace HushNode.Feeds;

public class CreateCustomCircleContentHandler(
    ICredentialsProvider credentialProvider,
    IFeedsStorageService feedsStorageService,
    IIdentityStorageService identityStorageService,
    IMemPoolService memPoolService)
    : ITransactionContentHandler, IAsyncTransactionContentHandler
{
    private const int MaxCustomCirclesPerOwner = 20;

    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;
    private readonly IMemPoolService _memPoolService = memPoolService;

    public bool CanValidate(Guid transactionKind) =>
        CreateCustomCirclePayloadHandler.CreateCustomCirclePayloadKind == transactionKind;

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction) =>
        this.ValidateAndSignAsync(transaction).GetAwaiter().GetResult();

    public async Task<AbstractTransaction?> ValidateAndSignAsync(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<CreateCustomCirclePayload>;
        if (signedTransaction == null)
        {
            return null;
        }

        var payload = signedTransaction.Payload;
        var ownerAddress = payload.OwnerPublicAddress;
        var signatoryAddress = signedTransaction.UserSignature?.Signatory;

        if (string.IsNullOrWhiteSpace(ownerAddress) || string.IsNullOrWhiteSpace(signatoryAddress) || signatoryAddress != ownerAddress)
        {
            return null;
        }

        if (!CustomCircleNameRules.TryNormalize(payload.CircleName, out _, out var normalizedCircleName))
        {
            return null;
        }

        var ownerIdentity = await this._identityStorageService.RetrieveIdentityAsync(ownerAddress);
        if (ownerIdentity is not Profile ownerProfile || string.IsNullOrWhiteSpace(ownerProfile.PublicEncryptAddress))
        {
            return null;
        }

        var ownerCircleCount = await this._feedsStorageService.GetCustomCircleCountByOwnerAsync(ownerAddress);
        if (ownerCircleCount >= MaxCustomCirclesPerOwner)
        {
            return null;
        }

        var alreadyExists = await this._feedsStorageService.OwnerHasCustomCircleNamedAsync(ownerAddress, normalizedCircleName);
        if (alreadyExists)
        {
            return null;
        }

        var hasPendingCreateForOwnerAndName = (this._memPoolService.PeekPendingValidatedTransactions() ?? Enumerable.Empty<AbstractTransaction>())
            .OfType<SignedTransaction<CreateCustomCirclePayload>>()
            .Any(x =>
                string.Equals(x.Payload.OwnerPublicAddress, ownerAddress, StringComparison.Ordinal) &&
                CustomCircleNameRules.TryNormalize(x.Payload.CircleName, out _, out var pendingNormalizedName) &&
                string.Equals(pendingNormalizedName, normalizedCircleName, StringComparison.Ordinal));

        if (hasPendingCreateForOwnerAndName)
        {
            return null;
        }

        var blockProducerCredentials = this._credentialProvider.GetCredentials();
        return signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);
    }
}
