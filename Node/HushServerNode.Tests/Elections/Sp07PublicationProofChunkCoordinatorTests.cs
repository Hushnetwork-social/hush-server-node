using FluentAssertions;
using HushShared.Elections.PublicationProof;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class Sp07PublicationProofChunkCoordinatorTests
{
    [Fact]
    public async Task RunAsync_ShouldRunProveAndVerifyForEveryChunkAndReturnOrderedResults()
    {
        var temp = CreateTempDirectory();
        try
        {
            var plan = CreatePlanner().CreatePlan(250, 8);
            var worker = new FakeRustWorkerClient();
            var coordinator = new Sp07PublicationProofChunkCoordinator(
                worker,
                new Sp07PublicationProofChunkCoordinatorOptions(temp, MaxParallelWorkers: 2));
            var statementBinding = new Sp07PublicationProofStatementBinding(
                ProtocolPackageHash: "protocol-package-hash",
                BallotDefinitionHash: "ballot-definition-hash",
                AcceptedBallotSetHash: "accepted-ballot-set-hash",
                PublishedBallotStreamHash: "published-ballot-stream-hash");

            var result = await coordinator.RunAsync("election/1", "session:1", plan, statementBinding);

            result.Passed.Should().BeTrue();
            result.ChunkCount.Should().Be(3);
            result.CompletedChunkCount.Should().Be(3);
            result.FailedChunkCount.Should().Be(0);
            result.Chunks.Select(chunk => chunk.ChunkIndex).Should().Equal(0, 1, 2);
            result.Chunks.Select(chunk => chunk.Count).Should().Equal(84, 83, 83);
            result.Chunks.Should().OnlyContain(chunk => chunk.ProofResult != null);
            result.Chunks.Should().OnlyContain(chunk => chunk.VerifyResult != null);
            result.Chunks.Should().OnlyContain(chunk => chunk.WorkDirectory.StartsWith(temp, StringComparison.Ordinal));
            worker.ProofJobs.Should().HaveCount(3);
            worker.VerifyJobs.Should().HaveCount(3);
            worker.ProofJobs.Select(job => job.Ballots).Should().Equal(84, 83, 83);
            worker.ProofJobs.Should().OnlyContain(job => job.Slots == 8);
            worker.ProofJobs.Should().OnlyContain(job => job.ProtocolPackageHash == "protocol-package-hash");
            worker.ProofJobs.Should().OnlyContain(job => job.BallotDefinitionHash == "ballot-definition-hash");
            worker.ProofJobs.Should().OnlyContain(job => job.AcceptedBallotSetHash == "accepted-ballot-set-hash");
            worker.ProofJobs.Should().OnlyContain(job => job.PublishedBallotStreamHash == "published-ballot-stream-hash");
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    [Fact]
    public async Task RunAsync_WhenAWorkerChunkFails_ShouldCaptureFailureAndFailTheSession()
    {
        var temp = CreateTempDirectory();
        try
        {
            var plan = CreatePlanner().CreatePlan(120, 8);
            var worker = new FakeRustWorkerClient(failChunkSuffix: "chunk-0002");
            var coordinator = new Sp07PublicationProofChunkCoordinator(
                worker,
                new Sp07PublicationProofChunkCoordinatorOptions(temp, MaxParallelWorkers: 2));

            var result = await coordinator.RunAsync("election-1", "session-1", plan);

            result.Passed.Should().BeFalse();
            result.ChunkCount.Should().Be(2);
            result.CompletedChunkCount.Should().Be(1);
            result.FailedChunkCount.Should().Be(1);
            result.Chunks[1].Passed.Should().BeFalse();
            result.Chunks[1].FailureCode.Should().Be("sp07_chunk_worker_failed");
            result.Chunks[1].FailureMessage.Should().Contain("synthetic worker failure");
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    [Fact]
    public async Task RunAsync_WhenChunkProofExceedsApprovedCeiling_ShouldFailTheSession()
    {
        var temp = CreateTempDirectory();
        try
        {
            var plan = CreatePlanner().CreatePlan(12, 4);
            var worker = new FakeRustWorkerClient(reportedProofMilliseconds: 6000);
            var coordinator = new Sp07PublicationProofChunkCoordinator(
                worker,
                new Sp07PublicationProofChunkCoordinatorOptions(
                    temp,
                    MaxApprovedChunkProofMilliseconds: 5000));

            var result = await coordinator.RunAsync("election-1", "session-1", plan);

            result.Passed.Should().BeFalse();
            result.FailedChunkCount.Should().Be(1);
            result.Chunks[0].FailureCode.Should().Be("sp07_chunk_performance_target_missed");
            result.Chunks[0].FailureMessage.Should().Contain("above the approved 5000ms ceiling");
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    [Fact]
    public async Task RunAsync_WhenProductionInputsAreProvided_ShouldForwardInputForEachChunk()
    {
        var temp = CreateTempDirectory();
        try
        {
            var plan = CreatePlanner().CreatePlan(12, 4);
            var worker = new FakeRustWorkerClient();
            var coordinator = new Sp07PublicationProofChunkCoordinator(
                worker,
                new Sp07PublicationProofChunkCoordinatorOptions(temp));
            var input = CreateProductionProofInput(ballots: 12, slots: 4);

            var result = await coordinator.RunAsync(
                "election-1",
                "session-1",
                plan,
                productionProofInputsByChunkId: new Dictionary<string, Sp07RustWorkerProductionProofInput>
                {
                    [plan.Chunks[0].ChunkId] = input
                });

            result.Passed.Should().BeTrue();
            worker.ProofJobs.Should().ContainSingle();
            worker.ProofJobs[0].ProductionProofInput.Should().BeSameAs(input);
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    [Fact]
    public async Task RunAsync_WhenProductionInputMapMissesChunk_ShouldFailThatChunk()
    {
        var temp = CreateTempDirectory();
        try
        {
            var plan = CreatePlanner().CreatePlan(12, 4);
            var worker = new FakeRustWorkerClient();
            var coordinator = new Sp07PublicationProofChunkCoordinator(
                worker,
                new Sp07PublicationProofChunkCoordinatorOptions(temp));

            var result = await coordinator.RunAsync(
                "election-1",
                "session-1",
                plan,
                productionProofInputsByChunkId: new Dictionary<string, Sp07RustWorkerProductionProofInput>());

            result.Passed.Should().BeFalse();
            result.FailedChunkCount.Should().Be(1);
            result.Chunks[0].FailureCode.Should().Be("sp07_chunk_worker_failed");
            result.Chunks[0].FailureMessage.Should().Contain("production proof input");
            worker.ProofJobs.Should().BeEmpty();
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    [Fact]
    public async Task ConfiguredWorker_ShouldRunOneChunkThroughCoordinator_WhenWorkerBinaryIsAvailable()
    {
        var workerPath = Environment.GetEnvironmentVariable(
            Sp07RustWorkerProcessOptions.WorkerPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(workerPath))
        {
            return;
        }

        var temp = CreateTempDirectory();
        try
        {
            var plan = CreatePlanner().CreatePlan(12, 4);
            var worker = new Sp07RustWorkerProcessClient(
                Sp07RustWorkerProcessOptions.FromEnvironment(defaultTimeout: TimeSpan.FromSeconds(60)));
            var coordinator = new Sp07PublicationProofChunkCoordinator(
                worker,
                new Sp07PublicationProofChunkCoordinatorOptions(temp));

            var result = await coordinator.RunAsync("election-1", "session-1", plan);

            result.Passed.Should().BeTrue();
            result.ChunkCount.Should().Be(1);
            result.FailedChunkCount.Should().Be(0);
            result.Chunks[0].ProofResult?.ResultCode.Should().Be("PUB-005");
            result.Chunks[0].VerifyResult?.ResultCode.Should().Be("PUB-005");
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    private static Sp07PublicationChunkPlanner CreatePlanner() =>
        new(new Sp07PublicationChunkPlannerOptions(
            MaxBallotsPerChunk: 100,
            MinBallotsPerChunk: 10,
            MaxChunks: 5,
            MaxEncryptedSlots: 8));

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "hush-sp07-coordinator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static Sp07RustWorkerProductionProofInput CreateProductionProofInput(int ballots, int slots)
    {
        var accepted = Enumerable.Range(0, ballots)
            .Select(ballot => CreateBallot(ballot, slots, "accepted"))
            .ToArray();
        var published = Enumerable.Range(0, ballots)
            .Select(ballot => CreateBallot(ballot, slots, "published"))
            .ToArray();
        var rerandomization = Enumerable.Range(0, ballots)
            .Select(_ => Enumerable.Range(0, slots).Select(slot => (slot + 1).ToString()).ToArray())
            .ToArray();

        return new Sp07RustWorkerProductionProofInput(
            new Sp07PointPayload("1", "2"),
            accepted,
            published,
            Enumerable.Range(0, ballots).ToArray(),
            rerandomization);
    }

    private static Sp07CipherBallotPayload CreateBallot(int ballot, int slots, string label) =>
        new(Enumerable.Range(0, slots)
            .Select(slot => new Sp07CipherSlotPayload(
                new Sp07PointPayload($"{label}-{ballot}-{slot}-c1-x", $"{label}-{ballot}-{slot}-c1-y"),
                new Sp07PointPayload($"{label}-{ballot}-{slot}-c2-x", $"{label}-{ballot}-{slot}-c2-y")))
            .ToArray());

    private sealed class FakeRustWorkerClient(
        string? failChunkSuffix = null,
        double reportedProofMilliseconds = 1.5) : ISp07RustWorkerClient
    {
        public List<Sp07RustWorkerProofJob> ProofJobs { get; } = [];
        public List<Sp07RustWorkerVerifyJob> VerifyJobs { get; } = [];

        public Task<Sp07RustWorkerCommandResult> ProveAsync(
            Sp07RustWorkerProofJob job,
            CancellationToken cancellationToken = default)
        {
            ProofJobs.Add(job);
            if (failChunkSuffix is not null && job.ChunkId.EndsWith(failChunkSuffix, StringComparison.Ordinal))
            {
                throw new Sp07RustWorkerException("synthetic worker failure");
            }

            return Task.FromResult(CreateResult(
                "prove",
                job.ElectionId,
                job.ProofSessionId,
                job.ChunkId,
                reportedProofMilliseconds));
        }

        public Task<Sp07RustWorkerCommandResult> VerifyAsync(
            Sp07RustWorkerVerifyJob job,
            CancellationToken cancellationToken = default)
        {
            VerifyJobs.Add(job);
            return Task.FromResult(CreateResult(
                "verify",
                job.ElectionId,
                job.ProofSessionId,
                job.ChunkId,
                0.5));
        }

        private static Sp07RustWorkerCommandResult CreateResult(
            string command,
            string electionId,
            string proofSessionId,
            string chunkId,
            double reportedProofMilliseconds)
        {
            var hash = new string('a', 128);
            return new Sp07RustWorkerCommandResult(
                "HushSp07RustWorkerCommandResultV1",
                "rust_arkworks_m1_process_worker",
                command,
                "completed",
                true,
                "PUB-005",
                "ok",
                electionId,
                proofSessionId,
                chunkId,
                "matrix_m_1_publication_proof_v1",
                "0.1.0",
                2,
                hash,
                hash,
                hash,
                hash,
                hash,
                1200,
                null,
                hash,
                reportedProofMilliseconds,
                new Sp07RustWorkerTelemetry(
                    reportedProofMilliseconds,
                    0,
                    1200,
                    reportedProofMilliseconds,
                    ["test worker memory note"],
                    new Dictionary<string, double>
                    {
                        ["generation"] = reportedProofMilliseconds,
                    }));
        }
    }
}
