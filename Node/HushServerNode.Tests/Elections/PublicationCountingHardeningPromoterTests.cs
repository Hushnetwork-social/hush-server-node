using System.Text.Json.Nodes;
using FluentAssertions;
using PublicationCountingHardeningPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class PublicationCountingHardeningPromoterTests
{
    private static readonly DateTimeOffset FixedGeneratedAt = DateTimeOffset.Parse("2026-05-28T12:00:00Z");

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();

        var errors = PublicationCountingHardeningContracts.ValidateSchemaSet(workspace.Paths.SchemasRoot);

        errors.Should().BeEmpty();
        File.Exists(Path.Combine(
            workspace.Paths.SchemasRoot,
            PublicationCountingHardeningPromotionPaths.SchemaFileName)).Should().BeTrue();
    }

    [Fact]
    public void ReleaseBaseline_SourceAndCurrentRefs_AreValid()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var source = PublicationCountingHardeningContracts.LoadSource(workspace.Paths);

        var errors = PublicationCountingHardeningContracts.ValidateSource(source)
            .Concat(PublicationCountingHardeningContracts.ValidateCurrentRefs(workspace.Paths, source))
            .ToArray();

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Promotion_CheckOnly_ShouldNotWritePackageRoot()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();

        var result = new PublicationCountingHardeningPromotionService().Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModeCheckOnly,
            null,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));

        result.Mode.Should().Be(PublicationCountingHardeningPromotionService.ModeCheckOnly);
        result.WrittenFiles.Should().BeEmpty();
        result.CheckedFiles.Should().BeEquivalentTo(PublicationCountingHardeningArtifactGenerator.RequiredArtifactPaths);
        Directory.Exists(workspace.Paths.DefaultPackageRoot).Should().BeFalse();
    }

    [Fact]
    public void Promotion_PackageMode_WritesDeterministicArtifacts()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var service = new PublicationCountingHardeningPromotionService();

        var first = service.Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModePackage,
            null,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));
        var second = service.Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModePackage,
            null,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));

        first.WrittenFiles.Should().HaveCount(PublicationCountingHardeningArtifactGenerator.RequiredArtifactPaths.Length);
        first.GeneratedPackage.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content))
            .Should()
            .Equal(second.GeneratedPackage.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content)));
        foreach (var relativePath in PublicationCountingHardeningArtifactGenerator.RequiredArtifactPaths)
        {
            File.Exists(Path.Combine(workspace.Paths.DefaultPackageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Should()
                .BeTrue(relativePath);
        }

        var noSecretScan = JsonNode.Parse(File.ReadAllText(Path.Combine(
            workspace.Paths.DefaultPackageRoot,
            PublicationCountingHardeningArtifactGenerator.NoSecretScanResultPath.Replace('/', Path.DirectorySeparatorChar))))!.AsObject();
        noSecretScan["status"]!.GetValue<string>().Should().Be("pass");
        noSecretScan["unexpectedFindingCount"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public void Promotion_CheckOnly_DetectsExistingPackageDrift()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var service = new PublicationCountingHardeningPromotionService();
        service.Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModePackage,
            null,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));
        File.WriteAllText(
            Path.Combine(workspace.Paths.DefaultPackageRoot, PublicationCountingHardeningArtifactGenerator.ReadmePath),
            "drifted package");

        var act = () => service.Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModeCheckOnly,
            null,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<PublicationCountingHardeningPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains(PublicationCountingHardeningArtifactGenerator.ReadmePath, StringComparison.Ordinal));
    }

    [Fact]
    public void PublicSafetyScan_ForbiddenPrivateField_Fails()
    {
        var artifacts = new[]
        {
            new PublicationCountingHardeningArtifact("validation/bad.json", "{\"shuffleMap\": [1,2,3]}"),
        };

        var result = PublicationCountingPublicSafetyScan.Scan(artifacts);

        result.Status.Should().Be("fail");
        result.Findings.Should().Contain(finding => finding.SignalId == "shuffle_map_field");
    }

    [Fact]
    public void SourceValidation_MissingVerifierSourceRef_FailsBeforeGeneration()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var source = workspace.LoadSource();
        source["verifierRefs"]!.AsObject().Remove("sourceRef");

        var errors = PublicationCountingHardeningContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("sourceRef", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_RegisterMutationRequest_FailsBeforeGeneration()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var source = workspace.LoadSource();
        source["readinessProposal"]!.AsObject()["doesNotMutateRegister"] = false;

        var errors = PublicationCountingHardeningContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("doesNotMutateRegister", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SourceValidation_LocalAbsolutePath_FailsBeforeGeneration()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var source = workspace.LoadSource();
        source["packageRefs"]!.AsObject()["packagePath"] = @"C:\private\package";
        var sourceInput = await workspace.WriteSourceAsync(source, "absolute-path-source.json");

        var act = () => new PublicationCountingHardeningPromotionService().Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModeCheckOnly,
            sourceInput,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<PublicationCountingHardeningPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("packagePath", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CurrentnessValidation_StaleVerifierBinaryHash_BlocksPromotion()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var source = workspace.LoadSource();
        source["verifierRefs"]!.AsObject()["binaryRelease"] = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
        var sourceInput = await workspace.WriteSourceAsync(source, "stale-verifier-source.json");

        var act = () => new PublicationCountingHardeningPromotionService().Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModeCheckOnly,
            sourceInput,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<PublicationCountingHardeningPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("verifierRefs.binaryRelease", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CurrentnessValidation_StaleManifestHash_BlocksPromotion()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var source = workspace.LoadSource();
        source["sourceRelease"]!.AsObject()["manifestHash"] = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
        var sourceInput = await workspace.WriteSourceAsync(source, "stale-manifest-source.json");

        var act = () => new PublicationCountingHardeningPromotionService().Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModeCheckOnly,
            sourceInput,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<PublicationCountingHardeningPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("sourceRelease.manifestHash", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CurrentnessValidation_StaleVerifierSourceRef_BlocksPromotion()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var source = workspace.LoadSource();
        source["verifierRefs"]!.AsObject()["sourceRef"] = "stale-source-ref";
        var sourceInput = await workspace.WriteSourceAsync(source, "stale-source-ref.json");

        var act = () => new PublicationCountingHardeningPromotionService().Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModeCheckOnly,
            sourceInput,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<PublicationCountingHardeningPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("verifierRefs.sourceRef", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CurrentnessValidation_StalePackageHash_BlocksPromotion()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var source = workspace.LoadSource();
        source["packageRefs"]!.AsObject()["packageHash"] = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
        var sourceInput = await workspace.WriteSourceAsync(source, "stale-package-hash.json");

        var act = () => new PublicationCountingHardeningPromotionService().Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModeCheckOnly,
            sourceInput,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<PublicationCountingHardeningPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("sourceRelease.goodPackageHash", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CurrentnessValidation_ExpectedResultDrift_BlocksPromotion()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var source = workspace.LoadSource();
        source["packageRefs"]!.AsObject()["expectedResultHash"] = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
        var sourceInput = await workspace.WriteSourceAsync(source, "expected-result-drift.json");

        var act = () => new PublicationCountingHardeningPromotionService().Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModeCheckOnly,
            sourceInput,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<PublicationCountingHardeningPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("packageRefs.expectedResultHash", StringComparison.Ordinal));
    }

    [Fact]
    public void Promotion_MissingInputPath_FailsDeterministically()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var missingInput = Path.Combine(workspace.Paths.SourceRoot, "examples", "release-baseline", "missing-source.json");

        var act = () => new PublicationCountingHardeningPromotionService().Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModeCheckOnly,
            missingInput,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<PublicationCountingHardeningPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("Source input was not found", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptedToPublishedBinding_ValidPackage_Passes()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();

        var result = PublicationCountingBindingChecks.CheckAcceptedToPublished(workspace.Paths, workspace.LoadSource());

        result.Status.Should().Be("accepted");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AcceptedToPublishedBinding_InsertedPublishedItem_Fails()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        workspace.MutatePackageArtifact(TempPublicationCountingWorkspace.PublishedBallotStreamPath, published =>
        {
            published["publishedBallotCount"] = 3;
            published["publishedBallots"]!.AsArray().Add(new JsonObject
            {
                ["publicationSequence"] = 3,
                ["proofBundleHash"] = TempPublicationCountingWorkspace.ProofHashC,
            });
        });

        var result = PublicationCountingBindingChecks.CheckAcceptedToPublished(workspace.Paths, workspace.LoadSource());

        result.Status.Should().Be("blocked");
        result.Errors.Should().Contain(error => error.Contains("Accepted/published count mismatch", StringComparison.Ordinal));
        result.Errors.Should().Contain(error => error.Contains("Published proof hash set", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptedToPublishedBinding_RemovedPublishedItem_Fails()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        workspace.MutatePackageArtifact(TempPublicationCountingWorkspace.PublishedBallotStreamPath, published =>
        {
            published["publishedBallotCount"] = 1;
            published["publishedBallots"]!.AsArray().RemoveAt(1);
        });

        var result = PublicationCountingBindingChecks.CheckAcceptedToPublished(workspace.Paths, workspace.LoadSource());

        result.Status.Should().Be("blocked");
        result.Errors.Should().Contain(error => error.Contains("Accepted/published count mismatch", StringComparison.Ordinal));
        result.Errors.Should().Contain(error => error.Contains("Published proof hash set", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptedToPublishedBinding_DuplicatedPublishedItem_Fails()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        workspace.MutatePackageArtifact(TempPublicationCountingWorkspace.PublishedBallotStreamPath, published =>
        {
            var ballots = published["publishedBallots"]!.AsArray();
            ballots[1]!.AsObject()["proofBundleHash"] = TempPublicationCountingWorkspace.ProofHashA;
        });

        var result = PublicationCountingBindingChecks.CheckAcceptedToPublished(workspace.Paths, workspace.LoadSource());

        result.Status.Should().Be("blocked");
        result.Errors.Should().Contain(error => error.Contains("Published ballot proof hashes contain duplicates", StringComparison.Ordinal));
        result.Errors.Should().Contain(error => error.Contains("Published proof hash set", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptedToPublishedBinding_ReplacedPublishedItem_Fails()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        workspace.MutatePackageArtifact(TempPublicationCountingWorkspace.PublishedBallotStreamPath, published =>
        {
            var ballots = published["publishedBallots"]!.AsArray();
            ballots[1]!.AsObject()["proofBundleHash"] = TempPublicationCountingWorkspace.ProofHashC;
        });

        var result = PublicationCountingBindingChecks.CheckAcceptedToPublished(workspace.Paths, workspace.LoadSource());

        result.Status.Should().Be("blocked");
        result.Errors.Should().Contain(error => error.Contains("Published proof hash set", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptedToPublishedBinding_MismatchedStreamRoot_Fails()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        workspace.MutatePackageArtifact(TempPublicationCountingWorkspace.PublishedBallotStreamPath, published =>
        {
            published["publishedBallotStreamHash"] = "bad-stream-root";
        });

        var result = PublicationCountingBindingChecks.CheckAcceptedToPublished(workspace.Paths, workspace.LoadSource());

        result.Status.Should().Be("blocked");
        result.Errors.Should().Contain(error => error.Contains("publication proof transcript", StringComparison.Ordinal));
        result.Errors.Should().Contain(error => error.Contains("tally replay publishedBallotStreamHash", StringComparison.Ordinal));
    }

    [Fact]
    public void TallyReplayBinding_ValidPackage_Passes()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();

        var result = PublicationCountingBindingChecks.CheckTallyReplay(workspace.Paths, workspace.LoadSource());

        result.Status.Should().Be("accepted");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TallyReplayBinding_WrongTallyTarget_Fails()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        workspace.MutatePackageArtifact(TempPublicationCountingWorkspace.TallyReplayPath, tally =>
        {
            tally["publishedBallotStreamHash"] = "wrong-stream-root";
        });

        var result = PublicationCountingBindingChecks.CheckTallyReplay(workspace.Paths, workspace.LoadSource());

        result.Status.Should().Be("blocked");
        result.Errors.Should().Contain(error => error.Contains("published stream hash must match tally replay", StringComparison.Ordinal));
    }

    [Fact]
    public void TallyReplayBinding_MissingTrusteeRelease_Fails()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        workspace.DeletePackageArtifact(TempPublicationCountingWorkspace.TrusteeReleaseEvidencePath);

        var result = PublicationCountingBindingChecks.CheckTallyReplay(workspace.Paths, workspace.LoadSource());

        result.Status.Should().Be("blocked");
        result.Errors.Should().Contain(error => error.Contains(TempPublicationCountingWorkspace.TrusteeReleaseEvidencePath, StringComparison.Ordinal));
    }

    [Fact]
    public void TallyReplayBinding_FinalResultMismatch_Fails()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        workspace.MutatePackageArtifact(TempPublicationCountingWorkspace.ResultBindingPath, resultBinding =>
        {
            resultBinding["electionId"] = "different-election";
        });

        var result = PublicationCountingBindingChecks.CheckTallyReplay(workspace.Paths, workspace.LoadSource());

        result.Status.Should().Be("blocked");
        result.Errors.Should().Contain(error => error.Contains("tally replay electionId must match result binding", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptedToPublishedBinding_MissingPublicationProof_Fails()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        workspace.DeletePackageArtifact(TempPublicationCountingWorkspace.PublicationProofTranscriptPath);

        var result = PublicationCountingBindingChecks.CheckAcceptedToPublished(workspace.Paths, workspace.LoadSource());

        result.Status.Should().Be("blocked");
        result.Errors.Should().Contain(error => error.Contains(TempPublicationCountingWorkspace.PublicationProofTranscriptPath, StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptedToPublishedBinding_WrongElection_Fails()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        workspace.MutatePackageArtifact(TempPublicationCountingWorkspace.PublishedBallotStreamPath, published =>
        {
            published["electionId"] = "different-election";
        });

        var result = PublicationCountingBindingChecks.CheckAcceptedToPublished(workspace.Paths, workspace.LoadSource());

        result.Status.Should().Be("blocked");
        result.Errors.Should().Contain(error => error.Contains("accepted ballot set electionId must match published stream", StringComparison.Ordinal));
    }

    [Fact]
    public void TamperStaleMatrix_SourceCoverage_IsComplete()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var source = workspace.LoadSource();
        var accepted = PublicationCountingBindingChecks.CheckAcceptedToPublished(workspace.Paths, source);
        var tally = PublicationCountingBindingChecks.CheckTallyReplay(workspace.Paths, source);

        var result = PublicationCountingTamperStaleMatrix.Evaluate(source, accepted, tally);

        result.Status.Should().Be("accepted");
        result.Errors.Should().BeEmpty();
        result.Diagnostics["requiredCaseCount"]!.GetValue<int>()
            .Should()
            .Be(PublicationCountingTamperStaleMatrix.RequiredCases.Length);
    }

    [Fact]
    public void TamperStaleMatrix_MissingRequiredCase_BlocksPackage()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var source = workspace.LoadSource();
        var matrix = source["tamperAndStaleMatrix"]!.AsArray();
        var staleManifestCase = matrix
            .Single(item => item!.AsObject()["caseId"]!.GetValue<string>() == "TM-STALE-MANIFEST-HASH");
        matrix.Remove(staleManifestCase);
        var accepted = PublicationCountingBindingChecks.CheckAcceptedToPublished(workspace.Paths, source);
        var tally = PublicationCountingBindingChecks.CheckTallyReplay(workspace.Paths, source);

        var result = PublicationCountingTamperStaleMatrix.Evaluate(source, accepted, tally);

        result.Status.Should().Be("blocked");
        result.Errors.Should().Contain(error => error.Contains("TM-STALE-MANIFEST-HASH", StringComparison.Ordinal));
    }

    [Fact]
    public void Promotion_CheckOnly_BindingFailure_ReturnsBlockedPackage()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        workspace.MutatePackageArtifact(TempPublicationCountingWorkspace.PublishedBallotStreamPath, published =>
        {
            var ballots = published["publishedBallots"]!.AsArray();
            ballots[1]!.AsObject()["proofBundleHash"] = TempPublicationCountingWorkspace.ProofHashC;
        });

        var result = new PublicationCountingHardeningPromotionService().Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModeCheckOnly,
            null,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));

        result.Status.Should().Be("blocked");
        var acceptedToPublishedSummary = result.GeneratedPackage.Artifacts
            .Single(artifact => artifact.RelativePath == PublicationCountingHardeningArtifactGenerator.AcceptedToPublishedBindingSummaryPath);
        JsonNode.Parse(acceptedToPublishedSummary.Content)!.AsObject()
            ["status"]!
            .GetValue<string>()
            .Should()
            .Be("blocked");
    }

    private sealed class TempPublicationCountingWorkspace : IDisposable
    {
        public const string AcceptedBallotSetPath = "artifacts/election-record/accepted-ballot-set.json";
        public const string PublishedBallotStreamPath = "artifacts/election-record/published-ballot-stream.json";
        public const string PublicationProofTranscriptPath = "artifacts/election-record/publication-proof-transcript.json";
        public const string PublicationProofVerifierOutputPath = "artifacts/election-record/publication-proof-verifier-output.json";
        public const string TallyReplayPath = "artifacts/election-record/tally-replay.json";
        public const string TrusteeReleaseEvidencePath = "artifacts/election-record/trustee-release-evidence.json";
        public const string TrusteeVerifierOutputPath = "artifacts/election-record/trustee-verifier-output.json";
        public const string ResultBindingPath = "artifacts/election-record/result-binding.json";

        public const string ProofHashA = "731CDE0C1EA51BE60D219636C7D517452F25D093375D5B175800A9B1DF941BEF";
        public const string ProofHashB = "B98A3D85F868C3175F492B3D75FBB79216610CF132A7D99CEF28DD4BF1CAE0E8";
        public const string ProofHashC = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        private const string VerifierSourceRef = "88e7d8f4f35e21a341d9ad1b92ecc73bdba0ab15";
        private const string VerifierBinaryHash = "sha256:7048677ba0c66c69c123ab7d046eb03a0d3c7642103f3dd6a2645873c928d6a4";
        private const string GoodPackageHash = "sha256:9014846bcf4fb7b7369e8fcb53e379d7ec3cfa6829478fb657af681e678942cc";
        private const string ElectionId = "13e6fa69-1d53-4968-8b1c-397333458253";
        private const string AcceptedBallotInventoryHash = "280650b39b0f2eb11709f7c84f3fd8f44c9b032e26aad948e1b4d5021b97f1a2";
        private const string PublishedBallotStreamHash = "193eaa3488b6c95c1c7dbd75162b9b788af608bc0b300b4d6280131dc6c8606a";
        private const string PublicationProofTranscriptHash = "sp07-transcript-hash";
        private const string PublicationProofHash = "0ceda866dc34824ab7b18efd0d6ff4770394e088c7f0fa0582d3b496811067e4";

        private TempPublicationCountingWorkspace(string root, PublicationCountingHardeningPromotionPaths paths)
        {
            Root = root;
            Paths = paths;
        }

        public string Root { get; }

        public PublicationCountingHardeningPromotionPaths Paths { get; }

        public static TempPublicationCountingWorkspace Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"hush-publication-counting-{Guid.NewGuid():N}");
            var paths = PublicationCountingHardeningPromotionPaths.FromWorkspaceRoot(root);
            Directory.CreateDirectory(paths.SchemasRoot);
            Directory.CreateDirectory(Path.Combine(paths.ExamplesRoot, "release-baseline"));
            Directory.CreateDirectory(Path.Combine(paths.PublicCorpusRoot, "hushvoting-v1", "v0.2.0", "fixtures"));
            Directory.CreateDirectory(Path.Combine(paths.PublicCorpusRoot, "hushvoting-v1", "v0.2.0", "expected-results"));
            Directory.CreateDirectory(Path.Combine(paths.PublicCorpusRoot, "hushvoting-v1", "v0.2.0", "packages", "sample-good-finalized-election"));

            var workspace = new TempPublicationCountingWorkspace(root, paths);
            workspace.WriteSchema();
            workspace.WriteCorpusInputs();
            workspace.WritePackageArtifacts();
            workspace.WriteSourceAsync(workspace.BuildSource(), PublicationCountingHardeningPromotionPaths.SourceFileName)
                .GetAwaiter()
                .GetResult();
            return workspace;
        }

        public JsonObject LoadSource() => PublicationCountingHardeningContracts.LoadSource(Paths);

        public async Task<string> WriteSourceAsync(JsonObject source, string fileName)
        {
            var path = Path.Combine(Paths.ExamplesRoot, "release-baseline", fileName);
            await File.WriteAllTextAsync(path, PublicationCountingHardeningContracts.CanonicalJson(source));
            return path;
        }

        public void MutatePackageArtifact(string relativePath, Action<JsonObject> mutate, bool rewriteManifest = true)
        {
            var path = PackageArtifactPath(relativePath);
            var artifact = PublicationCountingHardeningContracts.ReadJsonObject(path, relativePath);
            mutate(artifact);
            File.WriteAllText(path, PublicationCountingHardeningContracts.CanonicalJson(artifact));
            if (rewriteManifest)
            {
                RewriteAuditPackageManifest();
            }
        }

        public void DeletePackageArtifact(string relativePath)
        {
            var path = PackageArtifactPath(relativePath);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private void WriteSchema()
        {
            var schema = new JsonObject
            {
                ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                ["required"] = Strings("schemaVersion", "sourceId", "producerFeature"),
            };
            File.WriteAllText(
                Path.Combine(Paths.SchemasRoot, PublicationCountingHardeningPromotionPaths.SchemaFileName),
                PublicationCountingHardeningContracts.CanonicalJson(schema));
        }

        private void WriteCorpusInputs()
        {
            var releaseRoot = Path.Combine(Paths.PublicCorpusRoot, "hushvoting-v1", "v0.2.0");
            var manifest = new JsonObject
            {
                ["schemaVersion"] = "verifier-corpus-manifest.v1",
                ["protocolPackage"] = new JsonObject
                {
                    ["packageVersion"] = "v1.2.0",
                },
                ["verifier"] = new JsonObject
                {
                    ["sourceRef"] = VerifierSourceRef,
                    ["binaryRelease"] = VerifierBinaryHash,
                },
            };
            File.WriteAllText(Path.Combine(releaseRoot, "corpus-manifest.json"), PublicationCountingHardeningContracts.CanonicalJson(manifest));

            var fixtureIndex = new JsonObject
            {
                ["schemaVersion"] = "verifier-corpus-fixture-index.v1",
                ["fixtures"] = new JsonArray(),
            };
            File.WriteAllText(Path.Combine(releaseRoot, "fixtures", "fixture-index.json"), PublicationCountingHardeningContracts.CanonicalJson(fixtureIndex));

            var expectedResult = new JsonObject
            {
                ["schemaVersion"] = "verifier-corpus-expected-result.v1",
                ["fixtureId"] = "sample-good-finalized-election",
                ["expectedOverallStatus"] = "pass",
                ["expectedExitCode"] = 0,
                ["requiredResultCodes"] = Strings("package_structure_valid"),
                ["normalizedOutputHash"] = "sha256:b319a4b3e8f07a0bb5ee71cf8a3f7ddaeecd67dca96c7b088df7ab07b066ee8c",
            };
            File.WriteAllText(
                Path.Combine(releaseRoot, "expected-results", "sample-good-finalized-election.json"),
                PublicationCountingHardeningContracts.CanonicalJson(expectedResult));
        }

        private void WritePackageArtifacts()
        {
            Directory.CreateDirectory(Path.Combine(PackageRoot, "artifacts", "election-record"));
            WritePackageJson("VerifierProfile.json", new JsonObject
            {
                ["profileId"] = "public_anonymous_v1",
                ["displayName"] = "public_anonymous_v1",
            });
            WritePackageJson(AcceptedBallotSetPath, new JsonObject
            {
                ["electionId"] = ElectionId,
                ["acceptedBallotCount"] = 2,
                ["acceptedBallotInventoryHash"] = AcceptedBallotInventoryHash,
                ["acceptedBallots"] = new JsonArray(
                    new JsonObject
                    {
                        ["proofBundleHash"] = ProofHashA,
                    },
                    new JsonObject
                    {
                        ["proofBundleHash"] = ProofHashB,
                    }),
            });
            WritePackageJson(PublishedBallotStreamPath, new JsonObject
            {
                ["electionId"] = ElectionId,
                ["publishedBallotCount"] = 2,
                ["publishedBallotStreamHash"] = PublishedBallotStreamHash,
                ["publishedBallots"] = new JsonArray(
                    new JsonObject
                    {
                        ["publicationSequence"] = 1,
                        ["proofBundleHash"] = ProofHashA,
                    },
                    new JsonObject
                    {
                        ["publicationSequence"] = 2,
                        ["proofBundleHash"] = ProofHashB,
                    }),
            });
            WritePackageJson(PublicationProofTranscriptPath, new JsonObject
            {
                ["electionId"] = ElectionId,
                ["acceptedBallotCount"] = 2,
                ["publishedBallotCount"] = 2,
                ["acceptedBallotSetHash"] = AcceptedBallotInventoryHash,
                ["publishedBallotStreamHash"] = PublishedBallotStreamHash,
                ["transcriptHash"] = PublicationProofTranscriptHash,
                ["proofHash"] = PublicationProofHash,
            });
            WritePackageJson(PublicationProofVerifierOutputPath, new JsonObject
            {
                ["electionId"] = ElectionId,
                ["results"] = new JsonArray(new JsonObject
                {
                    ["status"] = "pass",
                    ["resultCode"] = "publication_proof_evidence_valid",
                    ["evidence"] = new JsonObject
                    {
                        ["accepted_ballot_count"] = "2",
                        ["published_ballot_count"] = "2",
                    },
                }),
            });
            WritePackageJson(TallyReplayPath, new JsonObject
            {
                ["electionId"] = ElectionId,
                ["evidenceStatus"] = "pass",
                ["resultCode"] = "publication_proof_evidence_valid",
                ["acceptedBallotSetHash"] = AcceptedBallotInventoryHash,
                ["publishedBallotStreamHash"] = PublishedBallotStreamHash,
                ["publicationProofTranscriptHash"] = PublicationProofTranscriptHash,
                ["publicationProofHash"] = PublicationProofHash,
            });
            WritePackageJson(TrusteeReleaseEvidencePath, new JsonObject
            {
                ["electionId"] = ElectionId,
                ["finalizationSessionCount"] = 0,
                ["acceptedShareCount"] = 0,
                ["acceptedShares"] = new JsonArray(),
            });
            WritePackageJson(TrusteeVerifierOutputPath, new JsonObject
            {
                ["electionId"] = ElectionId,
                ["results"] = new JsonArray(new JsonObject
                {
                    ["status"] = "pass",
                    ["resultCode"] = "trustee_control_domain_evidence_valid",
                }),
            });
            WritePackageJson(ResultBindingPath, new JsonObject
            {
                ["electionId"] = ElectionId,
                ["reportPackageHash"] = "report-package-hash",
                ["officialResultArtifactId"] = "official-result",
                ["unofficialResultArtifactId"] = "unofficial-result",
                ["outcomeStatus"] = "clean_finalized",
                ["cleanFinalization"] = true,
                ["finalizationMode"] = "clean_finalization",
            });
            RewriteAuditPackageManifest();
        }

        private void RewriteAuditPackageManifest()
        {
            var entries = new JsonArray(RequiredPackageArtifactPaths
                .Where(relativePath => File.Exists(PackageArtifactPath(relativePath)))
                .Select(relativePath => new JsonObject
                {
                    ["path"] = relativePath,
                    ["sha256Hash"] = PublicationCountingHardeningContracts.Sha256File(PackageArtifactPath(relativePath))
                        .Replace("sha256:", "", StringComparison.Ordinal),
                })
                .ToArray<JsonNode?>());
            WritePackageJson("AuditPackageManifest.json", new JsonObject
            {
                ["manifestVersion"] = "1.0",
                ["packageId"] = "HushElectionPackage-" + ElectionId,
                ["electionId"] = ElectionId,
                ["verifierProfileId"] = "public_anonymous_v1",
                ["entries"] = entries,
            });
        }

        private string PackageRoot => Path.Combine(
            Paths.PublicCorpusRoot,
            "hushvoting-v1",
            "v0.2.0",
            "packages",
            "sample-good-finalized-election");

        private string PackageArtifactPath(string relativePath) =>
            Path.Combine(PackageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private void WritePackageJson(string relativePath, JsonObject json)
        {
            var path = PackageArtifactPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, PublicationCountingHardeningContracts.CanonicalJson(json));
        }

        private JsonObject BuildSource()
        {
            var releaseRoot = Path.Combine(Paths.PublicCorpusRoot, "hushvoting-v1", "v0.2.0");
            var sourceRelease = new JsonObject
            {
                ["corpusFamily"] = "hushvoting-v1",
                ["corpusVersion"] = "v0.2.0",
                ["status"] = "accepted",
                ["publicPath"] = "HushVoting-Verifier-Corpus/hushvoting-v1/v0.2.0/",
                ["manifestHash"] = PublicationCountingHardeningContracts.Sha256File(Path.Combine(releaseRoot, "corpus-manifest.json")),
                ["fixtureIndexHash"] = PublicationCountingHardeningContracts.Sha256File(Path.Combine(releaseRoot, "fixtures", "fixture-index.json")),
                ["goodPackageHash"] = GoodPackageHash,
                ["resultCodeStabilityStatus"] = "accepted",
                ["cleanMachineValidationStatus"] = "windows_linux_replay_validated",
                ["noSecretScanStatus"] = "pass",
            };

            return new JsonObject
            {
                ["schemaVersion"] = PublicationCountingHardeningContracts.SourceSchemaVersion,
                ["sourceId"] = "FEAT153-TEST-SOURCE",
                ["generatedAt"] = "2026-05-28T12:00:00Z",
                ["producerFeature"] = PublicationCountingHardeningContracts.FeatureId,
                ["baselineRegister"] = new JsonObject
                {
                    ["registerVersionId"] = PublicationCountingHardeningContracts.CurrentRegisterId,
                    ["registerStatus"] = "AcceptedInternal",
                    ["totalScore"] = 71,
                    ["dimensionId"] = PublicationCountingHardeningContracts.TargetDimensionId,
                    ["dimensionName"] = "Publication/counting evidence",
                    ["currentScore"] = 7,
                    ["targetScoreBeforeReviewPilot"] = 8,
                    ["evidenceIds"] = Strings("RDY-EVID-AT-RDY-001-FEAT-113-001"),
                    ["residualRisk"] = "Publication and counting evidence must stay bound to current release and package refs.",
                },
                ["sourceRelease"] = sourceRelease,
                ["protocolRefs"] = new JsonObject
                {
                    ["packageId"] = "omega-hushvoting-v1",
                    ["packageVersion"] = "v1.2.0",
                    ["source"] = "https://github.com/Hushnetwork-social/protocol-omega-packages",
                    ["profileId"] = "public_anonymous_v1",
                    ["proofMode"] = "zk_rerandomization_shuffle_v1",
                    ["proofConstruction"] = "bayer_groth_reencryption_shuffle_argument_v1",
                    ["reviewLabel"] = "external_crypto_review_pending",
                },
                ["verifierRefs"] = new JsonObject
                {
                    ["repository"] = "https://github.com/Hushnetwork-social/hush-server-node",
                    ["sourceRef"] = VerifierSourceRef,
                    ["projectPath"] = "Tools/HushVotingVerifier/HushVotingVerifier.csproj",
                    ["runtime"] = ".NET 9",
                    ["profileId"] = "public_anonymous_v1",
                    ["binaryRelease"] = VerifierBinaryHash,
                },
                ["packageRefs"] = new JsonObject
                {
                    ["fixtureId"] = "sample-good-finalized-election",
                    ["packagePath"] = "HushVoting-Verifier-Corpus/hushvoting-v1/v0.2.0/packages/sample-good-finalized-election",
                    ["packageHash"] = GoodPackageHash,
                    ["expectedResultRef"] = "HushVoting-Verifier-Corpus/hushvoting-v1/v0.2.0/expected-results/sample-good-finalized-election.json",
                    ["expectedResultHash"] = PublicationCountingHardeningContracts.Sha256File(Path.Combine(releaseRoot, "expected-results", "sample-good-finalized-election.json")),
                    ["expectedOverallStatus"] = "pass",
                    ["expectedExitCode"] = 0,
                    ["requiredArtifacts"] = new JsonArray(new JsonObject
                    {
                        ["artifactId"] = "accepted-ballot-set",
                        ["path"] = "artifacts/election-record/accepted-ballot-set.json",
                        ["role"] = "Accepted ballot inventory used before publication.",
                    }),
                },
                ["acceptedToPublishedChecks"] = new JsonArray(BuildBindingCheck("AT-PUB-001-accepted-set-current")),
                ["tallyReplayChecks"] = new JsonArray(BuildBindingCheck("AT-TALLY-001-count-reconciliation")),
                ["tamperAndStaleMatrix"] = BuildTamperAndStaleMatrix(),
                ["publicSafety"] = new JsonObject
                {
                    ["visibility"] = "public_safe",
                    ["forbiddenMaterialCategories"] = Strings(
                        "shuffle_maps",
                        "rerandomization_randomness",
                        "plaintext_choices",
                        "voter_identity_joins",
                        "kms_secrets",
                        "support_case_data",
                        "local_absolute_paths",
                        "private_backend_logs",
                        "cloud_account_identifiers",
                        "database_connection_strings"),
                    ["expectedFindingCountInGeneratedPackage"] = 0,
                    ["allowedSyntheticTamperMarkersInInputCorpus"] = true,
                    ["publicBoundaryStatement"] = "Public-safe metadata only.",
                },
                ["packageLayout"] = new JsonObject
                {
                    ["targetPackagePath"] = PublicationCountingHardeningContracts.ExpectedTargetPackagePath,
                    ["immutableVersion"] = PublicationCountingHardeningContracts.TargetPackageVersion,
                    ["files"] = new JsonArray(PublicationCountingHardeningArtifactGenerator.RequiredArtifactPaths
                        .Select(path => new JsonObject
                        {
                            ["path"] = path,
                            ["purpose"] = "test artifact",
                            ["publicSafe"] = true,
                        })
                        .ToArray<JsonNode?>()),
                },
                ["readinessProposal"] = new JsonObject
                {
                    ["dimensionId"] = PublicationCountingHardeningContracts.TargetDimensionId,
                    ["proposedScoreFrom"] = 7,
                    ["proposedScoreTo"] = 8,
                    ["doesNotMutateRegister"] = true,
                    ["promotionOwner"] = "FEAT-156 or later explicit FEAT-130 promotion",
                    ["requiredPassingChecks"] = Strings("AT-PUB-001-accepted-set-current", "AT-TALLY-001-count-reconciliation"),
                    ["nonClaims"] = Strings("No production rollout claim"),
                },
                ["downstreamConsumers"] = Strings("FEAT-154", "FEAT-155", "FEAT-156"),
                ["residualRisks"] = Strings("External crypto review remains pending."),
            };
        }

        private static JsonObject BuildBindingCheck(string checkId) =>
            new()
            {
                ["checkId"] = checkId,
                ["purpose"] = "test binding",
                ["requiredArtifactIds"] = Strings("accepted-ballot-set"),
                ["mustBind"] = Strings("left", "right"),
                ["validFixtureIds"] = Strings("sample-good-finalized-election"),
                ["tamperFixtureIds"] = Strings("tamper-published-stream-hash"),
                ["expectedValidOverallStatus"] = "pass",
                ["expectedFailureResultCodes"] = Strings("published_ballot_stream_hash_mismatch"),
                ["blocksScoreMovementWhenFailing"] = true,
            };

        private static JsonArray BuildTamperAndStaleMatrix() =>
            new(PublicationCountingTamperStaleMatrix.RequiredCases
                .Select(required => new JsonObject
                {
                    ["caseId"] = required.CaseId,
                    ["fixtureId"] = required.CaseId.ToLowerInvariant().Replace("tm-", "tamper-", StringComparison.Ordinal),
                    ["category"] = required.Category,
                    ["changedArtifact"] = required.ChangedArtifact,
                    ["expectedPrimaryResultCode"] = required.ExpectedPrimaryResultCode,
                    ["expectedOverallStatus"] = "fail",
                    ["expectedExitCode"] = 1,
                    ["blocksScoreMovement"] = true,
                })
                .ToArray<JsonNode?>());

        private static JsonArray Strings(params string[] values) =>
            new(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());

        private static readonly string[] RequiredPackageArtifactPaths =
        [
            "VerifierProfile.json",
            AcceptedBallotSetPath,
            PublishedBallotStreamPath,
            PublicationProofTranscriptPath,
            PublicationProofVerifierOutputPath,
            TallyReplayPath,
            TrusteeReleaseEvidencePath,
            TrusteeVerifierOutputPath,
            ResultBindingPath,
        ];
    }
}
