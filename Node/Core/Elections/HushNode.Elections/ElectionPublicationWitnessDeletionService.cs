using System.Text;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections;

public interface IElectionPublicationWitnessDeletionService
{
    Task<ElectionPublicationWitnessDeletionResult> TryDeleteVerifiedWitnessesAsync(
        IElectionsRepository repository,
        ElectionId electionId,
        DateTime deletedAt);
}

public sealed record ElectionPublicationWitnessDeletionResult(
    bool IsCompleted,
    string? FailureCode,
    string? FailureReason,
    ElectionPublicationWitnessDeletionReceiptRecord? Receipt)
{
    public static ElectionPublicationWitnessDeletionResult Completed(
        ElectionPublicationWitnessDeletionReceiptRecord receipt) =>
        new(true, null, null, receipt);

    public static ElectionPublicationWitnessDeletionResult NotReady(string failureCode, string failureReason) =>
        new(false, failureCode, failureReason, null);
}

public sealed class ElectionPublicationWitnessDeletionService : IElectionPublicationWitnessDeletionService
{
    private const string DeletionActorRef = "hush-server-node:sp07-publication-witness-deletion-service";
    private const string DeletedWitnessMaterialPrefix = "deleted:sp07-publication-witness:";

    public async Task<ElectionPublicationWitnessDeletionResult> TryDeleteVerifiedWitnessesAsync(
        IElectionsRepository repository,
        ElectionId electionId,
        DateTime deletedAt)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var latestSession = (await repository.GetPublicationProofSessionsAsync(electionId))
            .OrderByDescending(x => x.CompletedAt ?? x.StartedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        if (latestSession is null)
        {
            return ElectionPublicationWitnessDeletionResult.NotReady(
                VerificationResultCodes.PublicationProofTranscriptMissing,
                "SP-07 witness deletion requires a verified publication-proof session.");
        }

        if (latestSession.Status is not ElectionPublicationProofSessionStatus.Verified and
            not ElectionPublicationProofSessionStatus.WitnessDeleted)
        {
            return ElectionPublicationWitnessDeletionResult.NotReady(
                VerificationResultCodes.PublicationProofEvidencePending,
                "SP-07 witness deletion waits until the publication-proof session is verified.");
        }

        var latestTranscript = (await repository.GetPublicationProofTranscriptsAsync(electionId))
            .Where(x => x.ProofSessionId == latestSession.Id)
            .OrderByDescending(x => x.GeneratedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        if (latestTranscript is null)
        {
            return ElectionPublicationWitnessDeletionResult.NotReady(
                VerificationResultCodes.PublicationProofTranscriptMissing,
                "SP-07 witness deletion requires the verified publication-proof transcript.");
        }

        if (!MatchesSession(latestSession, latestTranscript))
        {
            return ElectionPublicationWitnessDeletionResult.NotReady(
                VerificationResultCodes.PublicationProofTranscriptInvalid,
                "SP-07 witness deletion refused a session/transcript hash mismatch.");
        }

        var existingReceipt = (await repository.GetPublicationWitnessDeletionReceiptsAsync(electionId))
            .Where(x =>
                x.ProofSessionId == latestSession.Id &&
                x.DeletionStatus == ElectionPublicationWitnessDeletionStatus.Completed &&
                string.Equals(x.TranscriptHash, latestTranscript.TranscriptHash, StringComparison.Ordinal) &&
                string.Equals(x.ProofHash, latestTranscript.ProofHash, StringComparison.Ordinal))
            .OrderByDescending(x => x.DeletedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        if (existingReceipt is not null)
        {
            await MarkSessionWitnessDeletedAsync(repository, latestSession, existingReceipt, deletedAt);
            return ElectionPublicationWitnessDeletionResult.Completed(existingReceipt);
        }

        var witnesses = (await repository.GetPublicationWitnessesAsync(electionId, latestSession.WitnessSetId))
            .OrderBy(x => x.PublishedSequence ?? long.MaxValue)
            .ThenBy(x => x.AcceptedBallotId)
            .ThenBy(x => x.Id)
            .ToArray();
        if (witnesses.Length == 0)
        {
            return ElectionPublicationWitnessDeletionResult.NotReady(
                VerificationResultCodes.PublicationProofWitnessDeletionMissing,
                "SP-07 witness deletion requires sealed witness records for the verified witness set.");
        }

        if (witnesses.Any(x => x.CustodyStatus != ElectionPublicationWitnessCustodyStatus.Sealed))
        {
            return ElectionPublicationWitnessDeletionResult.NotReady(
                VerificationResultCodes.PublicationProofWitnessDeletionInvalid,
                "SP-07 witness deletion can only run while every witness in the verified set is still sealed.");
        }

        var witnessSetHash = ComputeWitnessSetHash(witnesses);
        var receipt = new ElectionPublicationWitnessDeletionReceiptRecord(
            Guid.NewGuid(),
            electionId,
            latestSession.Id,
            latestSession.WitnessSetId,
            witnessSetHash,
            witnesses.Length,
            latestTranscript.TranscriptHash,
            latestTranscript.ProofHash,
            ElectionPublicationWitnessDeletionStatus.Completed,
            deletedAt,
            DeletionActorRef,
            FailureCode: null,
            FailureReason: null);

        foreach (var witness in witnesses)
        {
            await repository.UpdatePublicationWitnessAsync(witness with
            {
                SealedWitnessMaterial = $"{DeletedWitnessMaterialPrefix}{witness.Id:N}",
                CustodyStatus = ElectionPublicationWitnessCustodyStatus.Deleted,
                DeletedAt = deletedAt,
            });
        }

        await repository.SavePublicationWitnessDeletionReceiptAsync(receipt);
        await MarkSessionWitnessDeletedAsync(repository, latestSession, receipt, deletedAt);

        return ElectionPublicationWitnessDeletionResult.Completed(receipt);
    }

    internal static string ComputeWitnessSetHash(IReadOnlyList<ElectionPublicationWitnessRecord> witnesses)
    {
        ArgumentNullException.ThrowIfNull(witnesses);

        var builder = new StringBuilder("sp07-publication-witness-set-v1");
        foreach (var witness in witnesses
                     .OrderBy(x => x.PublishedSequence ?? long.MaxValue)
                     .ThenBy(x => x.AcceptedBallotId)
                     .ThenBy(x => x.Id))
        {
            builder.Append('\n');
            builder.Append(witness.Id.ToString("N"));
            builder.Append('|');
            builder.Append(witness.AcceptedBallotId.ToString("N"));
            builder.Append('|');
            builder.Append(witness.PublishedSequence?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null");
            builder.Append('|');
            builder.Append(witness.AcceptedEncryptedBallotHash);
            builder.Append('|');
            builder.Append(witness.PublishedEncryptedBallotHash ?? string.Empty);
            builder.Append('|');
            builder.Append(witness.ProofMode);
            builder.Append('|');
            builder.Append(witness.ProofConstruction);
            builder.Append('|');
            builder.Append(witness.StatementId);
            builder.Append('|');
            builder.Append(witness.ProofProfileVersion);
            builder.Append('|');
            builder.Append(witness.SealedWitnessMaterialHash);
            builder.Append('|');
            builder.Append(witness.SealAlgorithm);
        }

        return VerificationCanonicalHash.ComputeSha256LowerHex(builder.ToString());
    }

    private static bool MatchesSession(
        ElectionPublicationProofSessionRecord session,
        ElectionPublicationProofTranscriptRecord transcript) =>
        MatchesOptional(session.AcceptedBallotSetHash, transcript.AcceptedBallotSetHash) &&
        MatchesOptional(session.PublishedBallotStreamHash, transcript.PublishedBallotStreamHash) &&
        MatchesOptional(session.TranscriptHash, transcript.TranscriptHash) &&
        MatchesOptional(session.ProofHash, transcript.ProofHash) &&
        session.WitnessSetId == transcript.WitnessSetId;

    private static bool MatchesOptional(string? expected, string actual) =>
        string.IsNullOrWhiteSpace(expected) ||
        string.Equals(expected.Trim(), actual, StringComparison.Ordinal);

    private static async Task MarkSessionWitnessDeletedAsync(
        IElectionsRepository repository,
        ElectionPublicationProofSessionRecord session,
        ElectionPublicationWitnessDeletionReceiptRecord receipt,
        DateTime completedAt)
    {
        if (session.Status == ElectionPublicationProofSessionStatus.WitnessDeleted &&
            session.DeletionReceiptId == receipt.Id)
        {
            return;
        }

        await repository.UpdatePublicationProofSessionAsync(session with
        {
            Status = ElectionPublicationProofSessionStatus.WitnessDeleted,
            CompletedAt = completedAt,
            DeletionReceiptId = receipt.Id,
        });
    }
}
