using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using VoidDecisionReadinessPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class VoidDecisionReadinessPromoterTests
{
    private static readonly DateTimeOffset FixedGeneratedAt =
        new(2026, 5, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        var paths = CreatePaths();

        var errors = VoidDecisionReadinessContracts.ValidateSchemaSet(paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in VoidDecisionReadinessContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(paths.SchemasRoot, schemaFile)).Should().BeTrue();
        }
    }

    [Fact]
    public void SourceFixture_ReleaseBaseline_IsAcceptedAfterFocusedTwinTestAndE2E()
    {
        var paths = CreatePaths();

        var sourceErrors = VoidDecisionReadinessContracts.ValidateSource(
            VoidDecisionReadinessContracts.LoadSource(paths));
        var generated = VoidDecisionReadinessArtifactGenerator.Generate(
            paths,
            generatedAt: FixedGeneratedAt);

        sourceErrors.Should().BeEmpty();
        generated.Status.Should().Be("accepted");
        generated.Blockers.Should().BeEmpty();
        generated.PublicForbiddenFindings.Should().BeEmpty();

        var readiness = ParseArtifact(generated, VoidDecisionReadinessArtifactGenerator.ReadinessFragmentPath);
        readiness["acceptanceGate"]!.GetValue<string>().Should().Be(VoidDecisionReadinessContracts.AcceptanceGate);
        readiness["status"]!.GetValue<string>().Should().Be("accepted");
        readiness["doesNotMutateRegister"]!.GetValue<bool>().Should().BeTrue();
        readiness["registerPromotionOwner"]!.GetValue<string>().Should().Be("FEAT-130");
        readiness["dimensionScoreChange"]!.AsObject()["acceptedScore"]!.GetValue<int>().Should().Be(6);
        readiness["dimensionScoreChange"]!.AsObject()["appliedScore"]!.GetValue<int>().Should().Be(6);
    }

    [Fact]
    public void GeneratedHandoff_IdentifiesDownstreamConsumersAndRuntimeBindingPolicy()
    {
        var generated = VoidDecisionReadinessArtifactGenerator.Generate(
            CreatePaths(),
            generatedAt: FixedGeneratedAt);

        var handoff = ParseArtifact(generated, VoidDecisionReadinessArtifactGenerator.DownstreamHandoffPath);
        var instructions = handoff["consumerInstructions"]!.AsObject();
        instructions.Should().ContainKeys("FEAT-130", "FEAT-139", "FEAT-141", "PBA-013");

        handoff["readinessRegisterHandoff"]!.AsObject()["targetFeature"]!.GetValue<string>().Should().Be("FEAT-130");
        handoff["feat139Handoff"]!.AsObject()["targetFeature"]!.GetValue<string>().Should().Be("FEAT-139");
        handoff["feat141Handoff"]!.AsObject()["targetFeature"]!.GetValue<string>().Should().Be("FEAT-141");
        handoff["pba013Handoff"]!.AsObject()["blockingPolicy"]!.GetValue<string>()
            .Should().Contain("must not block the owner void decision");
        handoff["privacyBoundary"]!.AsObject()["restrictedArtifacts"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .Should().Contain("historical unofficial result when present");
    }

    [Fact]
    public void GeneratedArtifacts_AreStableForSameInputsAndTimestamp()
    {
        var paths = CreatePaths();

        var first = VoidDecisionReadinessArtifactGenerator.Generate(paths, generatedAt: FixedGeneratedAt);
        var second = VoidDecisionReadinessArtifactGenerator.Generate(paths, generatedAt: FixedGeneratedAt);

        first.Artifacts
            .Select(artifact => new { artifact.RelativePath, artifact.Sha256Hash, artifact.Content })
            .Should()
            .Equal(second.Artifacts.Select(artifact => new { artifact.RelativePath, artifact.Sha256Hash, artifact.Content }));
    }

    [Fact]
    public void GeneratedArtifacts_IncludePublicScanAndPackageHashValidation()
    {
        var generated = VoidDecisionReadinessArtifactGenerator.Generate(
            CreatePaths(),
            generatedAt: FixedGeneratedAt);

        generated.Artifacts.Select(artifact => artifact.RelativePath)
            .Should()
            .Contain(VoidDecisionReadinessContracts.RequiredOutputFiles);

        var publicScan = ParseArtifact(generated, VoidDecisionReadinessArtifactGenerator.PublicArtifactScanPath);
        publicScan["status"]!.GetValue<string>().Should().Be("passed");
        publicScan["forbiddenMaterialFindings"]!.AsArray().Should().BeEmpty();

        var hashValidation = ParseArtifact(generated, VoidDecisionReadinessArtifactGenerator.PackageHashValidationPath);
        hashValidation["status"]!.GetValue<string>().Should().Be("passed");
        hashValidation["canonicalizationVersion"]!.GetValue<string>()
            .Should().Be(VoidDecisionReadinessContracts.CanonicalizationVersion);
        hashValidation["sourceEvidenceRefs"]!.AsArray().Should().NotBeEmpty();
        hashValidation["generatedArtifactHashes"]!.AsArray()
            .Select(node => node!.AsObject()["path"]!.GetValue<string>())
            .Should()
            .BeEquivalentTo([
                VoidDecisionReadinessArtifactGenerator.DownstreamHandoffPath,
                VoidDecisionReadinessArtifactGenerator.PublicArtifactScanPath,
                VoidDecisionReadinessArtifactGenerator.ReadinessFragmentPath,
            ]);
    }

    [Fact]
    public void PublicForbiddenMaterial_BlocksReadinessAndRecordsFinding()
    {
        using var workspace = TempVoidDecisionReadinessWorkspace.Create();
        var source = LoadSource(workspace.Paths);
        source["publicArtifactSamples"]!.AsArray()[0]!.AsObject()["content"] =
            "public fixture accidentally includes vote choice: option A";
        WriteSource(workspace.Paths, source);

        var generated = VoidDecisionReadinessArtifactGenerator.Generate(
            workspace.Paths,
            generatedAt: FixedGeneratedAt);

        generated.Status.Should().Be("blocked");
        generated.Blockers.Should().Contain("FEAT138-PUBLIC-FORBIDDEN-MATERIAL");
        generated.PublicForbiddenFindings.Should().Contain(finding => finding.Category == "vote_choice");

        var publicScan = ParseArtifact(generated, VoidDecisionReadinessArtifactGenerator.PublicArtifactScanPath);
        publicScan["status"]!.GetValue<string>().Should().Be("blocked");
    }

    [Fact]
    public void AcceptedSource_CanEmitAcceptedFragmentWhenAllFocusedGatesPass()
    {
        using var workspace = TempVoidDecisionReadinessWorkspace.Create();
        var source = LoadSource(workspace.Paths);
        foreach (var check in source["focusedVerification"]!.AsArray().OfType<JsonObject>())
        {
            check["status"] = "passed";
            check.Remove("blocker");
        }

        WriteSource(workspace.Paths, source);

        var generated = VoidDecisionReadinessArtifactGenerator.Generate(
            workspace.Paths,
            generatedAt: FixedGeneratedAt);

        generated.Status.Should().Be("accepted");
        generated.Blockers.Should().BeEmpty();

        var readiness = ParseArtifact(generated, VoidDecisionReadinessArtifactGenerator.ReadinessFragmentPath);
        readiness["dimensionScoreChange"]!.AsObject()["acceptedScore"]!.GetValue<int>().Should().Be(6);
        readiness["dimensionScoreChange"]!.AsObject()["appliedScore"]!.GetValue<int>().Should().Be(6);
    }

    [Fact]
    public void PromotionService_ValidateOnlyAndCheckOnly_WriteNoFiles()
    {
        using var workspace = TempVoidDecisionReadinessWorkspace.Create();
        var outputRoot = Path.Combine(workspace.Root, "output");
        var service = new VoidDecisionReadinessPromotionService();

        var validateOnly = service.Promote(new(
            workspace.Paths,
            Mode: null,
            SourceInput: null,
            OutputRoot: outputRoot,
            GeneratedAt: FixedGeneratedAt,
            ValidateOnly: true));
        var checkOnly = service.Promote(new(
            workspace.Paths,
            VoidDecisionReadinessPromotionService.ModeCheckOnly,
            SourceInput: null,
            OutputRoot: outputRoot,
            GeneratedAt: FixedGeneratedAt,
            ValidateOnly: false));

        validateOnly.Mode.Should().Be(VoidDecisionReadinessPromotionService.ModeValidateOnly);
        checkOnly.Mode.Should().Be(VoidDecisionReadinessPromotionService.ModeCheckOnly);
        validateOnly.WrittenFiles.Should().BeEmpty();
        checkOnly.WrittenFiles.Should().BeEmpty();
        Directory.Exists(outputRoot).Should().BeFalse();
    }

    [Fact]
    public void PromotionService_Package_WritesRequiredArtifactsDeterministically()
    {
        using var workspace = TempVoidDecisionReadinessWorkspace.Create();
        var outputRoot = Path.Combine(workspace.Root, "output");
        var service = new VoidDecisionReadinessPromotionService();
        var options = new VoidDecisionReadinessPromotionOptions(
            workspace.Paths,
            VoidDecisionReadinessPromotionService.ModePackage,
            SourceInput: null,
            OutputRoot: outputRoot,
            GeneratedAt: FixedGeneratedAt,
            ValidateOnly: false);

        var first = service.Promote(options);
        var second = service.Promote(options);

        first.Status.Should().Be("accepted");
        first.WrittenFiles.Should().HaveCount(VoidDecisionReadinessContracts.RequiredOutputFiles.Length);
        foreach (var relativePath in VoidDecisionReadinessContracts.RequiredOutputFiles)
        {
            File.Exists(Path.Combine(outputRoot, "release-baseline", relativePath)).Should().BeTrue();
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
                "promote-void-decision-readiness.ps1"))
            .Should()
            .BeTrue();
    }

    private static VoidDecisionReadinessPromotionPaths CreatePaths() =>
        HushVotingReadinessTestArtifacts.CreateVoidDecisionReadinessPaths();

    private static JsonObject LoadSource(VoidDecisionReadinessPromotionPaths paths) =>
        VoidDecisionReadinessContracts.ReadJsonObject(
            paths.DefaultSourceInput,
            VoidDecisionReadinessPromotionPaths.SourceFileName);

    private static JsonObject ParseArtifact(VoidDecisionReadinessGeneratedPackage generated, string relativePath) =>
        JsonNode.Parse(generated.Artifacts.Single(artifact => artifact.RelativePath == relativePath).Content)?.AsObject() ??
        throw new InvalidOperationException($"Generated artifact {relativePath} is not a JSON object.");

    private static void WriteSource(VoidDecisionReadinessPromotionPaths paths, JsonObject value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DefaultSourceInput)!);
        File.WriteAllText(paths.DefaultSourceInput, value.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        }));
    }

    private sealed class TempVoidDecisionReadinessWorkspace : IDisposable
    {
        private TempVoidDecisionReadinessWorkspace(string root, VoidDecisionReadinessPromotionPaths paths)
        {
            Root = root;
            Paths = paths;
        }

        public string Root { get; }

        public VoidDecisionReadinessPromotionPaths Paths { get; }

        public static TempVoidDecisionReadinessWorkspace Create()
        {
            var basePaths = CreatePaths();
            var root = HushVotingReadinessTestArtifacts.CreateEmptyWorkspace("hush-void-decision-readiness-");
            var sourceRoot = Path.Combine(
                root,
                "hush-memory-bank",
                "Overview",
                "HushVotingReadiness",
                VoidDecisionReadinessPromotionPaths.SourceFolder);
            CopyDirectory(basePaths.SourceRoot, sourceRoot);
            return new TempVoidDecisionReadinessWorkspace(
                root,
                VoidDecisionReadinessPromotionPaths.FromWorkspaceRoot(root));
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
