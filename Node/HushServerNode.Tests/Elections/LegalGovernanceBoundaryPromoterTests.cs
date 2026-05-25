using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using LegalGovernanceBoundaryPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class LegalGovernanceBoundaryPromoterTests
{
    private static readonly DateTimeOffset FixedGeneratedAt =
        new(2026, 5, 25, 13, 30, 0, TimeSpan.Zero);

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        var paths = CreatePaths();

        var errors = LegalGovernanceBoundaryContracts.ValidateSchemaSet(paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in LegalGovernanceBoundaryContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(paths.SchemasRoot, schemaFile)).Should().BeTrue();
        }
    }

    [Fact]
    public void SourceFixture_ReleaseBaseline_IsAcceptedWithLimitations()
    {
        var paths = CreatePaths();

        var sourceErrors = LegalGovernanceBoundaryContracts.ValidateSource(
            LegalGovernanceBoundaryContracts.LoadSource(paths));
        var generated = LegalGovernanceBoundaryArtifactGenerator.Generate(
            paths,
            generatedAt: FixedGeneratedAt);

        sourceErrors.Should().BeEmpty();
        generated.Status.Should().Be("accepted_with_limitations");
        generated.Blockers.Should().BeEmpty();
        generated.Downgrades.Should().Contain("FEAT140-NON-LEGAL-VALIDATION-LIMITATION");
        generated.PublicForbiddenFindings.Should().BeEmpty();

        var readiness = ParseArtifact(generated, LegalGovernanceBoundaryArtifactGenerator.ReadinessFragmentPath);
        readiness["acceptanceGate"]!.GetValue<string>().Should().Be(LegalGovernanceBoundaryContracts.AcceptanceGate);
        readiness["dimensionId"]!.GetValue<string>().Should().Be(LegalGovernanceBoundaryContracts.DimensionId);
        readiness["status"]!.GetValue<string>().Should().Be("accepted_with_limitations");
        readiness["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();
        readiness["registerPromotionOwner"]!.GetValue<string>().Should().Be("FEAT-130");
        readiness["scoreEffect"]!.AsObject()["appliedScore"]!.GetValue<int>().Should().Be(6);
    }

    [Fact]
    public void SourceValidation_NotApplicableGovernanceInput_RequiresReason()
    {
        using var workspace = TempLegalGovernanceWorkspace.Create();
        var source = LoadSource(workspace.Paths);
        var proxyInput = FindGovernanceInput(source, "proxy_delegation_rule");
        proxyInput["statusReason"] = "";
        WriteSource(workspace.Paths, source);

        var errors = LegalGovernanceBoundaryContracts.ValidateSource(
            LegalGovernanceBoundaryContracts.LoadSource(workspace.Paths));

        errors.Should().Contain(error => error.Contains("not_applicable without a reason", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingAuthority_BlocksAffectedClaims()
    {
        using var workspace = TempLegalGovernanceWorkspace.Create();
        var source = LoadSource(workspace.Paths);
        var authorityInput = FindGovernanceInput(source, "election_authority");
        authorityInput["status"] = "not_provided";
        authorityInput["statusReason"] = "Authority was not supplied for this package.";
        authorityInput["blockerIds"] = new JsonArray("FEAT140-AUTHORITY-MISSING");
        WriteSource(workspace.Paths, source);

        var generated = LegalGovernanceBoundaryArtifactGenerator.Generate(
            workspace.Paths,
            generatedAt: FixedGeneratedAt);

        generated.Status.Should().Be("blocked");
        generated.Blockers.Should().Contain("FEAT140-AUTHORITY-MISSING");

        var matrix = ParseArtifact(generated, LegalGovernanceBoundaryArtifactGenerator.ClaimImpactMatrixPath);
        var authorityRow = matrix["governanceInputs"]!.AsArray().OfType<JsonObject>()
            .Single(input => input["inputId"]!.GetValue<string>() == "election_authority");
        authorityRow["effectiveDecision"]!.GetValue<string>().Should().Be("block");
    }

    [Fact]
    public void GeneratedArtifacts_AreStableForSameInputsAndTimestamp()
    {
        var paths = CreatePaths();

        var first = LegalGovernanceBoundaryArtifactGenerator.Generate(paths, generatedAt: FixedGeneratedAt);
        var second = LegalGovernanceBoundaryArtifactGenerator.Generate(paths, generatedAt: FixedGeneratedAt);

        first.Artifacts
            .Select(artifact => new { artifact.RelativePath, artifact.Sha256Hash, artifact.Content })
            .Should()
            .Equal(second.Artifacts.Select(artifact => new { artifact.RelativePath, artifact.Sha256Hash, artifact.Content }));
    }

    [Fact]
    public void GeneratedArtifacts_IncludeAllRequiredPackageOutputs()
    {
        var generated = LegalGovernanceBoundaryArtifactGenerator.Generate(
            CreatePaths(),
            generatedAt: FixedGeneratedAt);

        generated.Artifacts.Select(artifact => artifact.RelativePath)
            .Should()
            .Contain(LegalGovernanceBoundaryContracts.RequiredOutputFiles);

        var hashValidation = ParseArtifact(generated, LegalGovernanceBoundaryArtifactGenerator.PackageHashValidationPath);
        hashValidation["status"]!.GetValue<string>().Should().Be("passed");
        hashValidation["canonicalizationVersion"]!.GetValue<string>()
            .Should().Be(LegalGovernanceBoundaryContracts.CanonicalizationVersion);
        hashValidation["generatedArtifactHashes"]!.AsArray()
            .Select(node => node!.AsObject()["path"]!.GetValue<string>())
            .Should()
            .BeEquivalentTo([
                LegalGovernanceBoundaryArtifactGenerator.ClaimImpactMatrixPath,
                LegalGovernanceBoundaryArtifactGenerator.DownstreamHandoffPath,
                LegalGovernanceBoundaryArtifactGenerator.Feat139HandoffPath,
                LegalGovernanceBoundaryArtifactGenerator.Feat146HandoffPath,
                LegalGovernanceBoundaryArtifactGenerator.PackagePath,
                LegalGovernanceBoundaryArtifactGenerator.PublicSafeSummaryPath,
                LegalGovernanceBoundaryArtifactGenerator.ReadinessFragmentPath,
                LegalGovernanceBoundaryArtifactGenerator.RestrictedIndexPath,
            ]);
    }

    [Fact]
    public void PublicForbiddenMaterial_BlocksReadinessAndRecordsFinding()
    {
        using var workspace = TempLegalGovernanceWorkspace.Create();
        var source = LoadSource(workspace.Paths);
        source["publicArtifactSamples"]!.AsArray()[0]!.AsObject()["content"] =
            "public fixture accidentally includes private contact paulo@example.invalid";
        WriteSource(workspace.Paths, source);

        var generated = LegalGovernanceBoundaryArtifactGenerator.Generate(
            workspace.Paths,
            generatedAt: FixedGeneratedAt);

        generated.Status.Should().Be("blocked");
        generated.Blockers.Should().Contain("FEAT140-PUBLIC-FORBIDDEN-MATERIAL");
        generated.PublicForbiddenFindings.Should().Contain(finding => finding.Category == "private_contact");
        generated.PublicForbiddenFindings.Should().Contain(finding => finding.Category == "email_or_private_contact");
    }

    [Fact]
    public void Handoffs_SeparateGovernanceBoundaryFromRuntimeOutcomeAuthority()
    {
        var generated = LegalGovernanceBoundaryArtifactGenerator.Generate(
            CreatePaths(),
            generatedAt: FixedGeneratedAt);

        var feat139 = ParseArtifact(generated, LegalGovernanceBoundaryArtifactGenerator.Feat139HandoffPath);
        var finalizedWithAnomaly = feat139["blockerMappings"]!.AsArray().OfType<JsonObject>()
            .Single(mapping => mapping["blockerId"]!.GetValue<string>() == "FEAT139-GOVERNED-FINALIZED-WITH-ANOMALY-MISSING");
        finalizedWithAnomaly["evidenceState"]!.GetValue<string>().Should().Be("governance_boundary_cleared");
        finalizedWithAnomaly["classification"]!.GetValue<string>().Should().Be("runtime_outcome_evidence_required");

        var feat146 = ParseArtifact(generated, LegalGovernanceBoundaryArtifactGenerator.Feat146HandoffPath);
        feat146["producerBoundary"]!.GetValue<string>().Should().Contain("FEAT-146 must validate state");
        feat146["authorityInputs"]!.AsArray().Should().Contain(input =>
            input!.AsObject()["outcome"]!.GetValue<string>() == "finalized_with_anomaly");
    }

    [Fact]
    public void PromotionService_ValidateOnlyAndCheckOnly_WriteNoFiles()
    {
        using var workspace = TempLegalGovernanceWorkspace.Create();
        var outputRoot = Path.Combine(workspace.Root, "output");
        var service = new LegalGovernanceBoundaryPromotionService();

        var validateOnly = service.Promote(new(
            workspace.Paths,
            Mode: null,
            SourceInput: null,
            OutputRoot: outputRoot,
            GeneratedAt: FixedGeneratedAt,
            ValidateOnly: true));
        var checkOnly = service.Promote(new(
            workspace.Paths,
            LegalGovernanceBoundaryPromotionService.ModeCheckOnly,
            SourceInput: null,
            OutputRoot: outputRoot,
            GeneratedAt: FixedGeneratedAt,
            ValidateOnly: false));

        validateOnly.Mode.Should().Be(LegalGovernanceBoundaryPromotionService.ModeValidateOnly);
        checkOnly.Mode.Should().Be(LegalGovernanceBoundaryPromotionService.ModeCheckOnly);
        validateOnly.WrittenFiles.Should().BeEmpty();
        checkOnly.WrittenFiles.Should().BeEmpty();
        Directory.Exists(outputRoot).Should().BeFalse();
    }

    [Fact]
    public void PromotionService_Package_WritesRequiredArtifactsDeterministically()
    {
        using var workspace = TempLegalGovernanceWorkspace.Create();
        var outputRoot = Path.Combine(workspace.Root, "output");
        var service = new LegalGovernanceBoundaryPromotionService();
        var options = new LegalGovernanceBoundaryPromotionOptions(
            workspace.Paths,
            LegalGovernanceBoundaryPromotionService.ModePackage,
            SourceInput: null,
            OutputRoot: outputRoot,
            GeneratedAt: FixedGeneratedAt,
            ValidateOnly: false);

        var first = service.Promote(options);
        var second = service.Promote(options);

        first.Status.Should().Be("accepted_with_limitations");
        first.WrittenFiles.Should().HaveCount(LegalGovernanceBoundaryContracts.RequiredOutputFiles.Length);
        foreach (var relativePath in LegalGovernanceBoundaryContracts.RequiredOutputFiles)
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
                "promote-legal-governance-boundary.ps1"))
            .Should()
            .BeTrue();
    }

    private static LegalGovernanceBoundaryPromotionPaths CreatePaths() =>
        HushVotingReadinessTestArtifacts.CreateLegalGovernanceBoundaryPaths();

    private static JsonObject LoadSource(LegalGovernanceBoundaryPromotionPaths paths) =>
        LegalGovernanceBoundaryContracts.ReadJsonObject(
            paths.DefaultSourceInput,
            LegalGovernanceBoundaryPromotionPaths.SourceFileName);

    private static JsonObject FindGovernanceInput(JsonObject source, string inputId) =>
        source["governanceInputs"]!.AsArray().OfType<JsonObject>()
            .Single(input => input["inputId"]!.GetValue<string>() == inputId);

    private static JsonObject ParseArtifact(LegalGovernanceBoundaryGeneratedPackage generated, string relativePath) =>
        JsonNode.Parse(generated.Artifacts.Single(artifact => artifact.RelativePath == relativePath).Content)?.AsObject() ??
        throw new InvalidOperationException($"Generated artifact {relativePath} is not a JSON object.");

    private static void WriteSource(LegalGovernanceBoundaryPromotionPaths paths, JsonObject value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DefaultSourceInput)!);
        File.WriteAllText(paths.DefaultSourceInput, value.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        }));
    }

    private sealed class TempLegalGovernanceWorkspace : IDisposable
    {
        private TempLegalGovernanceWorkspace(string root, LegalGovernanceBoundaryPromotionPaths paths)
        {
            Root = root;
            Paths = paths;
        }

        public string Root { get; }

        public LegalGovernanceBoundaryPromotionPaths Paths { get; }

        public static TempLegalGovernanceWorkspace Create()
        {
            var basePaths = CreatePaths();
            var root = HushVotingReadinessTestArtifacts.CreateEmptyWorkspace("hush-legal-governance-");
            var sourceRoot = Path.Combine(
                root,
                "hush-memory-bank",
                "Overview",
                "HushVotingReadiness",
                LegalGovernanceBoundaryPromotionPaths.SourceFolder);
            CopyDirectory(basePaths.SourceRoot, sourceRoot);
            return new TempLegalGovernanceWorkspace(
                root,
                LegalGovernanceBoundaryPromotionPaths.FromWorkspaceRoot(root));
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
