using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class FailedFinalizeContinuitySchemaTests
{
    private static readonly string[] RequiredSchemaFiles =
    [
        "failed-finalize-continuity-source.schema.json",
        "failed-finalize-readiness-fragment.schema.json",
        "failed-finalize-score-proposal.schema.json",
        "failed-finalize-downstream-handoff.schema.json",
    ];

    private static readonly string[] RequiredSourceProperties =
    [
        "schemaVersion",
        "sourceId",
        "featureId",
        "status",
        "generatedAt",
        "baselineRegister",
        "productionLikeRunContext",
        "governedOutcome",
        "noCleanResult",
        "publicSafeStatus",
        "restrictedEvidenceRefs",
        "packageValidation",
        "readinessProposal",
        "downstreamHandoff",
        "publicArtifactSamples",
        "residualRisks",
        "signoff",
    ];

    private static readonly string[] PublicForbiddenNeedles =
    [
        "private key",
        "vote choice",
        "voter address",
        "trustee secret",
        "local path",
        "support transcript",
    ];

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        foreach (var schemaFile in RequiredSchemaFiles)
        {
            var path = Path.Combine(SchemasRoot, schemaFile);
            File.Exists(path).Should().BeTrue($"{schemaFile} must be available to CI fixtures");

            var schema = ReadJsonObject(path);
            schema.ContainsKey("$schema").Should().BeTrue($"{schemaFile} must declare a JSON schema version");
            schema.ContainsKey("required").Should().BeTrue($"{schemaFile} must declare required fields");
        }
    }

    [Fact]
    public void SourceFixture_ReleaseBaseline_ModelsAcceptedFailedFinalizeContinuity()
    {
        var source = LoadSource();

        ValidateSource(source).Should().BeEmpty();

        var outcome = source["governedOutcome"]!.AsObject();
        outcome["decisionType"]!.GetValue<string>().Should().Be("record_failed_finalize_continuity");
        outcome["outcomeStatus"]!.GetValue<string>().Should().Be("failed_to_finalize");
        outcome["cleanFinalization"]!.GetValue<bool>().Should().BeFalse();
        outcome["previousLifecycleState"]!.GetValue<string>().Should().Be("Closed");
        outcome["resultingLifecycleState"]!.GetValue<string>().Should().Be("Closed");
        outcome["officialResultRef"].Should().BeNull();
        outcome["finalizeBoundaryRef"].Should().BeNull();

        var noCleanResult = source["noCleanResult"]!.AsObject();
        noCleanResult["officialResultArtifactPresent"]!.GetValue<bool>().Should().BeFalse();
        noCleanResult["cleanFinalPackagePresent"]!.GetValue<bool>().Should().BeFalse();

        var handoff = source["downstreamHandoff"]!.AsObject();
        handoff["consumers"]!.AsArray().Select(item => item!.GetValue<string>())
            .Should()
            .Contain(["FEAT-148", "FEAT-156"]);
        handoff["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void SourceFixture_WhenPromotionOwnerMissing_BlocksValidation()
    {
        var source = Clone(LoadSource());
        source["downstreamHandoff"]!.AsObject().Remove("registerPromotionOwner");

        ValidateSource(source).Should().Contain("downstreamHandoff.registerPromotionOwner must be FEAT-156.");
    }

    [Fact]
    public void SourceFixture_WhenDirectRegisterMutationIsEnabled_BlocksValidation()
    {
        var source = Clone(LoadSource());
        source["readinessProposal"]!.AsObject()["directRegisterMutation"] = true;
        source["downstreamHandoff"]!.AsObject()["directRegisterMutation"] = true;

        ValidateSource(source).Should().Contain([
            "readinessProposal.directRegisterMutation must be false.",
            "downstreamHandoff.directRegisterMutation must be false.",
        ]);
    }

    [Fact]
    public void SourceFixture_WhenPublicOutputContainsRestrictedMaterial_BlocksValidation()
    {
        var source = Clone(LoadSource());
        source["publicArtifactSamples"]!.AsArray()[0]!.AsObject()["content"] =
            "Public status accidentally includes voter address and vote choice.";

        ValidateSource(source).Should().Contain("publicArtifactSamples contains restricted public material: vote choice.");
    }

    private static IReadOnlyList<string> ValidateSource(JsonObject source)
    {
        var errors = new List<string>();
        foreach (var property in RequiredSourceProperties)
        {
            if (!source.ContainsKey(property) || source[property] is null)
            {
                errors.Add($"source is missing required property {property}.");
            }
        }

        RequireString(source, "schemaVersion", "failed-finalize-continuity-source.v1", errors);
        RequireString(source, "featureId", "FEAT-155", errors);

        var readinessProposal = source["readinessProposal"] as JsonObject;
        RequireBool(readinessProposal, "directRegisterMutation", false, "readinessProposal", errors);
        RequireString(readinessProposal, "promotionOwner", "FEAT-156", "readinessProposal", errors);
        RequireString(readinessProposal, "dimensionId", "RDY-DIM-009", "readinessProposal", errors);

        var handoff = source["downstreamHandoff"] as JsonObject;
        RequireBool(handoff, "directRegisterMutation", false, "downstreamHandoff", errors);
        RequireString(handoff, "registerPromotionOwner", "FEAT-156", "downstreamHandoff", errors);

        var outcome = source["governedOutcome"] as JsonObject;
        RequireBool(outcome, "cleanFinalization", false, "governedOutcome", errors);
        RequireString(outcome, "outcomeStatus", "failed_to_finalize", "governedOutcome", errors);
        RequireString(outcome, "resultingLifecycleState", "Closed", "governedOutcome", errors);
        if (outcome?["officialResultRef"] is not null)
        {
            errors.Add("governedOutcome.officialResultRef must be null for failed-finalize.");
        }

        if (outcome?["finalizeBoundaryRef"] is not null)
        {
            errors.Add("governedOutcome.finalizeBoundaryRef must be null for failed-finalize.");
        }

        ValidatePublicArtifactSamples(source, errors);
        return errors;
    }

    private static void ValidatePublicArtifactSamples(JsonObject source, List<string> errors)
    {
        if (source["publicArtifactSamples"] is not JsonArray samples)
        {
            errors.Add("publicArtifactSamples must be an array.");
            return;
        }

        foreach (var sample in samples.OfType<JsonObject>())
        {
            var content = sample["content"]?.GetValue<string>() ?? "";
            foreach (var needle in PublicForbiddenNeedles)
            {
                if (content.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"publicArtifactSamples contains restricted public material: {needle}.");
                }
            }
        }
    }

    private static void RequireString(
        JsonObject? value,
        string property,
        string expected,
        string scope,
        List<string> errors)
    {
        if (value?[property]?.GetValue<string>() != expected)
        {
            errors.Add($"{scope}.{property} must be {expected}.");
        }
    }

    private static void RequireString(
        JsonObject value,
        string property,
        string expected,
        List<string> errors)
    {
        if (value[property]?.GetValue<string>() != expected)
        {
            errors.Add($"{property} must be {expected}.");
        }
    }

    private static void RequireBool(
        JsonObject? value,
        string property,
        bool expected,
        string scope,
        List<string> errors)
    {
        if (value?[property]?.GetValue<bool>() != expected)
        {
            errors.Add($"{scope}.{property} must be {expected.ToString().ToLowerInvariant()}.");
        }
    }

    private static JsonObject LoadSource() =>
        ReadJsonObject(Path.Combine(SourceRoot, "failed-finalize-continuity-source.json"));

    private static JsonObject Clone(JsonObject source) =>
        JsonNode.Parse(source.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!.AsObject();

    private static JsonObject ReadJsonObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path)) as JsonObject ??
        throw new InvalidOperationException($"{path} is not a JSON object.");

    private static string FixtureRoot =>
        Path.Combine(
            HushVotingReadinessTestArtifacts.ServerNodeRoot,
            "Node",
            "HushServerNode.Tests",
            "Fixtures",
            "HushVotingReadiness",
            "Failed-Finalize-Continuity-Rehearsal");

    private static string SchemasRoot => Path.Combine(FixtureRoot, "schemas");

    private static string SourceRoot => Path.Combine(FixtureRoot, "examples", "release-baseline");
}
