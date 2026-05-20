using System.Text.Json.Nodes;
using FluentAssertions;
using VerifierCorpusPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class VerifierCorpusContractTests
{
    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        var paths = CreatePaths();

        var errors = VerifierCorpusContracts.ValidateSchemaSet(paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in VerifierCorpusContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(paths.SchemasRoot, schemaFile)).Should().BeTrue();
        }
    }

    [Fact]
    public void SourceFixtureSet_ReleaseBaseline_IsPublicSafeAndContractComplete()
    {
        var paths = CreatePaths();

        var errors = VerifierCorpusContracts.ValidateSourceFixtureSet(paths);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void CorpusManifest_MissingVerifierRef_FailsContractValidation()
    {
        var manifest = LoadExample("release-baseline/corpus-manifest.json");
        manifest.Remove("verifier");

        var errors = VerifierCorpusContracts.ValidateCorpusManifest(manifest);

        errors.Should().Contain(error => error.Contains("verifier", StringComparison.Ordinal));
    }

    [Fact]
    public void FixtureManifest_InvalidProfile_FailsContractValidation()
    {
        var fixture = LoadExample("release-baseline/fixtures/tamper-missing-artifact.fixture-manifest.json");
        fixture["profileId"] = "restricted_owner_auditor_v1";

        var errors = VerifierCorpusContracts.ValidateFixtureManifest(fixture, "tamper-missing-artifact.fixture-manifest.json");

        errors.Should().Contain(error => error.Contains("profileId", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpectedResult_InvalidVerifierResultCode_FailsContractValidation()
    {
        var expected = LoadExample("release-baseline/expected-results/tamper-missing-artifact.json");
        expected["requiredResultCodes"] = new JsonArray("not_a_verifier_result_code");

        var errors = VerifierCorpusContracts.ValidateExpectedResult(expected, "tamper-missing-artifact.json");

        errors.Should().Contain(error => error.Contains("unsupported verifier result code", StringComparison.Ordinal));
    }

    private static VerifierCorpusPromotionPaths CreatePaths() =>
        HushVotingReadinessTestArtifacts.CreateVerifierCorpusPaths();

    private static JsonObject LoadExample(string relativePath) =>
        VerifierCorpusContracts.ReadJsonObject(
            Path.Combine(CreatePaths().ExamplesRoot, relativePath),
            relativePath);
}
