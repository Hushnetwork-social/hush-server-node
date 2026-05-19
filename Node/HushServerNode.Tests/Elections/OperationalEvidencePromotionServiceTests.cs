using System.Text.Json.Nodes;
using FluentAssertions;
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

    private static OperationalEvidencePromotionPaths CreatePaths()
    {
        var workspaceRoot = WorkspaceRootFinder.Find(AppContext.BaseDirectory);
        return OperationalEvidencePromotionPaths.FromWorkspaceRoot(workspaceRoot);
    }

    private static JsonObject LoadExample(OperationalEvidencePromotionPaths paths, string relativePath) =>
        OperationalEvidenceContracts.ReadJsonObject(
            Path.Combine(paths.ExamplesRoot, relativePath),
            relativePath);

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
