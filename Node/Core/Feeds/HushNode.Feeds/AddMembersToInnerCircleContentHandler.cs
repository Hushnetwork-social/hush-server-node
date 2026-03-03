using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;

namespace HushNode.Feeds;

public class AddMembersToInnerCircleContentHandler(
    ICredentialsProvider credentialProvider,
    IFeedsStorageService feedsStorageService,
    IIdentityStorageService identityStorageService)
    : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;

    private const int MaxMembersPerTransaction = 100;

    public bool CanValidate(Guid transactionKind) =>
        AddMembersToInnerCirclePayloadHandler.AddMembersToInnerCirclePayloadKind == transactionKind;

    public AbstractTransaction ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<AddMembersToInnerCirclePayload>;
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

        if (payload.Members == null || payload.Members.Length == 0 || payload.Members.Length > MaxMembersPerTransaction)
        {
            return null!;
        }

        var innerCircle = this._feedsStorageService.GetInnerCircleByOwnerAsync(ownerAddress).GetAwaiter().GetResult();
        if (innerCircle == null || innerCircle.IsDeleted)
        {
            return null!;
        }

        var normalizedPayloadAddresses = payload.Members
            .Select(m => m.PublicAddress?.Trim())
            .ToList();

        if (normalizedPayloadAddresses.Any(string.IsNullOrWhiteSpace))
        {
            return null!;
        }

        var duplicateInPayload = normalizedPayloadAddresses
            .GroupBy(x => x!, StringComparer.Ordinal)
            .Any(g => g.Count() > 1);
        if (duplicateInPayload)
        {
            return null!;
        }

        foreach (var member in payload.Members)
        {
            if (string.IsNullOrWhiteSpace(member.PublicEncryptAddress))
            {
                return null!;
            }

            var existingParticipant = this._feedsStorageService
                .GetParticipantWithHistoryAsync(innerCircle.FeedId, member.PublicAddress)
                .GetAwaiter().GetResult();

            if (existingParticipant != null && existingParticipant.LeftAtBlock == null)
            {
                return null!;
            }

            var identity = this._identityStorageService.RetrieveIdentityAsync(member.PublicAddress).GetAwaiter().GetResult();
            if (identity is not Profile profile || string.IsNullOrWhiteSpace(profile.PublicEncryptAddress))
            {
                return null!;
            }

            if (!string.Equals(profile.PublicEncryptAddress, member.PublicEncryptAddress, StringComparison.Ordinal))
            {
                return null!;
            }
        }

        var blockProducerCredentials = this._credentialProvider.GetCredentials();
        return signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);
    }
}
