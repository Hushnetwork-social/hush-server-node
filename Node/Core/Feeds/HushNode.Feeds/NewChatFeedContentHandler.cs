using HushNode.Credentials;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class NewChatFeedContentHandler(
    ICredentialsProvider credentialProvider) 
    : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;

    public bool CanValidate(Guid transactionKind) =>
        NewChatFeedPayloadHandler.NewChatFeedPayloadKind == transactionKind;

    public AbstractTransaction ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<NewChatFeedPayload>;

        if (signedTransaction == null)
        {
            // throw new InvalidOperationException("Something went wrong and should never reach here. Some of the previous validation failed.");
            return null;    // <-- TODO [AboimPinto] Don't like to return null but for now works.
        }

        var blockProducerCredentials = this._credentialProvider.GetCredentials();

        var signedByValidationTransaction = signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress, 
            blockProducerCredentials.PrivateSigningKey);

        return signedByValidationTransaction;
    }
}
