using System.Text.Json.Nodes;
using DeploymentProofPackagePromoter;
using FluentAssertions;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class DeploymentProofPackagePromotionServiceTests
{
    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        var paths = CreatePaths();

        var errors = DeploymentProofPackageContracts.ValidateSchemaSet(paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in DeploymentProofPackageContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(paths.SchemasRoot, schemaFile)).Should().BeTrue();
        }
    }

    [Fact]
    public void ComponentProofFixtures_WebClientAndServerNode_AreAcceptedIndependently()
    {
        var paths = CreatePaths();
        var webClient = LoadExample(paths, "component-proofs", "hush-web-client-component-proof.json");
        var serverNode = LoadExample(paths, "component-proofs", "hush-server-node-component-proof.json");

        DeploymentProofPackageContracts.ValidateComponentProof(webClient).Should().BeEmpty();
        DeploymentProofPackageContracts.ValidateComponentProof(serverNode).Should().BeEmpty();

        webClient.ContainsKey("rehearsalElectionId").Should().BeFalse();
        serverNode.ContainsKey("rehearsalElectionId").Should().BeFalse();
        webClient["artifactRefs"]!.AsObject().ContainsKey("webArtifactHash").Should().BeTrue();
        serverNode["artifactRefs"]!.AsObject().ContainsKey("backendImageDigest").Should().BeTrue();
    }

    [Fact]
    public void BindingAndCeremonyFixtures_ReferenceBothComponentProofsAndRequiredStages()
    {
        var paths = CreatePaths();
        var proofSet = LoadExample(paths, "bindings", "deployment-proof-set.json");
        var ledger = LoadExample(paths, "bindings", "per-election-deployment-binding-ledger.json");
        var ceremony = LoadExample(paths, "ceremonies", "deployment-ceremony.json");

        DeploymentProofPackageContracts.ValidateProofSet(proofSet).Should().BeEmpty();
        DeploymentProofPackageContracts.ValidateBindingLedger(ledger).Should().BeEmpty();
        DeploymentProofPackageContracts.ValidateCeremony(ceremony).Should().BeEmpty();

        var stageIds = ceremony["ceremonyStages"]!.AsArray()
            .Select(stage => stage!["stageId"]!.GetValue<string>())
            .ToArray();
        stageIds.Should().Contain(DeploymentProofPackageContracts.RequiredCeremonyStageIds);
    }

    [Fact]
    public void ComponentProof_MissingMandatoryArtifactHash_FailsClosed()
    {
        var paths = CreatePaths();
        var webClient = LoadExample(paths, "component-proofs", "hush-web-client-component-proof.json");
        webClient["artifactRefs"]!.AsObject().Remove("webArtifactHash");

        var errors = DeploymentProofPackageContracts.ValidateComponentProof(webClient);

        errors.Should().Contain(error => error.Contains("webArtifactHash", StringComparison.Ordinal));
    }

    [Fact]
    public void ComponentProof_MutableSourceRef_FailsClosed()
    {
        var paths = CreatePaths();
        var webClient = LoadExample(paths, "component-proofs", "hush-web-client-component-proof.json");
        var sourceRef = webClient["sourceRef"]!.AsObject();
        sourceRef["refType"] = "branch";
        sourceRef["value"] = "main";
        sourceRef["immutable"] = false;

        var errors = DeploymentProofPackageContracts.ValidateComponentProof(webClient);

        errors.Should().Contain(error => error.Contains("immutable", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("mutable branch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ComponentProof_PublicKmsAndProviderIdentifiers_FailClosed()
    {
        var paths = CreatePaths();
        var serverNode = LoadExample(paths, "component-proofs", "hush-server-node-component-proof.json");
        serverNode["publicLeakTest"] = "arn:aws:kms:eu-west-1:123456789012:key/kms-key-public-leak";

        var errors = DeploymentProofPackageContracts.ValidateComponentProof(serverNode);

        errors.Should().Contain(error => error.Contains("arn:aws:kms", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("direct provider account identifier", StringComparison.OrdinalIgnoreCase));
    }

    private static DeploymentProofPackagePromotionPaths CreatePaths()
    {
        var workspaceRoot = WorkspaceRootFinder.Find(AppContext.BaseDirectory);
        return DeploymentProofPackagePromotionPaths.FromWorkspaceRoot(workspaceRoot);
    }

    private static JsonObject LoadExample(
        DeploymentProofPackagePromotionPaths paths,
        string folder,
        string fileName) =>
        DeploymentProofPackageContracts.ReadJsonObject(
            Path.Combine(paths.ExamplesRoot, folder, fileName),
            fileName);
}
