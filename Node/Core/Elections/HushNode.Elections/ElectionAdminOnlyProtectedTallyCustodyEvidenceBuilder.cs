using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public static class ElectionAdminOnlyProtectedTallyCustodyEvidenceBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ElectionAdminOnlyProtectedTallyCustodyReadinessFragment BuildOpenEvidence(
        ElectionRecord election,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelope,
        DateTime recordedAt,
        bool includeRestrictedEvidence = false,
        string? failureDetail = null) =>
        BuildEvidence(
            election,
            envelope,
            ElectionAdminOnlyProtectedTallyCustodyActionKind.Open,
            ResolveOpenResultCodes(envelope, failureDetail),
            recordedAt,
            includeRestrictedEvidence,
            failureDetail);

    public static ElectionAdminOnlyProtectedTallyCustodyReadinessFragment BuildFinalizationCleanupEvidence(
        ElectionRecord election,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelope,
        DateTime recordedAt,
        bool includeRestrictedEvidence = false) =>
        BuildEvidence(
            election,
            envelope,
            ElectionAdminOnlyProtectedTallyCustodyActionKind.FinalizationCleanup,
            ResolveFinalizationResultCodes(envelope),
            recordedAt,
            includeRestrictedEvidence,
            failureDetail: null);

    public static ElectionAdminOnlyProtectedTallyCustodyReadinessFragment BuildReconciliationEvidence(
        ElectionRecord election,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelope,
        DateTime recordedAt,
        bool includeRestrictedEvidence = false) =>
        BuildEvidence(
            election,
            envelope,
            ElectionAdminOnlyProtectedTallyCustodyActionKind.Reconciliation,
            ResolveReconciliationResultCodes(envelope),
            recordedAt,
            includeRestrictedEvidence,
            failureDetail: null);

    public static ElectionAdminOnlyProtectedTallyCustodyReadinessFragment BuildAggregateReadinessEvidence(
        ElectionRecord election,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelope,
        DateTime recordedAt,
        IReadOnlyList<ElectionAdminOnlyProtectedTallyCustodyReadinessFragment>? gateFragments)
    {
        ArgumentNullException.ThrowIfNull(election);

        var fragments = gateFragments ?? Array.Empty<ElectionAdminOnlyProtectedTallyCustodyReadinessFragment>();
        var acceptedGateIds = fragments
            .SelectMany(x => x.AcceptedGateIds)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var missingGateIds = ElectionAdminOnlyProtectedTallyCustodyReadinessIds.RequiredGateIds
            .Where(required => !acceptedGateIds.Contains(required, StringComparer.Ordinal))
            .ToArray();
        var sourceExceptions = fragments
            .SelectMany(x => x.Exceptions)
            .ToArray();
        var generatedExceptions = missingGateIds
            .Select(gateId => BuildMissingGateException(election, gateId, recordedAt))
            .ToArray();
        var exceptions = sourceExceptions
            .Concat(generatedExceptions)
            .ToArray();
        var acceptedAllRequiredGates = missingGateIds.Length == 0 &&
                                       exceptions.All(x => !x.BlocksReadinessScoreIncrease);
        var publicEvidence = BuildPublicEvidence(
            election,
            envelope,
            ElectionAdminOnlyProtectedTallyCustodyReadinessIds.RequiredGateIds,
            ResolveAggregateResultCodes(fragments, missingGateIds),
            recordedAt);
        var secretScanStatus = PublicEvidenceContainsRestrictedMaterial(publicEvidence, envelope)
            ? ElectionAdminOnlyProtectedTallyCustodyResultCodes.PublicSecretScanFailed
            : ElectionAdminOnlyProtectedTallyCustodyResultCodes.PublicSecretScanPassed;
        publicEvidence = publicEvidence with
        {
            PublicRecordSecretScanStatus = secretScanStatus,
        };

        if (string.Equals(
                secretScanStatus,
                ElectionAdminOnlyProtectedTallyCustodyResultCodes.PublicSecretScanFailed,
                StringComparison.Ordinal))
        {
            exceptions =
            [
                .. exceptions,
                new ElectionAdminOnlyProtectedTallyCustodyExceptionEvidence(
                    BuildExceptionId(election, ElectionAdminOnlyProtectedTallyCustodyActionKind.Reconciliation, recordedAt),
                    ElectionAdminOnlyProtectedTallyCustodyActionKind.Reconciliation,
                    ElectionAdminOnlyProtectedTallyCustodyActionResult.ExceptionRequired,
                    ElectionAdminOnlyProtectedTallyCustodyResultCodes.PublicSecretScanFailed,
                    "Aggregate custody readiness evidence contains restricted material.",
                    RestrictedOperatorNotes: null,
                    BlocksReadinessScoreIncrease: true,
                    recordedAt),
            ];
            acceptedAllRequiredGates = false;
        }

        return new ElectionAdminOnlyProtectedTallyCustodyReadinessFragment(
            publicEvidence,
            RestrictedEvidence: null,
            exceptions,
            acceptedGateIds,
            acceptedAllRequiredGates
                ? ResolveResidualRiskIds(ElectionAdminOnlyProtectedTallyCustodyActionResult.Passed)
                : ResolveResidualRiskIds(ElectionAdminOnlyProtectedTallyCustodyActionResult.FailedClosed),
            ProposedScore: acceptedAllRequiredGates ? 8 : null);
    }

    public static bool PublicEvidenceContainsRestrictedMaterial(
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence publicEvidence,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelope) =>
        FindRestrictedPublicMaterial(publicEvidence, envelope).Count > 0;

    private static ElectionAdminOnlyProtectedTallyCustodyReadinessFragment BuildEvidence(
        ElectionRecord election,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelope,
        ElectionAdminOnlyProtectedTallyCustodyActionKind actionKind,
        IReadOnlyList<string> resultCodes,
        DateTime recordedAt,
        bool includeRestrictedEvidence,
        string? failureDetail)
    {
        var gateId = ResolveGateId(actionKind);
        var publicEvidence = BuildPublicEvidence(
            election,
            envelope,
            [gateId],
            resultCodes,
            recordedAt);
        var secretScanStatus = PublicEvidenceContainsRestrictedMaterial(publicEvidence, envelope)
            ? ElectionAdminOnlyProtectedTallyCustodyResultCodes.PublicSecretScanFailed
            : ElectionAdminOnlyProtectedTallyCustodyResultCodes.PublicSecretScanPassed;
        publicEvidence = publicEvidence with
        {
            PublicRecordSecretScanStatus = secretScanStatus,
        };

        var actionResult = ResolveActionResult(resultCodes, secretScanStatus);
        var exceptions = actionResult == ElectionAdminOnlyProtectedTallyCustodyActionResult.Passed
            ? Array.Empty<ElectionAdminOnlyProtectedTallyCustodyExceptionEvidence>()
            : new[]
            {
                new ElectionAdminOnlyProtectedTallyCustodyExceptionEvidence(
                    BuildExceptionId(election, actionKind, recordedAt),
                    actionKind,
                    actionResult,
                    resultCodes.First(),
                    ResolvePublicImpact(actionKind, resultCodes.First()),
                    envelope?.CustodyLastErrorCode,
                    BlocksReadinessScoreIncrease: true,
                    recordedAt),
            };

        return new ElectionAdminOnlyProtectedTallyCustodyReadinessFragment(
            publicEvidence,
            includeRestrictedEvidence ? BuildRestrictedEvidence(election, envelope, failureDetail) : null,
            exceptions,
            AcceptedGateIds: actionResult == ElectionAdminOnlyProtectedTallyCustodyActionResult.Passed
                ? [gateId]
                : [],
            ResidualRiskIds: ResolveResidualRiskIds(actionResult),
            ProposedScore: null);
    }

    private static ElectionAdminOnlyProtectedTallyCustodyPublicEvidence BuildPublicEvidence(
        ElectionRecord election,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelope,
        IReadOnlyList<string> gateIds,
        IReadOnlyList<string> resultCodes,
        DateTime recordedAt) =>
        new(
            ElectionAdminOnlyProtectedTallyCustodyReadinessIds.EvidenceId,
            election.ElectionId,
            ResolveSelectedProfileId(election, envelope),
            envelope?.CustodyMode ?? ElectionAdminOnlyProtectedTallyCustodyModes.NotRequired,
            ResolveProviderFamily(envelope),
            string.IsNullOrWhiteSpace(envelope?.TallyPublicKeyFingerprint)
                ? "unavailable"
                : envelope.TallyPublicKeyFingerprint,
            envelope?.ResolveCustodyLifecycleState() ??
                ElectionAdminOnlyProtectedTallyCustodyLifecycleState.ProviderUnavailable,
            gateIds,
            resultCodes,
            ResolvePublicCustodyReferenceHash(election, envelope),
            PublicRecordSecretScanStatus: "pending",
            recordedAt);

    private static ElectionAdminOnlyProtectedTallyCustodyRestrictedEvidence BuildRestrictedEvidence(
        ElectionRecord election,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelope,
        string? failureDetail) =>
        new(
            BuildPrivateCustodyRowReference(election, envelope),
            envelope?.KmsKeyId,
            envelope?.KmsKeyArn,
            envelope?.KmsAlias,
            envelope?.KmsRegion,
            envelope?.KmsAccountBoundary,
            string.IsNullOrWhiteSpace(envelope?.KmsTagSetHash)
                ? null
                : $"tag-set-hash:{envelope.KmsTagSetHash}",
            envelope?.CustodyActionServiceIdentity,
            envelope?.SealedEnvelopeHash ?? ComputeOptionalHash(envelope?.SealedTallyPrivateScalar),
            envelope?.CustodyLastErrorCode,
            envelope?.CustodyLastErrorMessage ?? failureDetail);

    private static IReadOnlyList<string> ResolveOpenResultCodes(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelope,
        string? failureDetail)
    {
        if (envelope is null)
        {
            return string.IsNullOrWhiteSpace(failureDetail)
                ? [ElectionAdminOnlyProtectedTallyCustodyResultCodes.OpenMissingCustodyRow]
                : [ElectionAdminOnlyProtectedTallyCustodyResultCodes.OpenProviderUnavailable];
        }

        return envelope.ResolveCustodyLifecycleState() switch
        {
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.OpenBound =>
                [ElectionAdminOnlyProtectedTallyCustodyResultCodes.OpenBound],
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.RetryRequired =>
                [ElectionAdminOnlyProtectedTallyCustodyResultCodes.OpenRetryRequired],
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.ExceptionRequired =>
                [ElectionAdminOnlyProtectedTallyCustodyResultCodes.OpenExceptionRequired],
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.ProviderUnavailable =>
                [ElectionAdminOnlyProtectedTallyCustodyResultCodes.OpenProviderUnavailable],
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.LegacyStaticKms =>
                [ElectionAdminOnlyProtectedTallyCustodyResultCodes.OpenLegacyCompatibility],
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.NotRequired =>
                [ElectionAdminOnlyProtectedTallyCustodyResultCodes.OpenNotRequired],
            _ when envelope.HasPerElectionKmsCustody =>
                [ElectionAdminOnlyProtectedTallyCustodyResultCodes.OpenRetryRequired],
            _ => [ElectionAdminOnlyProtectedTallyCustodyResultCodes.OpenMissingCustodyRow],
        };
    }

    private static IReadOnlyList<string> ResolveFinalizationResultCodes(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelope)
    {
        if (envelope is null)
        {
            return [ElectionAdminOnlyProtectedTallyCustodyResultCodes.FinalizationMissingDestroyedMarker];
        }

        if (!HasDestroyedMarker(envelope))
        {
            return [ElectionAdminOnlyProtectedTallyCustodyResultCodes.FinalizationMissingDestroyedMarker];
        }

        return envelope.ResolveCustodyLifecycleState() switch
        {
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.DeletionScheduled =>
                [ElectionAdminOnlyProtectedTallyCustodyResultCodes.FinalizationDeletionScheduled],
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.KeyDisabled =>
                [ElectionAdminOnlyProtectedTallyCustodyResultCodes.FinalizationKeyDisabled],
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.ScalarDestroyed =>
                [ElectionAdminOnlyProtectedTallyCustodyResultCodes.FinalizationScalarDestroyed],
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.RetryRequired =>
                [ElectionAdminOnlyProtectedTallyCustodyResultCodes.FinalizationRetryRequired],
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.ExceptionRequired =>
                [ElectionAdminOnlyProtectedTallyCustodyResultCodes.FinalizationExceptionRequired],
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.LegacyStaticKms =>
                [ElectionAdminOnlyProtectedTallyCustodyResultCodes.FinalizationLegacyCompatibility],
            _ => [ElectionAdminOnlyProtectedTallyCustodyResultCodes.FinalizationMissingDestroyedMarker],
        };
    }

    private static IReadOnlyList<string> ResolveReconciliationResultCodes(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelope) =>
        envelope?.ResolveCustodyLifecycleState() switch
        {
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.RetryRequired =>
                [ElectionAdminOnlyProtectedTallyCustodyResultCodes.ReconciliationRetryRequired],
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.ExceptionRequired =>
                [ElectionAdminOnlyProtectedTallyCustodyResultCodes.ReconciliationExceptionRequired],
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.DeletionScheduled =>
                [ElectionAdminOnlyProtectedTallyCustodyResultCodes.ReconciliationAccepted],
            _ => [ElectionAdminOnlyProtectedTallyCustodyResultCodes.ReconciliationMissing],
        };

    private static IReadOnlyList<string> ResolveAggregateResultCodes(
        IReadOnlyList<ElectionAdminOnlyProtectedTallyCustodyReadinessFragment> fragments,
        IReadOnlyList<string> missingGateIds)
    {
        var resultCodes = fragments
            .SelectMany(x => x.PublicEvidence.PublicResultCodes)
            .Concat(missingGateIds.Select(ResolveMissingGateResultCode))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return resultCodes.Length == 0
            ? [ElectionAdminOnlyProtectedTallyCustodyResultCodes.ReconciliationMissing]
            : resultCodes;
    }

    private static ElectionAdminOnlyProtectedTallyCustodyExceptionEvidence BuildMissingGateException(
        ElectionRecord election,
        string gateId,
        DateTime recordedAt)
    {
        var actionKind = ResolveActionKindForGate(gateId);
        var reasonCode = ResolveMissingGateResultCode(gateId);
        return new ElectionAdminOnlyProtectedTallyCustodyExceptionEvidence(
            $"custody-{gateId.ToLowerInvariant()}-missing-{ComputeHash($"{election.ElectionId}|{gateId}|{recordedAt:O}")}",
            actionKind,
            ElectionAdminOnlyProtectedTallyCustodyActionResult.FailedClosed,
            reasonCode,
            ResolveMissingGateImpact(gateId, reasonCode),
            RestrictedOperatorNotes: null,
            BlocksReadinessScoreIncrease: true,
            recordedAt);
    }

    private static ElectionAdminOnlyProtectedTallyCustodyActionKind ResolveActionKindForGate(string gateId) =>
        gateId switch
        {
            ElectionAdminOnlyProtectedTallyCustodyReadinessIds.OpenGateId =>
                ElectionAdminOnlyProtectedTallyCustodyActionKind.Open,
            ElectionAdminOnlyProtectedTallyCustodyReadinessIds.FinalizationGateId =>
                ElectionAdminOnlyProtectedTallyCustodyActionKind.FinalizationCleanup,
            ElectionAdminOnlyProtectedTallyCustodyReadinessIds.ReconciliationGateId =>
                ElectionAdminOnlyProtectedTallyCustodyActionKind.Reconciliation,
            _ => ElectionAdminOnlyProtectedTallyCustodyActionKind.Reconciliation,
        };

    private static string ResolveMissingGateResultCode(string gateId) =>
        gateId switch
        {
            ElectionAdminOnlyProtectedTallyCustodyReadinessIds.OpenGateId =>
                ElectionAdminOnlyProtectedTallyCustodyResultCodes.OpenMissingCustodyRow,
            ElectionAdminOnlyProtectedTallyCustodyReadinessIds.FinalizationGateId =>
                ElectionAdminOnlyProtectedTallyCustodyResultCodes.FinalizationMissingDestroyedMarker,
            ElectionAdminOnlyProtectedTallyCustodyReadinessIds.ReconciliationGateId =>
                ElectionAdminOnlyProtectedTallyCustodyResultCodes.ReconciliationMissing,
            _ => ElectionAdminOnlyProtectedTallyCustodyResultCodes.ReconciliationMissing,
        };

    private static string ResolveMissingGateImpact(string gateId, string reasonCode) =>
        gateId switch
        {
            ElectionAdminOnlyProtectedTallyCustodyReadinessIds.OpenGateId =>
                $"Open custody evidence is missing. Safe reason code: {reasonCode}.",
            ElectionAdminOnlyProtectedTallyCustodyReadinessIds.FinalizationGateId =>
                $"Finalization cleanup evidence is missing. Safe reason code: {reasonCode}.",
            ElectionAdminOnlyProtectedTallyCustodyReadinessIds.ReconciliationGateId =>
                $"Custody reconciliation evidence is missing. Safe reason code: {reasonCode}.",
            _ => $"Custody evidence is missing. Safe reason code: {reasonCode}.",
        };

    private static ElectionAdminOnlyProtectedTallyCustodyActionResult ResolveActionResult(
        IReadOnlyList<string> resultCodes,
        string secretScanStatus)
    {
        if (string.Equals(
                secretScanStatus,
                ElectionAdminOnlyProtectedTallyCustodyResultCodes.PublicSecretScanFailed,
                StringComparison.Ordinal))
        {
            return ElectionAdminOnlyProtectedTallyCustodyActionResult.ExceptionRequired;
        }

        var first = resultCodes.FirstOrDefault() ?? string.Empty;
        return first switch
        {
            ElectionAdminOnlyProtectedTallyCustodyResultCodes.OpenBound or
            ElectionAdminOnlyProtectedTallyCustodyResultCodes.FinalizationDeletionScheduled or
            ElectionAdminOnlyProtectedTallyCustodyResultCodes.FinalizationKeyDisabled or
            ElectionAdminOnlyProtectedTallyCustodyResultCodes.FinalizationScalarDestroyed or
            ElectionAdminOnlyProtectedTallyCustodyResultCodes.ReconciliationAccepted =>
                ElectionAdminOnlyProtectedTallyCustodyActionResult.Passed,
            ElectionAdminOnlyProtectedTallyCustodyResultCodes.OpenRetryRequired or
            ElectionAdminOnlyProtectedTallyCustodyResultCodes.FinalizationRetryRequired or
            ElectionAdminOnlyProtectedTallyCustodyResultCodes.ReconciliationRetryRequired =>
                ElectionAdminOnlyProtectedTallyCustodyActionResult.RetryRequired,
            ElectionAdminOnlyProtectedTallyCustodyResultCodes.OpenExceptionRequired or
            ElectionAdminOnlyProtectedTallyCustodyResultCodes.FinalizationExceptionRequired or
            ElectionAdminOnlyProtectedTallyCustodyResultCodes.ReconciliationExceptionRequired =>
                ElectionAdminOnlyProtectedTallyCustodyActionResult.ExceptionRequired,
            _ => ElectionAdminOnlyProtectedTallyCustodyActionResult.FailedClosed,
        };
    }

    private static IReadOnlyList<string> ResolveResidualRiskIds(
        ElectionAdminOnlyProtectedTallyCustodyActionResult actionResult) =>
        actionResult == ElectionAdminOnlyProtectedTallyCustodyActionResult.Passed
            ? ["cloud_provider_incident", "iam_drift", "regional_kms_availability", "deployment_variant"]
            : ["custody_evidence_not_accepted"];

    private static string ResolveGateId(ElectionAdminOnlyProtectedTallyCustodyActionKind actionKind) =>
        actionKind switch
        {
            ElectionAdminOnlyProtectedTallyCustodyActionKind.Open =>
                ElectionAdminOnlyProtectedTallyCustodyReadinessIds.OpenGateId,
            ElectionAdminOnlyProtectedTallyCustodyActionKind.FinalizationCleanup =>
                ElectionAdminOnlyProtectedTallyCustodyReadinessIds.FinalizationGateId,
            ElectionAdminOnlyProtectedTallyCustodyActionKind.Reconciliation =>
                ElectionAdminOnlyProtectedTallyCustodyReadinessIds.ReconciliationGateId,
            _ => throw new ArgumentOutOfRangeException(nameof(actionKind), actionKind, "Unsupported custody action."),
        };

    private static string ResolveProviderFamily(ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelope)
    {
        if (envelope is null)
        {
            return "unavailable";
        }

        if (string.Equals(
                envelope.CustodyMode,
                ElectionAdminOnlyProtectedTallyCustodyModes.AwsKmsPerElectionEnvelopeV1,
                StringComparison.Ordinal))
        {
            return "aws-kms";
        }

        if (string.Equals(
                envelope.CustodyMode,
                ElectionAdminOnlyProtectedTallyCustodyModes.WindowsDpapiCurrentUserV1,
                StringComparison.Ordinal))
        {
            return "windows-dpapi";
        }

        return string.IsNullOrWhiteSpace(envelope.CustodyMode)
            ? "not-required"
            : "other";
    }

    private static string ResolvePublicCustodyReferenceHash(
        ElectionRecord election,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelope) =>
        string.IsNullOrWhiteSpace(envelope?.PublicCustodyReferenceHash)
            ? ComputeHash($"{election.ElectionId}|{ResolveSelectedProfileId(election, envelope)}|public-custody-ref")
            : envelope.PublicCustodyReferenceHash;

    private static string BuildPrivateCustodyRowReference(
        ElectionRecord election,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelope) =>
        $"elections/admin-only-protected-tally/{election.ElectionId}/{ResolveSelectedProfileId(election, envelope)}";

    private static string ResolveSelectedProfileId(
        ElectionRecord election,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelope)
    {
        if (!string.IsNullOrWhiteSpace(envelope?.SelectedProfileId))
        {
            return envelope.SelectedProfileId;
        }

        return string.IsNullOrWhiteSpace(election.SelectedProfileId)
            ? "unknown_profile"
            : election.SelectedProfileId.Trim();
    }

    private static string BuildExceptionId(
        ElectionRecord election,
        ElectionAdminOnlyProtectedTallyCustodyActionKind actionKind,
        DateTime recordedAt) =>
        $"custody-{actionKind.ToString().ToLowerInvariant()}-{ComputeHash($"{election.ElectionId}|{recordedAt:O}")}";

    private static string ResolvePublicImpact(
        ElectionAdminOnlyProtectedTallyCustodyActionKind actionKind,
        string resultCode) =>
        actionKind switch
        {
            ElectionAdminOnlyProtectedTallyCustodyActionKind.Open =>
                $"Open custody evidence did not pass. Safe reason code: {resultCode}.",
            ElectionAdminOnlyProtectedTallyCustodyActionKind.FinalizationCleanup =>
                $"Finalization cleanup evidence did not pass. Safe reason code: {resultCode}.",
            ElectionAdminOnlyProtectedTallyCustodyActionKind.Reconciliation =>
                $"Custody reconciliation evidence did not pass. Safe reason code: {resultCode}.",
            _ => $"Custody evidence did not pass. Safe reason code: {resultCode}.",
        };

    private static bool HasDestroyedMarker(ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope) =>
        envelope.DestroyedAt.HasValue &&
        string.Equals(
            envelope.SealedTallyPrivateScalar,
            AdminOnlyProtectedTallyEnvelopeCryptoConstants.DestroyedEnvelopeMarker,
            StringComparison.Ordinal);

    private static string? ComputeOptionalHash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : ComputeHash(value);

    private static string ComputeHash(string value) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";

    private static IReadOnlyList<string> FindRestrictedPublicMaterial(
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence publicEvidence,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelope)
    {
        if (envelope is null)
        {
            return Array.Empty<string>();
        }

        var publicPayload = JsonSerializer.Serialize(publicEvidence, JsonOptions);
        return EnumerateRestrictedCandidates(envelope)
            .Where(candidate => publicPayload.Contains(candidate, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateRestrictedCandidates(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope)
    {
        foreach (var candidate in new[]
                 {
                     envelope.KmsKeyId,
                     envelope.KmsKeyArn,
                     envelope.KmsAlias,
                     envelope.KmsRegion,
                     envelope.KmsAccountBoundary,
                     envelope.KmsTagSetHash,
                     envelope.EncryptionContextHash,
                     envelope.CustodyLastErrorMessage,
                     envelope.SealedEnvelopeHash,
                     envelope.SealedTallyPrivateScalar,
                 })
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                yield return candidate;
            }
        }
    }
}
