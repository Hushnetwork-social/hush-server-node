using System.Text.Json.Nodes;
using FluentAssertions;
using PublicationCountingHardeningPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class PublicationCountingHardeningPromoterTests
{
    private static readonly DateTimeOffset FixedGeneratedAt = DateTimeOffset.Parse("2026-05-28T12:00:00Z");

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();

        var errors = PublicationCountingHardeningContracts.ValidateSchemaSet(workspace.Paths.SchemasRoot);

        errors.Should().BeEmpty();
        File.Exists(Path.Combine(
            workspace.Paths.SchemasRoot,
            PublicationCountingHardeningPromotionPaths.SchemaFileName)).Should().BeTrue();
    }

    [Fact]
    public void ReleaseBaseline_SourceAndCurrentRefs_AreValid()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var source = PublicationCountingHardeningContracts.LoadSource(workspace.Paths);

        var errors = PublicationCountingHardeningContracts.ValidateSource(source)
            .Concat(PublicationCountingHardeningContracts.ValidateCurrentRefs(workspace.Paths, source))
            .ToArray();

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Promotion_CheckOnly_ShouldNotWritePackageRoot()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();

        var result = new PublicationCountingHardeningPromotionService().Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModeCheckOnly,
            null,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));

        result.Mode.Should().Be(PublicationCountingHardeningPromotionService.ModeCheckOnly);
        result.WrittenFiles.Should().BeEmpty();
        result.CheckedFiles.Should().BeEquivalentTo(PublicationCountingHardeningArtifactGenerator.RequiredArtifactPaths);
        Directory.Exists(workspace.Paths.DefaultPackageRoot).Should().BeFalse();
    }

    [Fact]
    public void Promotion_PackageMode_WritesDeterministicArtifacts()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var service = new PublicationCountingHardeningPromotionService();

        var first = service.Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModePackage,
            null,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));
        var second = service.Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModePackage,
            null,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));

        first.WrittenFiles.Should().HaveCount(PublicationCountingHardeningArtifactGenerator.RequiredArtifactPaths.Length);
        first.GeneratedPackage.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content))
            .Should()
            .Equal(second.GeneratedPackage.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content)));
        foreach (var relativePath in PublicationCountingHardeningArtifactGenerator.RequiredArtifactPaths)
        {
            File.Exists(Path.Combine(workspace.Paths.DefaultPackageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Should()
                .BeTrue(relativePath);
        }
    }

    [Fact]
    public void SourceValidation_MissingVerifierSourceRef_FailsBeforeGeneration()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var source = workspace.LoadSource();
        source["verifierRefs"]!.AsObject().Remove("sourceRef");

        var errors = PublicationCountingHardeningContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("sourceRef", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_RegisterMutationRequest_FailsBeforeGeneration()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var source = workspace.LoadSource();
        source["readinessProposal"]!.AsObject()["doesNotMutateRegister"] = false;

        var errors = PublicationCountingHardeningContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("doesNotMutateRegister", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SourceValidation_LocalAbsolutePath_FailsBeforeGeneration()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var source = workspace.LoadSource();
        source["packageRefs"]!.AsObject()["packagePath"] = @"C:\private\package";
        var sourceInput = await workspace.WriteSourceAsync(source, "absolute-path-source.json");

        var act = () => new PublicationCountingHardeningPromotionService().Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModeCheckOnly,
            sourceInput,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<PublicationCountingHardeningPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("packagePath", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CurrentnessValidation_StaleVerifierBinaryHash_BlocksPromotion()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var source = workspace.LoadSource();
        source["verifierRefs"]!.AsObject()["binaryRelease"] = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
        var sourceInput = await workspace.WriteSourceAsync(source, "stale-verifier-source.json");

        var act = () => new PublicationCountingHardeningPromotionService().Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModeCheckOnly,
            sourceInput,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<PublicationCountingHardeningPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("verifierRefs.binaryRelease", StringComparison.Ordinal));
    }

    [Fact]
    public void Promotion_MissingInputPath_FailsDeterministically()
    {
        using var workspace = TempPublicationCountingWorkspace.Create();
        var missingInput = Path.Combine(workspace.Paths.SourceRoot, "examples", "release-baseline", "missing-source.json");

        var act = () => new PublicationCountingHardeningPromotionService().Promote(new(
            workspace.Paths,
            PublicationCountingHardeningPromotionService.ModeCheckOnly,
            missingInput,
            null,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<PublicationCountingHardeningPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("Source input was not found", StringComparison.Ordinal));
    }

    private sealed class TempPublicationCountingWorkspace : IDisposable
    {
        private const string VerifierSourceRef = "88e7d8f4f35e21a341d9ad1b92ecc73bdba0ab15";
        private const string VerifierBinaryHash = "sha256:7048677ba0c66c69c123ab7d046eb03a0d3c7642103f3dd6a2645873c928d6a4";
        private const string GoodPackageHash = "sha256:9014846bcf4fb7b7369e8fcb53e379d7ec3cfa6829478fb657af681e678942cc";

        private TempPublicationCountingWorkspace(string root, PublicationCountingHardeningPromotionPaths paths)
        {
            Root = root;
            Paths = paths;
        }

        public string Root { get; }

        public PublicationCountingHardeningPromotionPaths Paths { get; }

        public static TempPublicationCountingWorkspace Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"hush-publication-counting-{Guid.NewGuid():N}");
            var paths = PublicationCountingHardeningPromotionPaths.FromWorkspaceRoot(root);
            Directory.CreateDirectory(paths.SchemasRoot);
            Directory.CreateDirectory(Path.Combine(paths.ExamplesRoot, "release-baseline"));
            Directory.CreateDirectory(Path.Combine(paths.PublicCorpusRoot, "hushvoting-v1", "v0.2.0", "fixtures"));
            Directory.CreateDirectory(Path.Combine(paths.PublicCorpusRoot, "hushvoting-v1", "v0.2.0", "expected-results"));
            Directory.CreateDirectory(Path.Combine(paths.PublicCorpusRoot, "hushvoting-v1", "v0.2.0", "packages", "sample-good-finalized-election"));

            var workspace = new TempPublicationCountingWorkspace(root, paths);
            workspace.WriteSchema();
            workspace.WriteCorpusInputs();
            workspace.WriteSourceAsync(workspace.BuildSource(), PublicationCountingHardeningPromotionPaths.SourceFileName)
                .GetAwaiter()
                .GetResult();
            return workspace;
        }

        public JsonObject LoadSource() => PublicationCountingHardeningContracts.LoadSource(Paths);

        public async Task<string> WriteSourceAsync(JsonObject source, string fileName)
        {
            var path = Path.Combine(Paths.ExamplesRoot, "release-baseline", fileName);
            await File.WriteAllTextAsync(path, PublicationCountingHardeningContracts.CanonicalJson(source));
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private void WriteSchema()
        {
            var schema = new JsonObject
            {
                ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                ["required"] = Strings("schemaVersion", "sourceId", "producerFeature"),
            };
            File.WriteAllText(
                Path.Combine(Paths.SchemasRoot, PublicationCountingHardeningPromotionPaths.SchemaFileName),
                PublicationCountingHardeningContracts.CanonicalJson(schema));
        }

        private void WriteCorpusInputs()
        {
            var releaseRoot = Path.Combine(Paths.PublicCorpusRoot, "hushvoting-v1", "v0.2.0");
            var manifest = new JsonObject
            {
                ["schemaVersion"] = "verifier-corpus-manifest.v1",
                ["protocolPackage"] = new JsonObject
                {
                    ["packageVersion"] = "v1.2.0",
                },
                ["verifier"] = new JsonObject
                {
                    ["sourceRef"] = VerifierSourceRef,
                    ["binaryRelease"] = VerifierBinaryHash,
                },
            };
            File.WriteAllText(Path.Combine(releaseRoot, "corpus-manifest.json"), PublicationCountingHardeningContracts.CanonicalJson(manifest));

            var fixtureIndex = new JsonObject
            {
                ["schemaVersion"] = "verifier-corpus-fixture-index.v1",
                ["fixtures"] = new JsonArray(),
            };
            File.WriteAllText(Path.Combine(releaseRoot, "fixtures", "fixture-index.json"), PublicationCountingHardeningContracts.CanonicalJson(fixtureIndex));

            var expectedResult = new JsonObject
            {
                ["schemaVersion"] = "verifier-corpus-expected-result.v1",
                ["fixtureId"] = "sample-good-finalized-election",
                ["expectedOverallStatus"] = "pass",
                ["expectedExitCode"] = 0,
                ["requiredResultCodes"] = Strings("package_structure_valid"),
                ["normalizedOutputHash"] = "sha256:b319a4b3e8f07a0bb5ee71cf8a3f7ddaeecd67dca96c7b088df7ab07b066ee8c",
            };
            File.WriteAllText(
                Path.Combine(releaseRoot, "expected-results", "sample-good-finalized-election.json"),
                PublicationCountingHardeningContracts.CanonicalJson(expectedResult));
        }

        private JsonObject BuildSource()
        {
            var releaseRoot = Path.Combine(Paths.PublicCorpusRoot, "hushvoting-v1", "v0.2.0");
            var sourceRelease = new JsonObject
            {
                ["corpusFamily"] = "hushvoting-v1",
                ["corpusVersion"] = "v0.2.0",
                ["status"] = "accepted",
                ["publicPath"] = "HushVoting-Verifier-Corpus/hushvoting-v1/v0.2.0/",
                ["manifestHash"] = PublicationCountingHardeningContracts.Sha256File(Path.Combine(releaseRoot, "corpus-manifest.json")),
                ["fixtureIndexHash"] = PublicationCountingHardeningContracts.Sha256File(Path.Combine(releaseRoot, "fixtures", "fixture-index.json")),
                ["goodPackageHash"] = GoodPackageHash,
                ["resultCodeStabilityStatus"] = "accepted",
                ["cleanMachineValidationStatus"] = "windows_linux_replay_validated",
                ["noSecretScanStatus"] = "pass",
            };

            return new JsonObject
            {
                ["schemaVersion"] = PublicationCountingHardeningContracts.SourceSchemaVersion,
                ["sourceId"] = "FEAT153-TEST-SOURCE",
                ["generatedAt"] = "2026-05-28T12:00:00Z",
                ["producerFeature"] = PublicationCountingHardeningContracts.FeatureId,
                ["baselineRegister"] = new JsonObject
                {
                    ["registerVersionId"] = PublicationCountingHardeningContracts.CurrentRegisterId,
                    ["registerStatus"] = "AcceptedInternal",
                    ["totalScore"] = 71,
                    ["dimensionId"] = PublicationCountingHardeningContracts.TargetDimensionId,
                    ["dimensionName"] = "Publication/counting evidence",
                    ["currentScore"] = 7,
                    ["targetScoreBeforeReviewPilot"] = 8,
                    ["evidenceIds"] = Strings("RDY-EVID-AT-RDY-001-FEAT-113-001"),
                    ["residualRisk"] = "Publication and counting evidence must stay bound to current release and package refs.",
                },
                ["sourceRelease"] = sourceRelease,
                ["protocolRefs"] = new JsonObject
                {
                    ["packageId"] = "omega-hushvoting-v1",
                    ["packageVersion"] = "v1.2.0",
                    ["source"] = "https://github.com/Hushnetwork-social/protocol-omega-packages",
                    ["profileId"] = "public_anonymous_v1",
                    ["proofMode"] = "zk_rerandomization_shuffle_v1",
                    ["proofConstruction"] = "bayer_groth_reencryption_shuffle_argument_v1",
                    ["reviewLabel"] = "external_crypto_review_pending",
                },
                ["verifierRefs"] = new JsonObject
                {
                    ["repository"] = "https://github.com/Hushnetwork-social/hush-server-node",
                    ["sourceRef"] = VerifierSourceRef,
                    ["projectPath"] = "Tools/HushVotingVerifier/HushVotingVerifier.csproj",
                    ["runtime"] = ".NET 9",
                    ["profileId"] = "public_anonymous_v1",
                    ["binaryRelease"] = VerifierBinaryHash,
                },
                ["packageRefs"] = new JsonObject
                {
                    ["fixtureId"] = "sample-good-finalized-election",
                    ["packagePath"] = "HushVoting-Verifier-Corpus/hushvoting-v1/v0.2.0/packages/sample-good-finalized-election",
                    ["packageHash"] = GoodPackageHash,
                    ["expectedResultRef"] = "HushVoting-Verifier-Corpus/hushvoting-v1/v0.2.0/expected-results/sample-good-finalized-election.json",
                    ["expectedResultHash"] = PublicationCountingHardeningContracts.Sha256File(Path.Combine(releaseRoot, "expected-results", "sample-good-finalized-election.json")),
                    ["expectedOverallStatus"] = "pass",
                    ["expectedExitCode"] = 0,
                    ["requiredArtifacts"] = new JsonArray(new JsonObject
                    {
                        ["artifactId"] = "accepted-ballot-set",
                        ["path"] = "artifacts/election-record/accepted-ballot-set.json",
                        ["role"] = "Accepted ballot inventory used before publication.",
                    }),
                },
                ["acceptedToPublishedChecks"] = new JsonArray(BuildBindingCheck("AT-PUB-001-accepted-set-current")),
                ["tallyReplayChecks"] = new JsonArray(BuildBindingCheck("AT-TALLY-001-count-reconciliation")),
                ["tamperAndStaleMatrix"] = new JsonArray(new JsonObject
                {
                    ["caseId"] = "TM-PUBLISHED-STREAM-HASH",
                    ["fixtureId"] = "tamper-published-stream-hash",
                    ["category"] = "accepted_to_published",
                    ["changedArtifact"] = "artifacts/election-record/published-ballot-stream.json",
                    ["expectedPrimaryResultCode"] = "published_ballot_stream_hash_mismatch",
                    ["expectedOverallStatus"] = "fail",
                    ["expectedExitCode"] = 1,
                    ["blocksScoreMovement"] = true,
                }),
                ["publicSafety"] = new JsonObject
                {
                    ["visibility"] = "public_safe",
                    ["forbiddenMaterialCategories"] = Strings(
                        "shuffle_maps",
                        "rerandomization_randomness",
                        "plaintext_choices",
                        "voter_identity_joins",
                        "kms_secrets",
                        "support_case_data",
                        "local_absolute_paths",
                        "private_backend_logs",
                        "cloud_account_identifiers",
                        "database_connection_strings"),
                    ["expectedFindingCountInGeneratedPackage"] = 0,
                    ["allowedSyntheticTamperMarkersInInputCorpus"] = true,
                    ["publicBoundaryStatement"] = "Public-safe metadata only.",
                },
                ["packageLayout"] = new JsonObject
                {
                    ["targetPackagePath"] = PublicationCountingHardeningContracts.ExpectedTargetPackagePath,
                    ["immutableVersion"] = PublicationCountingHardeningContracts.TargetPackageVersion,
                    ["files"] = new JsonArray(PublicationCountingHardeningArtifactGenerator.RequiredArtifactPaths
                        .Select(path => new JsonObject
                        {
                            ["path"] = path,
                            ["purpose"] = "test artifact",
                            ["publicSafe"] = true,
                        })
                        .ToArray<JsonNode?>()),
                },
                ["readinessProposal"] = new JsonObject
                {
                    ["dimensionId"] = PublicationCountingHardeningContracts.TargetDimensionId,
                    ["proposedScoreFrom"] = 7,
                    ["proposedScoreTo"] = 8,
                    ["doesNotMutateRegister"] = true,
                    ["promotionOwner"] = "FEAT-156 or later explicit FEAT-130 promotion",
                    ["requiredPassingChecks"] = Strings("AT-PUB-001-accepted-set-current", "AT-TALLY-001-count-reconciliation"),
                    ["nonClaims"] = Strings("No production rollout claim"),
                },
                ["downstreamConsumers"] = Strings("FEAT-154", "FEAT-155", "FEAT-156"),
                ["residualRisks"] = Strings("External crypto review remains pending."),
            };
        }

        private static JsonObject BuildBindingCheck(string checkId) =>
            new()
            {
                ["checkId"] = checkId,
                ["purpose"] = "test binding",
                ["requiredArtifactIds"] = Strings("accepted-ballot-set"),
                ["mustBind"] = Strings("left", "right"),
                ["validFixtureIds"] = Strings("sample-good-finalized-election"),
                ["tamperFixtureIds"] = Strings("tamper-published-stream-hash"),
                ["expectedValidOverallStatus"] = "pass",
                ["expectedFailureResultCodes"] = Strings("published_ballot_stream_hash_mismatch"),
                ["blocksScoreMovementWhenFailing"] = true,
            };

        private static JsonArray Strings(params string[] values) =>
            new(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());
    }
}
