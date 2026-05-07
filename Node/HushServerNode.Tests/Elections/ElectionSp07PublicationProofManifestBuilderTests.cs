using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using HushShared.Elections.PublicationProof;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionSp07PublicationProofManifestBuilderTests
{
    [Fact]
    public void Build_ShouldCreateDeterministicPublicManifestTranscript()
    {
        var request = CreateRequest(chunkCount: 2);
        var builder = new ElectionSp07PublicationProofManifestBuilder();

        var first = builder.Build(request);
        var second = builder.Build(request);

        first.ProofBytes.Should().Be(second.ProofBytes);
        first.ProofHash.Should().Be(second.ProofHash);
        first.TranscriptHash.Should().Be(second.TranscriptHash);
        first.ProofHash.Should().Be(VerificationCanonicalHash.ComputeSha256LowerHex(first.ProofBytes));
        first.Manifest.Schema.Should().Be(ElectionSp07PublicationProofManifestArtifactRecord.SchemaVersion);
        first.Manifest.ChunkCount.Should().Be(2);
        first.Manifest.CompletedChunkCount.Should().Be(2);
        first.Manifest.FailedChunkCount.Should().Be(0);
        first.Manifest.Chunks.Select(x => x.ChunkId).Should().Equal("chunk-0001", "chunk-0002");
        first.Manifest.Chunks.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.CanonicalProofBytesHex));
        first.Manifest.Chunks.Should().OnlyContain(x =>
            x.GenerationMilliseconds > 0 &&
            x.SelfVerificationMilliseconds > 0 &&
            x.CpuTimeMilliseconds > 0 &&
            x.MemoryNotes.Count > 0);
        first.Transcript.CanonicalProofBytesHex.Should().BeNull();
        first.Transcript.ProofBytes.Should().Contain(ElectionSp07PublicationProofManifestArtifactRecord.SchemaVersion);
        first.Transcript.PublicPrivacyBoundary.Should().Contain("no_rerandomization_randomness");
        first.Transcript.ProofBytes.Should().NotContain("publishedToAccepted");
        first.Transcript.ProofBytes.Should().NotContain("rerandomizationNonces");
    }

    [Fact]
    public void Build_WhenSingleChunk_ShouldCopyCanonicalFieldsToTranscriptForLegacyVerifierPath()
    {
        var request = CreateRequest(chunkCount: 1);

        var result = new ElectionSp07PublicationProofManifestBuilder().Build(request);
        var chunk = result.Manifest.Chunks[0];

        result.Transcript.StatementHashSha512.Should().Be(chunk.StatementHashSha512);
        result.Transcript.FiatShamirTranscriptHashSha512.Should().Be(chunk.FiatShamirTranscriptHashSha512);
        result.Transcript.CanonicalProofHashSha512.Should().Be(chunk.CanonicalProofHashSha512);
        result.Transcript.CanonicalProofByteLength.Should().Be(chunk.CanonicalProofByteLength);
        result.Transcript.CanonicalProofBytesHex.Should().Be(chunk.CanonicalProofBytesHex);
    }

    [Theory]
    [InlineData("proof")]
    [InlineData("verify")]
    public void Build_WhenChunkAcceptedSetBindingDiffers_ShouldFailClosed(string resultKind)
    {
        var request = CreateRequest(chunkCount: 1);
        var chunk = request.RunResult.Chunks[0];
        var tampered = chunk.ProofResult! with
        {
            AcceptedBallotSetHash = "different-accepted-set-hash",
        };
        var tamperedChunk = string.Equals(resultKind, "proof", StringComparison.Ordinal)
            ? chunk with
            {
                ProofResult = tampered,
                VerifyResult = chunk.VerifyResult,
            }
            : chunk with
            {
                ProofResult = chunk.ProofResult,
                VerifyResult = tampered with { Command = "verify" },
            };
        request = request with
        {
            RunResult = request.RunResult with
            {
                Chunks = [tamperedChunk],
            },
        };

        var act = () => new ElectionSp07PublicationProofManifestBuilder().Build(request);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*does not bind the transcript election, session, chunk, accepted set, or published stream*");
    }

    [Fact]
    public void Build_WhenVerifyResultBindsDifferentCanonicalProof_ShouldFailClosed()
    {
        var request = CreateRequest(chunkCount: 1);
        var chunk = request.RunResult.Chunks[0];
        var tamperedChunk = chunk with
        {
            VerifyResult = chunk.VerifyResult! with
            {
                ProofHashSha512 = new string('f', 128),
            },
        };
        request = request with
        {
            RunResult = request.RunResult with
            {
                Chunks = [tamperedChunk],
            },
        };

        var act = () => new ElectionSp07PublicationProofManifestBuilder().Build(request);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*verify result does not bind the same canonical proof as the proof result*");
    }

    private static ElectionSp07PublicationProofTranscriptBuildRequest CreateRequest(int chunkCount)
    {
        var proofSessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var witnessSetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var electionId = new ElectionId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var chunks = Enumerable.Range(1, chunkCount)
            .Select(CreateChunk)
            .ToArray();
        var runResult = new Sp07PublicationProofSessionRunResult(
            electionId.ToString(),
            proofSessionId.ToString("N"),
            "sp07-plan-test",
            Passed: true,
            ChunkCount: chunkCount,
            CompletedChunkCount: chunkCount,
            FailedChunkCount: 0,
            SlowestChunkMilliseconds: chunks.Max(x => x.ElapsedMilliseconds),
            chunks);

        return new ElectionSp07PublicationProofTranscriptBuildRequest(
            electionId,
            proofSessionId,
            witnessSetId,
            VerificationProfileIds.HighAssuranceV1,
            "ballot-definition-hash",
            "babyjubjub-elgamal-vector-ballot-v1",
            "election-public-key-id",
            "accepted-set-hash",
            "published-stream-hash",
            AcceptedBallotCount: chunkCount * 10,
            PublishedBallotCount: chunkCount * 10,
            CiphertextSlotCount: 8,
            runResult,
            DateTime.UnixEpoch.AddHours(12),
            GeneratorReleaseHash: "generator-release-hash",
            VerifierReleaseHash: "verifier-release-hash");
    }

    private static Sp07PublicationProofChunkRunResult CreateChunk(int index)
    {
        var proof = Encoding.UTF8.GetBytes($"canonical-proof-{index}");
        var proofHex = Convert.ToHexString(proof).ToLowerInvariant();
        var proofHash = Convert.ToHexString(SHA512.HashData(proof)).ToLowerInvariant();
        var result = new Sp07RustWorkerCommandResult(
            "HushSp07RustWorkerCommandResultV1",
            "rust_arkworks_m1_process_worker",
            "prove",
            "completed",
            Passed: true,
            "PUB-005",
            "ok",
            Guid.Parse("33333333-3333-3333-3333-333333333333").ToString(),
            Guid.Parse("11111111-1111-1111-1111-111111111111").ToString("N"),
            $"chunk-{index:D4}",
            "matrix_m_1_publication_proof_v1",
            "0.1.0",
            WorkerThreadCount: 4,
            StatementHashSha512: new string((char)('a' + index - 1), 128),
            TranscriptHashSha512: new string((char)('c' + index - 1), 128),
            ProofHashSha512: proofHash,
            AcceptedBallotSetHash: "accepted-set-hash",
            PublishedBallotStreamHash: "published-stream-hash",
            CanonicalProofByteLength: proof.Length,
            CanonicalProofBytesHex: proofHex,
            ProofExampleHashSha512: new string('e', 128),
            ElapsedMilliseconds: 10 + index,
            Telemetry: new Sp07RustWorkerTelemetry(
                GenerationMilliseconds: 8 + index,
                SelfVerificationMilliseconds: 1.25,
                ProofSizeBytes: proof.Length,
                CpuTimeMilliseconds: (10 + index) * 4,
                MemoryNotes:
                [
                    "test memory note",
                ],
                PhaseTimings: new Dictionary<string, double>
                {
                    ["generation"] = 8 + index,
                    ["self_verification"] = 1.25,
                }));

        return new Sp07PublicationProofChunkRunResult(
            $"chunk-{index:D4}",
            ChunkIndex: index - 1,
            Offset: (index - 1) * 10,
            Count: 10,
            Passed: true,
            WorkDirectory: $"work-{index}",
            RequestPath: $"request-{index}.json",
            ProofResultPath: $"proof-{index}.json",
            VerifyResultPath: $"verify-{index}.json",
            ProofResult: result,
            VerifyResult: result with { Command = "verify" },
            ElapsedMilliseconds: 10 + index);
    }
}
