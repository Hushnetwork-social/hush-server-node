using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using HushShared.Elections.PublicationProof;
using HushShared.Elections.Verification.Model;
using Moq;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionSp07PublicationProofSessionRunnerTests
{
    [Fact]
    public async Task RunAsync_ShouldPersistVerifiedSessionAndManifestTranscript()
    {
        var exportRequest = ElectionVerificationPackageExportServiceTests.CreateRequest(
            VerificationPackageView.PublicAnonymous,
            profileId: VerificationProfileIds.HighAssuranceV1);
        var repository = new Mock<IElectionsRepository>();
        var savedSessions = new List<ElectionPublicationProofSessionRecord>();
        var updatedSessions = new List<ElectionPublicationProofSessionRecord>();
        var savedTranscripts = new List<ElectionPublicationProofTranscriptRecord>();
        repository.Setup(x => x.GetAcceptedBallotsAsync(exportRequest.Election.ElectionId))
            .ReturnsAsync(exportRequest.AcceptedBallots);
        repository.Setup(x => x.GetPublishedBallotsAsync(exportRequest.Election.ElectionId))
            .ReturnsAsync(exportRequest.PublishedBallots);
        repository.Setup(x => x.GetPublicationWitnessesAsync(exportRequest.Election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionPublicationWitnessRecord>());
        repository.Setup(x => x.SavePublicationProofSessionAsync(It.IsAny<ElectionPublicationProofSessionRecord>()))
            .Callback<ElectionPublicationProofSessionRecord>(savedSessions.Add)
            .Returns(Task.CompletedTask);
        repository.Setup(x => x.UpdatePublicationProofSessionAsync(It.IsAny<ElectionPublicationProofSessionRecord>()))
            .Callback<ElectionPublicationProofSessionRecord>(updatedSessions.Add)
            .Returns(Task.CompletedTask);
        repository.Setup(x => x.SavePublicationProofTranscriptAsync(It.IsAny<ElectionPublicationProofTranscriptRecord>()))
            .Callback<ElectionPublicationProofTranscriptRecord>(savedTranscripts.Add)
            .Returns(Task.CompletedTask);
        var worker = new FakeRustWorkerClient();
        var runner = new ElectionSp07PublicationProofSessionRunner(
            new FakeProductionProofInputBuilder(),
            new Sp07PublicationProofChunkCoordinator(
                worker,
                new Sp07PublicationProofChunkCoordinatorOptions(CreateTempDirectory())),
            new ElectionSp07PublicationProofManifestBuilder());

        var result = await runner.RunAsync(new ElectionSp07PublicationProofSessionRunnerRequest(
            repository.Object,
            exportRequest.Election,
            ProtocolPackageBinding: null,
            VerificationProfileIds.HighAssuranceV1,
            DateTime.UnixEpoch.AddHours(20)));

        result.IsSuccessful.Should().BeTrue();
        savedSessions.Should().ContainSingle();
        updatedSessions.Should().ContainSingle();
        savedTranscripts.Should().ContainSingle();
        updatedSessions[0].Status.Should().Be(ElectionPublicationProofSessionStatus.Verified);
        updatedSessions[0].TranscriptHash.Should().Be(savedTranscripts[0].TranscriptHash);
        updatedSessions[0].ProofHash.Should().Be(savedTranscripts[0].ProofHash);
        savedTranscripts[0].ProofBytes.Should().Contain(ElectionSp07PublicationProofManifestArtifactRecord.SchemaVersion);
        savedTranscripts[0].CanonicalProofBytesHex.Should().NotBeNullOrWhiteSpace();
        worker.ProofJobs.Should().ContainSingle();
        worker.ProofJobs[0].ProductionProofInput.Should().NotBeNull();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "hush-sp07-session-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeProductionProofInputBuilder : IElectionSp07ProductionProofInputBuilder
    {
        public ElectionSp07ProductionProofInputBuildResult Build(
            ElectionId electionId,
            IReadOnlyList<ElectionAcceptedBallotRecord> acceptedBallots,
            IReadOnlyList<ElectionPublishedBallotRecord> publishedBallots,
            IReadOnlyList<ElectionPublicationWitnessRecord> witnesses,
            Sp07PublicationChunkPlan plan)
        {
            var inputs = plan.Chunks.ToDictionary(
                chunk => chunk.ChunkId,
                chunk => CreateProductionInput(chunk.Count, plan.EncryptedSlotCount),
                StringComparer.Ordinal);
            return ElectionSp07ProductionProofInputBuildResult.Success(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                acceptedBallots.Count,
                publishedBallots.Count,
                plan.EncryptedSlotCount,
                "babyjubjub-elgamal-pk-test",
                inputs);
        }

        private static Sp07RustWorkerProductionProofInput CreateProductionInput(int ballots, int slots)
        {
            var accepted = Enumerable.Range(0, ballots)
                .Select(ballot => CreateBallot(ballot, slots, "accepted"))
                .ToArray();
            var published = Enumerable.Range(0, ballots)
                .Select(ballot => CreateBallot(ballot, slots, "published"))
                .ToArray();
            return new Sp07RustWorkerProductionProofInput(
                new Sp07PointPayload("1", "2"),
                accepted,
                published,
                Enumerable.Range(0, ballots).ToArray(),
                Enumerable.Range(0, ballots)
                    .Select(_ => Enumerable.Range(0, slots).Select(slot => (slot + 1).ToString()).ToArray())
                    .ToArray());
        }

        private static Sp07CipherBallotPayload CreateBallot(int ballot, int slots, string label) =>
            new(Enumerable.Range(0, slots)
                .Select(slot => new Sp07CipherSlotPayload(
                    new Sp07PointPayload($"{label}-{ballot}-{slot}-c1-x", $"{label}-{ballot}-{slot}-c1-y"),
                    new Sp07PointPayload($"{label}-{ballot}-{slot}-c2-x", $"{label}-{ballot}-{slot}-c2-y")))
                .ToArray());
    }

    private sealed class FakeRustWorkerClient : ISp07RustWorkerClient
    {
        public List<Sp07RustWorkerProofJob> ProofJobs { get; } = [];

        public Task<Sp07RustWorkerCommandResult> ProveAsync(
            Sp07RustWorkerProofJob job,
            CancellationToken cancellationToken = default)
        {
            ProofJobs.Add(job);
            return Task.FromResult(CreateResult("prove", job));
        }

        public Task<Sp07RustWorkerCommandResult> VerifyAsync(
            Sp07RustWorkerVerifyJob job,
            CancellationToken cancellationToken = default)
        {
            var proofJob = ProofJobs.Single(x => x.ChunkId == job.ChunkId);
            return Task.FromResult(CreateResult("verify", proofJob));
        }

        private static Sp07RustWorkerCommandResult CreateResult(string command, Sp07RustWorkerProofJob job)
        {
            var proofBytes = Encoding.UTF8.GetBytes($"canonical-proof-{job.ChunkId}");
            return new Sp07RustWorkerCommandResult(
                "HushSp07RustWorkerCommandResultV1",
                "rust_arkworks_m1_process_worker",
                command,
                "completed",
                Passed: true,
                "PUB-005",
                "ok",
                job.ElectionId,
                job.ProofSessionId,
                job.ChunkId,
                "matrix_m_1_publication_proof_v1",
                "0.1.0",
                WorkerThreadCount: 2,
                StatementHashSha512: new string('a', 128),
                TranscriptHashSha512: new string('b', 128),
                ProofHashSha512: Convert.ToHexString(SHA512.HashData(proofBytes)).ToLowerInvariant(),
                AcceptedBallotSetHash: job.AcceptedBallotSetHash!,
                PublishedBallotStreamHash: job.PublishedBallotStreamHash!,
                CanonicalProofByteLength: proofBytes.Length,
                CanonicalProofBytesHex: Convert.ToHexString(proofBytes).ToLowerInvariant(),
                ProofExampleHashSha512: new string('c', 128),
                ElapsedMilliseconds: 3.5);
        }
    }
}
