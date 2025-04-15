using HushNode.Credentials;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class NewPersonalFeedContentHandler(
    ICredentialsProvider credentialProvider) : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;

    public bool CanValidate(Guid transactionKind) => 
        NewPersonalFeedPayloadHandler.NewPersonalFeedPayloadKind == transactionKind;

    public AbstractTransaction ValidateAndSign(AbstractTransaction transaction)
    {
        var newPersonalFeedPayload = transaction as SignedTransaction<NewPersonalFeedPayload>;

        if (newPersonalFeedPayload == null)
        {
            // throw new InvalidOperationException("Something went wrong and should never reach here. Some of the previous validation failed.");
            return null;    // <-- TODO [AboimPinto] Don't like to return null but for now works.
        }

        var blockProducerCredentials = this._credentialProvider.GetCredentials();

        var signedByValidationTransaction = newPersonalFeedPayload.SignByValidator(
            blockProducerCredentials.PublicSigningAddress, 
            blockProducerCredentials.PrivateSigningKey);

        return signedByValidationTransaction;
    }
}
