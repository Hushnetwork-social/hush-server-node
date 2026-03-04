using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

public enum SocialPostVisibility
{
    Open = 0,
    Private = 1
}

public enum SocialPostAttachmentKind
{
    Image = 0,
    Video = 1
}

public enum SocialPostContractErrorCode
{
    None = 0,
    PrivateAudienceRequiresAtLeastOneCircle = 1,
    DuplicateCircleTargets = 2,
    AttachmentCountExceeded = 3,
    AttachmentSizeExceeded = 4,
    AttachmentMimeTypeNotAllowed = 5
}

public enum SocialPermalinkAccessState
{
    Allowed = 0,
    GuestDenied = 1,
    UnauthorizedDenied = 2,
    NotFound = 3
}

public record SocialPostAttachment(
    string AttachmentId,
    string MimeType,
    long Size,
    string FileName,
    string Hash,
    SocialPostAttachmentKind Kind);

public record SocialPostAudience(
    SocialPostVisibility Visibility,
    string[] CircleFeedIds);

public record SocialPostOpenGraphContract(
    string Title,
    string Description,
    string? ImageUrl,
    bool IsGenericPrivate,
    string CacheControl);

public record SocialPostPermalinkContract(
    Guid PostId,
    SocialPermalinkAccessState AccessState,
    bool IsAuthenticated,
    bool CanInteract,
    string? AuthorPublicAddress,
    string? Content,
    long? CreatedAtBlock,
    string[] CircleFeedIds,
    SocialPostOpenGraphContract OpenGraph);

public record CreateSocialPostPayload(
    Guid PostId,
    string AuthorPublicAddress,
    string Content,
    SocialPostAudience Audience,
    SocialPostAttachment[] Attachments,
    long CreatedAtUnixMs) : ITransactionPayloadKind;

public static class CreateSocialPostPayloadHandler
{
    public static Guid CreateSocialPostPayloadKind { get; } = Guid.Parse("7d6f3fe6-d108-4f73-9d6f-cc76f8b53a4e");
}

public static class SocialPostContractRules
{
    public const int MaxAttachmentsPerPost = 5;
    public const long MaxAttachmentSizeBytes = 25L * 1024L * 1024L;

    public static readonly string[] AllowedAttachmentMimePrefixes = ["image/", "video/"];

    public static SocialPostContractValidationResult ValidateAudience(SocialPostAudience audience)
    {
        if (audience.Visibility != SocialPostVisibility.Private)
        {
            return SocialPostContractValidationResult.Success();
        }

        if (audience.CircleFeedIds.Length == 0)
        {
            return SocialPostContractValidationResult.Failure(
                SocialPostContractErrorCode.PrivateAudienceRequiresAtLeastOneCircle,
                "Private post requires at least one selected circle.");
        }

        var normalized = audience.CircleFeedIds
            .Select(circleId => circleId.Trim().ToLowerInvariant())
            .Where(circleId => circleId.Length > 0)
            .ToArray();

        if (normalized.Distinct().Count() != normalized.Length)
        {
            return SocialPostContractValidationResult.Failure(
                SocialPostContractErrorCode.DuplicateCircleTargets,
                "Duplicate circle targets are not allowed.");
        }

        return SocialPostContractValidationResult.Success();
    }

    public static SocialPostContractValidationResult ValidateAttachments(IReadOnlyCollection<SocialPostAttachment> attachments)
    {
        if (attachments.Count > MaxAttachmentsPerPost)
        {
            return SocialPostContractValidationResult.Failure(
                SocialPostContractErrorCode.AttachmentCountExceeded,
                $"Too many attachments: {attachments.Count} exceeds the maximum of {MaxAttachmentsPerPost}.");
        }

        foreach (var attachment in attachments)
        {
            if (attachment.Size > MaxAttachmentSizeBytes)
            {
                return SocialPostContractValidationResult.Failure(
                    SocialPostContractErrorCode.AttachmentSizeExceeded,
                    $"Attachment {attachment.AttachmentId} exceeds the maximum size of {MaxAttachmentSizeBytes} bytes.");
            }

            var normalizedMime = attachment.MimeType.Trim().ToLowerInvariant();
            var allowed = AllowedAttachmentMimePrefixes.Any(prefix => normalizedMime.StartsWith(prefix, StringComparison.Ordinal));
            if (!allowed)
            {
                return SocialPostContractValidationResult.Failure(
                    SocialPostContractErrorCode.AttachmentMimeTypeNotAllowed,
                    $"Attachment mime type '{attachment.MimeType}' is not allowed for social posts.");
            }
        }

        return SocialPostContractValidationResult.Success();
    }
}

public record SocialPostContractValidationResult(
    bool IsValid,
    SocialPostContractErrorCode ErrorCode,
    string Message)
{
    public static SocialPostContractValidationResult Success() =>
        new(true, SocialPostContractErrorCode.None, string.Empty);

    public static SocialPostContractValidationResult Failure(
        SocialPostContractErrorCode errorCode,
        string message) => new(false, errorCode, message);
}
