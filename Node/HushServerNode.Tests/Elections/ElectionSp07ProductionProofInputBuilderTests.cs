using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using HushShared.Elections.PublicationProof;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ElectionSp07ProductionProofInputBuilderTests
{
    private static readonly JsonSerializerOptions RustWorkerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void Build_WhenSealedWitnessesCoverPublishedChunks_ShouldCreatePerChunkProductionInput()
    {
        var electionId = ElectionId.NewElectionId;
        var witnessSetId = Guid.NewGuid();
        var accepted = new[]
        {
            CreateAcceptedBallot(electionId, "accepted-1", acceptedAtOffset: 0),
            CreateAcceptedBallot(electionId, "accepted-2", acceptedAtOffset: 1),
            CreateAcceptedBallot(electionId, "accepted-3", acceptedAtOffset: 2),
        };
        var published = new[]
        {
            CreatePublishedBallot(electionId, "published-3", sequence: 1),
            CreatePublishedBallot(electionId, "published-2", sequence: 2),
            CreatePublishedBallot(electionId, "published-1", sequence: 3),
        };
        var crypto = new TransparentTestElectionPublicationWitnessEnvelopeCrypto();
        var witnesses = new[]
        {
            CreateWitness(electionId, witnessSetId, accepted[2], published[0], ["31", "32"], crypto),
            CreateWitness(electionId, witnessSetId, accepted[1], published[1], ["21", "22"], crypto),
            CreateWitness(electionId, witnessSetId, accepted[0], published[2], ["11", "12"], crypto),
        };
        var plan = new Sp07PublicationChunkPlanner(
                new Sp07PublicationChunkPlannerOptions(
                    MaxBallotsPerChunk: 2,
                    MinBallotsPerChunk: 1,
                    MaxChunks: 3,
                    MaxEncryptedSlots: 2))
            .CreatePlan(accepted.Length, encryptedSlotCount: 2);

        var result = new ElectionSp07ProductionProofInputBuilder(crypto)
            .Build(electionId, accepted, published, witnesses, plan);

        result.IsSuccessful.Should().BeTrue(result.FailureReason);
        result.WitnessSetId.Should().Be(witnessSetId);
        result.EncryptedSlotCount.Should().Be(2);
        result.InputsByChunkId.Should().HaveCount(2);

        var firstChunk = result.InputsByChunkId[plan.Chunks[0].ChunkId];
        firstChunk.AcceptedBallots.Should().HaveCount(2);
        firstChunk.PublishedBallots.Should().HaveCount(2);
        firstChunk.PublishedToAccepted.Should().Equal(1, 0);
        firstChunk.RerandomizationByPublishedBallotAndSlot[0].Should().Equal("31", "32");
        firstChunk.RerandomizationByPublishedBallotAndSlot[1].Should().Equal("21", "22");
        firstChunk.AcceptedBallots[0].Slots[0].C1.X.Should().Be("2101");
        firstChunk.AcceptedBallots[1].Slots[0].C1.X.Should().Be("3101");

        var secondChunk = result.InputsByChunkId[plan.Chunks[1].ChunkId];
        secondChunk.PublishedToAccepted.Should().Equal(0);
        secondChunk.RerandomizationByPublishedBallotAndSlot[0].Should().Equal("11", "12");
        secondChunk.AcceptedBallots[0].Slots[0].C1.X.Should().Be("1101");
    }

    [Fact]
    public void Build_WhenWitnessWasAlreadyDeleted_ShouldFailBeforeUnsealing()
    {
        var electionId = ElectionId.NewElectionId;
        var witnessSetId = Guid.NewGuid();
        var accepted = new[] { CreateAcceptedBallot(electionId, "accepted-1", acceptedAtOffset: 0) };
        var published = new[] { CreatePublishedBallot(electionId, "published-1", sequence: 1) };
        var crypto = new TransparentTestElectionPublicationWitnessEnvelopeCrypto();
        var witness = CreateWitness(electionId, witnessSetId, accepted[0], published[0], ["11", "12"], crypto)
            with
            {
                CustodyStatus = ElectionPublicationWitnessCustodyStatus.Deleted,
                SealedWitnessMaterial = "deleted:sp07-publication-witness",
            };
        var plan = CreateOneBallotPlan();

        var result = new ElectionSp07ProductionProofInputBuilder(crypto)
            .Build(electionId, accepted, published, [witness], plan);

        result.IsSuccessful.Should().BeFalse();
        result.FailureCode.Should().Be("sp07_production_input_witness_not_sealed");
    }

    [Fact]
    public void Build_WhenWitnessMaterialHashDoesNotMatch_ShouldFail()
    {
        var electionId = ElectionId.NewElectionId;
        var witnessSetId = Guid.NewGuid();
        var accepted = new[] { CreateAcceptedBallot(electionId, "accepted-1", acceptedAtOffset: 0) };
        var published = new[] { CreatePublishedBallot(electionId, "published-1", sequence: 1) };
        var crypto = new TransparentTestElectionPublicationWitnessEnvelopeCrypto();
        var witness = CreateWitness(electionId, witnessSetId, accepted[0], published[0], ["11", "12"], crypto)
            with
            {
                SealedWitnessMaterialHash = new string('b', 128),
            };
        var plan = CreateOneBallotPlan();

        var result = new ElectionSp07ProductionProofInputBuilder(crypto)
            .Build(electionId, accepted, published, [witness], plan);

        result.IsSuccessful.Should().BeFalse();
        result.FailureCode.Should().Be("sp07_production_input_witness_material_hash_mismatch");
    }

    [Fact]
    public async Task Build_WhenRustFixtureIsAvailable_ShouldCreateProductionInputThatRustWorkerCanProveAndVerify()
    {
        var workerPath = ResolveAvailableWorkerPath();
        if (string.IsNullOrWhiteSpace(workerPath))
        {
            return;
        }

        var temp = CreateTempDirectory();
        try
        {
            var fixture = await GenerateRustProductionFixtureAsync(workerPath, temp, ballots: 4, slots: 3);
            var electionId = ElectionId.NewElectionId;
            var witnessSetId = Guid.NewGuid();
            var accepted = fixture.ProductionProofInput.AcceptedBallots
                .Select((ballot, index) => CreateAcceptedBallot(
                    electionId,
                    $"accepted-{index + 1}",
                    acceptedAtOffset: index,
                    encryptedBallotPackage: CreateBallotPackage(
                        fixture.ProductionProofInput.PublicKey,
                        ballot)))
                .ToArray();
            var published = fixture.ProductionProofInput.PublishedBallots
                .Select((ballot, index) => CreatePublishedBallot(
                    electionId,
                    $"published-{index + 1}",
                    sequence: index + 1,
                    encryptedBallotPackage: CreateBallotPackage(
                        fixture.ProductionProofInput.PublicKey,
                        ballot)))
                .ToArray();
            var crypto = new TransparentTestElectionPublicationWitnessEnvelopeCrypto();
            var witnesses = fixture.ProductionProofInput.PublishedToAccepted
                .Select((acceptedIndex, publishedIndex) => CreateWitness(
                    electionId,
                    witnessSetId,
                    accepted[acceptedIndex],
                    published[publishedIndex],
                    fixture.ProductionProofInput.RerandomizationByPublishedBallotAndSlot[publishedIndex],
                    crypto))
                .ToArray();
            var plan = new Sp07PublicationChunkPlanner(
                    new Sp07PublicationChunkPlannerOptions(
                        MaxBallotsPerChunk: fixture.Ballots,
                        MinBallotsPerChunk: 1,
                        MaxChunks: 1,
                        MaxEncryptedSlots: fixture.Slots))
                .CreatePlan(fixture.Ballots, fixture.Slots);

            var build = new ElectionSp07ProductionProofInputBuilder(crypto)
                .Build(electionId, accepted, published, witnesses, plan);

            build.IsSuccessful.Should().BeTrue(build.FailureReason);
            var chunkInput = build.InputsByChunkId[plan.Chunks[0].ChunkId];
            chunkInput.PublishedToAccepted.Should().Equal(fixture.ProductionProofInput.PublishedToAccepted);
            chunkInput.RerandomizationByPublishedBallotAndSlot
                .Should().BeEquivalentTo(fixture.ProductionProofInput.RerandomizationByPublishedBallotAndSlot);

            var acceptedHash = VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(accepted));
            var publishedHash = VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputePublishedBallotStreamHash(published));
            var job = new Sp07RustWorkerProofJob(
                electionId.ToString(),
                "production-fixture-session",
                plan.Chunks[0].ChunkId,
                fixture.Ballots,
                fixture.Slots,
                WorkDirectory: Path.Combine(temp, "work"),
                RequestPath: Path.Combine(temp, "work", "proof-request.json"),
                ResultPath: Path.Combine(temp, "work", "proof-result.json"),
                ProtocolPackageHash: new string('1', 128),
                BallotDefinitionHash: new string('2', 128),
                AcceptedBallotSetHash: acceptedHash,
                PublishedBallotStreamHash: publishedHash,
                ProductionProofInput: chunkInput);
            var client = new Sp07RustWorkerProcessClient(new Sp07RustWorkerProcessOptions(
                workerPath,
                TimeSpan.FromSeconds(60),
                WorkingDirectory: temp,
                Threads: 2));

            var proof = await client.ProveAsync(job);
            var verify = await client.VerifyAsync(new Sp07RustWorkerVerifyJob(
                job.ElectionId,
                job.ProofSessionId,
                job.ChunkId,
                job.ResultPath,
                Path.Combine(temp, "verify-result.json")));

            proof.Passed.Should().BeTrue();
            proof.ResultCode.Should().Be("PUB-005");
            proof.AcceptedBallotSetHash.Should().Be(acceptedHash);
            proof.PublishedBallotStreamHash.Should().Be(publishedHash);
            verify.Passed.Should().BeTrue();
            verify.ResultCode.Should().Be("PUB-005");
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    private static async Task<RustProductionFixture> GenerateRustProductionFixtureAsync(
        string workerPath,
        string temp,
        int ballots,
        int slots)
    {
        var output = Path.Combine(temp, "production-fixture.json");
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = workerPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            }
        };
        process.StartInfo.ArgumentList.Add("fixture");
        process.StartInfo.ArgumentList.Add("--ballots");
        process.StartInfo.ArgumentList.Add(ballots.ToString(System.Globalization.CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("--slots");
        process.StartInfo.ArgumentList.Add(slots.ToString(System.Globalization.CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("--output");
        process.StartInfo.ArgumentList.Add(output);

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.Should().Be(0, $"Rust fixture command failed. stdout: {stdout}; stderr: {stderr}");
        var fixture = JsonSerializer.Deserialize<RustProductionFixture>(
            await File.ReadAllTextAsync(output),
            RustWorkerJsonOptions);
        fixture.Should().NotBeNull();
        fixture!.Schema.Should().Be("HushSp07ProductionProofInputFixtureV1");
        fixture.Ballots.Should().Be(ballots);
        fixture.Slots.Should().Be(slots);
        return fixture;
    }

    private static Sp07PublicationChunkPlan CreateOneBallotPlan() =>
        new Sp07PublicationChunkPlanner(
                new Sp07PublicationChunkPlannerOptions(
                    MaxBallotsPerChunk: 2,
                    MinBallotsPerChunk: 1,
                    MaxChunks: 3,
                    MaxEncryptedSlots: 2))
            .CreatePlan(1, encryptedSlotCount: 2);

    private static ElectionAcceptedBallotRecord CreateAcceptedBallot(
        ElectionId electionId,
        string label,
        int acceptedAtOffset,
        string? encryptedBallotPackage = null)
    {
        var number = int.Parse(label.Split('-')[1], System.Globalization.CultureInfo.InvariantCulture);
        return ElectionModelFactory.CreateAcceptedBallotRecord(
            electionId,
            encryptedBallotPackage ?? CreateBallotPackage(number),
            $"proof-{label}",
            $"nullifier-{label}",
            acceptedAt: new DateTime(2026, 05, 07, 9, 0, 0, DateTimeKind.Utc).AddSeconds(acceptedAtOffset));
    }

    private static ElectionPublishedBallotRecord CreatePublishedBallot(
        ElectionId electionId,
        string label,
        long sequence,
        string? encryptedBallotPackage = null)
    {
        var number = int.Parse(label.Split('-')[1], System.Globalization.CultureInfo.InvariantCulture);
        return ElectionModelFactory.CreatePublishedBallotRecord(
            electionId,
            sequence,
            encryptedBallotPackage ?? CreateBallotPackage(number),
            $"proof-{label}",
            publishedAt: new DateTime(2026, 05, 07, 9, 5, 0, DateTimeKind.Utc).AddSeconds(sequence));
    }

    private static ElectionPublicationWitnessRecord CreateWitness(
        ElectionId electionId,
        Guid witnessSetId,
        ElectionAcceptedBallotRecord accepted,
        ElectionPublishedBallotRecord published,
        IReadOnlyList<string> nonces,
        IElectionPublicationWitnessEnvelopeCrypto crypto)
    {
        var witnessId = Guid.NewGuid();
        var material = JsonSerializer.Serialize(
            new
            {
                version = "sp07-publication-rerandomization-witness-v1",
                acceptedEncryptedBallotHash = ComputeSha256Upper(accepted.EncryptedBallotPackage),
                publishedEncryptedBallotHash = ComputeSha256Upper(published.EncryptedBallotPackage),
                sourceProofBundleHash = ComputeSha256Upper(accepted.ProofBundle),
                publishedProofBundleHash = ComputeSha256Upper(published.ProofBundle),
                selectionCount = nonces.Count,
                rerandomizationNonces = nonces,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new ElectionPublicationWitnessRecord(
            witnessId,
            electionId,
            witnessSetId,
            accepted.Id,
            published.PublicationSequence,
            ComputeSha256Lower(accepted.EncryptedBallotPackage),
            ComputeSha256Lower(published.EncryptedBallotPackage),
            ElectionSp07ProfileIds.PublicationProofMode,
            ElectionSp07ProfileIds.ProofConstruction,
            ElectionSp07ProfileIds.StatementId,
            ElectionSp07ProfileIds.ProofSystemVersion,
            crypto.SealWitnessMaterial(material, electionId, witnessId),
            ComputeSha512Lower(material),
            crypto.SealAlgorithm,
            ElectionPublicationWitnessCustodyStatus.Sealed,
            CreatedAt: DateTime.UtcNow);
    }

    private static string CreateBallotPackage(int number)
    {
        var slot1Base = number * 1000 + 100;
        var slot2Base = number * 1000 + 200;
        return JsonSerializer.Serialize(
            new
            {
                version = "election-ballot.v1",
                publicKey = new { x = "10", y = "20" },
                selectionCount = 2,
                ciphertext = new
                {
                    c1 = new[]
                    {
                        new { x = (slot1Base + 1).ToString(), y = (slot1Base + 2).ToString() },
                        new { x = (slot2Base + 1).ToString(), y = (slot2Base + 2).ToString() },
                    },
                    c2 = new[]
                    {
                        new { x = (slot1Base + 3).ToString(), y = (slot1Base + 4).ToString() },
                        new { x = (slot2Base + 3).ToString(), y = (slot2Base + 4).ToString() },
                    },
                },
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static string CreateBallotPackage(
        Sp07PointPayload publicKey,
        Sp07CipherBallotPayload ballot) =>
        JsonSerializer.Serialize(
            new
            {
                version = "babyjubjub-elgamal-vector-ballot-v1",
                publicKey = new { x = publicKey.X, y = publicKey.Y },
                selectionCount = ballot.Slots.Count,
                ciphertext = new
                {
                    c1 = ballot.Slots.Select(slot => new { x = slot.C1.X, y = slot.C1.Y }).ToArray(),
                    c2 = ballot.Slots.Select(slot => new { x = slot.C2.X, y = slot.C2.Y }).ToArray(),
                },
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "hush-sp07-production-input-builder-tests", Guid.NewGuid().ToString("N"));
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

    private static string ComputeSha256Upper(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    private static string ComputeSha256Lower(string value) =>
        ComputeSha256Upper(value).ToLowerInvariant();

    private static string ComputeSha512Lower(string value) =>
        Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

    private sealed record RustProductionFixture(
        string Schema,
        int Ballots,
        int Slots,
        Sp07RustWorkerProductionProofInput ProductionProofInput);
}
