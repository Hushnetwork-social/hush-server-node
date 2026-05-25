using System.Text.Json;
using System.Text.Json.Nodes;
using DisputeContinuityReadinessPromoter;
using FluentAssertions;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class DisputeContinuityReadinessPromoterTests
{
    private static readonly DateTimeOffset FixedGeneratedAt =
        new(2026, 5, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        var paths = CreatePaths();

        var errors = DisputeContinuityReadinessContracts.ValidateSchemaSet(paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in DisputeContinuityReadinessContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(paths.SchemasRoot, schemaFile)).Should().BeTrue();
        }
    }

    [Fact]
    public void SourceFixture_ReleaseBaseline_IsBlockedOnlyByFeat139ContinuityGaps()
    {
        var paths = CreatePaths();

        var sourceErrors = DisputeContinuityReadinessContracts.ValidateSource(
            DisputeContinuityReadinessContracts.LoadSource(paths));
        var generated = DisputeContinuityReadinessArtifactGenerator.Generate(
            paths,
            generatedAt: FixedGeneratedAt);

        sourceErrors.Should().BeEmpty();
        generated.Status.Should().Be("blocked");
        generated.Blockers.Should().NotContain(["FEAT138-INT", "FEAT138-E2E"]);
        generated.Blockers.Should().Contain([
            "FEAT139-GOVERNED-FAILED-FINALIZE-MISSING",
            "FEAT139-GOVERNED-FINALIZED-WITH-ANOMALY-MISSING",
            "FEAT139-PACKAGE-ARTIFACT-MISMATCH",
            "FEAT139-UNRESOLVED-BLOCKING-ANOMALY",
        ]);
        generated.PublicForbiddenFindings.Should().BeEmpty();

        var readiness = ParseArtifact(generated, DisputeContinuityReadinessArtifactGenerator.ReadinessFragmentPath);
        readiness["acceptanceGate"]!.GetValue<string>().Should().Be(DisputeContinuityReadinessContracts.AcceptanceGate);
        readiness["dimensionId"]!.GetValue<string>().Should().Be(DisputeContinuityReadinessContracts.DimensionId);
        readiness["status"]!.GetValue<string>().Should().Be("blocked");
        readiness["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();
        readiness["registerPromotionOwner"]!.GetValue<string>().Should().Be("FEAT-130");
        readiness["scoreEffect"]!.AsObject()["appliedScore"]!.GetValue<int>().Should().Be(4);

        var matrix = ParseArtifact(generated, DisputeContinuityReadinessArtifactGenerator.ClaimDecisionMatrixPath);
        var voidedElection = matrix["scenarioDecisions"]!.AsArray().OfType<JsonObject>()
            .Single(scenario => scenario["scenarioId"]!.GetValue<string>() == "voided_election");
        voidedElection["effectiveDecision"]!.GetValue<string>().Should().Be("allow_with_limitations");
        voidedElection["blockerIds"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public void ClaimMatrix_KeepsZeroAnomalyAndMissingEvidenceDistinct()
    {
        var generated = DisputeContinuityReadinessArtifactGenerator.Generate(
            CreatePaths(),
            generatedAt: FixedGeneratedAt);

        var matrix = ParseArtifact(generated, DisputeContinuityReadinessArtifactGenerator.ClaimDecisionMatrixPath);
        var scenarios = matrix["scenarioDecisions"]!.AsArray().OfType<JsonObject>().ToDictionary(
            scenario => scenario["scenarioId"]!.GetValue<string>(),
            StringComparer.Ordinal);

        scenarios["clean_finalized_zero_anomalies"]["evidenceStates"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .Should()
            .Contain("not_required_zero_anomalies");
        scenarios["clean_finalized_zero_anomalies"]["effectiveDecision"]!.GetValue<string>().Should().Be("allow");

        scenarios["failed_to_finalize"]["evidenceStates"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .Should()
            .Contain("missing_required");
        scenarios["failed_to_finalize"]["effectiveDecision"]!.GetValue<string>().Should().Be("block");
    }

    [Fact]
    public void GeneratedArtifacts_AreStableForSameInputsAndTimestamp()
    {
        var paths = CreatePaths();

        var first = DisputeContinuityReadinessArtifactGenerator.Generate(paths, generatedAt: FixedGeneratedAt);
        var second = DisputeContinuityReadinessArtifactGenerator.Generate(paths, generatedAt: FixedGeneratedAt);

        first.Artifacts
            .Select(artifact => new { artifact.RelativePath, artifact.Sha256Hash, artifact.Content })
            .Should()
            .Equal(second.Artifacts.Select(artifact => new { artifact.RelativePath, artifact.Sha256Hash, artifact.Content }));
    }

    [Fact]
    public void GeneratedArtifacts_IncludeAllRequiredPackageOutputs()
    {
        var generated = DisputeContinuityReadinessArtifactGenerator.Generate(
            CreatePaths(),
            generatedAt: FixedGeneratedAt);

        generated.Artifacts.Select(artifact => artifact.RelativePath)
            .Should()
            .Contain(DisputeContinuityReadinessContracts.RequiredOutputFiles);

        var hashValidation = ParseArtifact(generated, DisputeContinuityReadinessArtifactGenerator.PackageHashValidationPath);
        hashValidation["status"]!.GetValue<string>().Should().Be("passed");
        hashValidation["canonicalizationVersion"]!.GetValue<string>()
            .Should().Be(DisputeContinuityReadinessContracts.CanonicalizationVersion);
        hashValidation["generatedArtifactHashes"]!.AsArray()
            .Select(node => node!.AsObject()["path"]!.GetValue<string>())
            .Should()
            .BeEquivalentTo([
                DisputeContinuityReadinessArtifactGenerator.ClaimDecisionMatrixPath,
                DisputeContinuityReadinessArtifactGenerator.DownstreamHandoffPath,
                DisputeContinuityReadinessArtifactGenerator.EvidenceIndexPath,
                DisputeContinuityReadinessArtifactGenerator.PublicSafeSummaryPath,
                DisputeContinuityReadinessArtifactGenerator.ReadinessFragmentPath,
            ]);
    }

    [Fact]
    public void PublicForbiddenMaterial_BlocksReadinessAndRecordsFinding()
    {
        using var workspace = TempDisputeContinuityWorkspace.Create();
        var source = LoadSource(workspace.Paths);
        source["publicArtifactSamples"]!.AsArray()[0]!.AsObject()["content"] =
            "public fixture accidentally includes anomaly body details";
        WriteSource(workspace.Paths, source);

        var generated = DisputeContinuityReadinessArtifactGenerator.Generate(
            workspace.Paths,
            generatedAt: FixedGeneratedAt);

        generated.Status.Should().Be("blocked");
        generated.Blockers.Should().Contain("FEAT139-PUBLIC-FORBIDDEN-MATERIAL");
        generated.PublicForbiddenFindings.Should().Contain(finding => finding.Category == "anomaly_body");
    }

    [Fact]
    public void AcceptedWithLimitationsSource_DoesNotRequireScoreIncrease()
    {
        using var workspace = TempDisputeContinuityWorkspace.Create();
        var source = LoadSource(workspace.Paths);
        source["voidEvidence"]!.AsObject()["state"] = "accepted";
        source["voidEvidence"]!.AsObject()["blockerIds"] = new JsonArray();
        foreach (var outcome in source["governedOutcomeEvidence"]!.AsArray().OfType<JsonObject>())
        {
            outcome["state"] = "not_in_scope";
            outcome["blockerIds"] = new JsonArray();
        }

        foreach (var scenario in source["scenarioDecisions"]!.AsArray().OfType<JsonObject>())
        {
            var scenarioId = scenario["scenarioId"]!.GetValue<string>();
            if (scenarioId is "unresolved_blocking_anomaly" or "voided_election" or "failed_to_finalize" or
                "finalized_with_anomaly" or "package_artifact_mismatch")
            {
                scenario["evidenceStates"] = new JsonArray("not_in_scope");
                scenario["decision"] = "allow_with_limitations";
                scenario["blockerIds"] = new JsonArray();
            }
        }

        WriteSource(workspace.Paths, source);

        var generated = DisputeContinuityReadinessArtifactGenerator.Generate(
            workspace.Paths,
            generatedAt: FixedGeneratedAt);

        generated.Status.Should().Be("accepted_with_limitations");
        generated.Blockers.Should().BeEmpty();

        var readiness = ParseArtifact(generated, DisputeContinuityReadinessArtifactGenerator.ReadinessFragmentPath);
        readiness["scoreEffect"]!.AsObject()["appliedScore"]!.GetValue<int>().Should().Be(4);
        readiness["scoreEffect"]!.AsObject()["scoreIncreaseRequiredForFeatureAcceptance"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void PromotionService_ValidateOnlyAndCheckOnly_WriteNoFiles()
    {
        using var workspace = TempDisputeContinuityWorkspace.Create();
        var outputRoot = Path.Combine(workspace.Root, "output");
        var service = new DisputeContinuityReadinessPromotionService();

        var validateOnly = service.Promote(new(
            workspace.Paths,
            Mode: null,
            SourceInput: null,
            OutputRoot: outputRoot,
            GeneratedAt: FixedGeneratedAt,
            ValidateOnly: true));
        var checkOnly = service.Promote(new(
            workspace.Paths,
            DisputeContinuityReadinessPromotionService.ModeCheckOnly,
            SourceInput: null,
            OutputRoot: outputRoot,
            GeneratedAt: FixedGeneratedAt,
            ValidateOnly: false));

        validateOnly.Mode.Should().Be(DisputeContinuityReadinessPromotionService.ModeValidateOnly);
        checkOnly.Mode.Should().Be(DisputeContinuityReadinessPromotionService.ModeCheckOnly);
        validateOnly.WrittenFiles.Should().BeEmpty();
        checkOnly.WrittenFiles.Should().BeEmpty();
        Directory.Exists(outputRoot).Should().BeFalse();
    }

    [Fact]
    public void PromotionService_Package_WritesRequiredArtifactsDeterministically()
    {
        using var workspace = TempDisputeContinuityWorkspace.Create();
        var outputRoot = Path.Combine(workspace.Root, "output");
        var service = new DisputeContinuityReadinessPromotionService();
        var options = new DisputeContinuityReadinessPromotionOptions(
            workspace.Paths,
            DisputeContinuityReadinessPromotionService.ModePackage,
            SourceInput: null,
            OutputRoot: outputRoot,
            GeneratedAt: FixedGeneratedAt,
            ValidateOnly: false);

        var first = service.Promote(options);
        var second = service.Promote(options);

        first.Status.Should().Be("blocked");
        first.WrittenFiles.Should().HaveCount(DisputeContinuityReadinessContracts.RequiredOutputFiles.Length);
        foreach (var relativePath in DisputeContinuityReadinessContracts.RequiredOutputFiles)
        {
            File.Exists(Path.Combine(outputRoot, "package", relativePath)).Should().BeTrue();
        }

        first.GeneratedPackage.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash))
            .Should()
            .Equal(second.GeneratedPackage.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash)));
    }

    [Fact]
    public void PowerShellWrapper_ExistsAtStableScriptPath()
    {
        File.Exists(Path.Combine(
                HushVotingReadinessTestArtifacts.ServerNodeRoot,
                "Node",
                "scripts",
                "promote-dispute-continuity-readiness.ps1"))
            .Should()
            .BeTrue();
    }

    private static DisputeContinuityReadinessPromotionPaths CreatePaths() =>
        HushVotingReadinessTestArtifacts.CreateDisputeContinuityReadinessPaths();

    private static JsonObject LoadSource(DisputeContinuityReadinessPromotionPaths paths) =>
        DisputeContinuityReadinessContracts.ReadJsonObject(
            paths.DefaultSourceInput,
            DisputeContinuityReadinessPromotionPaths.SourceFileName);

    private static JsonObject ParseArtifact(DisputeContinuityGeneratedPackage generated, string relativePath) =>
        JsonNode.Parse(generated.Artifacts.Single(artifact => artifact.RelativePath == relativePath).Content)?.AsObject() ??
        throw new InvalidOperationException($"Generated artifact {relativePath} is not a JSON object.");

    private static void WriteSource(DisputeContinuityReadinessPromotionPaths paths, JsonObject value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DefaultSourceInput)!);
        File.WriteAllText(paths.DefaultSourceInput, value.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        }));
    }

    private sealed class TempDisputeContinuityWorkspace : IDisposable
    {
        private TempDisputeContinuityWorkspace(string root, DisputeContinuityReadinessPromotionPaths paths)
        {
            Root = root;
            Paths = paths;
        }

        public string Root { get; }

        public DisputeContinuityReadinessPromotionPaths Paths { get; }

        public static TempDisputeContinuityWorkspace Create()
        {
            var basePaths = CreatePaths();
            var root = HushVotingReadinessTestArtifacts.CreateEmptyWorkspace("hush-dispute-continuity-");
            var sourceRoot = Path.Combine(
                root,
                "hush-memory-bank",
                "Overview",
                "HushVotingReadiness",
                DisputeContinuityReadinessPromotionPaths.SourceFolder);
            CopyDirectory(basePaths.SourceRoot, sourceRoot);
            return new TempDisputeContinuityWorkspace(
                root,
                DisputeContinuityReadinessPromotionPaths.FromWorkspaceRoot(root));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, file);
                var destinationPath = Path.Combine(destinationDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(file, destinationPath, overwrite: true);
            }
        }
    }
}
