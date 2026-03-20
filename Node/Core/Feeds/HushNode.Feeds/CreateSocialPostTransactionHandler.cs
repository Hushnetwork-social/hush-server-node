using HushNode.Caching;
using HushNode.Feeds.gRPC;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Feeds;

public class CreateSocialPostTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache,
    ISocialPostNotificationService socialPostNotificationService,
    IAttachmentTempStorageService attachmentTempStorageService,
    IAttachmentStorageService attachmentStorageService,
    ILogger<CreateSocialPostTransactionHandler> logger)
    : ICreateSocialPostTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ISocialPostNotificationService _socialPostNotificationService = socialPostNotificationService;
    private readonly IAttachmentTempStorageService _attachmentTempStorageService = attachmentTempStorageService;
    private readonly IAttachmentStorageService _attachmentStorageService = attachmentStorageService;
    private readonly ILogger<CreateSocialPostTransactionHandler> _logger = logger;

    public async Task HandleCreateSocialPostTransactionAsync(ValidatedTransaction<CreateSocialPostPayload> transaction)
    {
        var payload = transaction.Payload;
        var authorAddress = payload.AuthorPublicAddress.Trim();
        var currentBlock = this._blockchainCache.LastBlockIndex;

        var normalizedCircleIds = payload.Audience.CircleFeedIds
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var socialPost = new SocialPostEntity
        {
            PostId = payload.PostId,
            ReactionScopeId = payload.ReactionScopeId == Guid.Empty ? payload.PostId : payload.ReactionScopeId,
            AuthorPublicAddress = authorAddress,
            AuthorCommitment = payload.AuthorCommitment,
            Content = payload.Content ?? string.Empty,
            AudienceVisibility = payload.Audience.Visibility,
            CreatedAtBlock = currentBlock,
            CreatedAtUnixMs = payload.CreatedAtUnixMs > 0
                ? payload.CreatedAtUnixMs
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        foreach (var circleFeedIdRaw in normalizedCircleIds)
        {
            var circleFeedId = new FeedId(Guid.Parse(circleFeedIdRaw));
            socialPost.AudienceCircles.Add(new SocialPostAudienceCircleEntity
            {
                PostId = payload.PostId,
                CircleFeedId = circleFeedId,
                Post = socialPost
            });
        }

        await this._feedsStorageService.CreateSocialPostAsync(socialPost);

        if (payload.Attachments is { Length: > 0 })
        {
            var postMessageId = new FeedMessageId(payload.PostId);
            foreach (var attachmentRef in payload.Attachments)
            {
                try
                {
                    var tempBlob = await this._attachmentTempStorageService.RetrieveAsync(attachmentRef.AttachmentId);
                    if (tempBlob?.EncryptedOriginal == null)
                    {
                        _logger.LogWarning(
                            "Temp blob missing for social attachment {AttachmentId} on post {PostId}.",
                            attachmentRef.AttachmentId,
                            payload.PostId);
                        continue;
                    }

                    var entity = new AttachmentEntity(
                        Id: attachmentRef.AttachmentId,
                        EncryptedOriginal: tempBlob.Value.EncryptedOriginal,
                        EncryptedThumbnail: tempBlob.Value.EncryptedThumbnail,
                        FeedMessageId: postMessageId,
                        OriginalSize: attachmentRef.Size,
                        ThumbnailSize: 0,
                        MimeType: attachmentRef.MimeType,
                        FileName: attachmentRef.FileName,
                        Hash: attachmentRef.Hash,
                        CreatedAt: DateTime.UtcNow);

                    await this._attachmentStorageService.CreateAttachmentAsync(entity);
                    await this._attachmentTempStorageService.DeleteAsync(attachmentRef.AttachmentId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to persist social attachment {AttachmentId} for post {PostId}.",
                        attachmentRef.AttachmentId,
                        payload.PostId);
                }
            }
        }

        await this._socialPostNotificationService.NotifyPostCreatedAsync(socialPost.PostId);

        this._logger.LogInformation(
            "social_post.create.indexed postId={PostId} author={Author} visibility={Visibility} circles={CircleCount}",
            socialPost.PostId,
            authorAddress,
            payload.Audience.Visibility,
            socialPost.AudienceCircles.Count);
    }
}
