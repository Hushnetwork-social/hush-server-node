using System.Text.Json.Nodes;
using FluentAssertions;
using ProductionRolloutReadinessPromoter;
using static HushServerNode.Tests.Elections.ProductionRolloutReadinessTestHelpers;
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

    [Fact]
    public void PackageGeneration_WithBlockedSource_GeneratesAllArtifactsAndBlockedResults()
    {
        // Arrange
        var paths = CreatePaths();
        var generatedAt = DateTimeOffset.Parse("2026-05-27T12:00:00Z");

        // Act
        var package = ProductionRolloutReadinessArtifactGenerator.Generate(paths, generatedAt: generatedAt);
        var checkResults = ReadArtifactJson(package, ProductionRolloutReadinessArtifactGenerator.CheckResultsPath);
        var readinessFragment = ReadArtifactJson(package, ProductionRolloutReadinessArtifactGenerator.ReadinessFragmentPath);
        var hashValidation = ReadArtifactJson(package, ProductionRolloutReadinessArtifactGenerator.PackageHashValidationPath);

        // Assert
        package.Status.Should().Be("blocked");
        package.Artifacts.Select(artifact => artifact.RelativePath)
            .Should().BeEquivalentTo(ProductionRolloutReadinessArtifactGenerator.RequiredArtifactPaths);
        checkResults["status"]!.GetValue<string>().Should().Be("blocked");
        readinessFragment["scoreEffect"]!.AsObject()["scoreChangeAllowed"]!.GetValue<bool>().Should().BeFalse();
        hashValidation["generatedArtifactHashes"]!.AsArray()
            .OfType<JsonObject>()
            .Select(item => item["path"]!.GetValue<string>())
            .Should()
            .BeEquivalentTo(ProductionRolloutReadinessArtifactGenerator.RequiredArtifactPaths
                .Except([ProductionRolloutReadinessArtifactGenerator.PackageHashValidationPath]));
    }

    [Fact]
    public void PackageGeneration_WithFixedTimestamp_IsDeterministic()
    {
        // Arrange
        var paths = CreatePaths();
        var generatedAt = DateTimeOffset.Parse("2026-05-27T12:00:00Z");

        // Act
        var first = ProductionRolloutReadinessArtifactGenerator.Generate(paths, generatedAt: generatedAt);
        var second = ProductionRolloutReadinessArtifactGenerator.Generate(paths, generatedAt: generatedAt);

        // Assert
        first.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content))
            .Should().Equal(second.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content)));
    }

    [Fact]
    public void PackageGeneration_WithAmberReadyEvidence_ProposesAllowedWithLimitationsCandidate()
    {
        // Arrange
        var paths = CreatePaths();
        var generatedAt = DateTimeOffset.Parse("2026-05-27T12:00:00Z");

        // Act
        var package = ProductionRolloutReadinessArtifactGenerator.Generate(
            paths,
            Path.Combine(paths.ExamplesRoot, "amber-ready"),
            generatedAt);
        var readinessFragment = ReadArtifactJson(package, ProductionRolloutReadinessArtifactGenerator.ReadinessFragmentPath);

        // Assert
        package.Status.Should().Be("allowed_with_limitations_candidate");
        readinessFragment["scoreEffect"]!.AsObject()["scoreChangeAllowed"]!.GetValue<bool>().Should().BeTrue();
        readinessFragment["claimEffect"]!.AsObject()["publicOrStateElection"]!.GetValue<string>().Should().Be("blocked");
    }

    [Fact]
    public void PackageGeneration_WithLocalHashMismatch_BlocksPackageAndRecordsAuditFailure()
    {
        // Arrange
        var paths = CreatePaths();
        var source = LoadExample("amber-ready");
        var evidencePath = Path.Combine(
            paths.WorkspaceRoot,
            "hush-documents",
            "PrivateServer_ElectronicVoting",
            "Production-Organizational-Rollout-Readiness",
            "mismatched-local-evidence.txt");
        File.WriteAllText(evidencePath, "actual evidence");
        var relativeEvidencePath = Path.GetRelativePath(paths.WorkspaceRoot, evidencePath);
        var evidence = source["runEvidence"]!.AsObject()["evidenceRefs"]!.AsArray()[0]!.AsObject();
        evidence["publicRef"] = relativeEvidencePath;
        evidence["restrictedRef"] = "";
        evidence["sha256Hash"] = new string('0', 64);
        var sourceInput = WriteSourceExample(paths, source, "local-hash-mismatch");

        // Act
        var package = ProductionRolloutReadinessArtifactGenerator.Generate(
            paths,
            sourceInput,
            DateTimeOffset.Parse("2026-05-27T12:00:00Z"));
        var audit = ReadArtifactJson(package, ProductionRolloutReadinessArtifactGenerator.ArtifactHashAuditPath);

        // Assert
        package.Status.Should().Be("blocked");
        package.AuditFailures.Should().Contain(item => item.Contains("FEAT148-PRODUCTION-LIKE-RUN-ACCEPTED-001", StringComparison.Ordinal));
        audit["status"]!.GetValue<string>().Should().Be("blocked");
        audit["artifacts"]!.AsArray()
            .OfType<JsonObject>()
            .Should().Contain(item =>
                item["evidenceId"]!.GetValue<string>() == "FEAT148-PRODUCTION-LIKE-RUN-ACCEPTED-001" &&
                item["auditResult"]!.GetValue<string>() == "failed");
    }

}
