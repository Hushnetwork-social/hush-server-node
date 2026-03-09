using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Olimpo;

namespace HushNode.Feeds;

public class CreateSocialPostContentHandler(
    ICredentialsProvider credentialProvider,
    IFeedsStorageService feedsStorageService,
    IIdentityStorageService identityStorageService)
    : ITransactionContentHandler, IAsyncTransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;

    public bool CanValidate(Guid transactionKind) =>
        CreateSocialPostPayloadHandler.CreateSocialPostPayloadKind == transactionKind;

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction) =>
        this.ValidateAndSignAsync(transaction).GetAwaiter().GetResult();

    public async Task<AbstractTransaction?> ValidateAndSignAsync(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<CreateSocialPostPayload>;
        if (signedTransaction == null)
        {
            return null;
        }

        var payload = signedTransaction.Payload;
        var signatoryAddress = signedTransaction.UserSignature?.Signatory;
        var authorAddress = payload.AuthorPublicAddress.Trim();

        if (string.IsNullOrWhiteSpace(authorAddress) || string.IsNullOrWhiteSpace(signatoryAddress))
        {
            return null;
        }

        if (!string.Equals(signatoryAddress, authorAddress, StringComparison.Ordinal))
        {
            return null;
        }

        if (payload.AuthorCommitment != null && payload.AuthorCommitment.Length != 32)
        {
            return null;
        }

        // Ensure author identity exists.
        var authorIdentity = await this._identityStorageService.RetrieveIdentityAsync(authorAddress);
        if (authorIdentity is not Profile)
        {
            return null;
        }

        var audienceValidation = SocialPostContractRules.ValidateAudience(payload.Audience);
        if (!audienceValidation.IsValid)
        {
            return null;
        }

        var attachmentValidation = SocialPostContractRules.ValidateAttachments(payload.Attachments);
        if (!attachmentValidation.IsValid)
        {
            return null;
        }

        if (payload.Audience.Visibility == SocialPostVisibility.Private)
        {
            var normalizedCircleIds = payload.Audience.CircleFeedIds
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var circleFeedIdRaw in normalizedCircleIds)
            {
                if (!Guid.TryParse(circleFeedIdRaw, out var circleGuid))
                {
                    return null;
                }

                var circleFeed = await this._feedsStorageService.GetGroupFeedAsync(new FeedId(circleGuid));
                if (circleFeed == null || circleFeed.IsDeleted)
                {
                    return null;
                }

                if (circleFeed.OwnerPublicAddress != authorAddress)
                {
                    return null;
                }
            }
        }

        var blockProducerCredentials = this._credentialProvider.GetCredentials();
        return signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);
    }
}
