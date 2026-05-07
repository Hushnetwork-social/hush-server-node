using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections;

internal static class ElectionSp07PublicationProofGate
{
    public static ElectionSp07PublicationProofGateResult EvaluateTallyReady(
        ElectionRecord election,
        IReadOnlyList<ElectionAcceptedBallotRecord> acceptedBallots,
        IReadOnlyList<ElectionPublishedBallotRecord> publishedBallots,
        IReadOnlyList<ElectionPublicationProofSessionRecord> proofSessions,
        IReadOnlyList<ElectionPublicationProofTranscriptRecord> proofTranscripts,
        IReadOnlyList<ElectionPublicationWitnessDeletionReceiptRecord> deletionReceipts)
    {
        ArgumentNullException.ThrowIfNull(election);
        ArgumentNullException.ThrowIfNull(acceptedBallots);
        ArgumentNullException.ThrowIfNull(publishedBallots);
        ArgumentNullException.ThrowIfNull(proofSessions);
        ArgumentNullException.ThrowIfNull(proofTranscripts);
        ArgumentNullException.ThrowIfNull(deletionReceipts);

        if (!IsEvidenceExpected(election) || acceptedBallots.Count == 0)
        {
            return ElectionSp07PublicationProofGateResult.Passed();
        }

        var latestSession = proofSessions
            .OrderByDescending(x => x.CompletedAt ?? x.StartedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        if (latestSession is null)
        {
            return ElectionSp07PublicationProofGateResult.Failed(
                VerificationResultCodes.PublicationProofTranscriptMissing,
                "High-assurance close/counting requires an SP-07 publication-proof session before tally_ready.",
                ElectionClosedProgressStatus.PublicationProofPending);
        }

        if (latestSession.Status == ElectionPublicationProofSessionStatus.Failed)
        {
            return ElectionSp07PublicationProofGateResult.Failed(
                latestSession.FailureCode ?? VerificationResultCodes.PublicationProofVerificationFailed,
                latestSession.FailureReason ?? "SP-07 publication proof generation or self-verification failed.",
                ElectionClosedProgressStatus.PublicationProofFailed);
        }

        if (latestSession.Status is ElectionPublicationProofSessionStatus.Pending or
            ElectionPublicationProofSessionStatus.Generating or
            ElectionPublicationProofSessionStatus.SelfVerifying)
        {
            return ElectionSp07PublicationProofGateResult.Failed(
                VerificationResultCodes.PublicationProofEvidencePending,
                "High-assurance close/counting is waiting for SP-07 publication-proof generation and self-verification.",
                MapProgressStatus(latestSession.Status));
        }

        var latestTranscript = proofTranscripts
            .Where(x => x.ProofSessionId == latestSession.Id)
            .OrderByDescending(x => x.GeneratedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        if (latestTranscript is null)
        {
            return ElectionSp07PublicationProofGateResult.Failed(
                VerificationResultCodes.PublicationProofTranscriptMissing,
                "High-assurance close/counting requires a verified SP-07 publication-proof transcript before tally_ready.",
                ElectionClosedProgressStatus.PublicationProofPending);
        }

        if (!MatchesOptionalHash(latestSession.AcceptedBallotSetHash, latestTranscript.AcceptedBallotSetHash) ||
            !MatchesOptionalHash(latestSession.PublishedBallotStreamHash, latestTranscript.PublishedBallotStreamHash) ||
            !MatchesOptionalHash(latestSession.TranscriptHash, latestTranscript.TranscriptHash) ||
            !MatchesOptionalHash(latestSession.ProofHash, latestTranscript.ProofHash))
        {
            return ElectionSp07PublicationProofGateResult.Failed(
                VerificationResultCodes.PublicationProofTranscriptInvalid,
                "SP-07 publication-proof session hashes do not match the verified transcript.",
                ElectionClosedProgressStatus.PublicationProofFailed);
        }

        var acceptedHash = ToLowerHex(VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(acceptedBallots));
        if (!string.Equals(latestTranscript.AcceptedBallotSetHash, acceptedHash, StringComparison.Ordinal))
        {
            return ElectionSp07PublicationProofGateResult.Failed(
                VerificationResultCodes.PublicationProofAcceptedSetMismatch,
                "SP-07 publication-proof transcript does not match the accepted-ballot set.",
                ElectionClosedProgressStatus.PublicationProofFailed);
        }

        var publishedHash = ToLowerHex(VerificationCanonicalHash.ComputePublishedBallotStreamHash(publishedBallots));
        if (!string.Equals(latestTranscript.PublishedBallotStreamHash, publishedHash, StringComparison.Ordinal))
        {
            return ElectionSp07PublicationProofGateResult.Failed(
                VerificationResultCodes.PublicationProofPublishedStreamMismatch,
                "SP-07 publication-proof transcript does not match the published ballot stream.",
                ElectionClosedProgressStatus.PublicationProofFailed);
        }

        if (latestTranscript.AcceptedBallotCount != acceptedBallots.Count ||
            latestTranscript.PublishedBallotCount != publishedBallots.Count)
        {
            return ElectionSp07PublicationProofGateResult.Failed(
                VerificationResultCodes.PublicationProofCountMismatch,
                "SP-07 publication-proof transcript ballot counts do not match the close/counting inventories.",
                ElectionClosedProgressStatus.PublicationProofFailed);
        }

        if (!string.Equals(latestTranscript.ProofMode, ElectionSp07ProfileIds.PublicationProofMode, StringComparison.Ordinal) ||
            !string.Equals(latestTranscript.ProofConstruction, ElectionSp07ProfileIds.ProofConstruction, StringComparison.Ordinal) ||
            !string.Equals(latestTranscript.StatementId, ElectionSp07ProfileIds.StatementId, StringComparison.Ordinal))
        {
            return ElectionSp07PublicationProofGateResult.Failed(
                VerificationResultCodes.PublicationProofTranscriptInvalid,
                "SP-07 publication-proof transcript uses an incompatible proof profile.",
                ElectionClosedProgressStatus.PublicationProofFailed);
        }

        var latestDeletionReceipt = deletionReceipts
            .Where(x => x.ProofSessionId == latestTranscript.ProofSessionId)
            .OrderByDescending(x => x.DeletedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        if (latestDeletionReceipt?.DeletionStatus != ElectionPublicationWitnessDeletionStatus.Completed)
        {
            return ElectionSp07PublicationProofGateResult.Failed(
                VerificationResultCodes.PublicationProofWitnessDeletionMissing,
                "High-assurance close/counting requires an SP-07 witness deletion receipt before tally_ready.",
                ElectionClosedProgressStatus.PublicationProofSelfVerifying);
        }

        if (!string.Equals(latestDeletionReceipt.TranscriptHash, latestTranscript.TranscriptHash, StringComparison.Ordinal) ||
            !string.Equals(latestDeletionReceipt.ProofHash, latestTranscript.ProofHash, StringComparison.Ordinal))
        {
            return ElectionSp07PublicationProofGateResult.Failed(
                VerificationResultCodes.PublicationProofWitnessDeletionInvalid,
                "SP-07 witness deletion receipt does not match the verified transcript.",
                ElectionClosedProgressStatus.PublicationProofFailed);
        }

        return ElectionSp07PublicationProofGateResult.Passed();
    }

    public static bool IsEvidenceExpected(ElectionRecord election) =>
        !election.SelectedProfileDevOnly &&
        (string.Equals(election.SelectedProfileId, ElectionSelectableProfileCatalog.TrusteeProductionProfileId, StringComparison.Ordinal) ||
         string.Equals(election.SelectedProfileId, ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId, StringComparison.Ordinal) ||
         string.Equals(election.ControlDomainProfileId, ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1, StringComparison.Ordinal));

    private static string ToLowerHex(byte[] bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();

    private static ElectionClosedProgressStatus MapProgressStatus(ElectionPublicationProofSessionStatus sessionStatus) =>
        sessionStatus switch
        {
            ElectionPublicationProofSessionStatus.Generating => ElectionClosedProgressStatus.PublicationProofGenerating,
            ElectionPublicationProofSessionStatus.SelfVerifying => ElectionClosedProgressStatus.PublicationProofSelfVerifying,
            _ => ElectionClosedProgressStatus.PublicationProofPending,
        };

    private static bool MatchesOptionalHash(string? sessionHash, string transcriptHash) =>
        string.IsNullOrWhiteSpace(sessionHash) ||
        string.Equals(sessionHash, transcriptHash, StringComparison.Ordinal);
}

internal sealed record ElectionSp07PublicationProofGateResult(
    bool IsSatisfied,
    string? FailureCode,
    string? FailureMessage,
    ElectionClosedProgressStatus ProgressStatus)
{
    public static ElectionSp07PublicationProofGateResult Passed() =>
        new(true, null, null, ElectionClosedProgressStatus.PublicationProofVerified);

    public static ElectionSp07PublicationProofGateResult Failed(
        string failureCode,
        string failureMessage,
        ElectionClosedProgressStatus progressStatus) =>
        new(false, failureCode, failureMessage, progressStatus);
}
