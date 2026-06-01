using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ProductionLikeOperationalRunSourceSchemaTests
{
    private static readonly string[] SourceRootRelativePath =
    [
        "Overview",
        "HushVotingReadiness",
        "Production-Like-Operational-Run"
    ];

    private static readonly string[] FixtureRootRelativePath =
    [
        "Node",
        "HushServerNode.Tests",
        "Elections",
        "TestFixtures",
        "Production-Like-Operational-Run"
    ];

    private static readonly string[] MandatorySections =
    [
        "baselineRegister",
        "runProfile",
        "dataScope",
        "deploymentProof",
        "runtimeBinding",
        "webClientObservation",
        "operationalEvidence",
        "securitySupportFreshness",
        "pilotLineage",
        "productionRolloutGateSource",
        "monitoring",
        "support",
        "backupRestore",
        "incidentDeclaration",
        "operatorHandoff",
        "postmortem",
        "publicSafety",
        "restrictedEvidenceRefs",
        "readinessProposal",
        "downstreamHandoff",
        "signoff",
        "residualRisks"
    ];

    [Fact]
    public void Schema_RequiresProductionLikeOperationalRunSections()
    {
        var schema = LoadSchema();

        var required = GetStringArray(schema, "required");

        foreach (var section in MandatorySections)
        {
            required.Should().Contain(section);
        }
    }

    [Fact]
    public void RunProfile_DisallowsLocalPrivateChainAndUncontrolledProduction()
    {
        var runProfile = GetDefinition(LoadSchema(), "runProfile");

        GetProperty(runProfile, "profileId")["const"]!.GetValue<string>()
            .Should().Be("controlled-hush-managed-staging-aws-like-v1");
        GetProperty(runProfile, "environmentClass")["const"]!.GetValue<string>()
            .Should().Be("controlled_hush_managed_staging_aws_like");
        GetProperty(runProfile, "localOnly")["const"]!.GetValue<bool>().Should().BeFalse();
        GetProperty(runProfile, "privateChainOnly")["const"]!.GetValue<bool>().Should().BeFalse();
        GetProperty(runProfile, "uncontrolledProduction")["const"]!.GetValue<bool>().Should().BeFalse();
        GetProperty(runProfile, "syntheticOrNonConfidentialData")["const"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void RuntimeWebClientAndOps_UseDedicatedEvidenceContracts()
    {
        var schema = LoadSchema();
        var properties = schema["properties"]!.AsObject();

        properties["runtimeBinding"]!.AsObject()["$ref"]!.GetValue<string>()
            .Should().Be("#/$defs/runtimeBinding");
        properties["webClientObservation"]!.AsObject()["$ref"]!.GetValue<string>()
            .Should().Be("#/$defs/webClientObservation");
        properties["operationalEvidence"]!.AsObject()["$ref"]!.GetValue<string>()
            .Should().Be("#/$defs/operationalEvidence");

        var runtimeRequired = GetStringArray(GetDefinition(schema, "runtimeBinding"), "required");
        runtimeRequired.Should().Contain("ledgerRefs");
        runtimeRequired.Should().Contain("lifecycleCheckpoints");
        runtimeRequired.Should().Contain("evidenceRefs");

        var webClientRequired = GetStringArray(GetDefinition(schema, "webClientObservation"), "required");
        webClientRequired.Should().Contain("observedHandshakeRefs");
        webClientRequired.Should().Contain("browserEvidenceBoundary");
        webClientRequired.Should().Contain("evidenceRefs");

        var operationalRequired = GetStringArray(GetDefinition(schema, "operationalEvidence"), "required");
        operationalRequired.Should().Contain("opsSourceStatus");
        operationalRequired.Should().Contain("opsCheckIds");
        operationalRequired.Should().Contain("evidenceRefs");
    }

    [Fact]
    public void ReadinessProposal_DisablesDirectRegisterMutation()
    {
        var proposal = GetDefinition(LoadSchema(), "readinessProposal");

        GetProperty(proposal, "dimensionId")["const"]!.GetValue<string>().Should().Be("RDY-DIM-007");
        GetProperty(proposal, "proposedScoreFrom")["const"]!.GetValue<int>().Should().Be(6);
        GetProperty(proposal, "proposedScoreTo")["const"]!.GetValue<int>().Should().Be(8);
        GetProperty(proposal, "doesNotMutateRegister")["const"]!.GetValue<bool>().Should().BeTrue();
        GetProperty(proposal, "directRegisterMutation")["const"]!.GetValue<bool>().Should().BeFalse();
        GetProperty(proposal, "promotionOwner")["const"]!.GetValue<string>().Should().Be("FEAT-156");
    }

    [Fact]
    public void DownstreamHandoff_RequiresAllConsumerFeatures()
    {
        var downstream = GetDefinition(LoadSchema(), "downstreamHandoff");
        var targetFeatures = GetProperty(downstream, "targetFeatures");

        targetFeatures["minItems"]!.GetValue<int>().Should().Be(3);
        targetFeatures["uniqueItems"]!.GetValue<bool>().Should().BeTrue();

        var requiredConsumers = downstream["allOf"]!
            .AsArray()
            .Select(item => item!
                .AsObject()["properties"]!
                .AsObject()["targetFeatures"]!
                .AsObject()["contains"]!
                .AsObject()["const"]!
                .GetValue<string>())
            .ToArray();

        requiredConsumers.Should().BeEquivalentTo(new[] { "FEAT-148", "FEAT-155", "FEAT-156" });
    }

    private static JsonObject LoadSchema([CallerFilePath] string sourceFilePath = "")
    {
        var schemaPath = Path.Combine(
            ResolveSourceRoot(FindWorkspaceRoot(sourceFilePath)),
            "schemas",
            "production-like-operational-run-source.schema.json");

        return JsonNode.Parse(File.ReadAllText(schemaPath))!.AsObject();
    }

    private static string FindWorkspaceRoot(string sourceFilePath)
    {
        foreach (var startPath in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory(), sourceFilePath })
        {
            var root = FindWorkspaceRootFrom(startPath);
            if (root is not null)
            {
                return root;
            }
        }

        throw new DirectoryNotFoundException("Could not locate HushNetworkOrg workspace root.");
    }

    private static string? FindWorkspaceRootFrom(string startPath)
    {
        var directory = new DirectoryInfo(startPath);

        while (directory is not null)
        {
            if (IsWorkspaceRoot(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool IsWorkspaceRoot(string path) =>
        Directory.Exists(GetMemoryBankSourceRoot(path)) ||
        Directory.Exists(GetVendoredFixtureSourceRoot(path));

    private static string ResolveSourceRoot(string workspaceRoot)
    {
        var memoryBankRoot = GetMemoryBankSourceRoot(workspaceRoot);
        return Directory.Exists(memoryBankRoot)
            ? memoryBankRoot
            : GetVendoredFixtureSourceRoot(workspaceRoot);
    }

    private static string GetMemoryBankSourceRoot(string workspaceRoot) =>
        Path.Combine(new[] { workspaceRoot, "hush-memory-bank" }.Concat(SourceRootRelativePath).ToArray());

    private static string GetVendoredFixtureSourceRoot(string workspaceRoot) =>
        Path.Combine(new[] { workspaceRoot }.Concat(FixtureRootRelativePath).ToArray());

    private static JsonObject GetDefinition(JsonObject schema, string name) =>
        schema["$defs"]!.AsObject()[name]!.AsObject();

    private static JsonObject GetProperty(JsonObject schema, string name) =>
        schema["properties"]!.AsObject()[name]!.AsObject();

    private static string[] GetStringArray(JsonObject schema, string name) =>
        schema[name]!.AsArray().Select(item => item!.GetValue<string>()).ToArray();
}
