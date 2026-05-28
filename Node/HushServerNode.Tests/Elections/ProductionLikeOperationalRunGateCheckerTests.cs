using System.Text.Json.Nodes;
using FluentAssertions;
using ProductionLikeOperationalRunPromoter;
using Xunit;
using static HushServerNode.Tests.Elections.ProductionLikeOperationalRunTestHelpers;

namespace HushServerNode.Tests.Elections;

public sealed class ProductionLikeOperationalRunGateCheckerTests
{
    [Fact]
    public void ReleaseBaselineSource_EvaluatesAcceptedAndCanGenerateScoreProposal()
    {
        // Arrange
        var source = LoadBaseline();

        // Act
        var sourceErrors = ProductionLikeOperationalRunContracts.ValidateSource(source);
        var evaluation = ProductionLikeOperationalRunGateChecker.Evaluate(source);

        // Assert
        sourceErrors.Should().BeEmpty();
        evaluation.Status.Should().Be("accepted");
        evaluation.ScoreProposalCanBeGenerated.Should().BeTrue();
        evaluation.ScoreChangeAllowed.Should().BeTrue();
        evaluation.DirectRegisterMutation.Should().BeFalse();
        evaluation.Blockers.Should().BeEmpty();
        evaluation.ProductionRolloutInputStatus.Should().Be("accepted_with_limitations");
        evaluation.PromotionRegisterInputStatus.Should().Be("accepted_with_limitations");
    }

    [Theory]
    [MemberData(nameof(FixtureCases))]
    public void FixtureCatalogCases_EvaluateWithStableStatusAndDiagnostics(string caseId)
    {
        // Arrange
        var fixtureCase = LoadCases().Single(item => item["caseId"]!.GetValue<string>() == caseId);
        var source = LoadSourceForCase(fixtureCase);
        var expectedStatus = fixtureCase["expectedStatus"]!.GetValue<string>();
        var expectedDiagnostics = fixtureCase["expectedDiagnostics"]!
            .AsArray()
            .Select(item => item!.GetValue<string>())
            .ToArray();

        // Act
        var evaluation = ProductionLikeOperationalRunGateChecker.Evaluate(source);

        // Assert
        evaluation.Status.Should().Be(expectedStatus, caseId);
        if (expectedDiagnostics.Length > 0)
        {
            evaluation.Diagnostics.Should().Contain(expectedDiagnostics, caseId);
        }
        evaluation.ScoreProposalCanBeGenerated.Should().Be(expectedStatus is "accepted" or "accepted_with_limitations");
    }

    [Fact]
    public void MissingDeploymentProof_BlocksWithDeploymentDiagnostic()
    {
        // Arrange
        var source = LoadSourceForCategory("missing_deployment_proof");

        // Act
        var evaluation = ProductionLikeOperationalRunGateChecker.Evaluate(source);

        // Assert
        evaluation.Status.Should().Be("blocked");
        evaluation.Blockers.Should().Contain("FEAT154-DEPLOYMENT-PROOF-MISSING");
        evaluation.Diagnostics.Should().Contain("FEAT154-DEPLOYMENT-PROOF-MISSING");
        evaluation.ScoreProposalCanBeGenerated.Should().BeFalse();
    }

    [Fact]
    public void AcceptedWithLimitations_PreservesOneRunLimitationWithoutBlocking()
    {
        // Arrange
        var source = LoadSourceForCategory("accepted_with_limitations");

        // Act
        var evaluation = ProductionLikeOperationalRunGateChecker.Evaluate(source);

        // Assert
        evaluation.Status.Should().Be("accepted_with_limitations");
        evaluation.Limitations.Should().Contain(ProductionLikeOperationalRunGateChecker.OneRunOnlyLimitation);
        evaluation.Blockers.Should().BeEmpty();
        evaluation.ScoreProposalCanBeGenerated.Should().BeTrue();
    }

    [Fact]
    public void PlaceholderEvidence_BlocksScoreAsDevelopmentPlaceholder()
    {
        // Arrange
        var source = LoadSourceForCategory("placeholder_evidence");

        // Act
        var evaluation = ProductionLikeOperationalRunGateChecker.Evaluate(source);

        // Assert
        evaluation.Status.Should().Be("development_placeholder");
        evaluation.Diagnostics.Should().Contain("FEAT154-PLACEHOLDER-EVIDENCE-BLOCKS-SCORE");
        evaluation.ScoreProposalCanBeGenerated.Should().BeFalse();
    }

    public static IEnumerable<object[]> FixtureCases() =>
        LoadCases().Select(item => new object[] { item["caseId"]!.GetValue<string>() });

    private static JsonObject LoadSourceForCategory(string category)
    {
        var fixtureCase = LoadCases().Single(item => item["category"]!.GetValue<string>() == category);
        return LoadSourceForCase(fixtureCase);
    }
}
