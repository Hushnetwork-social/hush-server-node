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

    [Fact]
    public void ExpectedResult_MissingPrimaryResultCode_FailsContractValidation()
    {
        var expected = LoadExample("release-baseline/expected-results/tamper-missing-artifact.json");
        expected.Remove("expectedPrimaryResultCode");

        var errors = VerifierCorpusContracts.ValidateExpectedResult(expected, "tamper-missing-artifact.json");

        errors.Should().Contain(error => error.Contains("expectedPrimaryResultCode", StringComparison.Ordinal));
    }

    [Fact]
    public void Audit95CorpusManifest_CompleteImmutableRefs_PassesContractValidation()
    {
        var manifest = CreateValidAudit95Manifest();

        var errors = VerifierCorpusContracts.ValidateAudit95CorpusManifest(manifest);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Audit95CorpusManifest_LocalGeneratedPublicRef_FailsContractValidation()
    {
        var manifest = CreateValidAudit95Manifest();
        manifest["publicRepositoryRef"] = "local-generated";

        var errors = VerifierCorpusContracts.ValidateAudit95CorpusManifest(manifest);

        errors.Should().Contain(error => error.Contains("local-generated", StringComparison.Ordinal));
    }

    [Fact]
    public void Audit95CorpusManifest_MissingWorkflowRunId_FailsContractValidation()
    {
        var manifest = CreateValidAudit95Manifest();
        manifest["ciReplay"]!.AsObject().Remove("workflowRunId");

        var errors = VerifierCorpusContracts.ValidateAudit95CorpusManifest(manifest);

        errors.Should().Contain(error => error.Contains("workflowRunId", StringComparison.Ordinal));
    }

    [Fact]
    public void CiRunManifest_MissingWorkflowRunId_FailsContractValidation()
    {
        var manifest = CreateValidCiRunManifest();
        manifest.Remove("workflowRunId");

        var errors = VerifierCorpusContracts.ValidateCiRunManifest(manifest);

        errors.Should().Contain(error => error.Contains("workflowRunId", StringComparison.Ordinal));
    }

    [Fact]
    public void CiRunManifest_MissingFixtureOutputHash_FailsContractValidation()
    {
        var manifest = CreateValidCiRunManifest();
        manifest["fixtures"]!.AsArray()[0]!.AsObject().Remove("normalizedOutputHash");

        var errors = VerifierCorpusContracts.ValidateCiRunManifest(manifest);

        errors.Should().Contain(error => error.Contains("normalizedOutputHash", StringComparison.Ordinal));
    }

    [Fact]
    public void CiRunManifest_NonObjectFixture_FailsContractValidation()
    {
        var manifest = CreateValidCiRunManifest();
        manifest["fixtures"]!.AsArray().Add("not-a-fixture-row");

        var errors = VerifierCorpusContracts.ValidateCiRunManifest(manifest);

        errors.Should().Contain(error => error.Contains("fixtures[1] must be an object", StringComparison.Ordinal));
    }

    [Fact]
    public void Audit95ScoreProposal_DirectRegisterMutation_FailsContractValidation()
    {
        var proposal = CreateValidAudit95ScoreProposal();
        proposal["doesNotMutateRegister"] = false;

        var errors = VerifierCorpusContracts.ValidateAudit95ScoreProposal(proposal);

        errors.Should().Contain(error => error.Contains("doesNotMutateRegister must be true", StringComparison.Ordinal));
    }

    [Fact]
    public void NoSecretScanResult_MissingForbiddenCategories_FailsContractValidation()
    {
        var scan = new JsonObject
        {
            ["schemaVersion"] = "verifier-corpus-no-secret-scan-result.v1",
            ["status"] = "pass",
            ["forbiddenCategories"] = new JsonArray("private_key"),
            ["unexpectedFindingCount"] = 0,
            ["expectedTamperFindingCount"] = 0,
            ["findings"] = new JsonArray(),
        };

        var errors = VerifierCorpusContracts.ValidateNoSecretScanResult(scan);

        errors.Should().Contain(error => error.Contains("cloud_secret", StringComparison.Ordinal));
    }

    private static VerifierCorpusPromotionPaths CreatePaths() =>
        HushVotingReadinessTestArtifacts.CreateVerifierCorpusPaths();

    private static JsonObject LoadExample(string relativePath) =>
        VerifierCorpusContracts.ReadJsonObject(
            Path.Combine(CreatePaths().ExamplesRoot, relativePath),
            relativePath);

    private static JsonObject CreateValidAudit95Manifest()
    {
        var manifest = LoadExample("release-baseline/corpus-manifest.json");
        manifest["corpusVersion"] = "v0.3.0";
        manifest["publicRepositoryRef"] = new JsonObject
        {
            ["refType"] = "tag_immutable",
            ["value"] = "v0.3.0",
            ["immutable"] = true,
            ["publicationMode"] = "release_archive",
        };

        var verifier = manifest["verifier"]!.AsObject();
        verifier["sourceRef"] = CommitRef;
        verifier["binaryRelease"] = Sha256A;

        manifest["ciReplay"] = new JsonObject
        {
            ["workflowName"] = "Verifier corpus CI",
            ["workflowPath"] = ".github/workflows/verifier-corpus-ci.yml",
            ["workflowRunId"] = "1234567890",
            ["workflowRunAttempt"] = 1,
            ["runManifestRef"] = "validation/ci-verifier-run-manifest.json",
            ["outputSummaryRef"] = "validation/ci-verifier-output-summary.json",
            ["corpusRepositoryRef"] = "v0.3.0",
            ["verifierSourceRef"] = CommitRef,
            ["verifierHash"] = Sha256A,
        };

        return manifest;
    }

    private static JsonObject CreateValidCiRunManifest() =>
        new()
        {
            ["schemaVersion"] = "verifier-corpus-ci-run-manifest.v1",
            ["corpusRepository"] = "https://github.com/Hushnetwork-social/HushVoting-Verifier-Corpus",
            ["corpusRepositoryRef"] = "v0.3.0",
            ["corpusVersion"] = "v0.3.0",
            ["corpusManifestHash"] = Sha256B,
            ["verifierRepository"] = "https://github.com/Hushnetwork-social/hush-server-node",
            ["verifierSourceRef"] = CommitRef,
            ["verifierHash"] = Sha256A,
            ["workflowName"] = "Verifier corpus CI",
            ["workflowPath"] = ".github/workflows/verifier-corpus-ci.yml",
            ["workflowRunId"] = "1234567890",
            ["workflowRunAttempt"] = 1,
            ["runStatus"] = "accepted",
            ["generatedAt"] = "2026-06-01T00:00:00Z",
            ["fixtures"] = new JsonArray
            {
                new JsonObject
                {
                    ["fixtureId"] = "sample-good-finalized-election",
                    ["expectedExitCode"] = 0,
                    ["observedExitCode"] = 0,
                    ["expectedPrimaryResultCode"] = "package_structure_valid",
                    ["observedPrimaryResultCode"] = "package_structure_valid",
                    ["normalizedOutputHash"] = Sha256B,
                    ["status"] = "matched",
                },
            },
        };

    private static JsonObject CreateValidAudit95ScoreProposal() =>
        new()
        {
            ["schemaVersion"] = "verifier-corpus-audit95-score-proposal.v1",
            ["proposalId"] = "RDY-DIM-002-v0.3.0-score-proposal",
            ["producerFeature"] = "FEAT-158",
            ["dimensionId"] = "RDY-DIM-002",
            ["proposedScoreFrom"] = 8,
            ["proposedScoreTo"] = 10,
            ["status"] = "accepted_candidate",
            ["doesNotMutateRegister"] = true,
            ["evidenceRefs"] = new JsonArray
            {
                new JsonObject
                {
                    ["path"] = "corpus-manifest.json",
                    ["sha256Hash"] = Sha256B,
                },
            },
        };

    private static readonly string CommitRef = new('a', 40);
    private static readonly string Sha256A = $"sha256:{new string('a', 64)}";
    private static readonly string Sha256B = $"sha256:{new string('b', 64)}";
}
