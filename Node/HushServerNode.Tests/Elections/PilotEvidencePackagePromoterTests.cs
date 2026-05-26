using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using PilotEvidencePackagePromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class PilotEvidencePackagePromoterTests
{
    private static readonly DateTimeOffset FixedGeneratedAt =
        new(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        var paths = CreatePaths();

        var errors = PilotEvidencePackageContracts.ValidateSchemaSet(paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in PilotEvidencePackageContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(paths.SchemasRoot, schemaFile)).Should().BeTrue();
        }
    }

    [Fact]
    public void SourceFixture_ReleaseBaseline_ProducesInternalRehearsalPackageWithLimitations()
    {
        var paths = CreatePaths();

        var sourceErrors = PilotEvidencePackageContracts.ValidateSource(
            PilotEvidencePackageContracts.LoadSource(paths));
        var generated = PilotEvidencePackageArtifactGenerator.Generate(
            paths,
            generatedAt: FixedGeneratedAt);

        sourceErrors.Should().BeEmpty();
        generated.Status.Should().Be("accepted_with_limitations");
        generated.Blockers.Should().BeEmpty();
        generated.Downgrades.Should().Contain("FEAT141-INTERNAL-ONLY-LIMITATION");
        generated.PublicForbiddenFindings.Should().BeEmpty();

        var package = ParseArtifact(generated, PilotEvidencePackageArtifactGenerator.PackagePath);
        package["profile"]!.AsObject()["profile"]!.GetValue<string>()
            .Should().Be("internal_non_binding_rehearsal");
        package["observedRunEvidence"]!.AsObject()["status"]!.GetValue<string>()
            .Should().Be("completed");

        var readiness = ParseArtifact(generated, PilotEvidencePackageArtifactGenerator.ReadinessFragmentPath);
        readiness["acceptanceGate"]!.GetValue<string>().Should().Be(PilotEvidencePackageContracts.AcceptanceGate);
        readiness["dimensionId"]!.GetValue<string>().Should().Be(PilotEvidencePackageContracts.DimensionId);
        readiness["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();
        readiness["registerPromotionOwner"]!.GetValue<string>().Should().Be("FEAT-130");
        readiness["status"]!.GetValue<string>().Should().Be("accepted_with_limitations");
        readiness["scoreEffect"]!.AsObject()["currentTotalScore"]!.GetValue<int>().Should().Be(60);
    }

    [Fact]
    public void CompletedRehearsal_MissingExportPackage_FailsSourceValidation()
    {
        using var workspace = TempPilotEvidenceWorkspace.Create();
        var source = LoadSource(workspace.Paths);
        source["observedRunEvidence"]!.AsObject().Remove("exportPackage");
        WriteSource(workspace.Paths, source);

        var errors = PilotEvidencePackageContracts.ValidateSource(
            PilotEvidencePackageContracts.LoadSource(workspace.Paths));

        errors.Should().Contain(error => error.Contains("exportPackage", StringComparison.Ordinal));
    }

    [Fact]
    public void CompletedRehearsal_MissingVerifierOutput_FailsSourceValidation()
    {
        using var workspace = TempPilotEvidenceWorkspace.Create();
        var source = LoadSource(workspace.Paths);
        source["observedRunEvidence"]!.AsObject().Remove("verifierOutput");
        WriteSource(workspace.Paths, source);

        var errors = PilotEvidencePackageContracts.ValidateSource(
            PilotEvidencePackageContracts.LoadSource(workspace.Paths));

        errors.Should().Contain(error => error.Contains("verifierOutput", StringComparison.Ordinal));
    }

    [Fact]
    public void SkippedRehearsal_RequiresPackageBlockingExceptionAndDoesNotPassClaim()
    {
        using var workspace = TempPilotEvidenceWorkspace.Create();
        var source = LoadSource(workspace.Paths);
        source["rehearsalDecision"]!.AsObject()["status"] = "skipped";
        var observed = source["observedRunEvidence"]!.AsObject();
        observed["status"] = "skipped";
        observed.Remove("exportPackage");
        observed.Remove("verifierOutput");
        source["exceptions"]!.AsArray().Add(new JsonObject
        {
            ["exceptionId"] = "FEAT141-REHEARSAL-SKIPPED",
            ["sourceGap"] = "Controlled pilot evidence",
            ["acceptanceGate"] = "AT-RDY-013",
            ["featureSlice"] = "FEAT-141",
            ["affectedClaim"] = "internal_non_binding_rehearsal",
            ["reason"] = "The rehearsal was skipped and cannot satisfy observed run evidence.",
            ["compensatingEvidence"] = "Package shape can still be reviewed.",
            ["scoreImpact"] = "block",
            ["claimImpact"] = "Internal rehearsal evidence is blocked.",
            ["reviewDue"] = "When a rehearsal is completed.",
            ["packageBlocking"] = true,
            ["signoff"] = "hush-readiness-owner",
        });
        WriteSource(workspace.Paths, source);

        var sourceErrors = PilotEvidencePackageContracts.ValidateSource(
            PilotEvidencePackageContracts.LoadSource(workspace.Paths));
        var generated = PilotEvidencePackageArtifactGenerator.Generate(
            workspace.Paths,
            generatedAt: FixedGeneratedAt);

        sourceErrors.Should().BeEmpty();
        generated.Status.Should().Be("blocked");
        generated.Blockers.Should().Contain("FEAT141-REHEARSAL-SKIPPED");
    }

    [Fact]
    public void FriendlyOrganizationPilot_RemainsBlockedByCurrentRegister()
    {
        var generated = PilotEvidencePackageArtifactGenerator.Generate(
            CreatePaths(),
            generatedAt: FixedGeneratedAt);

        var package = ParseArtifact(generated, PilotEvidencePackageArtifactGenerator.PackagePath);
        var friendlyPilot = package["claimDecisions"]!.AsArray().OfType<JsonObject>()
            .Single(claim => claim["claimId"]!.GetValue<string>() == "friendly_organization_pilot");

        friendlyPilot["state"]!.GetValue<string>().Should().Be("blocked");
        friendlyPilot["blockerIds"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .Should()
            .Contain("FEAT141-FRIENDLY-PILOT-BLOCKED-BY-RDY-REG-v0.1.3");
    }

    [Fact]
    public void Feat138_StaleHash_FailsSourceValidation()
    {
        using var workspace = TempPilotEvidenceWorkspace.Create();
        var source = LoadSource(workspace.Paths);
        var feat138 = FindUpstream(source, "FEAT-138");
        var artifact = feat138["artifacts"]!.AsArray().OfType<JsonObject>()
            .Single(item => item["path"]!.GetValue<string>() == "void-readiness-fragment.json");
        artifact["sha256Hash"] = "stale-hash";
        WriteSource(workspace.Paths, source);

        var errors = PilotEvidencePackageContracts.ValidateSource(
            PilotEvidencePackageContracts.LoadSource(workspace.Paths));

        errors.Should().Contain(error => error.Contains("stale or unexpected hash", StringComparison.Ordinal));
    }

    [Fact]
    public void Feat139_BlockedState_IsCarriedAsVisibleLimitation()
    {
        var generated = PilotEvidencePackageArtifactGenerator.Generate(
            CreatePaths(),
            generatedAt: FixedGeneratedAt);

        var package = ParseArtifact(generated, PilotEvidencePackageArtifactGenerator.PackagePath);
        var feat139 = package["upstreamEvidence"]!.AsArray().OfType<JsonObject>()
            .Single(evidence => evidence["featureSlice"]!.GetValue<string>() == "FEAT-139");
        var exceptions = package["exceptions"]!.AsArray().OfType<JsonObject>()
            .Select(exception => exception["exceptionId"]!.GetValue<string>());

        feat139["status"]!.GetValue<string>().Should().Be("blocked");
        exceptions.Should().Contain("FEAT141-FEAT139-BLOCKED-STATE-CARRIED");
        generated.Status.Should().Be("accepted_with_limitations");
    }

    [Fact]
    public void RuntimeEvidence_FailedToFinalizeCannotBeAcceptedWithoutFutureEvidence()
    {
        using var workspace = TempPilotEvidenceWorkspace.Create();
        var source = LoadSource(workspace.Paths);
        source["runtimeEvidence"]!.AsObject()["governedOutcome"]!.AsObject()["failedToFinalizeStatus"] = "accepted";
        WriteSource(workspace.Paths, source);

        var errors = PilotEvidencePackageContracts.ValidateSource(
            PilotEvidencePackageContracts.LoadSource(workspace.Paths));

        errors.Should().Contain(error => error.Contains("failedToFinalizeStatus cannot be accepted", StringComparison.Ordinal));
    }

    [Fact]
    public void PublicForbiddenMaterial_BlocksPackageAndRecordsFinding()
    {
        using var workspace = TempPilotEvidenceWorkspace.Create();
        var source = LoadSource(workspace.Paths);
        source["publicArtifactSamples"]!.AsArray()[0]!.AsObject()["content"] =
            "public sample accidentally includes voter identity and receipt secret";
        WriteSource(workspace.Paths, source);

        var generated = PilotEvidencePackageArtifactGenerator.Generate(
            workspace.Paths,
            generatedAt: FixedGeneratedAt);

        generated.Status.Should().Be("blocked");
        generated.Blockers.Should().Contain("FEAT141-PUBLIC-FORBIDDEN-MATERIAL");
        generated.PublicForbiddenFindings.Should().Contain(finding => finding.Category == "voter_identity");
        generated.PublicForbiddenFindings.Should().Contain(finding => finding.Category == "receipt_secret");
    }

    [Fact]
    public void GeneratedArtifacts_AreStableForSameInputsAndTimestamp()
    {
        var paths = CreatePaths();

        var first = PilotEvidencePackageArtifactGenerator.Generate(paths, generatedAt: FixedGeneratedAt);
        var second = PilotEvidencePackageArtifactGenerator.Generate(paths, generatedAt: FixedGeneratedAt);

        first.Artifacts
            .Select(artifact => new { artifact.RelativePath, artifact.Sha256Hash, artifact.Content })
            .Should()
            .Equal(second.Artifacts.Select(artifact => new { artifact.RelativePath, artifact.Sha256Hash, artifact.Content }));
    }

    [Fact]
    public void GeneratedArtifacts_IncludeAllRequiredPackageOutputs()
    {
        var generated = PilotEvidencePackageArtifactGenerator.Generate(
            CreatePaths(),
            generatedAt: FixedGeneratedAt);

        generated.Artifacts.Select(artifact => artifact.RelativePath)
            .Should()
            .Contain(PilotEvidencePackageContracts.RequiredOutputFiles);

        var hashValidation = ParseArtifact(generated, PilotEvidencePackageArtifactGenerator.PackageHashValidationPath);
        hashValidation["status"]!.GetValue<string>().Should().Be("passed");
        hashValidation["canonicalizationVersion"]!.GetValue<string>()
            .Should().Be(PilotEvidencePackageContracts.CanonicalizationVersion);
    }

    [Fact]
    public void PromotionService_ValidateOnlyAndCheckOnly_WriteNoFiles()
    {
        using var workspace = TempPilotEvidenceWorkspace.Create();
        var outputRoot = Path.Combine(workspace.Root, "output");
        var service = new PilotEvidencePackagePromotionService();

        var validateOnly = service.Promote(new(
            workspace.Paths,
            Mode: null,
            SourceInput: null,
            OutputRoot: outputRoot,
            GeneratedAt: FixedGeneratedAt,
            ValidateOnly: true));
        var checkOnly = service.Promote(new(
            workspace.Paths,
            PilotEvidencePackagePromotionService.ModeCheckOnly,
            SourceInput: null,
            OutputRoot: outputRoot,
            GeneratedAt: FixedGeneratedAt,
            ValidateOnly: false));

        validateOnly.Mode.Should().Be(PilotEvidencePackagePromotionService.ModeValidateOnly);
        checkOnly.Mode.Should().Be(PilotEvidencePackagePromotionService.ModeCheckOnly);
        validateOnly.WrittenFiles.Should().BeEmpty();
        checkOnly.WrittenFiles.Should().BeEmpty();
        Directory.Exists(outputRoot).Should().BeFalse();
    }

    [Fact]
    public void PromotionService_PackageWritesAndCheckOnlyValidatesExistingArtifacts()
    {
        using var workspace = TempPilotEvidenceWorkspace.Create();
        var outputRoot = Path.Combine(workspace.Root, "output");
        var service = new PilotEvidencePackagePromotionService();
        var options = new PilotEvidencePackagePromotionOptions(
            workspace.Paths,
            PilotEvidencePackagePromotionService.ModePackage,
            SourceInput: null,
            OutputRoot: outputRoot,
            GeneratedAt: FixedGeneratedAt,
            ValidateOnly: false);

        var package = service.Promote(options);
        var checkOnly = service.Promote(options with { Mode = PilotEvidencePackagePromotionService.ModeCheckOnly });

        package.Status.Should().Be("accepted_with_limitations");
        package.WrittenFiles.Should().HaveCount(PilotEvidencePackageContracts.RequiredOutputFiles.Length);
        checkOnly.WrittenFiles.Should().BeEmpty();
        foreach (var relativePath in PilotEvidencePackageContracts.RequiredOutputFiles)
        {
            File.Exists(Path.Combine(outputRoot, "package", relativePath)).Should().BeTrue();
        }
    }

    [Fact]
    public void PowerShellWrapper_ExistsAtStableScriptPath()
    {
        File.Exists(Path.Combine(
                HushVotingReadinessTestArtifacts.ServerNodeRoot,
                "Node",
                "scripts",
                "promote-pilot-evidence-package.ps1"))
            .Should()
            .BeTrue();
    }

    private static PilotEvidencePackagePromotionPaths CreatePaths() =>
        HushVotingReadinessTestArtifacts.CreatePilotEvidencePackagePaths();

    private static JsonObject LoadSource(PilotEvidencePackagePromotionPaths paths) =>
        PilotEvidencePackageContracts.ReadJsonObject(
            paths.DefaultSourceInput,
            PilotEvidencePackagePromotionPaths.SourceFileName);

    private static JsonObject FindUpstream(JsonObject source, string featureSlice) =>
        source["upstreamEvidence"]!.AsArray().OfType<JsonObject>()
            .Single(evidence => evidence["featureSlice"]!.GetValue<string>() == featureSlice);

    private static JsonObject ParseArtifact(PilotEvidenceGeneratedPackage generated, string relativePath) =>
        JsonNode.Parse(generated.Artifacts.Single(artifact => artifact.RelativePath == relativePath).Content)?.AsObject() ??
        throw new InvalidOperationException($"Generated artifact {relativePath} is not a JSON object.");

    private static void WriteSource(PilotEvidencePackagePromotionPaths paths, JsonObject value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DefaultSourceInput)!);
        File.WriteAllText(paths.DefaultSourceInput, value.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        }));
    }

    private sealed class TempPilotEvidenceWorkspace : IDisposable
    {
        private TempPilotEvidenceWorkspace(string root, PilotEvidencePackagePromotionPaths paths)
        {
            Root = root;
            Paths = paths;
        }

        public string Root { get; }

        public PilotEvidencePackagePromotionPaths Paths { get; }

        public static TempPilotEvidenceWorkspace Create()
        {
            var basePaths = CreatePaths();
            var root = HushVotingReadinessTestArtifacts.CreateEmptyWorkspace("hush-pilot-evidence-");
            var sourceRoot = Path.Combine(
                root,
                "hush-memory-bank",
                "Overview",
                "HushVotingReadiness",
                PilotEvidencePackagePromotionPaths.SourceFolder);
            CopyDirectory(basePaths.SourceRoot, sourceRoot);
            return new TempPilotEvidenceWorkspace(
                root,
                PilotEvidencePackagePromotionPaths.FromWorkspaceRoot(root));
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
