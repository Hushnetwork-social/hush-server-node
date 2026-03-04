using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;

namespace HushNode.Feeds;

public class AddMembersToCustomCircleContentHandler(
    ICredentialsProvider credentialProvider,
    IFeedsStorageService feedsStorageService,
    IIdentityStorageService identityStorageService)
    : ITransactionContentHandler, IAsyncTransactionContentHandler
{
    private const int MaxMembersPerTransaction = 100;

    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;

    public bool CanValidate(Guid transactionKind) =>
        AddMembersToCustomCirclePayloadHandler.AddMembersToCustomCirclePayloadKind == transactionKind;

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction) =>
        this.ValidateAndSignAsync(transaction).GetAwaiter().GetResult();

    public async Task<AbstractTransaction?> ValidateAndSignAsync(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<AddMembersToCustomCirclePayload>;
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

        if (payload.Members == null || payload.Members.Length == 0 || payload.Members.Length > MaxMembersPerTransaction)
        {
            return null;
        }

        var targetCircle = await this._feedsStorageService.GetGroupFeedAsync(payload.FeedId);
        if (targetCircle == null || targetCircle.IsDeleted || targetCircle.IsInnerCircle || targetCircle.OwnerPublicAddress != ownerAddress)
        {
            return null;
        }

        var normalizedPayloadAddresses = payload.Members
            .Select(m => m.PublicAddress?.Trim())
            .ToList();

        if (normalizedPayloadAddresses.Any(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        var duplicateInPayload = normalizedPayloadAddresses
            .GroupBy(x => x!, StringComparer.Ordinal)
            .Any(g => g.Count() > 1);
        if (duplicateInPayload)
        {
            return null;
        }

        foreach (var member in payload.Members)
        {
            if (string.IsNullOrWhiteSpace(member.PublicEncryptAddress))
            {
                return null;
            }

            var isFollowedByOwner = await this._feedsStorageService.OwnerHasChatFeedWithMemberAsync(ownerAddress, member.PublicAddress);
            if (!isFollowedByOwner)
            {
                return null;
            }

            var existingParticipant = await this._feedsStorageService.GetParticipantWithHistoryAsync(payload.FeedId, member.PublicAddress);
            if (existingParticipant != null && existingParticipant.LeftAtBlock == null)
            {
                return null;
            }

            var identity = await this._identityStorageService.RetrieveIdentityAsync(member.PublicAddress);
            if (identity is not Profile profile || string.IsNullOrWhiteSpace(profile.PublicEncryptAddress))
            {
                return null;
            }

            if (!string.Equals(profile.PublicEncryptAddress, member.PublicEncryptAddress, StringComparison.Ordinal))
            {
                return null;
            }
        }

        var blockProducerCredentials = this._credentialProvider.GetCredentials();
        return signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);
    }
}
