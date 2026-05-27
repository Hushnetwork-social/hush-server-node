using System.Text.Json.Nodes;
using FluentAssertions;
using PublicStateElectionPrerequisiteRegisterPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class PublicStateElectionPrerequisiteRegisterPromoterTests
{
    private static readonly DateTimeOffset FixedGeneratedAt =
        DateTimeOffset.Parse("2026-05-27T12:00:00Z");

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        var paths = CreatePaths();

        var errors = PublicStateElectionPrerequisiteContracts.ValidateSchemaSet(paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in PublicStateElectionPrerequisiteContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(paths.SchemasRoot, schemaFile)).Should().BeTrue();
        }
    }

    [Fact]
    public void ReleaseBaseline_KeepsPublicStateBlockerRedAndScoreDisabled()
    {
        var source = PublicStateElectionPrerequisiteContracts.LoadSource(CreatePaths());

        var sourceErrors = PublicStateElectionPrerequisiteContracts.ValidateSource(source);
        var evaluation = PublicStateElectionPrerequisiteGateChecker.Evaluate(source);

        sourceErrors.Should().BeEmpty();
        evaluation.Status.Should().Be("blocked");
        evaluation.PublicStateDecision.BlockerId.Should().Be(PublicStateElectionPrerequisiteContracts.PublicStateBlockerId);
        evaluation.PublicStateDecision.Severity.Should().Be("red");
        evaluation.PublicStateDecision.Status.Should().Be("open");
        evaluation.ScoreChangeAllowed.Should().BeFalse();
        evaluation.DirectRegisterMutation.Should().BeFalse();
        evaluation.PublicStateClaimAllowed.Should().BeFalse();
        evaluation.Blockers.Should().Contain(PublicStateElectionPrerequisiteContracts.PublicStateBlockerId);
        evaluation.Blockers.Should().Contain("FEAT149-FEAT148-DEPENDENCY-NOT-SATISFIED");
    }

    [Fact]
    public void MissingOwnerCategory_IsRejected()
    {
        var source = PublicStateElectionPrerequisiteContracts.LoadSource(CreatePaths());
        FindGroup(source, "target_jurisdiction_and_election_type").Remove("ownerCategory");

        var evaluation = PublicStateElectionPrerequisiteGateChecker.Evaluate(source);

        evaluation.Blockers.Should().Contain(item => item.Contains("ownerCategory is required", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingEvidenceType_IsRejected()
    {
        var source = PublicStateElectionPrerequisiteContracts.LoadSource(CreatePaths());
        FindGroup(source, "competent_election_authority").Remove("evidenceType");

        var evaluation = PublicStateElectionPrerequisiteGateChecker.Evaluate(source);

        evaluation.Blockers.Should().Contain(item => item.Contains("evidenceType is required", StringComparison.Ordinal));
    }

    [Fact]
    public void ScoreMovement_IsRejected()
    {
        var source = PublicStateElectionPrerequisiteContracts.LoadSource(CreatePaths());
        source["scorePolicy"]!.AsObject()["scoreChangeAllowed"] = true;

        var evaluation = PublicStateElectionPrerequisiteGateChecker.Evaluate(source);

        evaluation.Blockers.Should().Contain("FEAT149-SCORE-MOVEMENT-FORBIDDEN");
        evaluation.Blockers.Should().Contain(item => item.Contains("FEAT149-SCORE-MOVEMENT-FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void DirectRegisterMutation_IsRejected()
    {
        var source = PublicStateElectionPrerequisiteContracts.LoadSource(CreatePaths());
        source["scorePolicy"]!.AsObject()["directRegisterMutation"] = true;

        var evaluation = PublicStateElectionPrerequisiteGateChecker.Evaluate(source);

        evaluation.Blockers.Should().Contain("FEAT149-DIRECT-REGISTER-MUTATION-FORBIDDEN");
    }

    [Fact]
    public void AttemptedPublicStateBlockerResolution_IsRejected()
    {
        var source = PublicStateElectionPrerequisiteContracts.LoadSource(CreatePaths());
        source["blockerPolicy"]!.AsObject()["currentStatus"] = "resolved";

        var evaluation = PublicStateElectionPrerequisiteGateChecker.Evaluate(source);

        evaluation.Blockers.Should().Contain(PublicStateElectionPrerequisiteContracts.PublicStateBlockerId);
        evaluation.Blockers.Should().Contain(item => item.Contains("blockerPolicy.currentStatus must be open", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingCertificationPath_KeepsPublicStateBlocked()
    {
        var source = LoadExample("future-candidate");
        var certification = FindGroup(source, "certification_testing_or_not_applicable");
        certification["status"] = "missing";
        certification["evidenceRefs"] = new JsonArray();
        certification["blockerIds"] = new JsonArray(
            JsonValue.Create(PublicStateElectionPrerequisiteContracts.PublicStateBlockerId),
            JsonValue.Create("FEAT149-CERTIFICATION-PATH-MISSING"));

        var evaluation = PublicStateElectionPrerequisiteGateChecker.Evaluate(source);

        evaluation.Status.Should().Be("blocked");
        evaluation.PublicStateDecision.Severity.Should().Be("red");
        evaluation.Blockers.Should().Contain("FEAT149-CERTIFICATION-PATH-MISSING");
    }

    [Theory]
    [InlineData(
        "accessibility_usability_language_assistance",
        "FEAT149-ACCESSIBILITY-LANGUAGE-ASSISTANCE-MISSING")]
    [InlineData(
        "dispute_recount_challenge_remedy_finality",
        "FEAT149-DISPUTE-REMEDY-FINALITY-MISSING")]
    public void MissingMandatoryExternalPath_KeepsPublicStateBlocked(string groupId, string blockerId)
    {
        var source = LoadExample("future-candidate");
        var prerequisite = FindGroup(source, groupId);
        prerequisite["status"] = "missing";
        prerequisite["evidenceRefs"] = new JsonArray();
        prerequisite["blockerIds"] = new JsonArray(
            JsonValue.Create(PublicStateElectionPrerequisiteContracts.PublicStateBlockerId),
            JsonValue.Create(blockerId));

        var evaluation = PublicStateElectionPrerequisiteGateChecker.Evaluate(source);

        evaluation.Status.Should().Be("blocked");
        evaluation.PublicStateDecision.Severity.Should().Be("red");
        evaluation.PublicStateDecision.Status.Should().Be("open");
        evaluation.Blockers.Should().Contain(blockerId);
        evaluation.Blockers.Should().Contain(PublicStateElectionPrerequisiteContracts.PublicStateBlockerId);
    }

    [Fact]
    public void PublicSafeWordingOverclaim_IsRejected()
    {
        var source = PublicStateElectionPrerequisiteContracts.LoadSource(CreatePaths());
        source["publicArtifactSamples"]!.AsArray()[0]!.AsObject()["content"] =
            "HushVoting is government authorized and public election ready.";

        var evaluation = PublicStateElectionPrerequisiteGateChecker.Evaluate(source);

        evaluation.Blockers.Should().Contain("FEAT149-PUBLIC-SAFE-WORDING-OVERCLAIM");
    }

    [Fact]
    public void FutureCandidateFixture_RequiresExternalEvidenceAndStillDoesNotMoveScore()
    {
        var source = LoadExample("future-candidate");

        var sourceErrors = PublicStateElectionPrerequisiteContracts.ValidateSource(source);
        var evaluation = PublicStateElectionPrerequisiteGateChecker.Evaluate(source);

        sourceErrors.Should().BeEmpty();
        source["fixtureOnly"]!.GetValue<bool>().Should().BeTrue();
        evaluation.Status.Should().Be("blocked");
        evaluation.ScoreChangeAllowed.Should().BeFalse();
        evaluation.DirectRegisterMutation.Should().BeFalse();
        evaluation.Blockers.Should().Contain(PublicStateElectionPrerequisiteContracts.PublicStateBlockerId);
        PublicStateElectionPrerequisiteContracts.RequireArray(source, "prerequisiteGroups")
            .OfType<JsonObject>()
            .Where(group => PublicStateElectionPrerequisiteContracts.GetBool(group, "mandatory"))
            .Should()
            .OnlyContain(group => PublicStateElectionPrerequisiteContracts.GetStringArray(group, "evidenceRefs").Count > 0);
    }

    [Fact]
    public void PackageGeneration_ReleaseBaseline_CreatesRequiredBlockedArtifacts()
    {
        var package = PublicStateElectionPrerequisiteArtifactGenerator.Generate(
            CreatePaths(),
            generatedAt: FixedGeneratedAt);

        package.Status.Should().Be("blocked");
        package.Artifacts
            .Select(artifact => artifact.RelativePath)
            .Should()
            .BeEquivalentTo(PublicStateElectionPrerequisiteArtifactGenerator.RequiredArtifactPaths);

        var ledger = ArtifactJson(package, PublicStateElectionPrerequisiteArtifactGenerator.DecisionLedgerPath);
        ledger["packageStatus"]!.GetValue<string>().Should().Be("blocked");
        ledger["blockers"]!.AsArray()
            .Select(item => item!.GetValue<string>())
            .Should()
            .Contain(PublicStateElectionPrerequisiteContracts.PublicStateBlockerId);

        var publicDecision = ledger["decisions"]!.AsArray()
            .OfType<JsonObject>()
            .Single(item => item["decisionType"]!.GetValue<string>() == "public_state_blocker");
        publicDecision["blockerDecision"]!.AsObject()["severity"]!.GetValue<string>().Should().Be("red");
        publicDecision["blockerDecision"]!.AsObject()["status"]!.GetValue<string>().Should().Be("open");

        var hashValidation = ArtifactJson(package, PublicStateElectionPrerequisiteArtifactGenerator.PackageHashValidationPath);
        hashValidation["generatedArtifactHashes"]!.AsArray().Count.Should().Be(package.Artifacts.Count - 1);
    }

    [Fact]
    public void PackageGeneration_SameSourceAndTimestamp_IsDeterministic()
    {
        var first = PublicStateElectionPrerequisiteArtifactGenerator.Generate(
            CreatePaths(),
            generatedAt: FixedGeneratedAt);
        var second = PublicStateElectionPrerequisiteArtifactGenerator.Generate(
            CreatePaths(),
            generatedAt: FixedGeneratedAt);

        first.Artifacts
            .Select(artifact => (artifact.RelativePath, artifact.Sha256Hash))
            .Should()
            .Equal(second.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash)));
    }

    [Fact]
    public void ReadinessFragment_DisablesScoreMovementAndRegisterMutation()
    {
        var package = PublicStateElectionPrerequisiteArtifactGenerator.Generate(
            CreatePaths(),
            generatedAt: FixedGeneratedAt);

        var fragment = ArtifactJson(package, PublicStateElectionPrerequisiteArtifactGenerator.ReadinessFragmentPath);

        fragment["scoreChangeAllowed"]!.GetValue<bool>().Should().BeFalse();
        fragment["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();
        fragment["currentTotalScore"]!.GetValue<int>().Should().Be(71);
        fragment["proposedTotalScore"]!.GetValue<int>().Should().Be(71);
    }

    [Fact]
    public void GeneratedPackage_DoesNotChangeProductionOrPublicStateClaims()
    {
        var package = PublicStateElectionPrerequisiteArtifactGenerator.Generate(
            CreatePaths(),
            generatedAt: FixedGeneratedAt);

        var fragment = ArtifactJson(package, PublicStateElectionPrerequisiteArtifactGenerator.ReadinessFragmentPath);
        var policy = ArtifactJson(package, PublicStateElectionPrerequisiteArtifactGenerator.BlockerPolicyPath);

        fragment["strongestAllowedClaimBefore"]!.GetValue<string>().Should().Be("friendly_organization_pilot");
        fragment["strongestAllowedClaimAfter"]!.GetValue<string>().Should().Be("friendly_organization_pilot");
        fragment["productionOrganizationalRolloutClaimChanged"]!.GetValue<bool>().Should().BeFalse();
        fragment["publicStateClaimChanged"]!.GetValue<bool>().Should().BeFalse();
        policy["currentSeverity"]!.GetValue<string>().Should().Be("red");
        policy["currentStatus"]!.GetValue<string>().Should().Be("open");
    }

    [Fact]
    public void NegativeFixtureCatalog_CoversRequiredFailureModes()
    {
        var paths = CreatePaths();
        var negative = PublicStateElectionPrerequisiteContracts.ReadJsonObject(
            Path.Combine(paths.ExamplesRoot, "negative", PublicStateElectionPrerequisitePromotionPaths.NegativeFixturesFileName),
            "negative fixtures");

        var categories = negative["cases"]!.AsArray()
            .OfType<JsonObject>()
            .Select(item => item["category"]!.GetValue<string>())
            .ToArray();

        categories.Should().Contain(new[]
        {
            "missing_owner_category",
            "missing_evidence_type",
            "forbidden_score_movement",
            "attempted_blocker_resolution",
            "missing_jurisdiction",
            "missing_authority",
            "missing_certification",
            "feat148_bypass",
            "forbidden_public_wording",
        });
    }

    private static PublicStateElectionPrerequisitePromotionPaths CreatePaths() =>
        HushVotingReadinessTestArtifacts.CreatePublicStateElectionPrerequisitePaths();

    private static JsonObject LoadExample(string exampleFolder)
    {
        var paths = CreatePaths();
        var path = Path.Combine(paths.ExamplesRoot, exampleFolder, PublicStateElectionPrerequisitePromotionPaths.SourceFileName);
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject() ??
            throw new InvalidOperationException($"Example fixture {exampleFolder} is not a JSON object.");
    }

    private static JsonObject ArtifactJson(PublicStateGeneratedPackage package, string relativePath)
    {
        var artifact = package.Artifacts.Single(item => item.RelativePath == relativePath);
        return JsonNode.Parse(artifact.Content)?.AsObject() ??
            throw new InvalidOperationException($"Artifact {relativePath} is not a JSON object.");
    }

    private static JsonObject FindGroup(JsonObject source, string groupId) =>
        PublicStateElectionPrerequisiteContracts.RequireArray(source, "prerequisiteGroups")
            .OfType<JsonObject>()
            .Single(group => PublicStateElectionPrerequisiteContracts.GetString(group, "groupId") == groupId);
}
