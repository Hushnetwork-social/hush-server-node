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

    private static OperationalEvidencePromotionPaths CreatePaths()
    {
        var workspaceRoot = WorkspaceRootFinder.Find(AppContext.BaseDirectory);
        return OperationalEvidencePromotionPaths.FromWorkspaceRoot(workspaceRoot);
    }

    private static JsonObject LoadExample(OperationalEvidencePromotionPaths paths, string relativePath) =>
        OperationalEvidenceContracts.ReadJsonObject(
            Path.Combine(paths.ExamplesRoot, relativePath),
            relativePath);
}
