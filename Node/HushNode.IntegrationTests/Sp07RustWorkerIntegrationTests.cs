using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using HushShared.Elections.PublicationProof;
using HushShared.Elections.Verification.Model;
using Moq;
using Xunit;

namespace HushNode.IntegrationTests;

[Collection("Integration Tests")]
public sealed class Sp07RustWorkerIntegrationTests
{
    private static readonly JsonSerializerOptions RustWorkerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "FEAT-117")]
    public async Task ActualRustWorker_ShouldGenerateCanonicalProofBytesAndVerifyThem()
    {
        var workerPath = ResolveWorkerPath();
        var temp = CreateTempDirectory();
        try
        {
            var client = new Sp07RustWorkerProcessClient(new Sp07RustWorkerProcessOptions(
                workerPath,
                TimeSpan.FromSeconds(60),
                WorkingDirectory: Path.GetDirectoryName(workerPath),
                Threads: 2));
            var proofJob = new Sp07RustWorkerProofJob(
                ElectionId: "feat-117-integration-election",
                ProofSessionId: "feat-117-session-1",
                ChunkId: "chunk-0001",
                Ballots: 12,
                Slots: 4,
                WorkDirectory: temp,
                RequestPath: Path.Combine(temp, "prove-request.json"),
                ResultPath: Path.Combine(temp, "prove-result.json"),
                IncludeTamperVectors: true,
                IncludeLegacyPhaseArtifacts: false,
                ProtocolPackageHash: "protocol-package-hash",
                BallotDefinitionHash: "ballot-definition-hash",
                AcceptedBallotSetHash: "accepted-ballot-set-hash",
                PublishedBallotStreamHash: "published-ballot-stream-hash");

            var proof = await client.ProveAsync(proofJob);

            proof.Passed.Should().BeTrue();
            proof.ResultCode.Should().Be("PUB-005");
            proof.CanonicalProofBytesHex.Should().NotBeNullOrWhiteSpace();
            proof.ProtocolSafeHashesShouldBindStatement();

            var verifyJob = new Sp07RustWorkerVerifyJob(
                proofJob.ElectionId,
                proofJob.ProofSessionId,
                proofJob.ChunkId,
                proofJob.ResultPath,
                Path.Combine(temp, "verify-result.json"));

            var verify = await client.VerifyAsync(verifyJob);

            verify.Passed.Should().BeTrue();
            verify.ResultCode.Should().Be("PUB-005");
            verify.ProofHashSha512.Should().Be(proof.ProofHashSha512);
            verify.StatementHashSha512.Should().Be(proof.StatementHashSha512);
            verify.TranscriptHashSha512.Should().Be(proof.TranscriptHashSha512);
            verify.AcceptedBallotSetHash.Should().Be("accepted-ballot-set-hash");
            verify.PublishedBallotStreamHash.Should().Be("published-ballot-stream-hash");
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "FEAT-117")]
    public async Task EnvironmentPackagePublicVerifier_ShouldVerifyCanonicalProofBytesWithActualRustWorker()
    {
        var workerPath = ResolveWorkerPath();
        var temp = CreateTempDirectory();
        var previousWorkerPath = Environment.GetEnvironmentVariable(
            Sp07RustWorkerProcessOptions.WorkerPathEnvironmentVariable);
        try
        {
            var client = new Sp07RustWorkerProcessClient(new Sp07RustWorkerProcessOptions(
                workerPath,
                TimeSpan.FromSeconds(60),
                WorkingDirectory: Path.GetDirectoryName(workerPath),
                Threads: 2));
            var proofJob = new Sp07RustWorkerProofJob(
                ElectionId: "feat-117-package-verifier-election",
                ProofSessionId: "feat-117-package-session-1",
                ChunkId: "chunk-0001",
                Ballots: 8,
                Slots: 2,
                WorkDirectory: temp,
                RequestPath: Path.Combine(temp, "prove-request.json"),
                ResultPath: Path.Combine(temp, "prove-result.json"),
                IncludeTamperVectors: false,
                IncludeLegacyPhaseArtifacts: false,
                ProtocolPackageHash: "protocol-package-hash",
                BallotDefinitionHash: "ballot-definition-hash",
                AcceptedBallotSetHash: "accepted-ballot-set-hash",
                PublishedBallotStreamHash: "published-ballot-stream-hash");
            var proof = await client.ProveAsync(proofJob);
            proof.Passed.Should().BeTrue();
            proof.CanonicalProofBytesHex.Should().NotBeNullOrWhiteSpace();

            Environment.SetEnvironmentVariable(
                Sp07RustWorkerProcessOptions.WorkerPathEnvironmentVariable,
                workerPath);
            var verifier = new EnvironmentSp07RustPackagePublicProofVerifier();

            var result = await verifier.VerifyAsync(new Sp07PackagePublicProofVerificationRequest(
                proof.ElectionId,
                proof.ProofSessionId,
                proof.ChunkId,
                proof.StatementHashSha512,
                proof.TranscriptHashSha512,
                proof.ProofHashSha512,
                proof.AcceptedBallotSetHash!,
                proof.PublishedBallotStreamHash!,
                proof.CanonicalProofByteLength,
                proof.CanonicalProofBytesHex!));

            result.Passed.Should().BeTrue();
            result.ResultCode.Should().Be("PUB-005");
            result.Evidence.Should().ContainKey("worker_kind");
            result.Evidence.Should().ContainKey("proof_hash_sha512")
                .WhoseValue.Should().Be(proof.ProofHashSha512);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                Sp07RustWorkerProcessOptions.WorkerPathEnvironmentVariable,
                previousWorkerPath);
            DeleteTempDirectory(temp);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "FEAT-117")]
    public async Task PublicationProofSessionRunner_ShouldBuildProductionInputAndPersistVerifiedManifestWithActualRustWorker()
    {
        var workerPath = ResolveWorkerPath();
        var temp = CreateTempDirectory();
        try
        {
            var fixture = await GenerateRustProductionFixtureAsync(workerPath, temp, ballots: 4, slots: 2);
            var election = CreateClosedElection(fixture.Slots);
            var witnessSetId = Guid.NewGuid();
            var accepted = fixture.ProductionProofInput.AcceptedBallots
                .Select((ballot, index) => CreateAcceptedBallot(
                    election.ElectionId,
                    index,
                    CreateBallotPackage(fixture.ProductionProofInput.PublicKey, ballot)))
                .ToArray();
            var published = fixture.ProductionProofInput.PublishedBallots
                .Select((ballot, index) => CreatePublishedBallot(
                    election.ElectionId,
                    index + 1,
                    CreateBallotPackage(fixture.ProductionProofInput.PublicKey, ballot)))
                .ToArray();
            var crypto = new TransparentTestElectionPublicationWitnessEnvelopeCrypto();
            var witnesses = fixture.ProductionProofInput.PublishedToAccepted
                .Select((acceptedIndex, publishedIndex) => CreateWitness(
                    election.ElectionId,
                    witnessSetId,
                    accepted[acceptedIndex],
                    published[publishedIndex],
                    fixture.ProductionProofInput.RerandomizationByPublishedBallotAndSlot[publishedIndex],
                    crypto))
                .ToArray();
            var savedSessions = new List<ElectionPublicationProofSessionRecord>();
            var updatedSessions = new List<ElectionPublicationProofSessionRecord>();
            var savedTranscripts = new List<ElectionPublicationProofTranscriptRecord>();
            var repository = new Mock<IElectionsRepository>();
            repository.Setup(x => x.GetAcceptedBallotsAsync(election.ElectionId)).ReturnsAsync(accepted);
            repository.Setup(x => x.GetPublishedBallotsAsync(election.ElectionId)).ReturnsAsync(published);
            repository.Setup(x => x.GetPublicationWitnessesAsync(election.ElectionId)).ReturnsAsync(witnesses);
            repository.Setup(x => x.SavePublicationProofSessionAsync(It.IsAny<ElectionPublicationProofSessionRecord>()))
                .Callback<ElectionPublicationProofSessionRecord>(savedSessions.Add)
                .Returns(Task.CompletedTask);
            repository.Setup(x => x.UpdatePublicationProofSessionAsync(It.IsAny<ElectionPublicationProofSessionRecord>()))
                .Callback<ElectionPublicationProofSessionRecord>(updatedSessions.Add)
                .Returns(Task.CompletedTask);
            repository.Setup(x => x.SavePublicationProofTranscriptAsync(It.IsAny<ElectionPublicationProofTranscriptRecord>()))
                .Callback<ElectionPublicationProofTranscriptRecord>(savedTranscripts.Add)
                .Returns(Task.CompletedTask);
            var worker = new Sp07RustWorkerProcessClient(new Sp07RustWorkerProcessOptions(
                workerPath,
                TimeSpan.FromSeconds(60),
                WorkingDirectory: temp,
                Threads: 2));
            var runner = new ElectionSp07PublicationProofSessionRunner(
                new ElectionSp07ProductionProofInputBuilder(crypto),
                new Sp07PublicationProofChunkCoordinator(
                    worker,
                    new Sp07PublicationProofChunkCoordinatorOptions(Path.Combine(temp, "chunks"))),
                new ElectionSp07PublicationProofManifestBuilder());

            var result = await runner.RunAsync(new ElectionSp07PublicationProofSessionRunnerRequest(
                repository.Object,
                election,
                CreateProtocolPackageBinding(election.ElectionId),
                VerificationProfileIds.HighAssuranceV1,
                DateTime.UnixEpoch.AddHours(40)));

            result.IsSuccessful.Should().BeTrue(result.FailureReason);
            savedSessions.Should().ContainSingle();
            updatedSessions.Should().ContainSingle();
            updatedSessions[0].Status.Should().Be(ElectionPublicationProofSessionStatus.Verified);
            savedTranscripts.Should().ContainSingle();
            savedTranscripts[0].CanonicalProofBytesHex.Should().NotBeNullOrWhiteSpace();
            savedTranscripts[0].StatementHashSha512.Should().NotBeNullOrWhiteSpace();
            savedTranscripts[0].ProofHash.Should().Be(updatedSessions[0].ProofHash);
            result.WorkerResult.Should().NotBeNull();
            result.WorkerResult!.Passed.Should().BeTrue();
            result.WorkerResult.Chunks.Should().ContainSingle(x =>
                x.Passed &&
                x.ProofResult != null &&
                x.ProofResult.ResultCode == "PUB-005" &&
                x.VerifyResult != null &&
                x.VerifyResult.ResultCode == "PUB-005");
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    private static string ResolveWorkerPath()
    {
        var configured = Environment.GetEnvironmentVariable(
            Sp07RustWorkerProcessOptions.WorkerPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            File.Exists(configured).Should().BeTrue(
                $"{Sp07RustWorkerProcessOptions.WorkerPathEnvironmentVariable} must point to the built SP-07 worker");
            return configured;
        }

        var root = FindRepositoryRoot();
        var executableName = OperatingSystem.IsWindows()
            ? "hush-sp07-rust-worker.exe"
            : "hush-sp07-rust-worker";
        var candidates = new[]
        {
            Path.Combine(root, "Tools", "HushSp07RustWorker", "target", "debug", executableName),
            Path.Combine(root, "Tools", "HushSp07RustWorker", "target", "release", executableName)
        };

        var workerPath = candidates.FirstOrDefault(File.Exists);
        workerPath.Should().NotBeNull(
            "FEAT-117 integration tests require a built SP-07 Rust worker. Run cargo build in Tools/HushSp07RustWorker or set HUSH_SP07_RUST_WORKER_PATH.");
        return workerPath!;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Tools", "HushSp07RustWorker")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate hush-server-node repository root.");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "hush-sp07-integration", Guid.NewGuid().ToString("N"));
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

    private static ElectionRecord CreateClosedElection(int optionCount)
    {
        var options = Enumerable.Range(0, optionCount)
            .Select(index => new ElectionOptionDefinition(
                $"option-{index + 1}",
                $"Option {index + 1}",
                null,
                index,
                IsBlankOption: false))
            .ToArray();
        return new ElectionRecord(
            ElectionId.NewElectionId,
            "FEAT-117 integration election",
            null,
            "owner-address",
            null,
            ElectionLifecycleState.Closed,
            ElectionClass.SeriousSecretBallotVoting,
            ElectionBindingStatus.Binding,
            VerificationProfileIds.HighAssuranceV1,
            SelectedProfileDevOnly: false,
            ElectionGovernanceMode.TrusteeThreshold,
            ElectionDisclosureMode.FinalResultsOnly,
            ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            VoteUpdatePolicy.SingleSubmissionOnly,
            EligibilitySourceType.OrganizationImportedRoster,
            EligibilityMutationPolicy.FrozenAtOpen,
            new OutcomeRuleDefinition(
                OutcomeRuleKind.SingleWinner,
                "single-choice-plurality",
                SeatCount: 1,
                BlankVoteCountsForTurnout: true,
                BlankVoteExcludedFromWinnerSelection: true,
                BlankVoteExcludedFromThresholdDenominator: true,
                TieResolutionRule: "manual",
                CalculationBasis: "counted_published_ballots"),
            ApprovedClientApplications: [],
            ProtocolOmegaVersion: "v1.1.8",
            ReportingPolicy.DefaultPhaseOnePackage,
            ReviewWindowPolicy.NoReviewWindow,
            OfficialResultVisibilityPolicy.ParticipantEncryptedOnly,
            CurrentDraftRevision: 1,
            options,
            AcknowledgedWarningCodes: [],
            RequiredApprovalCount: 3,
            CreatedAt: DateTime.UnixEpoch,
            LastUpdatedAt: DateTime.UnixEpoch,
            OpenedAt: DateTime.UnixEpoch.AddHours(1),
            VoteAcceptanceLockedAt: DateTime.UnixEpoch.AddHours(2),
            ClosedAt: DateTime.UnixEpoch.AddHours(3),
            FinalizedAt: null,
            TallyReadyAt: null,
            ElectionClosedProgressStatus.PublicationProofGenerating,
            OpenArtifactId: Guid.NewGuid(),
            CloseArtifactId: Guid.NewGuid(),
            TallyReadyArtifactId: null,
            FinalizeArtifactId: null,
            UnofficialResultArtifactId: null,
            OfficialResultArtifactId: null,
            BallotDefinitionVersion: 1,
            BallotDefinitionHash: SHA256.HashData(Encoding.UTF8.GetBytes("feat-117-ballot-definition")),
            BallotDefinitionSealedAt: DateTime.UnixEpoch.AddHours(1),
            BallotDefinitionMutationPolicy: ElectionBallotDefinitionMutationPolicy.ImmutableAfterOpen,
            ContactCodeProviderReadiness: ElectionContactCodeProviderReadiness.Ready,
            ControlDomainProfileId: "high_assurance_independent_trustees_v1",
            ControlDomainProfileVersion: "sp06-control-domain-v1",
            ThresholdProfileId: "dkg-prod-3of5");
    }

    private static ProtocolPackageBindingRecord CreateProtocolPackageBinding(ElectionId electionId) =>
        new(
            Guid.NewGuid(),
            electionId,
            "omega-hushvoting-v1",
            "v1.1.8",
            VerificationProfileIds.HighAssuranceV1,
            new string('1', 64),
            new string('2', 64),
            new string('3', 64),
            [new ProtocolPackageAccessLocationRecord(
                ProtocolPackageAccessLocationKind.PublicWebsite,
                "spec",
                "https://www.hushnetwork.social/protocol-omega/hushvoting-v1/v1.1.8/spec.zip",
                new string('1', 64))],
            [new ProtocolPackageAccessLocationRecord(
                ProtocolPackageAccessLocationKind.PublicWebsite,
                "proof",
                "https://www.hushnetwork.social/protocol-omega/hushvoting-v1/v1.1.8/proof.zip",
                new string('2', 64))],
            ProtocolPackageApprovalStatus.DraftPrivate,
            ProtocolPackageBindingStatus.Sealed,
            ProtocolPackageBindingSource.SealedAtOpen,
            DraftRevision: 1,
            BoundAt: DateTime.UnixEpoch.AddHours(1),
            SealedAt: DateTime.UnixEpoch.AddHours(1),
            BoundByPublicAddress: "owner-address",
            ProtocolPackageExternalReviewStatus.NotReviewed,
            SourceTransactionId: null,
            SourceBlockHeight: null,
            SourceBlockId: null);

    private static ElectionAcceptedBallotRecord CreateAcceptedBallot(
        ElectionId electionId,
        int index,
        string encryptedBallotPackage) =>
        ElectionModelFactory.CreateAcceptedBallotRecord(
            electionId,
            encryptedBallotPackage,
            $"proof-accepted-{index + 1}",
            $"nullifier-accepted-{index + 1}",
            DateTime.UnixEpoch.AddHours(2).AddSeconds(index));

    private static ElectionPublishedBallotRecord CreatePublishedBallot(
        ElectionId electionId,
        long sequence,
        string encryptedBallotPackage) =>
        ElectionModelFactory.CreatePublishedBallotRecord(
            electionId,
            sequence,
            encryptedBallotPackage,
            $"proof-published-{sequence}",
            DateTime.UnixEpoch.AddHours(3).AddSeconds(sequence));

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

internal static class Sp07RustWorkerIntegrationAssertions
{
    public static void ProtocolSafeHashesShouldBindStatement(this Sp07RustWorkerCommandResult result)
    {
        result.ProtocolPackageHashShouldBeAbsentFromUnsafeLocations();
        result.StatementHashSha512.Should().NotBeNullOrWhiteSpace();
        result.TranscriptHashSha512.Should().NotBeNullOrWhiteSpace();
        result.ProofHashSha512.Should().NotBeNullOrWhiteSpace();
        result.AcceptedBallotSetHash.Should().Be("accepted-ballot-set-hash");
        result.PublishedBallotStreamHash.Should().Be("published-ballot-stream-hash");
    }

    private static void ProtocolPackageHashShouldBeAbsentFromUnsafeLocations(
        this Sp07RustWorkerCommandResult result)
    {
        result.CanonicalProofBytesHex.Should().NotContain(Utf8Hex("protocol-package-hash"));
        result.CanonicalProofBytesHex.Should().NotContain(Utf8Hex("ballot-definition-hash"));
    }

    private static string Utf8Hex(string value) =>
        Convert.ToHexString(Encoding.UTF8.GetBytes(value)).ToLowerInvariant();
}
