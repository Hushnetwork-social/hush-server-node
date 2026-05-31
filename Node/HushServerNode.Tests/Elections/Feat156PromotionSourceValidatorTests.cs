using System.Text.Json.Nodes;
using FluentAssertions;
using ReadinessRegisterPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class Feat156PromotionSourceValidatorTests
{
    [Fact]
    public void Validate_WithAcceptedSource_PassesAndRecalculates80()
    {
        var source = CreateAcceptedSource();
        var workspaceRoot = CreateWorkspaceWithCompletedFeatures();

        var result = new Feat156PromotionSourceValidator().Validate(source, workspaceRoot);

        result.IsValid.Should().BeTrue(string.Join(Environment.NewLine, result.Errors));
        result.RecalculatedScore.Should().Be(80);
        result.ProductionDecision.Should().Be("amber/allowed_with_limitations");
        result.PublicStateDecision.Should().Be("red/blocked");
    }

    [Fact]
    public void Validate_WithFeat155OutsideCompletedFolder_FailsClosed()
    {
        var source = CreateAcceptedSource();
        var workspaceRoot = CreateWorkspaceWithCompletedFeatures("FEAT-151", "FEAT-152", "FEAT-153", "FEAT-154");

        var result = new Feat156PromotionSourceValidator().Validate(source, workspaceRoot);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("FEAT-155 must be formally completed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("blocked")]
    [InlineData("rejected")]
    [InlineData("superseded")]
    public void Validate_WithNonAcceptedMovementStatus_FailsClosed(string status)
    {
        var source = CreateAcceptedSource();
        FindMovement(source, "FEAT-154")["status"] = status;

        var result = new Feat156PromotionSourceValidator().Validate(source);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains($"FEAT-154 has non-accepted lifecycle status {status}", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithStaleMovement_FailsClosed()
    {
        var source = CreateAcceptedSource();
        FindMovement(source, "FEAT-151")["freshness"] = "stale";

        var result = new Feat156PromotionSourceValidator().Validate(source);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("FEAT-151 evidence freshness must be current", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithPlaceholderArtifactHash_FailsClosed()
    {
        var source = CreateAcceptedSource();
        FindMovement(source, "FEAT-152")["artifactRefs"]!.AsArray()[0]!.AsObject()["sha256Hash"] = new string('0', 64);

        var result = new Feat156PromotionSourceValidator().Validate(source);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("uses a placeholder hash", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithTamperedKnownArtifactHash_FailsClosed()
    {
        var source = CreateAcceptedSource();
        FindMovement(source, "FEAT-153")["artifactRefs"]!.AsArray()[0]!.AsObject()["sha256Hash"] =
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        var result = new Feat156PromotionSourceValidator().Validate(source);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("FEAT153-PUBLICATION-COUNTING-MANIFEST artifact hash mismatch.");
    }

    [Fact]
    public void Validate_WithDuplicateDimensionMovement_FailsClosed()
    {
        var source = CreateAcceptedSource();
        FindMovement(source, "FEAT-152")["dimensionId"] = "RDY-DIM-002";

        var result = new Feat156PromotionSourceValidator().Validate(source);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("duplicate score movement for dimension RDY-DIM-002", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithScoreBelow80_FailsClosed()
    {
        var source = CreateAcceptedSource();
        var movement = FindMovement(source, "FEAT-156");
        movement["acceptedScore"] = 7;
        movement["delta"] = 0;

        var result = new Feat156PromotionSourceValidator().Validate(source);

        result.IsValid.Should().BeFalse();
        result.RecalculatedScore.Should().Be(79);
        result.Errors.Should().Contain(error => error.Contains("recalculated score must be exactly 80", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithProductionGreen_FailsClosed()
    {
        var source = CreateAcceptedSource();
        var productionClaim = source["targetRegister"]!.AsObject()["productionClaim"]!.AsObject();
        productionClaim["severity"] = "green";
        productionClaim["status"] = "allowed";

        var result = new Feat156PromotionSourceValidator().Validate(source);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("production rollout must be amber", StringComparison.Ordinal));
        result.Errors.Should().Contain(error => error.Contains("production green", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WithPublicStateUnblocked_FailsClosed()
    {
        var source = CreateAcceptedSource();
        var publicStateClaim = source["targetRegister"]!.AsObject()["publicStateClaim"]!.AsObject();
        publicStateClaim["severity"] = "amber";
        publicStateClaim["status"] = "allowed_with_limitations";

        var result = new Feat156PromotionSourceValidator().Validate(source);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("public or state election claim must remain red and blocked", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithMissingTopLevelSignoff_FailsClosed()
    {
        var source = CreateAcceptedSource();
        source["signoff"]!.AsObject()["status"] = "draft";

        var result = new Feat156PromotionSourceValidator().Validate(source);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("status must be accepted.");
    }

    [Fact]
    public void Validate_WithPublicForbiddenMaterial_FailsClosed()
    {
        var source = CreateAcceptedSource();
        source["publicSafeOutputRules"]!.AsObject()["generatedOutputSample"] =
            @"Reviewer output includes C:\myWork\HushNetworkOrg\hush-documents\PrivateServer_ElectronicVoting\restricted-evidence\raw-log.json";

        var result = new Feat156PromotionSourceValidator().Validate(source);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("public-safe output contains forbidden material", StringComparison.Ordinal));
    }

    private static JsonObject CreateAcceptedSource() =>
        new()
        {
            ["schemaVersion"] = "production-rollout-promotion-source.v1",
            ["featureId"] = "FEAT-156",
            ["status"] = "accepted",
            ["baselineRegister"] = new JsonObject
            {
                ["registerVersionId"] = "RDY-REG-v0.1.5",
                ["status"] = "AcceptedInternal",
                ["totalScore"] = 71,
                ["strongestAllowedClaim"] = "friendly_organization_pilot",
            },
            ["targetRegister"] = new JsonObject
            {
                ["registerVersionId"] = "RDY-REG-v0.1.6",
                ["registerVersion"] = "v0.1.6",
                ["status"] = "AcceptedInternal",
                ["totalScore"] = 80,
                ["strongestAllowedClaim"] = "production_organizational_rollout",
                ["publicationStatus"] = "production_rollout_with_limitations",
                ["productionClaim"] = new JsonObject
                {
                    ["claimLevel"] = "production_organizational_rollout",
                    ["severity"] = "amber",
                    ["status"] = "allowed_with_limitations",
                    ["wording"] = "HushVoting may support limited organizational rollout only when residual limits and customer-owned governance responsibilities remain visible.",
                },
                ["publicStateClaim"] = new JsonObject
                {
                    ["claimLevel"] = "public_or_state_election",
                    ["severity"] = "red",
                    ["status"] = "blocked",
                    ["wording"] = "Public or state election readiness remains blocked.",
                },
            },
            ["scoreModel"] = new JsonObject
            {
                ["baselineTotal"] = 71,
                ["acceptedInputDelta"] = 8,
                ["feat156Delta"] = 1,
                ["targetTotal"] = 80,
                ["minimumProductionLimitedScore"] = 80,
                ["scoreCannotBypassBlockers"] = true,
            },
            ["scoreMovements"] = new JsonArray(
                Movement("FEAT-151", "RDY-DIM-002", 6, 8, 2, "FEAT151-CORPUS-MANIFEST", "bd6d7d179368fbb7a13811d2fea497ad68306efd949a8178778ca2890554a48c"),
                Movement("FEAT-152", "RDY-DIM-003", 7, 8, 1, "FEAT152-RECEIPT-CHANNEL-MANIFEST", "d9b09012846bab1d07b7082c88fdd70c206160b0b31dd38a9655e440d5ec2c64"),
                Movement("FEAT-153", "RDY-DIM-004", 7, 8, 1, "FEAT153-PUBLICATION-COUNTING-MANIFEST", "9ae9c5a78d14c4417b8283e6ba996f08e567d5776c540c27bfdfdcebb8742ca3"),
                Movement("FEAT-154", "RDY-DIM-007", 6, 8, 2, "FEAT154-PRODUCTION-LIKE-RUN-MANIFEST", "62b2c9afb605bb6e0d26876629b7df122b7da566df37f536b4790a9398ecb410"),
                Movement("FEAT-155", "RDY-DIM-009", 6, 8, 2, "FEAT155-FAILED-FINALIZE-MANIFEST", "9ca42435559bbcc5b91ce99428a100e14d1637f60e0947eff21d869f8b36037b"),
                Movement("FEAT-156", "RDY-DIM-010", 7, 8, 1, "FEAT156-PLANNING-ANALYSIS", "867cb50db400715fb444fd6e2d7e15763e6d84bc054b36c00bda3ddbaadf51ec", "accepted_with_limitations")),
            ["policyBaselines"] = new JsonArray(
                new JsonObject { ["featureId"] = "FEAT-148" },
                new JsonObject { ["featureId"] = "FEAT-149" }),
            ["evidenceLifecyclePolicy"] = new JsonObject
            {
                ["requiredCompletedFeatures"] = new JsonArray("FEAT-151", "FEAT-152", "FEAT-153", "FEAT-154", "FEAT-155"),
                ["freshnessRequired"] = "current",
                ["tamperCheckRequired"] = true,
                ["placeholderInputsBlock"] = true,
            },
            ["blockerDecisions"] = new JsonArray(
                new JsonObject
                {
                    ["blockerId"] = "RDY-BLOCK-PRODUCTION_ORGANIZATIONAL_ROLLOUT-001",
                    ["targetSeverity"] = "amber",
                    ["targetStatus"] = "allowed_with_limitations",
                    ["decision"] = "allow_with_limitations",
                },
                new JsonObject
                {
                    ["blockerId"] = "RDY-BLOCK-PUBLIC_OR_STATE_ELECTION-001",
                    ["targetSeverity"] = "red",
                    ["targetStatus"] = "open",
                    ["decision"] = "keep_policy_blocked",
                }),
            ["claimPolicy"] = new JsonObject
            {
                ["productionGreenForbidden"] = true,
                ["publicStateUnlockForbidden"] = true,
                ["legalSufficiencyForbidden"] = true,
                ["independentCertificationForbidden"] = true,
                ["fullAgmProductClaimForbidden"] = true,
            },
            ["publicSafeOutputRules"] = new JsonObject
            {
                ["forbiddenMaterialNeedles"] = new JsonArray(
                    @"C:\myWork\HushNetworkOrg",
                    "restricted-evidence",
                    "raw log"),
                ["forbiddenClaimNeedles"] = new JsonArray(
                    "legal sufficiency",
                    "independent certification",
                    "full AGM management software"),
                ["numericScorePublicDisclosure"] = false,
            },
            ["signoff"] = new JsonObject
            {
                ["engineeringRole"] = "engineering-owner",
                ["operationsProductRole"] = "operations-product-owner",
                ["status"] = "accepted",
            },
        };

    private static JsonObject Movement(
        string featureId,
        string dimensionId,
        int previousScore,
        int acceptedScore,
        int delta,
        string artifactId,
        string artifactHash,
        string status = "accepted") =>
        new()
        {
            ["featureId"] = featureId,
            ["dimensionId"] = dimensionId,
            ["previousScore"] = previousScore,
            ["acceptedScore"] = acceptedScore,
            ["delta"] = delta,
            ["status"] = status,
            ["freshness"] = "current",
            ["directRegisterMutation"] = false,
            ["registerPromotionOwner"] = "FEAT-156",
            ["artifactRefs"] = new JsonArray(
                new JsonObject
                {
                    ["artifactId"] = artifactId,
                    ["sha256Hash"] = artifactHash,
                }),
            ["signoff"] = new JsonObject
            {
                ["sourceFeatureCompleted"] = featureId != "FEAT-156",
                ["acceptedForPromotion"] = true,
            },
        };

    private static JsonObject FindMovement(JsonObject source, string featureId) =>
        source["scoreMovements"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(movement => movement["featureId"]!.GetValue<string>() == featureId);

    private static string CreateWorkspaceWithCompletedFeatures(params string[] completedFeatures)
    {
        var features = completedFeatures.Length == 0
            ? ["FEAT-151", "FEAT-152", "FEAT-153", "FEAT-154", "FEAT-155"]
            : completedFeatures;
        var root = HushVotingReadinessTestArtifacts.CreateEmptyWorkspace("hush-feat-156-");
        var completedRoot = Path.Combine(root, "hush-memory-bank", "Features", "04_COMPLETED");
        Directory.CreateDirectory(completedRoot);
        foreach (var featureId in features)
        {
            Directory.CreateDirectory(Path.Combine(completedRoot, $"{featureId}-accepted-test-artifact"));
        }

        return root;
    }
}
