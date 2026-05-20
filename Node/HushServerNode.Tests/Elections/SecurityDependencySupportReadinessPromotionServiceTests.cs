using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using SecurityDependencySupportReadinessPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class SecurityDependencySupportReadinessPromotionServiceTests
{
    private static readonly DateTimeOffset FixedGeneratedAt = DateTimeOffset.Parse("2026-05-19T13:00:00Z");

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        var paths = CreatePaths();

        var errors = SecurityDependencySupportContracts.ValidateSchemaSet(paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in SecurityDependencySupportContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(paths.SchemasRoot, schemaFile)).Should().BeTrue();
        }
    }

    [Fact]
    public void SourceFixtureSet_ReleaseBaseline_IsAcceptedWithWarningsAndPublicSafe()
    {
        var paths = CreatePaths();

        var errors = SecurityDependencySupportContracts.ValidateSourceFixtureSet(paths, generatedAt: FixedGeneratedAt);
        var result = SecurityDependencySupportChecker.Evaluate(paths, generatedAt: FixedGeneratedAt);

        errors.Should().BeEmpty();
        result.Status.Should().Be("accepted_with_warnings");
        result.Blockers.Should().BeEmpty();
        result.Warnings.Should().Contain(["SDS-003", "SDS-004", "SDS-005", "SDS-007", "SDS-010"]);
        result.Checks.Select(check => check.CheckId).Should().Contain(SecurityDependencySupportContracts.RequiredSdsCheckIds);
    }

    [Fact]
    public void SdsChecker_CommercialProviderLicensing_IsOutOfScopeOfDependencyGate()
    {
        using var workspace = TempSecurityDependencySupportWorkspace.Create();
        var package = LoadExample(workspace.Paths, "examples/release-baseline/security-dependency-support-package.json");
        package["commercialProviderLicensing"] = new JsonObject
        {
            ["scope"] = "out_of_scope",
            ["note"] = "Provider/customer entitlement does not re-enable third-party dependency gates per Deployment Proof Package."
        };
        WriteExample(workspace.Paths, "examples/release-baseline/security-dependency-support-package.json", package);

        var result = SecurityDependencySupportChecker.Evaluate(workspace.Paths, generatedAt: FixedGeneratedAt);

        result.Blockers.Should().BeEmpty();
        result.Checks.Single(check => check.CheckId == "SDS-003").Status.Should().Be("warning");
        result.Checks.Single(check => check.CheckId == "SDS-003").Reason.Should().NotContain("Provider");
    }

    [Fact]
    public void SdsChecker_OpenCriticalOrHighVulnerability_Blocks()
    {
        using var workspace = TempSecurityDependencySupportWorkspace.Create();
        var vulnerability = LoadExample(workspace.Paths, "examples/release-baseline/vulnerability-scan-normalized.json");
        vulnerability["findings"]!.AsArray().Add(new JsonObject
        {
            ["findingId"] = "SDS-VULN-HIGH-TEST",
            ["package"] = "test-package",
            ["severity"] = "high",
            ["status"] = "open",
        });
        WriteExample(workspace.Paths, "examples/release-baseline/vulnerability-scan-normalized.json", vulnerability);

        var result = SecurityDependencySupportChecker.Evaluate(workspace.Paths, generatedAt: FixedGeneratedAt);

        result.Status.Should().Be("blocked");
        result.Blockers.Should().Contain("SDS-004");
        result.Checks.Single(check => check.CheckId == "SDS-004").Reason.Should().Contain("open high vulnerability");
    }

    [Fact]
    public void SdsChecker_StaleVulnerabilityScan_Blocks()
    {
        using var workspace = TempSecurityDependencySupportWorkspace.Create();
        var vulnerability = LoadExample(workspace.Paths, "examples/release-baseline/vulnerability-scan-normalized.json");
        vulnerability["freshness"]!.AsObject()["producedAt"] = "2026-03-01T00:00:00Z";
        WriteExample(workspace.Paths, "examples/release-baseline/vulnerability-scan-normalized.json", vulnerability);

        var result = SecurityDependencySupportChecker.Evaluate(workspace.Paths, generatedAt: FixedGeneratedAt);

        result.Status.Should().Be("blocked");
        result.Blockers.Should().Contain("SDS-004");
        result.Checks.Single(check => check.CheckId == "SDS-004").Reason.Should().Contain("older than 30 days");
    }

    [Fact]
    public void SdsChecker_UnknownLicense_Blocks()
    {
        using var workspace = TempSecurityDependencySupportWorkspace.Create();
        var license = LoadExample(workspace.Paths, "examples/release-baseline/license-scan-normalized.json");
        license["unknownLicenses"] = new JsonArray("mystery-package");
        license["licenseFindings"]!.AsArray().Add(new JsonObject
        {
            ["findingId"] = "SDS-LIC-UNKNOWN-TEST",
            ["dependencyName"] = "mystery-package",
            ["componentId"] = "hush-web-client",
            ["license"] = "UNKNOWN",
            ["classification"] = "unknown",
            ["scope"] = "client_runtime",
            ["status"] = "unresolved"
        });
        WriteExample(workspace.Paths, "examples/release-baseline/license-scan-normalized.json", license);

        var result = SecurityDependencySupportChecker.Evaluate(workspace.Paths, generatedAt: FixedGeneratedAt);

        result.Status.Should().Be("blocked");
        result.Blockers.Should().Contain("SDS-003");
    }

    [Fact]
    public void SdsChecker_RestrictedClientRuntimeDependencyWithoutException_Blocks()
    {
        using var workspace = TempSecurityDependencySupportWorkspace.Create();
        var license = LoadExample(workspace.Paths, "examples/release-baseline/license-scan-normalized.json");
        var finding = license["licenseFindings"]!.AsArray()[0]!.AsObject();
        finding.Remove("exceptionRef");
        finding["status"] = "unresolved";
        WriteExample(workspace.Paths, "examples/release-baseline/license-scan-normalized.json", license);

        var result = SecurityDependencySupportChecker.Evaluate(workspace.Paths, generatedAt: FixedGeneratedAt);

        result.Status.Should().Be("blocked");
        result.Blockers.Should().Contain("SDS-003");
        result.Checks.Single(check => check.CheckId == "SDS-003").Reason.Should().Contain("without exception");
    }

    [Fact]
    public void SdsChecker_MissingMobileEvidence_Blocks()
    {
        using var workspace = TempSecurityDependencySupportWorkspace.Create();
        var guidance = LoadExample(workspace.Paths, "examples/release-baseline/voter-client-integrity-guidance.json");
        guidance["mobileEvidenceRefs"] = new JsonArray();
        WriteExample(workspace.Paths, "examples/release-baseline/voter-client-integrity-guidance.json", guidance);

        var result = SecurityDependencySupportChecker.Evaluate(workspace.Paths, generatedAt: FixedGeneratedAt);

        result.Status.Should().Be("blocked");
        result.Blockers.Should().Contain("SDS-007");
    }

    [Fact]
    public void SdsChecker_SupportExportForbiddenMaterial_Blocks()
    {
        using var workspace = TempSecurityDependencySupportWorkspace.Create();
        var proof = LoadExample(workspace.Paths, "examples/release-baseline/support-export-privacy-proof.json");
        proof["leakTest"] = "receipt secret leaked in support export";
        WriteExample(workspace.Paths, "examples/release-baseline/support-export-privacy-proof.json", proof);

        var result = SecurityDependencySupportChecker.Evaluate(workspace.Paths, generatedAt: FixedGeneratedAt);

        result.Status.Should().Be("blocked");
        result.Blockers.Should().Contain("SDS-009");
        result.ForbiddenMaterialFindings.Should().Contain(finding => finding.Category == "receipt_secret");
    }

    [Fact]
    public void ArtifactGenerator_RequiredOutputs_ReadinessFragmentAndHandoff_AreGenerated()
    {
        var paths = CreatePaths();

        var generated = SecurityDependencySupportArtifactGenerator.Generate(
            paths,
            sourceInput: null,
            releaseId: "HV-REL-BASELINE-2026-05",
            version: "v0.1.0",
            generatedAt: FixedGeneratedAt,
            publicationStatus: "not_for_publication");

        generated.Status.Should().Be("accepted_with_warnings");
        generated.Artifacts.Select(artifact => artifact.RelativePath)
            .Should()
            .Contain(SecurityDependencySupportContracts.RequiredOutputFiles);
        generated.ScanFindings.Should().BeEmpty();
        var generatedText = string.Join('\n', generated.Artifacts.Select(artifact => artifact.RelativePath + "\n" + artifact.Content));
        generatedText.Should()
            .NotContain("FEAT-")
            .And.NotContain("EPIC-")
            .And.NotContain("AT-RDY")
            .And.NotContain("RDY-");

        var readiness = ParseArtifact(generated, SecurityDependencySupportArtifactGenerator.ReadinessFragmentPath);
        readiness["evidenceId"]!.GetValue<string>().Should().Be(SecurityDependencySupportArtifactGenerator.ExternalReadinessEvidenceId);
        readiness.Should().NotContainKey("featureSlice");
        readiness.Should().NotContainKey("acceptanceGate");

        var handoff = ParseArtifact(generated, SecurityDependencySupportArtifactGenerator.SecuritySupportHandoffPath);
        handoff["producer"]!.GetValue<string>().Should().Be("security_dependency_support_readiness");
        handoff["consumerInstructions"]!.AsObject().Should().ContainKey("pilotEvidencePackage");
    }

    [Fact]
    public void ArtifactGenerator_PublicSummary_DoesNotExposeRestrictedMaterial()
    {
        var paths = CreatePaths();

        var generated = SecurityDependencySupportArtifactGenerator.Generate(
            paths,
            sourceInput: null,
            releaseId: "HV-REL-BASELINE-2026-05",
            version: "v0.1.0",
            generatedAt: FixedGeneratedAt,
            publicationStatus: "not_for_publication");

        var publicSummary = generated.Artifacts.Single(artifact =>
            artifact.RelativePath == SecurityDependencySupportArtifactGenerator.PublicSafeSummaryPath).Content;
        publicSummary.Should().Contain("Commercial provider/customer licensing is outside");
        publicSummary.Should().NotContain("rawReportHash")
            .And.NotContain("arn:aws:kms")
            .And.NotContain("BEGIN PRIVATE KEY");
    }

    [Fact]
    public void PromotionService_ValidateOnly_WritesNoFiles()
    {
        using var workspace = TempSecurityDependencySupportWorkspace.Create();
        var outputRoot = Path.Combine(workspace.Root, "output");

        var result = new SecurityDependencySupportPromotionService().Promote(new SecurityDependencySupportPromotionOptions(
            workspace.Paths,
            Mode: null,
            SourceInput: null,
            ReleaseId: "HV-REL-BASELINE-2026-05",
            Version: "v0.1.0",
            GeneratedAt: FixedGeneratedAt,
            OutputRoot: outputRoot,
            PublicationStatus: null,
            ValidateOnly: true));

        result.Mode.Should().Be(SecurityDependencySupportPromotionService.ModeValidateOnly);
        result.WrittenFiles.Should().BeEmpty();
        Directory.Exists(outputRoot).Should().BeFalse();
    }

    [Fact]
    public void PromotionService_CheckOnly_ReturnsSdsResultsWithoutWrites()
    {
        using var workspace = TempSecurityDependencySupportWorkspace.Create();
        var outputRoot = Path.Combine(workspace.Root, "output");

        var result = new SecurityDependencySupportPromotionService().Promote(new SecurityDependencySupportPromotionOptions(
            workspace.Paths,
            SecurityDependencySupportPromotionService.ModeCheckOnly,
            SourceInput: null,
            ReleaseId: "HV-REL-BASELINE-2026-05",
            Version: "v0.1.0",
            GeneratedAt: FixedGeneratedAt,
            OutputRoot: outputRoot,
            PublicationStatus: null,
            ValidateOnly: false));

        result.Mode.Should().Be(SecurityDependencySupportPromotionService.ModeCheckOnly);
        result.CheckResult.Checks.Select(check => check.CheckId).Should().Contain(SecurityDependencySupportContracts.RequiredSdsCheckIds);
        result.WrittenFiles.Should().BeEmpty();
        Directory.Exists(outputRoot).Should().BeFalse();
    }

    [Fact]
    public void PromotionService_Package_WritesRequiredPackageCatalogAndArchiveDeterministically()
    {
        using var workspace = TempSecurityDependencySupportWorkspace.Create();
        var outputRoot = Path.Combine(workspace.Root, "output");
        var service = new SecurityDependencySupportPromotionService();
        var options = new SecurityDependencySupportPromotionOptions(
            workspace.Paths,
            SecurityDependencySupportPromotionService.ModePackage,
            SourceInput: null,
            ReleaseId: "HV-REL-BASELINE-2026-05",
            Version: "v0.1.0",
            GeneratedAt: FixedGeneratedAt,
            OutputRoot: outputRoot,
            PublicationStatus: null,
            ValidateOnly: false);

        var first = service.Promote(options);
        var second = service.Promote(options);

        first.Status.Should().Be("accepted_with_warnings");
        File.Exists(Path.Combine(outputRoot, "packages", "HV-REL-BASELINE-2026-05", "security-dependency-support-package.json"))
            .Should().BeTrue();
        File.Exists(Path.Combine(outputRoot, "security-dependency-support-catalog.json")).Should().BeTrue();
        File.Exists(Path.Combine(outputRoot, "archives", "HV-REL-BASELINE-2026-05-v0.1.0-security-dependency-support.zip"))
            .Should().BeTrue();
        first.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash))
            .Should()
            .Equal(second.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash)));
    }

    [Fact]
    public void PromotionService_CatalogHashConflict_FailsClosed()
    {
        using var workspace = TempSecurityDependencySupportWorkspace.Create();
        var outputRoot = Path.Combine(workspace.Root, "output");
        var service = new SecurityDependencySupportPromotionService();
        var options = new SecurityDependencySupportPromotionOptions(
            workspace.Paths,
            SecurityDependencySupportPromotionService.ModePackage,
            SourceInput: null,
            ReleaseId: "HV-REL-BASELINE-2026-05",
            Version: "v0.1.0",
            GeneratedAt: FixedGeneratedAt,
            OutputRoot: outputRoot,
            PublicationStatus: null,
            ValidateOnly: false);
        service.Promote(options);
        Directory.Delete(Path.Combine(outputRoot, "packages"), recursive: true);
        var package = LoadExample(workspace.Paths, "examples/release-baseline/security-dependency-support-package.json");
        package["claimLevel"] = "mutated-for-hash-conflict";
        WriteExample(workspace.Paths, "examples/release-baseline/security-dependency-support-package.json", package);

        var act = () => service.Promote(options);

        act.Should()
            .Throw<SecurityDependencySupportPromotionException>()
            .WithMessage("FEAT-134 catalog hash conflict*");
    }

    [Fact]
    public void PromotionService_SourceTraversal_IsRejectedBeforeWrites()
    {
        using var workspace = TempSecurityDependencySupportWorkspace.Create();
        var outputRoot = Path.Combine(workspace.Root, "output");
        var package = LoadExample(workspace.Paths, "examples/release-baseline/security-dependency-support-package.json");
        package["sourceRefs"]!.AsObject()["dependencyInventory"] = "../outside.json";
        WriteExample(workspace.Paths, "examples/release-baseline/security-dependency-support-package.json", package);

        var act = () => new SecurityDependencySupportPromotionService().Promote(new SecurityDependencySupportPromotionOptions(
            workspace.Paths,
            SecurityDependencySupportPromotionService.ModePackage,
            SourceInput: null,
            ReleaseId: "HV-REL-BASELINE-2026-05",
            Version: "v0.1.0",
            GeneratedAt: FixedGeneratedAt,
            OutputRoot: outputRoot,
            PublicationStatus: null,
            ValidateOnly: false));

        act.Should()
            .Throw<SecurityDependencySupportPromotionException>()
            .WithMessage("FEAT-134 source fixture validation failed.");
        Directory.Exists(outputRoot).Should().BeFalse();
    }

    [Fact]
    public void PowerShellWrapper_ExistsAtStableScriptPath()
    {
        File.Exists(Path.Combine(
                HushVotingReadinessTestArtifacts.ServerNodeRoot,
                "Node",
                "scripts",
                "promote-security-dependency-support-readiness.ps1"))
            .Should()
            .BeTrue();
    }

    private static SecurityDependencySupportPromotionPaths CreatePaths() =>
        HushVotingReadinessTestArtifacts.CreateSecurityDependencySupportPaths();

    private static JsonObject LoadExample(SecurityDependencySupportPromotionPaths paths, string relativePath) =>
        SecurityDependencySupportContracts.ReadJsonObject(
            Path.Combine(paths.SourceRoot, relativePath),
            relativePath);

    private static JsonObject ParseArtifact(SecurityDependencySupportGeneratedPackage generated, string relativePath) =>
        JsonNode.Parse(generated.Artifacts.Single(artifact => artifact.RelativePath == relativePath).Content)?.AsObject() ??
        throw new InvalidOperationException($"Generated artifact {relativePath} is not a JSON object.");

    private static void WriteExample(
        SecurityDependencySupportPromotionPaths paths,
        string relativePath,
        JsonObject value)
    {
        var path = Path.Combine(paths.SourceRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        }));
    }

    private sealed class TempSecurityDependencySupportWorkspace : IDisposable
    {
        private TempSecurityDependencySupportWorkspace(string root, SecurityDependencySupportPromotionPaths paths)
        {
            Root = root;
            Paths = paths;
        }

        public string Root { get; }

        public SecurityDependencySupportPromotionPaths Paths { get; }

        public static TempSecurityDependencySupportWorkspace Create()
        {
            var basePaths = CreatePaths();
            var root = HushVotingReadinessTestArtifacts.CreateEmptyWorkspace("hush-security-dependency-support-");
            var sourceRoot = Path.Combine(root, "hush-memory-bank", "Overview", "HushVotingReadiness", "Security-Dependency-Support-Readiness");
            CopyDirectory(basePaths.SourceRoot, sourceRoot);
            var paths = SecurityDependencySupportPromotionPaths.FromWorkspaceRoot(root);
            return new TempSecurityDependencySupportWorkspace(root, paths);
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
