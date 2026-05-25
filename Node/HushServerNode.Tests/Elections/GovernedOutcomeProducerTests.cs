using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using GovernedOutcomeProducer;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class GovernedOutcomeProducerTests
{
    private static readonly DateTimeOffset FixedGeneratedAt =
        new(2026, 5, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        var paths = CreatePaths();

        var errors = GovernedOutcomeProducerContracts.ValidateSchemaSet(paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in GovernedOutcomeProducerContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(paths.SchemasRoot, schemaFile)).Should().BeTrue();
        }
    }

    [Fact]
    public void SourceFixture_ReleaseBaseline_ProducesAcceptedHandoffs()
    {
        var paths = CreatePaths();

        var sourceErrors = GovernedOutcomeProducerContracts.ValidateSource(
            GovernedOutcomeProducerContracts.LoadSource(paths));
        var generated = GovernedOutcomeProducerArtifactGenerator.Generate(
            paths,
            generatedAt: FixedGeneratedAt);

        sourceErrors.Should().BeEmpty();
        generated.Status.Should().Be("accepted");
        generated.Blockers.Should().BeEmpty();
        generated.PublicForbiddenFindings.Should().BeEmpty();

        var feat139 = ParseArtifact(generated, GovernedOutcomeProducerArtifactGenerator.Feat139HandoffPath);
        feat139["status"]!.GetValue<string>().Should().Be("accepted");
        feat139["governedOutcome"]!.AsObject()["outcomeStatus"]!.GetValue<string>()
            .Should().Be("finalized_with_anomaly");
        feat139["governedOutcome"]!.AsObject()["cleanFinalization"]!.GetValue<bool>().Should().BeFalse();
        feat139["governedOutcome"]!.AsObject()["keyLostEnforcementStatus"]!.GetValue<string>()
            .Should().Be("enforced");

        var blockerEffect = feat139["blockerEffect"]!.AsObject();
        blockerEffect["blockersCleared"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .Should()
            .Contain("FEAT139-GOVERNED-FINALIZED-WITH-ANOMALY-MISSING");
        blockerEffect["blockersStillOpen"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .Should()
            .Contain("FEAT139-GOVERNED-FAILED-FINALIZE-MISSING");
        blockerEffect["acceptedRuntimeEvidence"]!.GetValue<bool>().Should().BeTrue();

        var feat141 = ParseArtifact(generated, GovernedOutcomeProducerArtifactGenerator.Feat141HandoffPath);
        feat141["claimStates"]!.AsArray()
            .Select(node => node!.AsObject()["state"]!.GetValue<string>())
            .Should()
            .Contain(["accepted", "accepted_with_limitations", "downgraded", "blocked"]);
        feat141["verifierSummary"]!.AsObject()["cleanFinalizationClaim"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void GeneratedArtifacts_AreStableForSameInputsAndTimestamp()
    {
        var paths = CreatePaths();

        var first = GovernedOutcomeProducerArtifactGenerator.Generate(paths, generatedAt: FixedGeneratedAt);
        var second = GovernedOutcomeProducerArtifactGenerator.Generate(paths, generatedAt: FixedGeneratedAt);

        first.Artifacts
            .Select(artifact => new { artifact.RelativePath, artifact.Sha256Hash, artifact.Content })
            .Should()
            .Equal(second.Artifacts.Select(artifact => new { artifact.RelativePath, artifact.Sha256Hash, artifact.Content }));
    }

    [Fact]
    public void GeneratedArtifacts_IncludeAllRequiredPackageOutputs()
    {
        var generated = GovernedOutcomeProducerArtifactGenerator.Generate(
            CreatePaths(),
            generatedAt: FixedGeneratedAt);

        generated.Artifacts.Select(artifact => artifact.RelativePath)
            .Should()
            .Contain(GovernedOutcomeProducerContracts.RequiredOutputFiles);

        var hashValidation = ParseArtifact(generated, GovernedOutcomeProducerArtifactGenerator.PackageHashValidationPath);
        hashValidation["status"]!.GetValue<string>().Should().Be("passed");
        hashValidation["canonicalizationVersion"]!.GetValue<string>()
            .Should().Be(GovernedOutcomeProducerContracts.CanonicalizationVersion);
        hashValidation["generatedArtifactHashes"]!.AsArray()
            .Select(node => node!.AsObject()["path"]!.GetValue<string>())
            .Should()
            .BeEquivalentTo([
                GovernedOutcomeProducerArtifactGenerator.Feat139HandoffPath,
                GovernedOutcomeProducerArtifactGenerator.Feat141HandoffPath,
            ]);
    }

    [Fact]
    public void PublicForbiddenMaterial_BlocksPackageAndRecordsFinding()
    {
        using var workspace = TempGovernedOutcomeWorkspace.Create();
        var source = LoadSource(workspace.Paths);
        source["publicArtifactSamples"]!.AsArray()[0]!.AsObject()["content"] =
            "public sample accidentally includes private key material";
        WriteSource(workspace.Paths, source);

        var generated = GovernedOutcomeProducerArtifactGenerator.Generate(
            workspace.Paths,
            generatedAt: FixedGeneratedAt);

        generated.Status.Should().Be("blocked");
        generated.Blockers.Should().Contain("FEAT146-PUBLIC-FORBIDDEN-MATERIAL");
        generated.PublicForbiddenFindings.Should().Contain(finding => finding.Category == "trustee_secret_material");

        var hashValidation = ParseArtifact(generated, GovernedOutcomeProducerArtifactGenerator.PackageHashValidationPath);
        hashValidation["status"]!.GetValue<string>().Should().Be("blocked");
    }

    [Fact]
    public void PromotionService_ValidateOnlyAndCheckOnly_WriteNoFiles()
    {
        using var workspace = TempGovernedOutcomeWorkspace.Create();
        var outputRoot = Path.Combine(workspace.Root, "output");
        var service = new GovernedOutcomeProducerPromotionService();

        var validateOnly = service.Promote(new(
            workspace.Paths,
            Mode: null,
            SourceInput: null,
            OutputRoot: outputRoot,
            GeneratedAt: FixedGeneratedAt,
            ValidateOnly: true));
        var checkOnly = service.Promote(new(
            workspace.Paths,
            GovernedOutcomeProducerPromotionService.ModeCheckOnly,
            SourceInput: null,
            OutputRoot: outputRoot,
            GeneratedAt: FixedGeneratedAt,
            ValidateOnly: false));

        validateOnly.Mode.Should().Be(GovernedOutcomeProducerPromotionService.ModeValidateOnly);
        checkOnly.Mode.Should().Be(GovernedOutcomeProducerPromotionService.ModeCheckOnly);
        validateOnly.WrittenFiles.Should().BeEmpty();
        checkOnly.WrittenFiles.Should().BeEmpty();
        Directory.Exists(outputRoot).Should().BeFalse();
    }

    [Fact]
    public void PromotionService_Package_WritesRequiredArtifactsDeterministically()
    {
        using var workspace = TempGovernedOutcomeWorkspace.Create();
        var outputRoot = Path.Combine(workspace.Root, "output");
        var service = new GovernedOutcomeProducerPromotionService();
        var options = new GovernedOutcomeProducerPromotionOptions(
            workspace.Paths,
            GovernedOutcomeProducerPromotionService.ModePackage,
            SourceInput: null,
            OutputRoot: outputRoot,
            GeneratedAt: FixedGeneratedAt,
            ValidateOnly: false);

        var first = service.Promote(options);
        var second = service.Promote(options);

        first.Status.Should().Be("accepted");
        first.WrittenFiles.Should().HaveCount(GovernedOutcomeProducerContracts.RequiredOutputFiles.Length);
        foreach (var relativePath in GovernedOutcomeProducerContracts.RequiredOutputFiles)
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
                "promote-governed-outcome-producer.ps1"))
            .Should()
            .BeTrue();
    }

    private static GovernedOutcomeProducerPaths CreatePaths() =>
        HushVotingReadinessTestArtifacts.CreateGovernedOutcomeProducerPaths();

    private static JsonObject LoadSource(GovernedOutcomeProducerPaths paths) =>
        GovernedOutcomeProducerContracts.ReadJsonObject(
            paths.DefaultSourceInput,
            GovernedOutcomeProducerPaths.SourceFileName);

    private static JsonObject ParseArtifact(GovernedOutcomeGeneratedPackage generated, string relativePath) =>
        JsonNode.Parse(generated.Artifacts.Single(artifact => artifact.RelativePath == relativePath).Content)?.AsObject() ??
        throw new InvalidOperationException($"Generated artifact {relativePath} is not a JSON object.");

    private static void WriteSource(GovernedOutcomeProducerPaths paths, JsonObject value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DefaultSourceInput)!);
        File.WriteAllText(paths.DefaultSourceInput, value.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        }));
    }

    private sealed class TempGovernedOutcomeWorkspace : IDisposable
    {
        private TempGovernedOutcomeWorkspace(string root, GovernedOutcomeProducerPaths paths)
        {
            Root = root;
            Paths = paths;
        }

        public string Root { get; }

        public GovernedOutcomeProducerPaths Paths { get; }

        public static TempGovernedOutcomeWorkspace Create()
        {
            var basePaths = CreatePaths();
            var root = HushVotingReadinessTestArtifacts.CreateEmptyWorkspace("hush-governed-outcome-");
            var sourceRoot = Path.Combine(
                root,
                "hush-memory-bank",
                "Overview",
                "HushVotingReadiness",
                GovernedOutcomeProducerPaths.SourceFolder);
            CopyDirectory(basePaths.SourceRoot, sourceRoot);
            return new TempGovernedOutcomeWorkspace(
                root,
                GovernedOutcomeProducerPaths.FromWorkspaceRoot(root));
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
