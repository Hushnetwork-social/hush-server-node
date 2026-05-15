using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HushShared.Elections.Model;

public static class ElectionAnomalyIntakeManifestHasher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    public static AnomalyIntakeManifest FromProjection(ElectionAnomalyEvidenceManifestProjection projection) =>
        new(
            projection.CanonicalizationId,
            projection.ElectionId.ToString(),
            projection.ScopeId,
            projection.PackageReadinessStatusId,
            projection.PackageReadinessBlockerIds
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray(),
            projection.Threads
                .OrderBy(x => x.AnomalyThreadId)
                .Select(thread => new AnomalyIntakeManifestThread(
                    thread.AnomalyThreadId,
                    thread.CategoryId,
                    thread.CaseStateId,
                    thread.CurrentThreadHash,
                    thread.GovernedDecisionRef,
                    thread.HasOpenClarificationRequest,
                    thread.OpenClarificationRequestId,
                    NormalizeUtc(thread.CreatedAtUtc),
                    NormalizeUtc(thread.UpdatedAtUtc),
                    thread.AttachmentManifests
                        .OrderBy(x => x.AttachmentManifestId)
                        .Select(attachment => new AnomalyIntakeManifestAttachment(
                            attachment.AttachmentManifestId,
                            attachment.EventId,
                            attachment.EventHash,
                            attachment.AttachmentKindId,
                            attachment.EncryptedPayloadReference,
                            attachment.EncryptedPayloadHash,
                            attachment.ContentHash,
                            attachment.SizeBytes,
                            attachment.MimeType,
                            attachment.ValidationStatusId,
                            attachment.ScannerStatusId,
                            attachment.PayloadAvailabilityStatusId,
                            attachment.ClarificationRequestId,
                            NormalizeUtc(attachment.RecordedAtUtc),
                            attachment.SourceTransactionId))
                        .ToArray(),
                    thread.Redactions
                        .OrderBy(x => x.RedactionEventId)
                        .Select(redaction => new AnomalyIntakeManifestRedaction(
                            redaction.RedactionEventId,
                            redaction.EventId,
                            redaction.EventHash,
                            redaction.TargetKindId,
                            redaction.TargetId,
                            redaction.ReasonCodeId,
                            redaction.OriginalHash,
                            redaction.ReplacementManifestHash,
                            redaction.TombstoneStatusId,
                            NormalizeUtc(redaction.RecordedAtUtc),
                            redaction.SourceTransactionId))
                        .ToArray(),
                    thread.RecipientWraps
                        .OrderBy(x => x.RecipientRoleId, StringComparer.Ordinal)
                        .ThenBy(x => x.WrapStatusId, StringComparer.Ordinal)
                        .Select(status => new AnomalyIntakeManifestRecipientStatus(
                            status.RecipientRoleId,
                            status.WrapStatusId))
                        .ToArray()))
                .ToArray());

    public static string ComputeHash(AnomalyIntakeManifest manifest)
    {
        var input = NormalizeForHash(manifest);
        return ComputeSha256Reference(JsonSerializer.SerializeToUtf8Bytes(input, JsonOptions));
    }

    private static ManifestHashInput NormalizeForHash(AnomalyIntakeManifest manifest) =>
        new(
            manifest.CanonicalizationId,
            manifest.ElectionId,
            manifest.ScopeId,
            manifest.PackageReadinessStatusId,
            manifest.PackageReadinessBlockerIds
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray(),
            manifest.Threads
                .OrderBy(x => x.AnomalyThreadId)
                .Select(thread => new ManifestThreadHashInput(
                    thread.AnomalyThreadId,
                    thread.CategoryId,
                    thread.CaseStateId,
                    thread.CurrentThreadHash,
                    thread.GovernedDecisionRef,
                    thread.HasOpenClarificationRequest,
                    thread.OpenClarificationRequestId,
                    FormatUtc(thread.CreatedAtUtc),
                    FormatUtc(thread.UpdatedAtUtc),
                    thread.Attachments
                        .OrderBy(x => x.AttachmentManifestId)
                        .Select(attachment => new ManifestAttachmentHashInput(
                            attachment.AttachmentManifestId,
                            attachment.EventId,
                            attachment.EventHash,
                            attachment.AttachmentKindId,
                            attachment.EncryptedPayloadReference,
                            attachment.EncryptedPayloadHash,
                            attachment.ContentHash,
                            attachment.SizeBytes,
                            attachment.MimeType,
                            attachment.ValidationStatusId,
                            attachment.ScannerStatusId,
                            attachment.PayloadAvailabilityStatusId,
                            attachment.ClarificationRequestId,
                            FormatUtc(attachment.RecordedAtUtc),
                            attachment.SourceTransactionId))
                        .ToArray(),
                    thread.Redactions
                        .OrderBy(x => x.RedactionEventId)
                        .Select(redaction => new ManifestRedactionHashInput(
                            redaction.RedactionEventId,
                            redaction.EventId,
                            redaction.EventHash,
                            redaction.TargetKindId,
                            redaction.TargetId,
                            redaction.ReasonCodeId,
                            redaction.OriginalHash,
                            redaction.ReplacementManifestHash,
                            redaction.TombstoneStatusId,
                            FormatUtc(redaction.RecordedAtUtc),
                            redaction.SourceTransactionId))
                        .ToArray(),
                    thread.RecipientStatuses
                        .OrderBy(x => x.RecipientRoleId, StringComparer.Ordinal)
                        .ThenBy(x => x.WrapStatusId, StringComparer.Ordinal)
                        .Select(status => new ManifestRecipientStatusHashInput(
                            status.RecipientRoleId,
                            status.WrapStatusId))
                        .ToArray()))
                .ToArray());

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private static string FormatUtc(DateTime value) =>
        NormalizeUtc(value).ToString("O", CultureInfo.InvariantCulture);

    private static string ComputeSha256Reference(byte[] bytes) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";

    private sealed record ManifestHashInput(
        string CanonicalizationId,
        string ElectionId,
        string ScopeId,
        string PackageReadinessStatusId,
        IReadOnlyList<string> PackageReadinessBlockerIds,
        IReadOnlyList<ManifestThreadHashInput> Threads);

    private sealed record ManifestThreadHashInput(
        Guid AnomalyThreadId,
        string CategoryId,
        string CaseStateId,
        string CurrentThreadHash,
        string? GovernedDecisionRef,
        bool HasOpenClarificationRequest,
        Guid? OpenClarificationRequestId,
        string CreatedAtUtc,
        string UpdatedAtUtc,
        IReadOnlyList<ManifestAttachmentHashInput> Attachments,
        IReadOnlyList<ManifestRedactionHashInput> Redactions,
        IReadOnlyList<ManifestRecipientStatusHashInput> RecipientStatuses);

    private sealed record ManifestAttachmentHashInput(
        Guid AttachmentManifestId,
        Guid EventId,
        string EventHash,
        string AttachmentKindId,
        string EncryptedPayloadReference,
        string EncryptedPayloadHash,
        string ContentHash,
        long SizeBytes,
        string MimeType,
        string ValidationStatusId,
        string ScannerStatusId,
        string PayloadAvailabilityStatusId,
        Guid? ClarificationRequestId,
        string RecordedAtUtc,
        Guid SourceTransactionId);

    private sealed record ManifestRedactionHashInput(
        Guid RedactionEventId,
        Guid EventId,
        string EventHash,
        string TargetKindId,
        string TargetId,
        string ReasonCodeId,
        string OriginalHash,
        string? ReplacementManifestHash,
        string? TombstoneStatusId,
        string RecordedAtUtc,
        Guid SourceTransactionId);

    private sealed record ManifestRecipientStatusHashInput(
        string RecipientRoleId,
        string WrapStatusId);
}
