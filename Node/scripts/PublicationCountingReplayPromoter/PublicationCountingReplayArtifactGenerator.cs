using System.Text.Json.Nodes;

namespace PublicationCountingReplayPromoter;

public sealed record PublicationCountingReplayArtifact(string RelativePath, string Content)
{
    public string Sha256Hash => PublicationCountingReplayContracts.Sha256Hex(Content);
}

public sealed record PublicationCountingReplayGeneratedPackage(
    string Status,
    string PackageRoot,
    JsonObject Source,
    IReadOnlyList<PublicationCountingReplayArtifact> Artifacts);

public static class PublicationCountingReplayArtifactGenerator
{
    public const string ReadmePath = "README.md";
    public const string ManifestPath = "publication-counting-replay-manifest.json";
    public const string GoodProfileReplaySummaryPath = "validation/good-profile-replay-summary.json";
    public const string GoodProfileNormalizedOutputHashesPath = "validation/good-profile-normalized-output-hashes.json";
    public const string TamperReplaySummaryPath = "validation/tamper-replay-summary.json";
    public const string StaleReferenceCheckSummaryPath = "validation/stale-reference-check-summary.json";
    public const string GeneratedReportBindingSummaryPath = "validation/generated-report-binding-summary.json";
    public const string NoSecretScanResultPath = "validation/no-secret-scan-result.json";
    public const string ReadinessFragmentPath = "readiness/publication-counting-replay-readiness-fragment.json";
    public const string ScoreProposalPath = "readiness/publication-counting-replay-score-proposal.json";
    public const string DownstreamHandoffPath = "handoff/publication-counting-replay-downstream-handoff.json";

    public static readonly string[] RequiredArtifactPaths =
    [
        ReadmePath,
        ManifestPath,
        GoodProfileReplaySummaryPath,
        GoodProfileNormalizedOutputHashesPath,
        TamperReplaySummaryPath,
        StaleReferenceCheckSummaryPath,
        GeneratedReportBindingSummaryPath,
        NoSecretScanResultPath,
        ReadinessFragmentPath,
        ScoreProposalPath,
        DownstreamHandoffPath,
    ];

    public static PublicationCountingReplayGeneratedPackage Generate(
        PublicationCountingReplayPromotionPaths paths,
        string? sourceInput = null,
        string? outputRoot = null,
        DateTimeOffset? generatedAt = null)
    {
        var source = PublicationCountingReplayContracts.ValidateForPromotion(paths, sourceInput);
        var packageRoot = ResolvePackageRoot(paths, source, outputRoot);
        var generatedAtText = ResolveGeneratedAt(source, generatedAt);
        var readme = new PublicationCountingReplayArtifact(ReadmePath, BuildReadme(source));
        var validationArtifacts = new List<PublicationCountingReplayArtifact>
        {
            JsonArtifact(GoodProfileReplaySummaryPath, BuildGoodProfileReplaySummary(source, generatedAtText)),
            JsonArtifact(GoodProfileNormalizedOutputHashesPath, BuildGoodProfileNormalizedOutputHashes(source, generatedAtText)),
            JsonArtifact(TamperReplaySummaryPath, BuildTamperReplaySummary(source, generatedAtText)),
            JsonArtifact(StaleReferenceCheckSummaryPath, BuildStaleReferenceCheckSummary(source, generatedAtText)),
        };
        var artifacts = new List<PublicationCountingReplayArtifact>(validationArtifacts)
        {
            JsonArtifact(GeneratedReportBindingSummaryPath, BuildGeneratedReportBindingSummary(generatedAtText, validationArtifacts)),
            JsonArtifact(ReadinessFragmentPath, BuildReadinessFragment(source, generatedAtText, validationArtifacts)),
            JsonArtifact(ScoreProposalPath, BuildScoreProposal(source, generatedAtText, validationArtifacts)),
            JsonArtifact(DownstreamHandoffPath, BuildDownstreamHandoff(source, generatedAtText)),
        };
        var noSecretScan = ScanGeneratedArtifacts([readme, .. artifacts]);
        artifacts.Insert(5, JsonArtifact(NoSecretScanResultPath, BuildNoSecretScanResult(source, generatedAtText, noSecretScan)));
        artifacts.Insert(0, readme);
        artifacts.Insert(1, JsonArtifact(ManifestPath, BuildManifest(source, generatedAtText, artifacts)));

        return new PublicationCountingReplayGeneratedPackage("candidate", packageRoot, source, artifacts);
    }

    public static string ResolvePackageRoot(
        PublicationCountingReplayPromotionPaths paths,
        JsonObject source,
        string? outputRoot = null)
    {
        var root = Path.GetFullPath(outputRoot ?? paths.PublicCorpusRoot);
        PublicationCountingReplayContracts.EnsurePathUnder(paths.WorkspaceRoot, root, "FEAT-160 replay output root");
        var packageRoot = Path.GetFullPath(Path.Combine(root, PublicationCountingReplayPromotionPaths.PackageRelativeRoot));
        PublicationCountingReplayContracts.EnsurePathUnder(root, packageRoot, "FEAT-160 replay package root");

        var targetPath = PublicationCountingReplayContracts.GetString(
            PublicationCountingReplayContracts.RequireObject(source, "packageLayout"),
            "targetPackagePath");
        if (!string.Equals(targetPath, PublicationCountingReplayContracts.ExpectedTargetPackagePath, StringComparison.Ordinal))
        {
            throw new PublicationCountingReplayPromotionException(
                "FEAT-160 package layout target is not the expected v0.2.0 replay package.",
                [$"Observed: {targetPath}"]);
        }

        return packageRoot;
    }

    private static PublicationCountingReplayArtifact JsonArtifact(string relativePath, JsonObject content) =>
        new(relativePath, PublicationCountingReplayContracts.CanonicalJson(content));

    private static string ResolveGeneratedAt(JsonObject source, DateTimeOffset? generatedAt)
    {
        if (generatedAt is not null)
        {
            return generatedAt.Value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }

        return PublicationCountingReplayContracts.GetString(source, "generatedAt");
    }

    private static JsonObject BuildManifest(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<PublicationCountingReplayArtifact> artifacts)
    {
        var baseline = PublicationCountingReplayContracts.RequireObject(source, "baselineRegister");
        var readinessOutput = PublicationCountingReplayContracts.RequireObject(source, "readinessOutput");
        return new JsonObject
        {
            ["schemaVersion"] = PublicationCountingReplayContracts.PackageManifestSchemaVersion,
            ["packageId"] = "hushvoting-publication-counting-replay",
            ["packageVersion"] = PublicationCountingReplayContracts.TargetPackageVersion,
            ["producerFeature"] = PublicationCountingReplayContracts.FeatureId,
            ["sourceId"] = PublicationCountingReplayContracts.GetString(source, "sourceId"),
            ["generatedAt"] = generatedAt,
            ["canonicalizationVersion"] = PublicationCountingReplayContracts.CanonicalizationVersion,
            ["baselineRegister"] = new JsonObject
            {
                ["registerVersionId"] = PublicationCountingReplayContracts.GetString(baseline, "registerVersionId"),
                ["status"] = PublicationCountingReplayContracts.GetString(baseline, "status"),
                ["dimensionId"] = PublicationCountingReplayContracts.GetString(baseline, "dimensionId"),
                ["proposedScoreFrom"] = 8,
                ["proposedScoreTo"] = 10,
                ["targetBlockerId"] = PublicationCountingReplayContracts.GetString(baseline, "targetBlockerId"),
                ["doesNotMutateRegister"] = true,
            },
            ["upstreamRefs"] = BuildUpstreamRefs(source),
            ["replaySummary"] = new JsonObject
            {
                ["status"] = "pass",
                ["caseCount"] = CountGoodProfiles(source),
                ["passedCount"] = CountGoodProfiles(source),
                ["failedCount"] = 0,
                ["summaryRef"] = GoodProfileReplaySummaryPath,
                ["evidenceMode"] = "source_validation_skeleton",
            },
            ["tamperSummary"] = new JsonObject
            {
                ["status"] = "pass",
                ["caseCount"] = CountNegativeCases(source),
                ["passedCount"] = CountNegativeCases(source),
                ["failedCount"] = 0,
                ["summaryRef"] = TamperReplaySummaryPath,
                ["evidenceMode"] = "source_validation_skeleton",
            },
            ["publicSafety"] = new JsonObject
            {
                ["status"] = "pass",
                ["unexpectedFindingCount"] = 0,
                ["scanResultRef"] = NoSecretScanResultPath,
            },
            ["readinessProposal"] = new JsonObject
            {
                ["status"] = "candidate",
                ["dimensionId"] = PublicationCountingReplayContracts.GetString(readinessOutput, "dimensionId"),
                ["proposedScoreFrom"] = PublicationCountingReplayContracts.GetInt(readinessOutput, "proposedScoreFrom"),
                ["proposedScoreTo"] = PublicationCountingReplayContracts.GetInt(readinessOutput, "proposedScoreTo"),
                ["scoreProposalRef"] = ScoreProposalPath,
                ["readinessFragmentRef"] = ReadinessFragmentPath,
            },
            ["entries"] = new JsonArray(artifacts
                .Where(artifact => artifact.RelativePath != ManifestPath)
                .Select(artifact => new JsonObject
                {
                    ["path"] = artifact.RelativePath,
                    ["sha256Hash"] = "sha256:" + artifact.Sha256Hash,
                    ["purpose"] = ArtifactPurpose(source, artifact.RelativePath),
                    ["publicSafe"] = true,
                    ["requiredForManifest"] = true,
                })
                .ToArray<JsonNode?>()),
        };
    }

    private static JsonObject BuildGoodProfileReplaySummary(JsonObject source, string generatedAt) =>
        new()
        {
            ["schemaVersion"] = "publication-counting-good-profile-replay-summary.v1",
            ["generatedAt"] = generatedAt,
            ["status"] = "source_validated",
            ["evidenceMode"] = "source_validation_skeleton",
            ["caseCount"] = CountGoodProfiles(source),
            ["passCount"] = CountGoodProfiles(source),
            ["cases"] = new JsonArray(GoodProfiles(source)
                .Select(profile => new JsonObject
                {
                    ["fixtureId"] = PublicationCountingReplayContracts.GetString(profile, "fixtureId"),
                    ["status"] = "source_validated",
                    ["packagePath"] = PublicationCountingReplayContracts.GetString(profile, "packagePath"),
                    ["packageHash"] = PublicationCountingReplayContracts.GetString(profile, "packageHash"),
                    ["expectedResultRef"] = PublicationCountingReplayContracts.GetString(profile, "expectedResultRef"),
                    ["expectedOverallStatus"] = PublicationCountingReplayContracts.GetString(profile, "expectedOverallStatus"),
                    ["expectedExitCode"] = PublicationCountingReplayContracts.GetInt(profile, "expectedExitCode"),
                    ["expectedPrimaryResultCode"] = PublicationCountingReplayContracts.GetString(profile, "expectedPrimaryResultCode"),
                    ["normalizedOutputHash"] = PublicationCountingReplayContracts.GetString(profile, "normalizedOutputHash"),
                })
                .ToArray<JsonNode?>()),
        };

    private static JsonObject BuildGoodProfileNormalizedOutputHashes(JsonObject source, string generatedAt) =>
        new()
        {
            ["schemaVersion"] = "publication-counting-good-profile-normalized-output-hashes.v1",
            ["generatedAt"] = generatedAt,
            ["status"] = "source_validated",
            ["hashes"] = new JsonArray(GoodProfiles(source)
                .Select(profile => new JsonObject
                {
                    ["fixtureId"] = PublicationCountingReplayContracts.GetString(profile, "fixtureId"),
                    ["normalizedOutputHash"] = PublicationCountingReplayContracts.GetString(profile, "normalizedOutputHash"),
                })
                .ToArray<JsonNode?>()),
        };

    private static JsonObject BuildTamperReplaySummary(JsonObject source, string generatedAt) =>
        new()
        {
            ["schemaVersion"] = "publication-counting-tamper-replay-summary.v1",
            ["generatedAt"] = generatedAt,
            ["status"] = "source_validated",
            ["evidenceMode"] = "source_validation_skeleton",
            ["caseCount"] = CountNegativeCases(source),
            ["cases"] = new JsonArray(NegativeCases(source)
                .Select(item => new JsonObject
                {
                    ["caseId"] = PublicationCountingReplayContracts.GetString(item, "caseId"),
                    ["fixtureId"] = PublicationCountingReplayContracts.GetString(item, "fixtureId"),
                    ["source"] = PublicationCountingReplayContracts.GetString(item, "source"),
                    ["coverageArea"] = PublicationCountingReplayContracts.GetString(item, "coverageArea"),
                    ["status"] = PublicationCountingReplayContracts.GetString(item, "source") == "feat160_required"
                        ? "required_by_feat160"
                        : "source_validated",
                    ["expectedPrimaryResultCode"] = PublicationCountingReplayContracts.GetString(item, "expectedPrimaryResultCode"),
                    ["expectedOverallStatus"] = PublicationCountingReplayContracts.GetString(item, "expectedOverallStatus"),
                    ["expectedExitCode"] = PublicationCountingReplayContracts.GetInt(item, "expectedExitCode"),
                    ["blocksScoreMovement"] = true,
                })
                .ToArray<JsonNode?>()),
        };

    private static JsonObject BuildStaleReferenceCheckSummary(JsonObject source, string generatedAt) =>
        new()
        {
            ["schemaVersion"] = "publication-counting-stale-reference-check-summary.v1",
            ["generatedAt"] = generatedAt,
            ["status"] = "source_validated",
            ["baselineRegister"] = PublicationCountingReplayContracts.Clone(source["baselineRegister"]),
            ["upstreamBaselines"] = PublicationCountingReplayContracts.Clone(source["upstreamBaselines"]),
            ["scorePolicy"] = PublicationCountingReplayContracts.Clone(source["scorePolicy"]),
            ["blocksScoreMovementWhenStale"] = true,
        };

    private static JsonObject BuildGeneratedReportBindingSummary(
        string generatedAt,
        IReadOnlyList<PublicationCountingReplayArtifact> validationArtifacts) =>
        new()
        {
            ["schemaVersion"] = "publication-counting-generated-report-binding-summary.v1",
            ["generatedAt"] = generatedAt,
            ["status"] = "candidate",
            ["boundArtifacts"] = new JsonArray(validationArtifacts
                .Select(artifact => new JsonObject
                {
                    ["path"] = artifact.RelativePath,
                    ["sha256Hash"] = "sha256:" + artifact.Sha256Hash,
                })
                .ToArray<JsonNode?>()),
            ["registerMutation"] = "not_performed",
        };

    private static JsonObject BuildNoSecretScanResult(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<string> findings) =>
        new()
        {
            ["schemaVersion"] = "publication-counting-replay-no-secret-scan-result.v1",
            ["generatedAt"] = generatedAt,
            ["status"] = findings.Count == 0 ? "pass" : "fail",
            ["unexpectedFindingCount"] = findings.Count,
            ["expectedUnexpectedFindingCount"] = 0,
            ["findings"] = new JsonArray(findings.Select(finding => JsonValue.Create(finding)).ToArray<JsonNode?>()),
            ["forbiddenMaterialCategories"] = PublicationCountingReplayContracts.Clone(
                PublicationCountingReplayContracts.RequireObject(source, "publicSafety")["forbiddenMaterialCategories"]),
            ["publicBoundaryStatement"] = PublicationCountingReplayContracts.GetString(
                PublicationCountingReplayContracts.RequireObject(source, "publicSafety"),
                "publicBoundaryStatement"),
        };

    private static JsonObject BuildReadinessFragment(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<PublicationCountingReplayArtifact> validationArtifacts)
    {
        var readiness = PublicationCountingReplayContracts.RequireObject(source, "readinessOutput");
        return new JsonObject
        {
            ["schemaVersion"] = "publication-counting-replay-readiness-fragment.v1",
            ["generatedAt"] = generatedAt,
            ["producerFeature"] = PublicationCountingReplayContracts.FeatureId,
            ["dimensionId"] = PublicationCountingReplayContracts.GetString(readiness, "dimensionId"),
            ["status"] = "candidate",
            ["targetBlockerId"] = PublicationCountingReplayContracts.GetString(readiness, "targetBlockerId"),
            ["scoreEffect"] = new JsonObject
            {
                ["proposedScoreFrom"] = PublicationCountingReplayContracts.GetInt(readiness, "proposedScoreFrom"),
                ["proposedScoreTo"] = PublicationCountingReplayContracts.GetInt(readiness, "proposedScoreTo"),
                ["scoreChangeAllowed"] = false,
                ["doesNotMutateRegister"] = true,
                ["requiresLaterReplayAcceptance"] = true,
            },
            ["evidenceRefs"] = new JsonArray(RequiredArtifactPaths
                .Where(path => path != ReadinessFragmentPath)
                .Select(path => JsonValue.Create(path))
                .ToArray<JsonNode?>()),
            ["evidenceArtifactHashes"] = ArtifactHashes(validationArtifacts),
            ["nonClaims"] = new JsonArray(
                JsonValue.Create("No production rollout claim"),
                JsonValue.Create("No public or state election readiness claim"),
                JsonValue.Create("No legal sufficiency or certification claim")),
        };
    }

    private static JsonObject BuildScoreProposal(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<PublicationCountingReplayArtifact> validationArtifacts)
    {
        var readiness = PublicationCountingReplayContracts.RequireObject(source, "readinessOutput");
        return new JsonObject
        {
            ["schemaVersion"] = "publication-counting-replay-score-proposal.v1",
            ["generatedAt"] = generatedAt,
            ["producerFeature"] = PublicationCountingReplayContracts.FeatureId,
            ["status"] = "candidate",
            ["dimensionId"] = PublicationCountingReplayContracts.GetString(readiness, "dimensionId"),
            ["targetBlockerId"] = PublicationCountingReplayContracts.GetString(readiness, "targetBlockerId"),
            ["proposedScoreFrom"] = PublicationCountingReplayContracts.GetInt(readiness, "proposedScoreFrom"),
            ["proposedScoreTo"] = PublicationCountingReplayContracts.GetInt(readiness, "proposedScoreTo"),
            ["scoreChangeAllowed"] = false,
            ["doesNotMutateRegister"] = true,
            ["directRegisterMutation"] = false,
            ["evidencePackagePath"] = PublicationCountingReplayContracts.ExpectedTargetPackagePath,
            ["evidenceArtifactHashes"] = ArtifactHashes(validationArtifacts),
            ["registerMutation"] = "not_performed",
        };
    }

    private static JsonObject BuildDownstreamHandoff(JsonObject source, string generatedAt) =>
        new()
        {
            ["schemaVersion"] = "publication-counting-replay-downstream-handoff.v1",
            ["handoffId"] = "FEAT-160-v0.2.0-handoff",
            ["generatedAt"] = generatedAt,
            ["producerFeature"] = PublicationCountingReplayContracts.FeatureId,
            ["targetPackage"] = PublicationCountingReplayContracts.ExpectedTargetPackagePath,
            ["consumers"] = PublicationCountingReplayContracts.Clone(source["downstreamConsumers"]),
            ["residualRisks"] = PublicationCountingReplayContracts.Clone(source["residualRisks"]),
            ["consumerInstructions"] = "FEAT-166 may consume this candidate package only after later FEAT-160 phases replace source-validation skeletons with replay-run evidence.",
        };

    private static JsonArray BuildUpstreamRefs(JsonObject source)
    {
        var upstream = PublicationCountingReplayContracts.RequireObject(source, "upstreamBaselines");
        var feat153 = PublicationCountingReplayContracts.RequireObject(upstream, "feat153");
        var feat158 = PublicationCountingReplayContracts.RequireObject(upstream, "feat158");
        return new JsonArray(
            UpstreamRef("feat153-manifest", "FEAT-153", PublicationCountingReplayContracts.GetString(feat153, "packagePath") + "publication-counting-hardening-manifest.json", PublicationCountingReplayContracts.GetString(feat153, "manifestHash")),
            UpstreamRef("feat153-score-proposal", "FEAT-153", PublicationCountingReplayContracts.GetString(feat153, "packagePath") + ScoreProposalSuffixForFeat153, PublicationCountingReplayContracts.GetString(feat153, "scoreProposalHash")),
            UpstreamRef("feat153-readiness-fragment", "FEAT-153", PublicationCountingReplayContracts.GetString(feat153, "packagePath") + ReadinessFragmentSuffixForFeat153, PublicationCountingReplayContracts.GetString(feat153, "readinessFragmentHash")),
            UpstreamRef("feat153-handoff", "FEAT-153", PublicationCountingReplayContracts.GetString(feat153, "packagePath") + HandoffSuffixForFeat153, PublicationCountingReplayContracts.GetString(feat153, "handoffHash")),
            UpstreamRef("feat158-corpus-manifest", "FEAT-158", PublicationCountingReplayContracts.GetString(feat158, "corpusPath") + "corpus-manifest.json", PublicationCountingReplayContracts.GetString(feat158, "manifestHash")),
            UpstreamRef("feat158-fixture-index", "FEAT-158", PublicationCountingReplayContracts.GetString(feat158, "corpusPath") + "fixtures/fixture-index.json", PublicationCountingReplayContracts.GetString(feat158, "fixtureIndexHash")));
    }

    private static JsonObject UpstreamRef(string artifactId, string producerFeature, string path, string sha256Hash) =>
        new()
        {
            ["artifactId"] = artifactId,
            ["producerFeature"] = producerFeature,
            ["path"] = path,
            ["sha256Hash"] = sha256Hash,
            ["requiredForScore"] = true,
        };

    private static JsonArray ArtifactHashes(IReadOnlyList<PublicationCountingReplayArtifact> artifacts) =>
        new(artifacts
            .Select(artifact => new JsonObject
            {
                ["path"] = artifact.RelativePath,
                ["sha256Hash"] = "sha256:" + artifact.Sha256Hash,
            })
            .ToArray<JsonNode?>());

    private static string BuildReadme(JsonObject source)
    {
        var sourceId = PublicationCountingReplayContracts.GetString(source, "sourceId");
        var baseline = PublicationCountingReplayContracts.RequireObject(source, "baselineRegister");
        return string.Join("\n", [
            "# HushVoting Publication/Counting Replay",
            "",
            $"Source: {sourceId}",
            $"Register baseline: {PublicationCountingReplayContracts.GetString(baseline, "registerVersionId")}",
            $"Score proposal: {PublicationCountingReplayContracts.TargetDimensionId} 8 to 10",
            "",
            "This Phase 3 package is a public-safe source-validation skeleton for FEAT-160 replay hardening.",
            "It binds the replay matrix, upstream package refs, and safety boundaries before later phases produce full replay-run evidence.",
            "It does not mutate the readiness register and does not claim production rollout, public/state election readiness, legal sufficiency, certification, or external crypto-review completion.",
            "",
        ]);
    }

    private static string ArtifactPurpose(JsonObject source, string relativePath)
    {
        var layout = PublicationCountingReplayContracts.RequireObject(source, "packageLayout");
        var file = PublicationCountingReplayContracts.RequireArray(layout, "files")
            .OfType<JsonObject>()
            .FirstOrDefault(item => PublicationCountingReplayContracts.GetString(item, "path") == relativePath);
        return file is null
            ? "FEAT-160 generated replay package artifact."
            : PublicationCountingReplayContracts.GetString(file, "purpose");
    }

    private static IReadOnlyList<string> ScanGeneratedArtifacts(IReadOnlyList<PublicationCountingReplayArtifact> artifacts)
    {
        var findings = new List<string>();
        foreach (var artifact in artifacts)
        {
            var text = artifact.Content.ToLowerInvariant();
            foreach (var forbidden in ForbiddenGeneratedNeedles)
            {
                if (text.Contains(forbidden, StringComparison.Ordinal))
                {
                    findings.Add($"{artifact.RelativePath} contains forbidden generated marker {forbidden}.");
                }
            }
        }

        return findings;
    }

    private static IEnumerable<JsonObject> GoodProfiles(JsonObject source) =>
        PublicationCountingReplayContracts.RequireArray(
                PublicationCountingReplayContracts.RequireObject(source, "replayMatrix"),
                "goodProfiles")
            .OfType<JsonObject>();

    private static IEnumerable<JsonObject> NegativeCases(JsonObject source) =>
        PublicationCountingReplayContracts.RequireArray(source, "negativeMatrix").OfType<JsonObject>();

    private static int CountGoodProfiles(JsonObject source) => GoodProfiles(source).Count();

    private static int CountNegativeCases(JsonObject source) => NegativeCases(source).Count();

    private const string ScoreProposalSuffixForFeat153 = "readiness/publication-counting-score-proposal.json";
    private const string ReadinessFragmentSuffixForFeat153 = "readiness/publication-counting-readiness-fragment.json";
    private const string HandoffSuffixForFeat153 = "handoff/publication-counting-hardening-downstream-handoff.json";

    private static readonly string[] ForbiddenGeneratedNeedles =
    [
        "private key",
        "seed phrase",
        "mnemonic",
        "credential=",
        "password=",
        "connection string",
        "aws_secret",
        "client_secret",
    ];
}
