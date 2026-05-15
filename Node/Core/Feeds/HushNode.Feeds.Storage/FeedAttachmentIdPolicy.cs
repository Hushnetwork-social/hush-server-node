using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public static class FeedAttachmentIdPolicy
{
    public const string ElectionAnomalyRestrictedPayloadReferencePrefix = "hush-election-anomaly-payload-v1:";

    public static bool IsElectionAnomalyRestrictedPayloadReference(string? attachmentId) =>
        !string.IsNullOrWhiteSpace(attachmentId) &&
        attachmentId.Trim().StartsWith(ElectionAnomalyRestrictedPayloadReferencePrefix, StringComparison.Ordinal);

    public static void EnsureCanStore(AttachmentEntity attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        if (IsElectionAnomalyRestrictedPayloadReference(attachment.Id))
        {
            throw new ArgumentException(
                "Election anomaly restricted payload references cannot be stored as feed attachments.",
                nameof(attachment));
        }
    }
}
