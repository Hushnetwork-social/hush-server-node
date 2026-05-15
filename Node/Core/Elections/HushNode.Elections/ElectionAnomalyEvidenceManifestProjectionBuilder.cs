using System.Text.Json;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public static class ElectionAnomalyEvidenceManifestProjectionBuilder
{
    public static async Task<ElectionAnomalyEvidenceManifestProjection> BuildAsync(
        IElectionsRepository repository,
        ElectionId electionId,
        string scopeId,
        string? actorPublicAddress = null)
    {
        if (!ElectionAnomalyEvidenceManifestScopeIds.IsKnown(scopeId))
        {
            throw new ArgumentException("Unknown anomaly evidence manifest scope.", nameof(scopeId));
        }

        var threads = await repository.GetAnomalyThreadsAsync(electionId);
        var threadProjections = new List<ElectionAnomalyEvidenceManifestThreadProjection>();
        foreach (var thread in threads.OrderBy(x => x.Id))
        {
            var wraps = await repository.GetAnomalyRecipientWrapsAsync(thread.Id);
            var attachments = await repository.GetAnomalyAttachmentManifestsAsync(thread.Id);
            var redactions = await repository.GetAnomalyEvidenceRedactionsAsync(thread.Id);
            threadProjections.Add(new ElectionAnomalyEvidenceManifestThreadProjection(
                thread.Id,
                thread.ElectionId,
                thread.CurrentCategoryId,
                thread.CurrentCaseStateId,
                thread.CurrentThreadHash,
                thread.GovernedDecisionRef,
                thread.HasOpenClarificationRequest,
                thread.OpenClarificationRequestId,
                thread.CreatedAt,
                thread.LastUpdatedAt,
                attachments
                    .OrderBy(x => x.Id)
                    .Select(x => new ElectionAnomalyAttachmentManifestProjection(
                        x.Id,
                        x.AnomalyThreadId,
                        x.EventId,
                        x.EventHash,
                        x.AttachmentKindId,
                        x.EncryptedPayloadReference,
                        x.EncryptedPayloadHash,
                        x.ContentHash,
                        x.SizeBytes,
                        x.MimeType,
                        x.ValidationStatusId,
                        x.ScannerStatusId,
                        x.PayloadAvailabilityStatusId,
                        x.ClarificationRequestId,
                        x.ActorRoleId,
                        x.RecordedAt,
                        x.SourceTransactionId,
                        ResolveCallerContentKeyWrap(x.ContentKeyWrapsJson, actorPublicAddress)))
                    .ToArray(),
                redactions
                    .OrderBy(x => x.Id)
                    .Select(x => new ElectionAnomalyEvidenceRedactionProjection(
                        x.Id,
                        x.AnomalyThreadId,
                        x.EventId,
                        x.EventHash,
                        x.TargetKindId,
                        x.TargetId,
                        x.ReasonCodeId,
                        x.OriginalHash,
                        x.ReplacementManifestHash,
                        x.TombstoneStatusId,
                        x.RecordedAt,
                        x.SourceTransactionId))
                    .ToArray(),
                wraps
                    .GroupBy(x => new { x.RecipientRoleId, x.WrapStatusId })
                    .OrderBy(x => x.Key.RecipientRoleId)
                    .ThenBy(x => x.Key.WrapStatusId)
                    .Select(x => new ElectionAnomalyRestrictedRecipientWrapProjection(
                        x.Key.RecipientRoleId,
                        x.Key.WrapStatusId))
                    .ToArray()));
        }

        var blockers = ResolvePackageReadinessBlockers(threadProjections);
        var projection = new ElectionAnomalyEvidenceManifestProjection(
            electionId,
            scopeId,
            ElectionAnomalyManifestCanonicalizationIds.Current,
            ManifestHash: string.Empty,
            blockers.Count == 0
                ? ElectionAnomalyPackageReadinessStatusIds.Ready
                : ElectionAnomalyPackageReadinessStatusIds.Blocked,
            blockers,
            threadProjections);

        return projection with
        {
            ManifestHash = ElectionAnomalyIntakeManifestHasher.ComputeHash(
                ElectionAnomalyIntakeManifestHasher.FromProjection(projection)),
        };
    }

    public static IReadOnlyList<string> ResolvePackageReadinessBlockers(
        IReadOnlyList<ElectionAnomalyEvidenceManifestThreadProjection> threads)
    {
        var blockers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var attachment in threads.SelectMany(x => x.AttachmentManifests))
        {
            if (string.Equals(
                    attachment.ScannerStatusId,
                    ElectionAnomalyEvidenceScannerStatusIds.Pending,
                    StringComparison.Ordinal) ||
                string.Equals(
                    attachment.ScannerStatusId,
                    ElectionAnomalyEvidenceScannerStatusIds.Quarantined,
                    StringComparison.Ordinal) ||
                string.Equals(
                    attachment.ScannerStatusId,
                    ElectionAnomalyEvidenceScannerStatusIds.ScannerUnavailable,
                    StringComparison.Ordinal))
            {
                blockers.Add(attachment.ScannerStatusId);
            }

            if (string.Equals(
                    attachment.PayloadAvailabilityStatusId,
                    ElectionAnomalyPayloadAvailabilityStatusIds.PayloadMissing,
                    StringComparison.Ordinal) ||
                string.Equals(
                    attachment.PayloadAvailabilityStatusId,
                    ElectionAnomalyPayloadAvailabilityStatusIds.ManifestHashMismatch,
                    StringComparison.Ordinal) ||
                string.Equals(
                    attachment.PayloadAvailabilityStatusId,
                    ElectionAnomalyPayloadAvailabilityStatusIds.Quarantined,
                    StringComparison.Ordinal))
            {
                blockers.Add(attachment.PayloadAvailabilityStatusId);
            }
        }

        return blockers.ToArray();
    }

    private static ElectionAnomalyAttachmentCallerContentKeyWrapProjection? ResolveCallerContentKeyWrap(
        string? contentKeyWrapsJson,
        string? actorPublicAddress)
    {
        if (string.IsNullOrWhiteSpace(contentKeyWrapsJson) ||
            string.IsNullOrWhiteSpace(actorPublicAddress))
        {
            return null;
        }

        IReadOnlyList<ElectionAnomalyAttachmentContentKeyWrapPayload>? wraps;
        try
        {
            wraps = JsonSerializer.Deserialize<IReadOnlyList<ElectionAnomalyAttachmentContentKeyWrapPayload>>(
                contentKeyWrapsJson);
        }
        catch (JsonException)
        {
            return null;
        }

        var callerWrap = wraps?
            .FirstOrDefault(wrap =>
                string.Equals(wrap.RecipientPublicAddress, actorPublicAddress, StringComparison.Ordinal) &&
                string.Equals(wrap.WrapStatusId, ElectionAnomalyRecipientWrapStatusIds.Available, StringComparison.Ordinal));
        return callerWrap is null
            ? null
            : new ElectionAnomalyAttachmentCallerContentKeyWrapProjection(
                callerWrap.WrapStatusId,
                callerWrap.RecipientKeyFingerprint,
                callerWrap.EncryptedContentKey,
                callerWrap.WrapAlgorithm);
    }
}
