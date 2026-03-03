using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;

namespace HushNode.Feeds;

public class CreateInnerCircleContentHandler(
    ICredentialsProvider credentialProvider,
    IFeedsStorageService feedsStorageService,
    IIdentityStorageService identityStorageService)
    : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;

    public bool CanValidate(Guid transactionKind) =>
        CreateInnerCirclePayloadHandler.CreateInnerCirclePayloadKind == transactionKind;

    public AbstractTransaction ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<CreateInnerCirclePayload>;
        if (signedTransaction == null)
        {
            return null!;
        }

        var payload = signedTransaction.Payload;
        var ownerAddress = payload.OwnerPublicAddress;
        var signatoryAddress = signedTransaction.UserSignature?.Signatory;

        if (string.IsNullOrWhiteSpace(ownerAddress))
        {
            return null!;
        }

        if (string.IsNullOrWhiteSpace(signatoryAddress) || signatoryAddress != ownerAddress)
        {
            return null!;
        }

        var ownerIdentity = this._identityStorageService.RetrieveIdentityAsync(ownerAddress).GetAwaiter().GetResult();
        if (ownerIdentity is not Profile ownerProfile || string.IsNullOrWhiteSpace(ownerProfile.PublicEncryptAddress))
        {
            return null!;
        }

        var alreadyExists = this._feedsStorageService.OwnerHasInnerCircleAsync(ownerAddress).GetAwaiter().GetResult();
        if (alreadyExists)
        {
            return null!;
        }

        var blockProducerCredentials = this._credentialProvider.GetCredentials();
        return signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);
    }
}
