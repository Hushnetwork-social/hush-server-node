using System.Text.Json;
using HushShared.Elections.Model;
using HushShared.Elections.PublicationProof;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections;

public interface IElectionSp07PublicationProofManifestBuilder
{
    ElectionSp07PublicationProofTranscriptBuildResult Build(
        ElectionSp07PublicationProofTranscriptBuildRequest request);
}

public sealed record ElectionSp07PublicationProofTranscriptBuildRequest(
    ElectionId ElectionId,
    Guid ProofSessionId,
    Guid WitnessSetId,
    string ProfileId,
    string BallotDefinitionHash,
    string BallotEncryptionSchemeVersion,
    string ElectionPublicKeyId,
    string AcceptedBallotSetHash,
    string PublishedBallotStreamHash,
    int AcceptedBallotCount,
    int PublishedBallotCount,
    int CiphertextSlotCount,
    Sp07PublicationProofSessionRunResult RunResult,
    DateTime GeneratedAt,
    string? GeneratorReleaseHash,
    string? VerifierReleaseHash);

public sealed record ElectionSp07PublicationProofTranscriptBuildResult(
    ElectionPublicationProofTranscriptRecord Transcript,
    ElectionSp07PublicationProofManifestArtifactRecord Manifest,
    string ProofBytes,
    string ProofHash,
    string TranscriptHash);

public sealed class ElectionSp07PublicationProofManifestBuilder : IElectionSp07PublicationProofManifestBuilder
{
    private static readonly IReadOnlyList<string> PublicPrivacyBoundary =
    [
        "no_hidden_permutation",
        "no_shuffle_map",
        "no_rerandomization_randomness",
        "no_raw_witness",
        "no_voter_identity",
        "no_plaintext_choice",
    ];

    public ElectionSp07PublicationProofTranscriptBuildResult Build(
        ElectionSp07PublicationProofTranscriptBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RunResult);

        if (!string.Equals(
                request.RunResult.ElectionId,
                request.ElectionId.ToString(),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SP-07 run result election id does not match the transcript request.");
        }

        if (!string.Equals(
                request.RunResult.ProofSessionId,
                request.ProofSessionId.ToString("N"),
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                request.RunResult.ProofSessionId,
                request.ProofSessionId.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("SP-07 run result proof session id does not match the transcript request.");
        }

        var manifest = BuildManifest(request);
        var proofBytes = JsonSerializer.Serialize(manifest, VerificationJson.Options);
        var proofHash = VerificationCanonicalHash.ComputeSha256LowerHex(proofBytes);
        var transcriptHash = ComputeTranscriptHash(request, proofHash);
        var singleChunkProof = request.RunResult.Chunks.Count == 1
            ? request.RunResult.Chunks[0].ProofResult
            : null;
        var transcript = new ElectionPublicationProofTranscriptRecord(
            Guid.NewGuid(),
            request.ElectionId,
            request.ProofSessionId,
            request.WitnessSetId,
            ElectionSp07ProfileIds.TranscriptVersion,
            ElectionSp07ProfileIds.PublicationProofMode,
            ElectionSp07ProfileIds.ProofConstruction,
            ElectionSp07ProfileIds.StatementId,
            request.ProfileId,
            request.BallotDefinitionHash,
            request.BallotEncryptionSchemeVersion,
            request.ElectionPublicKeyId,
            request.AcceptedBallotSetHash,
            request.PublishedBallotStreamHash,
            request.AcceptedBallotCount,
            request.PublishedBallotCount,
            request.CiphertextSlotCount,
            ElectionSp07ProfileIds.ProofSystemVersion,
            proofBytes,
            proofHash,
            transcriptHash,
            ElectionSp07ProfileIds.ExternalReviewStatus,
            request.GeneratedAt,
            request.GeneratorReleaseHash,
            request.VerifierReleaseHash,
            PublicPrivacyBoundary,
            StatementHashSha512: singleChunkProof?.StatementHashSha512,
            FiatShamirTranscriptHashSha512: singleChunkProof?.TranscriptHashSha512,
            CanonicalProofBytesHex: singleChunkProof?.CanonicalProofBytesHex,
            CanonicalProofHashSha512: singleChunkProof?.ProofHashSha512,
            CanonicalProofByteLength: singleChunkProof?.CanonicalProofByteLength);

        return new ElectionSp07PublicationProofTranscriptBuildResult(
            transcript,
            manifest,
            proofBytes,
            proofHash,
            transcriptHash);
    }

    private static ElectionSp07PublicationProofManifestArtifactRecord BuildManifest(
        ElectionSp07PublicationProofTranscriptBuildRequest request)
    {
        var chunks = request.RunResult.Chunks
            .OrderBy(chunk => chunk.ChunkIndex)
            .Select(chunk =>
            {
                var proof = chunk.ProofResult ??
                    throw new InvalidOperationException(
                        $"SP-07 chunk {chunk.ChunkId} has no proof result.");
                var verifier = chunk.VerifyResult;
                ValidateChunkStatementBinding(request, chunk, proof, "proof");
                if (verifier is not null)
                {
                    ValidateChunkStatementBinding(request, chunk, verifier, "verify");
                    if (!string.Equals(verifier.StatementHashSha512, proof.StatementHashSha512, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(verifier.TranscriptHashSha512, proof.TranscriptHashSha512, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(verifier.ProofHashSha512, proof.ProofHashSha512, StringComparison.OrdinalIgnoreCase) ||
                        verifier.CanonicalProofByteLength != proof.CanonicalProofByteLength)
                    {
                        throw new InvalidOperationException(
                            $"SP-07 chunk {chunk.ChunkId} verify result does not bind the same canonical proof as the proof result.");
                    }
                }

                return new ElectionSp07PublicationProofManifestChunkArtifactRecord(
                    chunk.ChunkId,
                    chunk.ChunkIndex,
                    chunk.Offset,
                    chunk.Count,
                    chunk.Passed,
                    verifier?.ResultCode ?? proof.ResultCode,
                    proof.ProofProfileId,
                    proof.WorkerKind,
                    proof.WorkerVersion,
                    proof.WorkerThreadCount,
                    proof.StatementHashSha512,
                    proof.TranscriptHashSha512,
                    proof.ProofHashSha512,
                    proof.CanonicalProofByteLength,
                    proof.CanonicalProofBytesHex,
                    proof.PublishedBallotStreamHash,
                    chunk.ElapsedMilliseconds,
                    proof.Telemetry?.GenerationMilliseconds,
                    proof.Telemetry?.SelfVerificationMilliseconds,
                    proof.Telemetry?.CpuTimeMilliseconds,
                    proof.Telemetry?.MemoryNotes);
            })
            .ToArray();

        return new ElectionSp07PublicationProofManifestArtifactRecord(
            ElectionSp07PublicationProofManifestArtifactRecord.SchemaVersion,
            request.ElectionId.ToString(),
            request.ProofSessionId.ToString("N"),
            request.RunResult.PlanId,
            ElectionSp07ProfileIds.PublicationProofMode,
            ElectionSp07ProfileIds.ProofConstruction,
            ElectionSp07ProfileIds.StatementId,
            request.ProfileId,
            request.AcceptedBallotSetHash,
            request.PublishedBallotStreamHash,
            request.AcceptedBallotCount,
            request.PublishedBallotCount,
            request.CiphertextSlotCount,
            request.RunResult.ChunkCount,
            request.RunResult.CompletedChunkCount,
            request.RunResult.FailedChunkCount,
            request.RunResult.SlowestChunkMilliseconds,
            chunks,
            PublicPrivacyBoundary);
    }

    private static void ValidateChunkStatementBinding(
        ElectionSp07PublicationProofTranscriptBuildRequest request,
        Sp07PublicationProofChunkRunResult chunk,
        Sp07RustWorkerCommandResult result,
        string resultKind)
    {
        if (!string.Equals(result.ElectionId, request.ElectionId.ToString(), StringComparison.Ordinal) ||
            !ProofSessionIdMatches(result.ProofSessionId, request.ProofSessionId) ||
            !string.Equals(result.ChunkId, chunk.ChunkId, StringComparison.Ordinal) ||
            !string.Equals(result.AcceptedBallotSetHash, request.AcceptedBallotSetHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(result.PublishedBallotStreamHash, request.PublishedBallotStreamHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SP-07 chunk {chunk.ChunkId} {resultKind} result does not bind the transcript election, session, chunk, accepted set, or published stream.");
        }
    }

    private static bool ProofSessionIdMatches(string resultProofSessionId, Guid proofSessionId) =>
        string.Equals(resultProofSessionId, proofSessionId.ToString("N"), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resultProofSessionId, proofSessionId.ToString(), StringComparison.OrdinalIgnoreCase);

    private static string ComputeTranscriptHash(
        ElectionSp07PublicationProofTranscriptBuildRequest request,
        string proofHash) =>
        VerificationCanonicalHash.ComputeSha256LowerHex(string.Join(
            '\n',
            "HUSH_SP07_PUBLICATION_PROOF_TRANSCRIPT_V1",
            request.ElectionId.ToString(),
            request.ProofSessionId.ToString("N"),
            request.WitnessSetId.ToString("N"),
            request.ProfileId,
            request.BallotDefinitionHash,
            request.AcceptedBallotSetHash,
            request.PublishedBallotStreamHash,
            proofHash));
}
