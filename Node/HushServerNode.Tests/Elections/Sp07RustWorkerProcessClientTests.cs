using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using HushShared.Elections.PublicationProof;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class Sp07RustWorkerProcessClientTests
{
    [Fact]
    public void ProcessOptions_FromEnvironmentVariables_ShouldResolveWorkerPathTimeoutThreadsAndWorkingDirectory()
    {
        var options = Sp07RustWorkerProcessOptions.FromEnvironmentVariables(
            CreateEnvironmentReader(
                (Sp07RustWorkerProcessOptions.WorkerPathEnvironmentVariable, "  /opt/hush/hush-sp07-rust-worker  "),
                (Sp07RustWorkerProcessOptions.WorkerTimeoutSecondsEnvironmentVariable, "45.5"),
                (Sp07RustWorkerProcessOptions.WorkerThreadsEnvironmentVariable, "4")),
            defaultWorkingDirectory: "/work");

        options.ExecutablePath.Should().Be("/opt/hush/hush-sp07-rust-worker");
        options.Timeout.Should().Be(TimeSpan.FromSeconds(45.5));
        options.Threads.Should().Be(4);
        options.WorkingDirectory.Should().Be("/work");
    }

    [Fact]
    public void ProcessOptions_FromEnvironmentVariables_WhenWorkerPathIsMissing_ShouldFailClosed()
    {
        var act = () => Sp07RustWorkerProcessOptions.FromEnvironmentVariables(CreateEnvironmentReader());

        act.Should().Throw<Sp07RustWorkerException>()
            .WithMessage($"*{Sp07RustWorkerProcessOptions.WorkerPathEnvironmentVariable}*");
    }

    [Fact]
    public void ProcessOptions_FromEnvironmentVariables_WhenThreadsAreInvalid_ShouldFailClosed()
    {
        var act = () => Sp07RustWorkerProcessOptions.FromEnvironmentVariables(
            CreateEnvironmentReader(
                (Sp07RustWorkerProcessOptions.WorkerPathEnvironmentVariable, "/opt/hush/hush-sp07-rust-worker"),
                (Sp07RustWorkerProcessOptions.WorkerThreadsEnvironmentVariable, "0")));

        act.Should().Throw<Sp07RustWorkerException>()
            .WithMessage($"*{Sp07RustWorkerProcessOptions.WorkerThreadsEnvironmentVariable}*positive integer*");
    }

    [Fact]
    public async Task ProveAsync_ShouldWriteRequestStartWorkerAndReadResult()
    {
        var temp = CreateTempDirectory();
        try
        {
            var job = CreateProofJob(temp);
            var runner = new FakeRunner(async invocation =>
            {
                invocation.Arguments[0].Should().Be("prove");
                invocation.ExecutablePath.Should().Be("fake-hush-sp07-worker");
                invocation.Arguments.Should().ContainInOrder(
                    "prove",
                    "--input",
                    job.RequestPath,
                    "--output",
                    job.ResultPath,
                    "--workdir",
                    job.WorkDirectory,
                    "--threads",
                    "2");

                var requestJson = await File.ReadAllTextAsync(job.RequestPath);
                using var request = JsonDocument.Parse(requestJson);
                request.RootElement.GetProperty("election_id").GetString().Should().Be(job.ElectionId);
                request.RootElement.GetProperty("proof_session_id").GetString().Should().Be(job.ProofSessionId);
                request.RootElement.GetProperty("chunk_id").GetString().Should().Be(job.ChunkId);

                await File.WriteAllTextAsync(job.ResultPath, CreateResultJson("prove", job));
                return new Sp07RustWorkerProcessResult(0, "ok", string.Empty, false);
            });
            var client = CreateClient(runner);

            var result = await client.ProveAsync(job);

            result.Passed.Should().BeTrue();
            result.ResultCode.Should().Be("PUB-005");
            result.Command.Should().Be("prove");
            result.Telemetry.Should().NotBeNull();
            result.Telemetry!.ProofSizeBytes.Should().Be(result.CanonicalProofByteLength);
            result.Telemetry.MemoryNotes.Should().NotBeEmpty();
            runner.Invocations.Should().HaveCount(1);
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    [Fact]
    public async Task ProveAsync_WhenJobHasStatementBindings_ShouldWriteBindingsIntoRequest()
    {
        var temp = CreateTempDirectory();
        try
        {
            var job = CreateProofJob(temp) with
            {
                ProtocolPackageHash = " protocol-package-hash ",
                BallotDefinitionHash = "ballot-definition-hash",
                AcceptedBallotSetHash = "accepted-ballot-set-hash",
                PublishedBallotStreamHash = "published-ballot-stream-hash"
            };
            var runner = new FakeRunner(async _ =>
            {
                var requestJson = await File.ReadAllTextAsync(job.RequestPath);
                using var request = JsonDocument.Parse(requestJson);
                request.RootElement.GetProperty("protocol_package_hash").GetString()
                    .Should().Be("protocol-package-hash");
                request.RootElement.GetProperty("ballot_definition_hash").GetString()
                    .Should().Be("ballot-definition-hash");
                request.RootElement.GetProperty("accepted_ballot_set_hash").GetString()
                    .Should().Be("accepted-ballot-set-hash");
                request.RootElement.GetProperty("published_ballot_stream_hash").GetString()
                    .Should().Be("published-ballot-stream-hash");

                await File.WriteAllTextAsync(job.ResultPath, CreateResultJson("prove", job));
                return new Sp07RustWorkerProcessResult(0, "ok", string.Empty, false);
            });
            var client = CreateClient(runner);

            var result = await client.ProveAsync(job);

            result.Passed.Should().BeTrue();
            runner.Invocations.Should().HaveCount(1);
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    [Fact]
    public async Task ProveAsync_WhenJobHasProductionProofInput_ShouldWriteRestrictedInputIntoRequest()
    {
        var temp = CreateTempDirectory();
        try
        {
            var productionInput = CreateProductionProofInput(ballots: 2, slots: 2);
            var job = CreateProofJob(temp) with
            {
                Ballots = 2,
                Slots = 2,
                ProductionProofInput = productionInput
            };
            var runner = new FakeRunner(async _ =>
            {
                var requestJson = await File.ReadAllTextAsync(job.RequestPath);
                using var request = JsonDocument.Parse(requestJson);
                var input = request.RootElement.GetProperty("production_proof_input");
                input.GetProperty("public_key").GetProperty("x").GetString().Should().Be("1");
                input.GetProperty("accepted_ballots").GetArrayLength().Should().Be(2);
                input.GetProperty("published_ballots").GetArrayLength().Should().Be(2);
                input.GetProperty("published_to_accepted").EnumerateArray()
                    .Select(x => x.GetInt32())
                    .Should().Equal(1, 0);
                input.GetProperty("rerandomization_by_published_ballot_and_slot")[0]
                    .EnumerateArray()
                    .Select(x => x.GetString())
                    .Should().Equal("11", "12");

                await File.WriteAllTextAsync(job.ResultPath, CreateResultJson("prove", job));
                return new Sp07RustWorkerProcessResult(0, "ok", string.Empty, false);
            });
            var client = CreateClient(runner);

            var result = await client.ProveAsync(job);

            result.Passed.Should().BeTrue();
            runner.Invocations.Should().HaveCount(1);
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    [Fact]
    public async Task ProveAsync_WhenProductionProofInputPermutationIsInvalid_ShouldFailBeforeWorkerLaunch()
    {
        var temp = CreateTempDirectory();
        try
        {
            var job = CreateProofJob(temp) with
            {
                Ballots = 2,
                Slots = 2,
                ProductionProofInput = CreateProductionProofInput(ballots: 2, slots: 2) with
                {
                    PublishedToAccepted = [0, 0]
                }
            };
            var runner = new FakeRunner(_ =>
                Task.FromResult(new Sp07RustWorkerProcessResult(0, "ok", string.Empty, false)));
            var client = CreateClient(runner);

            var act = async () => await client.ProveAsync(job);

            await act.Should().ThrowAsync<Sp07RustWorkerException>()
                .WithMessage("*published-to-accepted map*full permutation*");
            runner.Invocations.Should().BeEmpty();
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    [Fact]
    public async Task ProveAsync_WhenWorkerDoesNotCreateResult_ShouldFailContractValidation()
    {
        var temp = CreateTempDirectory();
        try
        {
            var job = CreateProofJob(temp);
            var client = CreateClient(new FakeRunner(_ =>
                Task.FromResult(new Sp07RustWorkerProcessResult(0, "ok", string.Empty, false))));

            var act = async () => await client.ProveAsync(job);

            await act.Should().ThrowAsync<Sp07RustWorkerException>()
                .WithMessage("*did not produce result file*");
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldStartWorkerAndValidateReturnedEnvelope()
    {
        var temp = CreateTempDirectory();
        try
        {
            var input = Path.Combine(temp, "proof-result.json");
            var resultPath = Path.Combine(temp, "verify-result.json");
            var job = new Sp07RustWorkerVerifyJob("election-1", "session-1", "chunk-1", input, resultPath);
            await File.WriteAllTextAsync(input, CreateResultJson("prove", CreateProofJob(temp)));

            var runner = new FakeRunner(async invocation =>
            {
                invocation.Arguments.Should().ContainInOrder("verify", "--input", input, "--output", resultPath);
                await File.WriteAllTextAsync(resultPath, CreateResultJson("verify", CreateProofJob(temp)));
                return new Sp07RustWorkerProcessResult(0, "ok", string.Empty, false);
            });
            var client = CreateClient(runner);

            var result = await client.VerifyAsync(job);

            result.Passed.Should().BeTrue();
            result.Command.Should().Be("verify");
            runner.Invocations.Should().HaveCount(1);
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    [Fact]
    public async Task ProveAsync_WhenWorkerExitsNonZero_ShouldExposeProcessFailure()
    {
        var temp = CreateTempDirectory();
        try
        {
            var job = CreateProofJob(temp);
            var client = CreateClient(new FakeRunner(_ =>
                Task.FromResult(new Sp07RustWorkerProcessResult(7, string.Empty, "boom", false))));

            var act = async () => await client.ProveAsync(job);

            await act.Should().ThrowAsync<Sp07RustWorkerException>()
                .WithMessage("*exited with code 7*boom*");
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    [Fact]
    public async Task ConfiguredWorker_ShouldProveAndVerifySmallVector_WhenWorkerBinaryIsAvailable()
    {
        var workerPath = ResolveAvailableWorkerPath();
        if (string.IsNullOrWhiteSpace(workerPath))
        {
            return;
        }

        File.Exists(workerPath).Should().BeTrue(
            $"{Sp07RustWorkerProcessOptions.WorkerPathEnvironmentVariable} must point to the CI-built Rust worker");

        var temp = CreateTempDirectory();
        try
        {
            var job = CreateProofJob(temp) with
            {
                ProtocolPackageHash = "real-worker-protocol-package-hash",
                BallotDefinitionHash = "real-worker-ballot-definition-hash",
                AcceptedBallotSetHash = "real-worker-accepted-ballot-set-hash",
                PublishedBallotStreamHash = "real-worker-published-ballot-stream-hash"
            };
            var options = new Sp07RustWorkerProcessOptions(
                workerPath,
                TimeSpan.FromSeconds(60),
                WorkingDirectory: Environment.CurrentDirectory,
                Threads: 2);
            var client = new Sp07RustWorkerProcessClient(options);

            var proof = await client.ProveAsync(job);
            var verify = await client.VerifyAsync(new Sp07RustWorkerVerifyJob(
                job.ElectionId,
                job.ProofSessionId,
                job.ChunkId,
                job.ResultPath,
                Path.Combine(temp, "verify-result.json")));

            proof.Passed.Should().BeTrue();
            proof.Command.Should().Be("prove");
            proof.ResultCode.Should().Be("PUB-005");
            proof.CanonicalProofBytesHex.Should().NotBeNullOrWhiteSpace();
            proof.AcceptedBallotSetHash.Should().Be("real-worker-accepted-ballot-set-hash");
            proof.PublishedBallotStreamHash.Should().Be("real-worker-published-ballot-stream-hash");
            ComputeStatementHashFromRustWorkerProofResult(job.ResultPath, job, proof)
                .Should().Be(proof.StatementHashSha512);
            verify.Passed.Should().BeTrue();
            verify.Command.Should().Be("verify");
            verify.ResultCode.Should().Be("PUB-005");

            var tamperedInputPath = Path.Combine(temp, "tampered-proof-result.json");
            await File.WriteAllTextAsync(
                tamperedInputPath,
                TamperCanonicalProofBytesHex(await File.ReadAllTextAsync(job.ResultPath)));
            var tampered = await client.VerifyAsync(new Sp07RustWorkerVerifyJob(
                job.ElectionId,
                job.ProofSessionId,
                job.ChunkId,
                tamperedInputPath,
                Path.Combine(temp, "tampered-verify-result.json")));

            tampered.Passed.Should().BeFalse();
            tampered.ResultCode.Should().Be("PUB-015");
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    private static Sp07RustWorkerProcessClient CreateClient(FakeRunner runner) =>
        new(
            new Sp07RustWorkerProcessOptions(
                "fake-hush-sp07-worker",
                TimeSpan.FromSeconds(10),
                WorkingDirectory: Environment.CurrentDirectory,
                Threads: 2),
            runner);

    private static Sp07RustWorkerProofJob CreateProofJob(string temp) =>
        new(
            "election-1",
            "session-1",
            "chunk-1",
            Ballots: 12,
            Slots: 4,
            WorkDirectory: Path.Combine(temp, "work"),
            RequestPath: Path.Combine(temp, "work", "proof-request.json"),
            ResultPath: Path.Combine(temp, "work", "proof-result.json"));

    private static string CreateResultJson(string command, Sp07RustWorkerProofJob job)
    {
        var hash = new string('a', 128);
        return $$"""
        {
          "schema": "HushSp07RustWorkerCommandResultV1",
          "worker_kind": "rust_arkworks_m1_process_worker",
          "command": "{{command}}",
          "status": "completed",
          "passed": true,
          "result_code": "PUB-005",
          "message": "ok",
          "election_id": "{{job.ElectionId}}",
          "proof_session_id": "{{job.ProofSessionId}}",
          "chunk_id": "{{job.ChunkId}}",
          "proof_profile_id": "matrix_m_1_publication_proof_v1",
          "worker_version": "0.1.0",
          "worker_thread_count": 2,
          "statement_hash_sha512": "{{hash}}",
          "transcript_hash_sha512": "{{hash}}",
          "proof_hash_sha512": "{{hash}}",
          "accepted_ballot_set_hash": "{{hash}}",
          "published_ballot_stream_hash": "{{hash}}",
            "canonical_proof_byte_length": 1200,
            "proof_example_hash_sha512": "{{hash}}",
          "elapsed_milliseconds": 1.5,
          "telemetry": {
            "generation_milliseconds": 1.1,
            "self_verification_milliseconds": 0.4,
            "proof_size_bytes": 1200,
            "cpu_time_milliseconds": 3.0,
            "memory_notes": [
              "test worker memory note"
            ],
            "phase_timings": {
              "generation": 1.1,
              "self_verification": 0.4
            }
          }
        }
        """;
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
            .Select(ballot => Enumerable.Range(0, slots)
                .Select(slot => ((ballot + 1) * 10 + slot + 1).ToString())
                .ToArray())
            .ToArray();

        return new Sp07RustWorkerProductionProofInput(
            new Sp07PointPayload("1", "2"),
            accepted,
            published,
            PublishedToAccepted: [1, 0],
            rerandomization);
    }

    private static Sp07CipherBallotPayload CreateBallot(int ballot, int slots, string label) =>
        new(Enumerable.Range(0, slots)
            .Select(slot => new Sp07CipherSlotPayload(
                new Sp07PointPayload($"{label}-{ballot}-{slot}-c1-x", $"{label}-{ballot}-{slot}-c1-y"),
                new Sp07PointPayload($"{label}-{ballot}-{slot}-c2-x", $"{label}-{ballot}-{slot}-c2-y")))
            .ToArray());

    private static string TamperCanonicalProofBytesHex(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        var proofBytesHex = node["canonical_proof_bytes_hex"]!.GetValue<string>();
        var replacement = proofBytesHex.StartsWith("00", StringComparison.Ordinal) ? "01" : "00";
        node["canonical_proof_bytes_hex"] = replacement + proofBytesHex[2..];
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ComputeStatementHashFromRustWorkerProofResult(
        string resultPath,
        Sp07RustWorkerProofJob job,
        Sp07RustWorkerCommandResult proof)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(resultPath));
        var publicKey = document.RootElement
            .GetProperty("proof_example")
            .GetProperty("statement")
            .GetProperty("public_key");

        return Sp07PackagePublicStatementHasher.ComputeStatementHashSha512(
            new Sp07PackagePublicStatementHashInput(
                job.ElectionId,
                job.ChunkId,
                job.ProtocolPackageHash!,
                job.BallotDefinitionHash!,
                new Sp07PackagePublicPoint(
                    publicKey.GetProperty("x").GetString()!,
                    publicKey.GetProperty("y").GetString()!),
                job.Ballots,
                job.Slots,
                proof.AcceptedBallotSetHash,
                proof.PublishedBallotStreamHash));
    }

    private static string? ResolveAvailableWorkerPath()
    {
        var configured = Environment.GetEnvironmentVariable(
            Sp07RustWorkerProcessOptions.WorkerPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            return null;
        }

        var localDebugWorker = Path.Combine(
            repositoryRoot,
            "Tools",
            "HushSp07RustWorker",
            "target",
            "debug",
            OperatingSystem.IsWindows()
                ? "hush-sp07-rust-worker.exe"
                : "hush-sp07-rust-worker");
        return File.Exists(localDebugWorker) ? localDebugWorker : null;
    }

    private static string? FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Tools", "HushSp07RustWorker", "Cargo.toml")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static Func<string, string?> CreateEnvironmentReader(
        params (string Key, string? Value)[] values)
    {
        var dictionary = values.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        return key => dictionary.GetValueOrDefault(key);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "hush-sp07-worker-tests", Guid.NewGuid().ToString("N"));
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

    private sealed class FakeRunner(
        Func<Sp07RustWorkerProcessInvocation, Task<Sp07RustWorkerProcessResult>> handler)
        : ISp07RustWorkerProcessRunner
    {
        public List<Sp07RustWorkerProcessInvocation> Invocations { get; } = [];

        public async Task<Sp07RustWorkerProcessResult> RunAsync(
            Sp07RustWorkerProcessInvocation invocation,
            CancellationToken cancellationToken)
        {
            Invocations.Add(invocation);
            return await handler(invocation);
        }
    }
}
