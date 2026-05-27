using System.Text.Json.Nodes;
using FluentAssertions;
using ProductionRolloutReadinessPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ProductionRolloutReadinessPromoterTests
{
    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        // Arrange
        var paths = CreatePaths();

        // Act
        var errors = ProductionRolloutReadinessContracts.ValidateSchemaSet(paths.SchemasRoot);

        // Assert
        errors.Should().BeEmpty();
        foreach (var schemaFile in ProductionRolloutReadinessContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(paths.SchemasRoot, schemaFile)).Should().BeTrue();
        }
    }

    [Fact]
    public void ReleaseBaseline_MissingRunEvidence_KeepsProductionBlockerRed()
    {
        // Arrange
        var source = ProductionRolloutReadinessContracts.LoadSource(CreatePaths());

        // Act
        var sourceErrors = ProductionRolloutReadinessContracts.ValidateSource(source);
        var evaluation = ProductionRolloutReadinessGateChecker.Evaluate(source);

        // Assert
        sourceErrors.Should().BeEmpty();
        evaluation.Status.Should().Be("blocked");
        evaluation.ProductionDecision.Severity.Should().Be("red");
        evaluation.ProductionDecision.Status.Should().Be("open");
        evaluation.Blockers.Should().Contain("FEAT148-PRODUCTION-LIKE-RUN-MISSING");
    }

    [Fact]
    public void AmberReadyEvidence_CanProposeAllowedWithLimitations()
    {
        // Arrange
        var source = LoadExample("amber-ready");

        // Act
        var sourceErrors = ProductionRolloutReadinessContracts.ValidateSource(source);
        var evaluation = ProductionRolloutReadinessGateChecker.Evaluate(source);

        // Assert
        sourceErrors.Should().BeEmpty();
        evaluation.Status.Should().Be("allowed_with_limitations_candidate");
        evaluation.ProductionDecision.Severity.Should().Be("amber");
        evaluation.ProductionDecision.Status.Should().Be("allowed_with_limitations");
        evaluation.GreenAllowed.Should().BeFalse();
        evaluation.Limitations.Should().Contain("FEAT148-REPEATED-OPERATING-HISTORY-REQUIRED-FOR-GREEN");
        evaluation.PublicStateDecision.Severity.Should().Be("red");
        evaluation.PublicStateDecision.Status.Should().Be("open");
    }

    [Fact]
    public void AmberReadyEvidence_WithScoreBelow80_KeepsProductionBlockerRed()
    {
        // Arrange
        var source = LoadExample("amber-ready");
        source["scorePolicy"]!.AsObject()["candidateTotalScoreWhenAccepted"] = 79;

        // Act
        var evaluation = ProductionRolloutReadinessGateChecker.Evaluate(source);

        // Assert
        evaluation.Status.Should().Be("blocked");
        evaluation.ProductionDecision.Severity.Should().Be("red");
        evaluation.Blockers.Should().Contain("FEAT148-SCORE-BELOW-80");
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("placeholder")]
    [InlineData("private_only")]
    [InlineData("mismatched")]
    [InlineData("missing")]
    public void ScoreBlockingEvidenceStatuses_RejectProductionMovement(string status)
    {
        // Arrange
        var source = LoadExample("amber-ready");
        source["runEvidence"]!.AsObject()["status"] = status;

        // Act
        var evaluation = ProductionRolloutReadinessGateChecker.Evaluate(source);

        // Assert
        evaluation.Status.Should().Be("blocked");
        evaluation.ProductionDecision.Severity.Should().Be("red");
        evaluation.Blockers.Should().Contain(blocker => blocker.Contains(status.ToUpperInvariant(), StringComparison.Ordinal));
    }

    [Fact]
    public void PublicStateOverclaim_RemainsBlockedAndRecordsDiagnostic()
    {
        // Arrange
        var source = LoadExample("amber-ready");
        source["claimPolicy"]!.AsObject()["publicStateClaimState"] = "allowed_with_limitations_candidate";

        // Act
        var evaluation = ProductionRolloutReadinessGateChecker.Evaluate(source);

        // Assert
        evaluation.Status.Should().Be("blocked");
        evaluation.PublicStateDecision.BlockerId.Should().Be(ProductionRolloutReadinessContracts.PublicStateBlockerId);
        evaluation.PublicStateDecision.Severity.Should().Be("red");
        evaluation.Blockers.Should().Contain(ProductionRolloutReadinessContracts.PublicStateBlockerId);
    }

    [Fact]
    public void FailedFinalizeResidual_RemainsVisibleAsProductionLimitation()
    {
        // Arrange
        var source = LoadExample("amber-ready");
        source["governedOutcomeEvidence"]!.AsObject()["blockerIds"]!.AsArray()
            .Add(JsonValue.Create("FEAT148-FEAT139-FAILED-FINALIZE-PRODUCTION-LIMITATION"));

        // Act
        var evaluation = ProductionRolloutReadinessGateChecker.Evaluate(source);

        // Assert
        evaluation.Status.Should().Be("allowed_with_limitations_candidate");
        evaluation.Limitations.Should().Contain("FEAT148-FEAT139-FAILED-FINALIZE-PRODUCTION-LIMITATION");
    }

    [Fact]
    public void NegativeFixtureCatalog_CoversRequiredFailureModes()
    {
        // Arrange
        var paths = CreatePaths();
        var negative = ProductionRolloutReadinessContracts.ReadJsonObject(
            Path.Combine(paths.ExamplesRoot, "negative", "production-rollout-negative-fixtures.json"),
            "negative fixtures");

        // Act
        var categories = negative["cases"]!.AsArray()
            .OfType<JsonObject>()
            .Select(item => item["category"]!.GetValue<string>())
            .ToArray();

        // Assert
        categories.Should().Contain(new[]
        {
            "stale",
            "placeholder",
            "private_only",
            "mismatched",
            "missing",
            "public_state_overclaim",
        });
    }

    private static ProductionRolloutReadinessPromotionPaths CreatePaths() =>
        HushVotingReadinessTestArtifacts.CreateProductionRolloutReadinessPaths();

    private static JsonObject LoadExample(string exampleFolder)
    {
        var paths = CreatePaths();
        var path = Path.Combine(paths.ExamplesRoot, exampleFolder, ProductionRolloutReadinessPromotionPaths.SourceFileName);
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject() ??
            throw new InvalidOperationException($"Example fixture {exampleFolder} is not a JSON object.");
    }
}
