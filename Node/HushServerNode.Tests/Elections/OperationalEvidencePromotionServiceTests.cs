using System.Text.Json.Nodes;
using FluentAssertions;
using HushShared.Elections.Verification.Model;
using OperationalEvidencePromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class OperationalEvidencePromotionServiceTests
{
    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        var paths = CreatePaths();

        var errors = OperationalEvidenceContracts.ValidateSchemaSet(paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in OperationalEvidenceContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(paths.SchemasRoot, schemaFile)).Should().BeTrue();
        }
    }

    [Fact]
    public void SourceFixtureSet_InternalRehearsal_IsAcceptedAndPublicSafe()
    {
        var paths = CreatePaths();

        var errors = OperationalEvidenceContracts.ValidateSourceFixtureSet(paths);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void OperationalRun_MissingRunId_FailsContractValidation()
    {
        var paths = CreatePaths();
        var run = LoadExample(paths, OperationalEvidenceContracts.AcceptedRunFixture);
        run.Remove("runId");

        var errors = OperationalEvidenceContracts.ValidateOperationalRun(run, paths);

        errors.Should().Contain(error => error.Contains("runId", StringComparison.Ordinal));
    }

    [Fact]
    public void OperationalRun_UnknownDeploymentClassification_FailsClosed()
    {
        var paths = CreatePaths();
        var run = LoadExample(paths, OperationalEvidenceContracts.AcceptedRunFixture);
        var feat132Refs = run["feat132Refs"]!.AsObject();
        feat132Refs["unknownClassificationState"] = "unknown_pending_classification";
        feat132Refs["impactClassification"] = "unknown_pending_classification";

        var errors = OperationalEvidenceContracts.ValidateOperationalRun(run, paths);

        errors.Should().Contain(error => error.Contains("unknownClassificationState", StringComparison.Ordinal));
        errors.Should().Contain(error => error.Contains("unknown_pending_classification", StringComparison.Ordinal));
    }

    [Fact]
    public void OperationalRun_RequiredCustodyBlocker_FailsClosed()
    {
        var paths = CreatePaths();
        var run = LoadExample(paths, OperationalEvidenceContracts.AcceptedRunFixture);
        run["feat131Refs"]!.AsObject()["unresolvedBlockers"] = new JsonArray("CUSTODY-BLOCKER-TEST");

        var errors = OperationalEvidenceContracts.ValidateOperationalRun(run, paths);

        errors.Should().Contain(error => error.Contains("unresolvedBlockers", StringComparison.Ordinal));
    }

    [Fact]
    public void OperationalRun_PlaceholderAcceptedEvidence_FailsClosed()
    {
        var paths = CreatePaths();
        var run = LoadExample(paths, OperationalEvidenceContracts.AcceptedRunFixture);
        var placeholderState = run["placeholderState"]!.AsObject();
        placeholderState["hasPlaceholders"] = true;
        placeholderState["placeholderRefs"] = new JsonArray("OPS-PLACEHOLDER-001");

        var errors = OperationalEvidenceContracts.ValidateOperationalRun(run, paths);

        errors.Should().Contain(error => error.Contains("placeholders", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OperationalCheckResults_RequireEveryOpsCheckAndExpectedScoreMovement()
    {
        var paths = CreatePaths();
        var results = LoadExample(paths, OperationalEvidenceContracts.CheckResultsFixture);

        var errors = OperationalEvidenceContracts.ValidateOperationalCheckResults(results);

        errors.Should().BeEmpty();
        var checks = results["checks"]!.AsArray()
            .Select(node => node!["checkId"]!.GetValue<string>())
            .ToArray();
        checks.Should().Contain(OperationalEvidenceContracts.RequiredOpsCheckIds);
        results["scoreEffect"]!.AsObject()["dimensionId"]!.GetValue<string>().Should().Be("RDY-DIM-007");
    }

    [Fact]
    public void OperationalCheckResults_MissingOpsCheck_FailsContractValidation()
    {
        var paths = CreatePaths();
        var results = LoadExample(paths, OperationalEvidenceContracts.CheckResultsFixture);
        var checks = results["checks"]!.AsArray();
        var filtered = new JsonArray(checks
            .Where(node => node!["checkId"]!.GetValue<string>() != "OPS-007")
            .Select(node => node!.DeepClone())
            .ToArray());
        results["checks"] = filtered;

        var errors = OperationalEvidenceContracts.ValidateOperationalCheckResults(results);

        errors.Should().Contain(error => error.Contains("OPS-007", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadinessFragment_RecordsFeat133ScoreMovementAndNoDirectPromotion()
    {
        var paths = CreatePaths();
        var readiness = LoadExample(paths, OperationalEvidenceContracts.ReadinessFragmentFixture);

        var errors = OperationalEvidenceContracts.ValidateReadinessFragment(readiness);

        errors.Should().BeEmpty();
        readiness["fragmentId"]!.GetValue<string>().Should().Be("RDY-EVID-AT-RDY-006-FEAT-133-001");
        readiness["promotionInstructions"]!.GetValue<string>().Should().Contain("FEAT-130");
        readiness["dimensionScoreChange"]!.AsObject()["acceptedScore"]!.GetValue<int>().Should().Be(8);
        readiness["totalScoreChange"]!.AsObject()["acceptedScore"]!.GetValue<int>().Should().Be(61);
    }

    [Fact]
    public void DownstreamHandoff_ProvidesStableConsumerRefsWithoutRawPrivateEvidence()
    {
        var paths = CreatePaths();
        var handoff = LoadExample(paths, OperationalEvidenceContracts.HandoffFixture);

        var errors = OperationalEvidenceContracts.ValidateOperationalHandoff(handoff);

        errors.Should().BeEmpty();
        handoff["producerFeature"]!.GetValue<string>().Should().Be("FEAT-133");
        handoff["consumerInstructions"]!.AsObject().Should().ContainKey("FEAT-130");
        handoff["consumerInstructions"]!.AsObject().Should().ContainKey("FEAT-137");
        handoff["consumerInstructions"]!.AsObject().Should().ContainKey("FEAT-141");
        handoff["consumerInstructions"]!.AsObject().Should().ContainKey("FEAT-142");
    }

    [Fact]
    public void PublicExamples_ForbiddenMaterial_FailsContractValidation()
    {
        var paths = CreatePaths();
        var handoff = LoadExample(paths, OperationalEvidenceContracts.HandoffFixture);
        handoff["publicLeakTest"] = "arn:aws:kms:eu-west-1:123456789012:key/leaked";

        var errors = OperationalEvidenceContracts.ValidateOperationalHandoff(handoff);

        errors.Should().Contain(error => error.Contains("arn:aws:kms", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("direct provider account identifier", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OpsChecker_AcceptedInternalRehearsal_ReturnsAcceptedWithExpectedWarnings()
    {
        var paths = CreatePaths();

        var result = OperationalEvidenceChecker.Evaluate(paths);

        result.Status.Should().Be("accepted_with_warnings");
        result.Blockers.Should().BeEmpty();
        result.PlaceholderFindings.Should().BeEmpty();
        result.ForbiddenMaterialFindings.Should().BeEmpty();
        result.Warnings.Should().BeEquivalentTo(["OPS-006", "OPS-008"]);
        result.NotApplicable.Should().Contain("OPS-004");
        result.Checks.Select(check => check.CheckId).Should().Contain(OperationalEvidenceContracts.RequiredOpsCheckIds);
    }

    [Fact]
    public void OpsChecker_MissingDeploymentProfile_BlocksOps000()
    {
        var paths = CreatePaths();
        var run = LoadExample(paths, OperationalEvidenceContracts.AcceptedRunFixture);
        run.Remove("deploymentProfile");

        var result = OperationalEvidenceChecker.Evaluate(paths, run);

        result.Status.Should().Be("blocked");
        result.Blockers.Should().Contain("OPS-000");
        result.Checks.Single(check => check.CheckId == "OPS-000").Reason.Should().Contain("Deployment profile");
    }

    [Fact]
    public void OpsChecker_Sp08AgreementMissing_BlocksOps001()
    {
        var paths = CreatePaths();
        var run = LoadExample(paths, OperationalEvidenceContracts.AcceptedRunFixture);
        run["sp08Refs"]!.AsObject()["agreesWithFeat132DeploymentRefs"] = false;

        var result = OperationalEvidenceChecker.Evaluate(paths, run);

        result.Status.Should().Be("blocked");
        result.Blockers.Should().Contain("OPS-001");
    }

    [Fact]
    public void OpsChecker_UnknownDeploymentClassification_BlocksOps001()
    {
        var paths = CreatePaths();
        var run = LoadExample(paths, OperationalEvidenceContracts.AcceptedRunFixture);
        var feat132Refs = run["feat132Refs"]!.AsObject();
        feat132Refs["unknownClassificationState"] = "unknown_pending_classification";
        feat132Refs["impactClassification"] = "unknown_pending_classification";

        var result = OperationalEvidenceChecker.Evaluate(paths, run);

        result.Status.Should().Be("blocked");
        result.Blockers.Should().Contain("OPS-001");
    }

    [Fact]
    public void OpsChecker_CustodyBlocker_BlocksOps003()
    {
        var paths = CreatePaths();
        var run = LoadExample(paths, OperationalEvidenceContracts.AcceptedRunFixture);
        run["feat131Refs"]!.AsObject()["unresolvedBlockers"] = new JsonArray("CUSTODY-BLOCKER-TEST");

        var result = OperationalEvidenceChecker.Evaluate(paths, run);

        result.Status.Should().Be("blocked");
        result.Blockers.Should().Contain("OPS-003");
    }

    [Fact]
    public void OpsChecker_PublicForbiddenMaterial_BlocksOps005()
    {
        var paths = CreatePaths();
        var run = LoadExample(paths, OperationalEvidenceContracts.AcceptedRunFixture);
        run["publicLeakTest"] = "arn:aws:kms:eu-west-1:123456789012:key/leaked";

        var result = OperationalEvidenceChecker.Evaluate(paths, run);

        result.Status.Should().Be("blocked");
        result.Blockers.Should().Contain("OPS-005");
        result.ForbiddenMaterialFindings.Should().NotBeEmpty();
    }

    [Fact]
    public void OpsChecker_MissingIncidentDeclaration_BlocksOps007()
    {
        using var workspace = TempOperationalEvidenceWorkspace.Create();
        var incident = LoadExample(workspace.Paths, "incidents/incident-source.json");
        incident.Remove("publicSafeStatement");
        WriteExample(workspace.Paths, "incidents/incident-source.json", incident);

        var result = OperationalEvidenceChecker.Evaluate(workspace.Paths);

        result.Status.Should().Be("blocked");
        result.Blockers.Should().Contain("OPS-007");
    }

    [Fact]
    public void OpsChecker_IncompleteAccessSnapshot_IsWarningForInternalRehearsal()
    {
        using var workspace = TempOperationalEvidenceWorkspace.Create();
        var access = LoadExample(workspace.Paths, "access-control/access-control-source.json");
        access.Remove("roleCounts");
        WriteExample(workspace.Paths, "access-control/access-control-source.json", access);

        var result = OperationalEvidenceChecker.Evaluate(workspace.Paths);

        result.Blockers.Should().BeEmpty();
        result.Warnings.Should().Contain(["OPS-002", "OPS-006", "OPS-008"]);
    }

    [Fact]
    public void OpsChecker_PlaceholderAcceptedEvidence_BlocksScoreIncrease()
    {
        var paths = CreatePaths();
        var run = LoadExample(paths, OperationalEvidenceContracts.AcceptedRunFixture);
        var placeholderState = run["placeholderState"]!.AsObject();
        placeholderState["hasPlaceholders"] = true;
        placeholderState["placeholderRefs"] = new JsonArray("OPS-PLACEHOLDER-001");

        var result = OperationalEvidenceChecker.Evaluate(paths, run);

        result.Status.Should().Be("blocked");
        result.PlaceholderFindings.Should().Contain("OPS-PLACEHOLDER-001");
        result.BlocksAcceptedEvidence.Should().BeTrue();
    }

    [Fact]
    public void ArtifactGenerator_PublicSp10Artifacts_AreGeneratedWithStableRefs()
    {
        var paths = CreatePaths();

        var generated = OperationalEvidenceArtifactGenerator.Generate(paths, FixedGeneratedAt);

        generated.GenerationStatus.Should().Be(
            "accepted_with_warnings",
            string.Join("; ", generated.ScanFindings.Select(finding =>
                $"{finding.Boundary}:{finding.RelativePath}:{finding.Category}:{finding.Evidence}")));
        generated.Artifacts
            .Where(artifact => artifact.Visibility == OperationalEvidenceArtifactVisibility.Public)
            .Select(artifact => artifact.RelativePath)
            .Should()
            .Contain([
                VerificationPackageFileNames.Sp10OperationalSecuritySummary,
                VerificationPackageFileNames.Sp10OperationalDeploymentEvidence,
                VerificationPackageFileNames.Sp10OperationalCustodyEvidence,
                VerificationPackageFileNames.Sp10OperationalVerifierOutput,
            ]);
        generated.ScanFindings.Should().BeEmpty();

        var summary = System.Text.Json.JsonSerializer.Deserialize<ElectionSp10OperationalSecurityStatusArtifactRecord>(
            generated.GetArtifact(VerificationPackageFileNames.Sp10OperationalSecuritySummary).Content,
            VerificationJson.Options);
        summary.Should().NotBeNull();
        ElectionSp10OperationalSecurityRules.Validate(summary!).Should().BeEmpty();

        var deployment = ParseArtifact(generated, VerificationPackageFileNames.Sp10OperationalDeploymentEvidence);
        deployment["feat132Refs"]!.AsObject()["webClientProofId"]!.GetValue<string>()
            .Should()
            .Be("DPP-WEB-20260519-001");
        deployment["feat132Refs"]!.AsObject()["serverNodeProofId"]!.GetValue<string>()
            .Should()
            .Be("DPP-SERVER-20260519-001");
        generated.GetArtifact(VerificationPackageFileNames.Sp10OperationalDeploymentEvidence)
            .Content.Should()
            .NotContain("artifacts/restricted");

        var custody = ParseArtifact(generated, VerificationPackageFileNames.Sp10OperationalCustodyEvidence);
        custody["feat131Refs"]!.AsObject()["custodyEvidenceId"]!.GetValue<string>()
            .Should()
            .Be("RDY-EVID-AT-RDY-002-FEAT-131-001");
        custody["feat131Refs"]!.AsObject()["publicCustodyHash"]!.GetValue<string>()
            .Should()
            .Be("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee");

        var verifier = ParseArtifact(generated, VerificationPackageFileNames.Sp10OperationalVerifierOutput);
        verifier["results"]!.AsArray()
            .Select(node => node!["checkCode"]!.GetValue<string>())
            .Should()
            .Contain(OperationalEvidenceContracts.RequiredOpsCheckIds);
    }

    [Fact]
    public void ArtifactGenerator_RestrictedArtifacts_AreGeneratedWithRestrictedRefs()
    {
        var paths = CreatePaths();

        var generated = OperationalEvidenceArtifactGenerator.Generate(paths, FixedGeneratedAt);

        generated.Artifacts
            .Where(artifact => artifact.Visibility == OperationalEvidenceArtifactVisibility.Restricted)
            .Select(artifact => artifact.RelativePath)
            .Should()
            .Contain([
                VerificationPackageFileNames.RestrictedSp10AccessControlSnapshot,
                VerificationPackageFileNames.RestrictedSp10LoggingEvidence,
                VerificationPackageFileNames.RestrictedSp10BackupRestoreEvidence,
                VerificationPackageFileNames.RestrictedSp10IncidentEvidence,
                VerificationPackageFileNames.RestrictedSp10AuditorRoomAccessLog,
            ]);
        generated.GetArtifact(VerificationPackageFileNames.RestrictedSp10AccessControlSnapshot)
            .Content.Should()
            .Contain("restrictedActorHashesRef");
        generated.GetArtifact(VerificationPackageFileNames.RestrictedSp10LoggingEvidence)
            .Content.Should()
            .Contain("rawLogRestrictedRefs");
        generated.GetArtifact(OperationalEvidenceArtifactGenerator.PublicSummaryMarkdownPath)
            .Content.Should()
            .NotContain("rawLogRestrictedRefs")
            .And.NotContain("artifacts/restricted");
    }

    [Fact]
    public void ArtifactGenerator_MarkdownDocuments_AreGeneratedFromCanonicalBoundaries()
    {
        var paths = CreatePaths();

        var generated = OperationalEvidenceArtifactGenerator.Generate(paths, FixedGeneratedAt);

        var publicSummary = generated.GetArtifact(OperationalEvidenceArtifactGenerator.PublicSummaryMarkdownPath);
        publicSummary.Content.Should().Contain("# FEAT-133 Public Operational Summary");
        publicSummary.Content.Should().Contain("OPS-006");
        publicSummary.Content.Should().NotContain("\r\n");
        publicSummary.Content.Should().NotContain("artifacts/restricted");

        var restrictedIndex = generated.GetArtifact(OperationalEvidenceArtifactGenerator.RestrictedIndexMarkdownPath);
        restrictedIndex.Content.Should().Contain("# FEAT-133 Restricted Operational Evidence Index");
        restrictedIndex.Content.Should().Contain(VerificationPackageFileNames.RestrictedSp10AccessControlSnapshot);
        restrictedIndex.Content.Should().Contain("Review Instructions");
        restrictedIndex.Content.Should().NotContain("\r\n");
    }

    [Fact]
    public void ArtifactGenerator_PublicForbiddenMaterial_BlocksGeneratedScan()
    {
        var paths = CreatePaths();
        var run = LoadExample(paths, OperationalEvidenceContracts.AcceptedRunFixture);
        run["sp08Refs"]!.AsObject()["immutableDeploymentRef"] =
            "arn:aws:kms:eu-west-1:123456789012:key/leaked";

        var generated = OperationalEvidenceArtifactGenerator.Generate(paths, run, FixedGeneratedAt);

        generated.GenerationStatus.Should().Be("blocked");
        generated.BlocksAcceptedEvidence.Should().BeTrue();
        generated.ScanFindings.Should().Contain(finding =>
            finding.Boundary == "public" &&
            finding.Category == "kms" &&
            finding.RelativePath == VerificationPackageFileNames.Sp10OperationalDeploymentEvidence);
        generated.ScanFindings.Should().Contain(finding =>
            finding.Boundary == "public" &&
            finding.Category == "provider_account_identifier");
    }

    [Fact]
    public void ArtifactGenerator_RestrictedForbiddenMaterial_BlocksGeneratedScan()
    {
        using var workspace = TempOperationalEvidenceWorkspace.Create();
        var logging = LoadExample(workspace.Paths, "logging/logging-source.json");
        logging["rawLogRestrictedRefs"] = new JsonArray("aws_secret_access_key=do-not-log");
        WriteExample(workspace.Paths, "logging/logging-source.json", logging);

        var generated = OperationalEvidenceArtifactGenerator.Generate(workspace.Paths, FixedGeneratedAt);

        generated.GenerationStatus.Should().Be("blocked");
        generated.BlocksAcceptedEvidence.Should().BeTrue();
        generated.ScanFindings.Should().Contain(finding =>
            finding.Boundary == "restricted" &&
            finding.Category == "credential" &&
            finding.RelativePath == VerificationPackageFileNames.RestrictedSp10LoggingEvidence);
    }

    [Fact]
    public void ArtifactGenerator_SameSourcesAndTimestamp_AreDeterministic()
    {
        var paths = CreatePaths();

        var first = OperationalEvidenceArtifactGenerator.Generate(paths, FixedGeneratedAt);
        var second = OperationalEvidenceArtifactGenerator.Generate(paths, FixedGeneratedAt);

        first.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash))
            .Should()
            .Equal(second.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash)));
        first.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Content))
            .Should()
            .Equal(second.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Content)));
    }

    [Fact]
    public void PromotionService_ValidateOnly_WritesNoFiles()
    {
        using var workspace = TempOperationalEvidenceWorkspace.Create();
        var packageOutputRoot = Path.Combine(workspace.Root, "package-output");
        var restrictedOutputRoot = Path.Combine(workspace.Root, "restricted-output");

        var result = new OperationalEvidencePromotionService().Promote(new OperationalEvidencePromotionOptions(
            workspace.Paths,
            Mode: null,
            RunId: null,
            GeneratedAt: FixedGeneratedAt,
            PackageOutputRoot: packageOutputRoot,
            RestrictedOutputRoot: restrictedOutputRoot,
            ValidateOnly: true,
            AllowLiveCapture: false));

        result.Mode.Should().Be(OperationalEvidencePromotionService.ModeValidateOnly);
        result.WrittenFiles.Should().BeEmpty();
        Directory.Exists(packageOutputRoot).Should().BeFalse();
        Directory.Exists(restrictedOutputRoot).Should().BeFalse();
    }

    [Fact]
    public void PromotionService_CheckOnly_ReturnsOpsResultsWithoutWrites()
    {
        using var workspace = TempOperationalEvidenceWorkspace.Create();
        var packageOutputRoot = Path.Combine(workspace.Root, "package-output");
        var restrictedOutputRoot = Path.Combine(workspace.Root, "restricted-output");

        var result = new OperationalEvidencePromotionService().Promote(new OperationalEvidencePromotionOptions(
            workspace.Paths,
            OperationalEvidencePromotionService.ModeCheckOnly,
            RunId: "OPS-RUN-REHEARSAL-20260519-001",
            GeneratedAt: FixedGeneratedAt,
            PackageOutputRoot: packageOutputRoot,
            RestrictedOutputRoot: restrictedOutputRoot,
            ValidateOnly: false,
            AllowLiveCapture: false));

        result.Mode.Should().Be(OperationalEvidencePromotionService.ModeCheckOnly);
        result.CheckResult.Checks.Select(check => check.CheckId).Should().Contain(OperationalEvidenceContracts.RequiredOpsCheckIds);
        result.WrittenFiles.Should().BeEmpty();
        Directory.Exists(packageOutputRoot).Should().BeFalse();
        Directory.Exists(restrictedOutputRoot).Should().BeFalse();
    }

    [Fact]
    public void PromotionService_RehearsalPackage_WritesRequiredArtifactsDeterministically()
    {
        using var workspace = TempOperationalEvidenceWorkspace.Create();
        var packageOutputRoot = Path.Combine(workspace.Root, "package-output");
        var restrictedOutputRoot = Path.Combine(workspace.Root, "restricted-output");
        var service = new OperationalEvidencePromotionService();
        var options = new OperationalEvidencePromotionOptions(
            workspace.Paths,
            OperationalEvidencePromotionService.ModeRehearsalPackage,
            RunId: "OPS-RUN-REHEARSAL-20260519-001",
            GeneratedAt: FixedGeneratedAt,
            PackageOutputRoot: packageOutputRoot,
            RestrictedOutputRoot: restrictedOutputRoot,
            ValidateOnly: false,
            AllowLiveCapture: false);

        var first = service.Promote(options);
        var second = service.Promote(options);

        File.Exists(Path.Combine(packageOutputRoot, VerificationPackageFileNames.Sp10OperationalSecuritySummary))
            .Should()
            .BeTrue();
        File.Exists(Path.Combine(packageOutputRoot, OperationalEvidenceArtifactGenerator.OperationalReadinessFragmentPath))
            .Should()
            .BeTrue();
        File.Exists(Path.Combine(restrictedOutputRoot, VerificationPackageFileNames.RestrictedSp10AccessControlSnapshot))
            .Should()
            .BeTrue();
        first.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash))
            .Should()
            .Equal(second.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash)));
        second.WrittenFiles.Count.Should().Be(first.Artifacts.Count);
    }

    [Fact]
    public void PromotionService_RehearsalPackage_RejectsExistingDifferentOutput()
    {
        using var workspace = TempOperationalEvidenceWorkspace.Create();
        var packageOutputRoot = Path.Combine(workspace.Root, "package-output");
        var restrictedOutputRoot = Path.Combine(workspace.Root, "restricted-output");
        var service = new OperationalEvidencePromotionService();
        var options = new OperationalEvidencePromotionOptions(
            workspace.Paths,
            OperationalEvidencePromotionService.ModeRehearsalPackage,
            RunId: null,
            GeneratedAt: FixedGeneratedAt,
            PackageOutputRoot: packageOutputRoot,
            RestrictedOutputRoot: restrictedOutputRoot,
            ValidateOnly: false,
            AllowLiveCapture: false);
        service.Promote(options);
        File.WriteAllText(
            Path.Combine(packageOutputRoot, VerificationPackageFileNames.Sp10OperationalSecuritySummary),
            "changed");

        var act = () => service.Promote(options);

        act.Should()
            .Throw<OperationalEvidencePromotionException>()
            .WithMessage("Existing generated output differs*");
    }

    [Fact]
    public void PromotionService_SourceTraversal_IsRejectedBeforeWrites()
    {
        using var workspace = TempOperationalEvidenceWorkspace.Create();
        var run = LoadExample(workspace.Paths, OperationalEvidenceContracts.AcceptedRunFixture);
        run["sourceRefs"]!.AsObject()["loggingSource"] = "../outside.json";
        WriteExample(workspace.Paths, OperationalEvidenceContracts.AcceptedRunFixture, run);
        var packageOutputRoot = Path.Combine(workspace.Root, "package-output");
        var restrictedOutputRoot = Path.Combine(workspace.Root, "restricted-output");

        var act = () => new OperationalEvidencePromotionService().Promote(new OperationalEvidencePromotionOptions(
            workspace.Paths,
            OperationalEvidencePromotionService.ModeRehearsalPackage,
            RunId: null,
            GeneratedAt: FixedGeneratedAt,
            PackageOutputRoot: packageOutputRoot,
            RestrictedOutputRoot: restrictedOutputRoot,
            ValidateOnly: false,
            AllowLiveCapture: false));

        act.Should()
            .Throw<OperationalEvidencePromotionException>()
            .WithMessage("Operational run source paths failed containment checks.");
        Directory.Exists(packageOutputRoot).Should().BeFalse();
        Directory.Exists(restrictedOutputRoot).Should().BeFalse();
    }

    [Fact]
    public void PowerShellWrapper_ExistsAtStableScriptPath()
    {
        var paths = CreatePaths();

        File.Exists(Path.Combine(
                paths.WorkspaceRoot,
                "hush-server-node",
                "Node",
                "scripts",
                "promote-operational-evidence.ps1"))
            .Should()
            .BeTrue();
    }

    private static OperationalEvidencePromotionPaths CreatePaths()
    {
        var workspaceRoot = WorkspaceRootFinder.Find(AppContext.BaseDirectory);
        return OperationalEvidencePromotionPaths.FromWorkspaceRoot(workspaceRoot);
    }

    private static readonly DateTimeOffset FixedGeneratedAt = DateTimeOffset.Parse("2026-05-19T13:00:00Z");

    private static JsonObject LoadExample(OperationalEvidencePromotionPaths paths, string relativePath) =>
        OperationalEvidenceContracts.ReadJsonObject(
            Path.Combine(paths.ExamplesRoot, relativePath),
            relativePath);

    private static JsonObject ParseArtifact(OperationalEvidenceGeneratedArtifactSet generated, string relativePath) =>
        JsonNode.Parse(generated.GetArtifact(relativePath).Content)?.AsObject() ??
        throw new InvalidOperationException($"Generated artifact {relativePath} is not a JSON object.");

    private static void WriteExample(
        OperationalEvidencePromotionPaths paths,
        string relativePath,
        JsonObject value)
    {
        var path = Path.Combine(paths.ExamplesRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value.ToJsonString(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        }));
    }

    private sealed class TempOperationalEvidenceWorkspace : IDisposable
    {
        private TempOperationalEvidenceWorkspace(string root, OperationalEvidencePromotionPaths paths)
        {
            Root = root;
            Paths = paths;
        }

        public string Root { get; }

        public OperationalEvidencePromotionPaths Paths { get; }

        public static TempOperationalEvidenceWorkspace Create()
        {
            var basePaths = CreatePaths();
            var root = Path.Combine(basePaths.WorkspaceRoot, ".tmp-feat133-tests", Guid.NewGuid().ToString("N"));
            var sourceRoot = Path.Combine(root, "Operational-Evidence");
            CopyDirectory(basePaths.SourceRoot, sourceRoot);
            var restrictedRoot = Path.Combine(root, "restricted");
            Directory.CreateDirectory(restrictedRoot);
            return new TempOperationalEvidenceWorkspace(root, basePaths with
            {
                SourceRoot = sourceRoot,
                RestrictedTemplateRoot = restrictedRoot,
            });
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
