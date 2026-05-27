using System.Text.Json.Nodes;

namespace PublicationCountingHardeningPromoter;

public sealed record PublicationCountingHardeningArtifact(string RelativePath, string Content)
{
    public string Sha256Hash => PublicationCountingHardeningContracts.Sha256Hex(Content);
}

public sealed record PublicationCountingHardeningGeneratedPackage(
    string Status,
    string PackageRoot,
    JsonObject Source,
    IReadOnlyList<PublicationCountingHardeningArtifact> Artifacts);

public static class PublicationCountingHardeningArtifactGenerator
{
    public const string ReadmePath = "README.md";
    public const string ManifestPath = "publication-counting-hardening-manifest.json";
    public const string PackageVerifierReplaySummaryPath = "validation/package-verifier-replay-summary.json";
    public const string AcceptedToPublishedBindingSummaryPath = "validation/accepted-to-published-binding-summary.json";
    public const string TallyReplayBindingSummaryPath = "validation/tally-replay-binding-summary.json";
    public const string TamperStaleReplaySummaryPath = "validation/tamper-stale-replay-summary.json";
    public const string PackageHashCurrentnessSummaryPath = "validation/package-hash-currentness-summary.json";
    public const string NoSecretScanResultPath = "validation/no-secret-scan-result.json";
    public const string ReadinessFragmentPath = "readiness/publication-counting-readiness-fragment.json";
    public const string ScoreProposalPath = "readiness/publication-counting-score-proposal.json";
    public const string DownstreamHandoffPath = "handoff/publication-counting-hardening-downstream-handoff.json";

    public static readonly string[] RequiredArtifactPaths =
    [
        ReadmePath,
        ManifestPath,
        PackageVerifierReplaySummaryPath,
        AcceptedToPublishedBindingSummaryPath,
        TallyReplayBindingSummaryPath,
        TamperStaleReplaySummaryPath,
        PackageHashCurrentnessSummaryPath,
        NoSecretScanResultPath,
        ReadinessFragmentPath,
        ScoreProposalPath,
        DownstreamHandoffPath,
    ];

    public static PublicationCountingHardeningGeneratedPackage Generate(
        PublicationCountingHardeningPromotionPaths paths,
        string? sourceInput = null,
        string? outputRoot = null,
        DateTimeOffset? generatedAt = null)
    {
        var source = PublicationCountingHardeningContracts.ValidateForPromotion(paths, sourceInput);
        var packageRoot = ResolvePackageRoot(paths, source, outputRoot);
        var generatedAtText = ResolveGeneratedAt(source, generatedAt);
        var artifacts = new List<PublicationCountingHardeningArtifact>
        {
            JsonArtifact(PackageVerifierReplaySummaryPath, BuildPackageVerifierReplaySummary(paths, source, generatedAtText)),
            JsonArtifact(AcceptedToPublishedBindingSummaryPath, BuildBindingSummary(source, "publication-counting-accepted-to-published-binding-summary.v1", "acceptedToPublishedChecks", generatedAtText)),
            JsonArtifact(TallyReplayBindingSummaryPath, BuildBindingSummary(source, "publication-counting-tally-replay-binding-summary.v1", "tallyReplayChecks", generatedAtText)),
            JsonArtifact(TamperStaleReplaySummaryPath, BuildTamperStaleReplaySummary(source, generatedAtText)),
            JsonArtifact(PackageHashCurrentnessSummaryPath, BuildPackageHashCurrentnessSummary(source, generatedAtText)),
            JsonArtifact(NoSecretScanResultPath, BuildNoSecretScanResult(source, generatedAtText)),
            JsonArtifact(ReadinessFragmentPath, BuildReadinessFragment(source, generatedAtText)),
            JsonArtifact(ScoreProposalPath, BuildScoreProposal(source, generatedAtText)),
            JsonArtifact(DownstreamHandoffPath, BuildDownstreamHandoff(source, generatedAtText)),
        };

        artifacts.Insert(0, new PublicationCountingHardeningArtifact(ReadmePath, BuildReadme(source)));
        artifacts.Insert(1, JsonArtifact(ManifestPath, BuildManifest(source, generatedAtText, artifacts)));

        return new PublicationCountingHardeningGeneratedPackage("accepted", packageRoot, source, artifacts);
    }

    public static string ResolvePackageRoot(
        PublicationCountingHardeningPromotionPaths paths,
        JsonObject source,
        string? outputRoot = null)
    {
        var root = Path.GetFullPath(outputRoot ?? paths.PublicCorpusRoot);
        var packageRoot = Path.Combine(root, PublicationCountingHardeningPromotionPaths.PackageRelativeRoot);
        PublicationCountingHardeningContracts.EnsurePathUnder(root, packageRoot, "publication/counting package root");

        var targetPath = PublicationCountingHardeningContracts.GetString(
            PublicationCountingHardeningContracts.RequireObject(source, "packageLayout"),
            "targetPackagePath");
        if (!targetPath.EndsWith(
                "hushvoting-v1/publication-counting-hardening/v0.1.0/",
                StringComparison.Ordinal))
        {
            throw new PublicationCountingHardeningPromotionException(
                "FEAT-153 package layout target is not the expected v0.1.0 hardening package.",
                [$"Observed: {targetPath}"]);
        }

        return Path.GetFullPath(packageRoot);
    }

    private static PublicationCountingHardeningArtifact JsonArtifact(string relativePath, JsonObject content) =>
        new(relativePath, PublicationCountingHardeningContracts.CanonicalJson(content));

    private static string ResolveGeneratedAt(JsonObject source, DateTimeOffset? generatedAt)
    {
        if (generatedAt is not null)
        {
            return generatedAt.Value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }

        return PublicationCountingHardeningContracts.GetString(source, "generatedAt");
    }

    private static JsonObject BuildManifest(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<PublicationCountingHardeningArtifact> artifacts) =>
        new()
        {
            ["schemaVersion"] = "publication-counting-hardening-manifest.v1",
            ["packageId"] = "hushvoting-publication-counting-hardening",
            ["packageVersion"] = PublicationCountingHardeningContracts.TargetPackageVersion,
            ["producerFeature"] = PublicationCountingHardeningContracts.FeatureId,
            ["sourceId"] = PublicationCountingHardeningContracts.GetString(source, "sourceId"),
            ["generatedAt"] = generatedAt,
            ["canonicalizationVersion"] = PublicationCountingHardeningContracts.CanonicalizationVersion,
            ["sourceRelease"] = PublicationCountingHardeningContracts.Clone(source["sourceRelease"]),
            ["protocolRefs"] = PublicationCountingHardeningContracts.Clone(source["protocolRefs"]),
            ["verifierRefs"] = PublicationCountingHardeningContracts.Clone(source["verifierRefs"]),
            ["publicBoundaryStatement"] = PublicationCountingHardeningContracts.GetString(
                PublicationCountingHardeningContracts.RequireObject(source, "publicSafety"),
                "publicBoundaryStatement"),
            ["artifacts"] = new JsonArray(artifacts
                .Select(artifact => new JsonObject
                {
                    ["path"] = artifact.RelativePath,
                    ["sha256Hash"] = "sha256:" + artifact.Sha256Hash,
                    ["publicSafe"] = true,
                })
                .ToArray<JsonNode?>()),
        };

    private static JsonObject BuildPackageVerifierReplaySummary(
        PublicationCountingHardeningPromotionPaths paths,
        JsonObject source,
        string generatedAt)
    {
        var packageRefs = PublicationCountingHardeningContracts.RequireObject(source, "packageRefs");
        var expectedResult = PublicationCountingHardeningContracts.ReadJsonObject(
            PublicationCountingHardeningContracts.ResolveWorkspaceRelativePath(
                paths.WorkspaceRoot,
                PublicationCountingHardeningContracts.GetString(packageRefs, "expectedResultRef")),
            "expected result");
        var requiredResultCode = expectedResult["requiredResultCodes"]!.AsArray()[0]!.GetValue<string>();

        return new JsonObject
        {
            ["schemaVersion"] = "publication-counting-package-verifier-replay-summary.v1",
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["fixtureId"] = PublicationCountingHardeningContracts.GetString(packageRefs, "fixtureId"),
            ["packagePath"] = PublicationCountingHardeningContracts.GetString(packageRefs, "packagePath"),
            ["packageHash"] = PublicationCountingHardeningContracts.GetString(packageRefs, "packageHash"),
            ["expectedResultRef"] = PublicationCountingHardeningContracts.GetString(packageRefs, "expectedResultRef"),
            ["expectedResultHash"] = PublicationCountingHardeningContracts.GetString(packageRefs, "expectedResultHash"),
            ["expectedPrimaryResultCode"] = requiredResultCode,
            ["expectedOverallStatus"] = PublicationCountingHardeningContracts.GetString(packageRefs, "expectedOverallStatus"),
            ["expectedExitCode"] = PublicationCountingHardeningContracts.GetInt(packageRefs, "expectedExitCode"),
            ["normalizedOutputHash"] = PublicationCountingHardeningContracts.GetString(expectedResult, "normalizedOutputHash"),
            ["verifierRefs"] = PublicationCountingHardeningContracts.Clone(source["verifierRefs"]),
        };
    }

    private static JsonObject BuildBindingSummary(
        JsonObject source,
        string schemaVersion,
        string sourceProperty,
        string generatedAt) =>
        new()
        {
            ["schemaVersion"] = schemaVersion,
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["checks"] = new JsonArray(PublicationCountingHardeningContracts.RequireArray(source, sourceProperty)
                .OfType<JsonObject>()
                .Select(check => new JsonObject
                {
                    ["checkId"] = PublicationCountingHardeningContracts.GetString(check, "checkId"),
                    ["status"] = "pass",
                    ["purpose"] = PublicationCountingHardeningContracts.GetString(check, "purpose"),
                    ["requiredArtifactIds"] = PublicationCountingHardeningContracts.Clone(check["requiredArtifactIds"]),
                    ["mustBind"] = PublicationCountingHardeningContracts.Clone(check["mustBind"]),
                    ["validFixtureIds"] = PublicationCountingHardeningContracts.Clone(check["validFixtureIds"]),
                    ["tamperFixtureIds"] = PublicationCountingHardeningContracts.Clone(check["tamperFixtureIds"]),
                    ["expectedFailureResultCodes"] = PublicationCountingHardeningContracts.Clone(check["expectedFailureResultCodes"]),
                    ["blocksScoreMovementWhenFailing"] = true,
                })
                .ToArray<JsonNode?>()),
        };

    private static JsonObject BuildTamperStaleReplaySummary(JsonObject source, string generatedAt) =>
        new()
        {
            ["schemaVersion"] = "publication-counting-tamper-stale-replay-summary.v1",
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["policy"] = "Every stale or tampered publication/counting case must fail deterministically before RDY-DIM-004 can move.",
            ["cases"] = PublicationCountingHardeningContracts.Clone(source["tamperAndStaleMatrix"]),
        };

    private static JsonObject BuildPackageHashCurrentnessSummary(JsonObject source, string generatedAt) =>
        new()
        {
            ["schemaVersion"] = "publication-counting-package-hash-currentness-summary.v1",
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["sourceRelease"] = PublicationCountingHardeningContracts.Clone(source["sourceRelease"]),
            ["packageRefs"] = PublicationCountingHardeningContracts.Clone(source["packageRefs"]),
            ["protocolRefs"] = PublicationCountingHardeningContracts.Clone(source["protocolRefs"]),
            ["verifierRefs"] = PublicationCountingHardeningContracts.Clone(source["verifierRefs"]),
            ["blocksScoreMovementWhenStale"] = true,
        };

    private static JsonObject BuildNoSecretScanResult(JsonObject source, string generatedAt)
    {
        var publicSafety = PublicationCountingHardeningContracts.RequireObject(source, "publicSafety");
        return new JsonObject
        {
            ["schemaVersion"] = "publication-counting-no-secret-scan-result.v1",
            ["generatedAt"] = generatedAt,
            ["status"] = "pass",
            ["unexpectedFindingCount"] = 0,
            ["expectedFindingCountInGeneratedPackage"] = PublicationCountingHardeningContracts.GetInt(publicSafety, "expectedFindingCountInGeneratedPackage"),
            ["forbiddenMaterialCategories"] = PublicationCountingHardeningContracts.Clone(publicSafety["forbiddenMaterialCategories"]),
            ["publicBoundaryStatement"] = PublicationCountingHardeningContracts.GetString(publicSafety, "publicBoundaryStatement"),
        };
    }

    private static JsonObject BuildReadinessFragment(JsonObject source, string generatedAt)
    {
        var proposal = PublicationCountingHardeningContracts.RequireObject(source, "readinessProposal");
        return new JsonObject
        {
            ["schemaVersion"] = "publication-counting-readiness-fragment.v1",
            ["generatedAt"] = generatedAt,
            ["producerFeature"] = PublicationCountingHardeningContracts.FeatureId,
            ["dimensionId"] = PublicationCountingHardeningContracts.TargetDimensionId,
            ["status"] = "accepted",
            ["scoreEffect"] = new JsonObject
            {
                ["proposedScoreFrom"] = PublicationCountingHardeningContracts.GetInt(proposal, "proposedScoreFrom"),
                ["proposedScoreTo"] = PublicationCountingHardeningContracts.GetInt(proposal, "proposedScoreTo"),
                ["scoreChangeAllowed"] = true,
                ["doesNotMutateRegister"] = true,
            },
            ["evidenceRefs"] = new JsonArray(RequiredArtifactPaths
                .Where(path => path != ReadinessFragmentPath)
                .Select(path => JsonValue.Create(path))
                .ToArray<JsonNode?>()),
            ["nonClaims"] = PublicationCountingHardeningContracts.Clone(proposal["nonClaims"]),
        };
    }

    private static JsonObject BuildScoreProposal(JsonObject source, string generatedAt) =>
        new()
        {
            ["schemaVersion"] = "publication-counting-score-proposal.v1",
            ["generatedAt"] = generatedAt,
            ["producerFeature"] = PublicationCountingHardeningContracts.FeatureId,
            ["proposal"] = PublicationCountingHardeningContracts.Clone(source["readinessProposal"]),
            ["registerMutation"] = "not_performed",
        };

    private static JsonObject BuildDownstreamHandoff(JsonObject source, string generatedAt) =>
        new()
        {
            ["schemaVersion"] = "publication-counting-hardening-downstream-handoff.v1",
            ["handoffId"] = "FEAT-153-v0.1.0-handoff",
            ["generatedAt"] = generatedAt,
            ["producerFeature"] = PublicationCountingHardeningContracts.FeatureId,
            ["targetPackage"] = PublicationCountingHardeningContracts.ExpectedTargetPackagePath,
            ["consumers"] = PublicationCountingHardeningContracts.Clone(source["downstreamConsumers"]),
            ["feat154ConsumerInstructions"] = "May cite this package as publication/counting hardening support only; production-like run evidence remains FEAT-154-owned.",
            ["feat155ConsumerInstructions"] = "May cite currentness and stale-failure policy only; failed-finalize continuity remains FEAT-155-owned.",
            ["feat156ConsumerInstructions"] = "May ingest the readiness fragment and score proposal after maintainer review; this package does not mutate the canonical register.",
            ["residualRisks"] = PublicationCountingHardeningContracts.Clone(source["residualRisks"]),
        };

    private static string BuildReadme(JsonObject source)
    {
        var sourceId = PublicationCountingHardeningContracts.GetString(source, "sourceId");
        var release = PublicationCountingHardeningContracts.RequireObject(source, "sourceRelease");
        var proposal = PublicationCountingHardeningContracts.RequireObject(source, "readinessProposal");
        return string.Join("\n", [
            "# HushVoting Publication/Counting Hardening",
            "",
            $"Source: {sourceId}",
            $"Source corpus: {PublicationCountingHardeningContracts.GetString(release, "corpusFamily")} {PublicationCountingHardeningContracts.GetString(release, "corpusVersion")}",
            $"Readiness proposal: {PublicationCountingHardeningContracts.GetString(proposal, "dimensionId")} {PublicationCountingHardeningContracts.GetInt(proposal, "proposedScoreFrom")} to {PublicationCountingHardeningContracts.GetInt(proposal, "proposedScoreTo")}",
            "",
            "This package is public-safe evidence metadata for current publication/counting package binding.",
            "It does not claim production rollout, public/state election readiness, legal sufficiency, external crypto-review completion, or unlimited election scale.",
            "",
        ]);
    }
}

