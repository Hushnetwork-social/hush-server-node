using Microsoft.Extensions.Options;
using HushShared.Blockchain;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushNode.Credentials;

public class CredentialsProvider : ICredentialsProvider
{
    private static CredentialsProvider _instance;
    private readonly CredentialsProfile _credentialsProfile;

    internal static CredentialsProvider Instance { get => _instance; }

    public CredentialsProvider(IOptions<CredentialsProfile> credentials)
    {
        this._credentialsProfile = credentials.Value;

        _instance ??= this;
    }

    public CredentialsProfile GetCredentials() => this._credentialsProfile;
}

public static class SignerValidatorTransactionHandler 
{
    public static SignedTransaction<T> SignTransactionWithLocalUser<T>(this UnsignedTransaction<T> transaction)
        where T : ITransactionPayloadKind
    {
        var localCredendials = CredentialsProvider.Instance.GetCredentials();

        return transaction.SignByUser(localCredendials.PublicSigningAddress, localCredendials.PrivateSigningKey);
    }

    public static ValidatedTransaction<T> ValidateTransactionWithLocalUser<T>(this SignedTransaction<T> transaction)
        where T : ITransactionPayloadKind
    {
        var localCredendials = CredentialsProvider.Instance.GetCredentials();

        return transaction.SignByValidator(localCredendials.PublicSigningAddress, localCredendials.PrivateSigningKey);
    }
}