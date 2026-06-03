using System.Text;
using System.Text.Json.Nodes;

namespace DisputeContinuityMatrixPromoter;

public sealed record DisputeContinuityMatrixArtifact(string RelativePath, string Content)
{
    public string Sha256Hash => DisputeContinuityMatrixContracts.Sha256Hex(Content);
    public int SizeBytes => Encoding.UTF8.GetByteCount(Content);
}

public sealed record DisputeContinuityMatrixGeneratedPackage(
    string Status,
    string PackageRoot,
    string RestrictedIndexRoot,
    JsonObject Source,
    IReadOnlyList<DisputeContinuityMatrixArtifact> Artifacts,
    IReadOnlyList<DisputeContinuityMatrixArtifact> RestrictedArtifacts)
{
    public int ArtifactCount => Artifacts.Count + RestrictedArtifacts.Count;
}

public static class DisputeContinuityMatrixArtifactGenerator
{
    public const string ReadmePath = "README.md";
    public const string ManifestPath = "dispute-continuity-matrix-manifest.json";
    public const string PackageIndexPath = "dispute-continuity-matrix-package.json";
    public const string SourceSchemaPath = "schemas/dispute-continuity-matrix-source.schema.json";
    public const string PackageManifestSchemaPath = "schemas/dispute-continuity-matrix-package-manifest.schema.json";
    public const string ScenarioCatalogPath = "scenarios/dispute-continuity-scenario-catalog.json";
    public const string ResultCodesPath = "scenarios/verifier-challenge-result-codes.json";
    public const string ReadinessBaselineCurrentnessSummaryPath = "validation/readiness-baseline-currentness-summary.json";
    public const string UpstreamCurrentnessSummaryPath = "validation/upstream-evidence-currentness-summary.json";
    public const string ScenarioCoverageSummaryPath = "validation/scenario-coverage-summary.json";
    public const string ReplacementPublicationSummaryPath = "validation/replacement-publication-summary.json";
    public const string VerifierChallengeSummaryPath = "validation/verifier-challenge-summary.json";
    public const string CustomerRemedyBoundarySummaryPath = "validation/customer-remedy-boundary-summary.json";
    public const string PublicOnlyValidationSummaryPath = "validation/public-only-validation-summary.json";
    public const string NoSecretScanResultPath = "validation/no-secret-scan-result.json";
    public const string ReadinessFragmentPath = "readiness/dispute-continuity-matrix-readiness-fragment.json";
    public const string ScoreProposalPath = "readiness/dispute-continuity-matrix-score-proposal.json";
    public const string DownstreamHandoffPath = "handoff/dispute-continuity-matrix-downstream-handoff.json";
    public const string ClaimBoundaryReviewPath = "handoff/claim-boundary-review.json";
    public const string RestrictedEvidenceIndexNotePath = "restricted/restricted-evidence-index.schema-note.md";
    public const string PrivateRestrictedEvidenceIndexPath = "restricted-evidence-index.json";

    public static readonly string[] RequiredArtifactPaths =
    [
        ReadmePath,
        ManifestPath,
        PackageIndexPath,
        SourceSchemaPath,
        PackageManifestSchemaPath,
        ScenarioCatalogPath,
        ResultCodesPath,
        ReadinessBaselineCurrentnessSummaryPath,
        UpstreamCurrentnessSummaryPath,
        ScenarioCoverageSummaryPath,
        ReplacementPublicationSummaryPath,
        VerifierChallengeSummaryPath,
        CustomerRemedyBoundarySummaryPath,
        PublicOnlyValidationSummaryPath,
        NoSecretScanResultPath,
        ReadinessFragmentPath,
        ScoreProposalPath,
        DownstreamHandoffPath,
        ClaimBoundaryReviewPath,
        RestrictedEvidenceIndexNotePath,
    ];

    public static readonly string[] RequiredRestrictedArtifactPaths =
    [
        PrivateRestrictedEvidenceIndexPath,
    ];

    private static readonly string[] PublicArtifactForbiddenNeedles =
    [
        "PrivateServer_ElectronicVoting",
        "hush-documents/",
        @"C:\",
        "/Users/",
        "/home/",
        "BEGIN PRIVATE KEY",
        "aws_access_key_id",
        "aws_secret_access_key",
        "credential=",
        "password=",
        "client_secret",
        "private_key",
        "rawDisputeBody",
        "disputePayload",
        "anomalyThread",
        "challengeThread",
        "voterMaterial",
        "trusteeMaterial",
        "trusteeShare",
    ];

    public static DisputeContinuityMatrixGeneratedPackage Generate(
        DisputeContinuityMatrixPromotionPaths paths,
        string? sourceInput = null,
        string? outputRoot = null,
        DateTimeOffset? generatedAt = null,
        bool publicOnly = false)
    {
        var source = DisputeContinuityMatrixContracts.ValidateForPromotion(paths, sourceInput, publicOnly);
        var generatedAtText = generatedAt?.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ??
            "2026-06-03T12:00:00.0000000Z";
        var packageRoot = ResolvePackageRoot(paths, outputRoot);
        var restrictedIndexRoot = ResolveRestrictedIndexRoot(paths);
        var publicArtifacts = new List<DisputeContinuityMatrixArtifact>
        {
            new(ReadmePath, BuildReadme()),
            JsonArtifact(PackageIndexPath, BuildPackageIndex(source, generatedAtText, publicOnly)),
            JsonFileArtifact(SourceSchemaPath, Path.Combine(paths.SchemasRoot, DisputeContinuityMatrixPromotionPaths.SourceSchemaFileName), "source schema"),
            JsonFileArtifact(PackageManifestSchemaPath, Path.Combine(paths.SchemasRoot, DisputeContinuityMatrixPromotionPaths.PackageManifestSchemaFileName), "package manifest schema"),
            JsonFileArtifact(ScenarioCatalogPath, Path.Combine(paths.ScenariosRoot, DisputeContinuityMatrixPromotionPaths.ScenarioCatalogFileName), "scenario catalog"),
            JsonFileArtifact(ResultCodesPath, Path.Combine(paths.ScenariosRoot, DisputeContinuityMatrixPromotionPaths.ResultCodesFileName), "result-code catalog"),
            JsonArtifact(ReadinessBaselineCurrentnessSummaryPath, BuildReadinessBaselineCurrentnessSummary(source, generatedAtText, publicOnly)),
            JsonArtifact(UpstreamCurrentnessSummaryPath, BuildUpstreamCurrentnessSummary(source, generatedAtText, publicOnly)),
            JsonArtifact(ScenarioCoverageSummaryPath, BuildScenarioCoverageSummary(source, generatedAtText, publicOnly)),
            JsonArtifact(ReplacementPublicationSummaryPath, BuildReplacementPublicationSummary(source, generatedAtText, publicOnly)),
            JsonArtifact(VerifierChallengeSummaryPath, BuildVerifierChallengeSummary(paths, source, generatedAtText, publicOnly)),
            JsonArtifact(CustomerRemedyBoundarySummaryPath, BuildCustomerRemedyBoundarySummary(source, generatedAtText, publicOnly)),
            JsonArtifact(PublicOnlyValidationSummaryPath, BuildPublicOnlyValidationSummary(source, generatedAtText, publicOnly)),
            JsonArtifact(ReadinessFragmentPath, BuildReadinessFragment(source, generatedAtText)),
            JsonArtifact(ScoreProposalPath, BuildScoreProposal(source, generatedAtText)),
            JsonArtifact(DownstreamHandoffPath, BuildDownstreamHandoff(source, generatedAtText)),
            JsonArtifact(ClaimBoundaryReviewPath, BuildClaimBoundaryReview(generatedAtText)),
            new(RestrictedEvidenceIndexNotePath, BuildRestrictedEvidenceIndexNote()),
        };
        var scanFindings = ScanPublicArtifacts(publicArtifacts);
        if (scanFindings.Count > 0)
        {
            throw new DisputeContinuityMatrixPromotionException(
                "FEAT-165 generated dispute continuity matrix package public-safety scan failed.",
                scanFindings);
        }

        publicArtifacts.Add(JsonArtifact(NoSecretScanResultPath, BuildNoSecretScanResult(generatedAtText, CountScannedPublicArtifacts(publicArtifacts))));
        publicArtifacts.Insert(1, JsonArtifact(ManifestPath, BuildManifest(source, generatedAtText, publicArtifacts)));
        var restrictedArtifacts = new List<DisputeContinuityMatrixArtifact>
        {
            JsonArtifact(PrivateRestrictedEvidenceIndexPath, BuildPrivateRestrictedEvidenceIndex(source, generatedAtText, publicArtifacts)),
        };

        return new DisputeContinuityMatrixGeneratedPackage("accepted", packageRoot, restrictedIndexRoot, source, publicArtifacts, restrictedArtifacts);
    }

    public static string ResolvePackageRoot(
        DisputeContinuityMatrixPromotionPaths paths,
        string? outputRoot = null)
    {
        var root = Path.GetFullPath(outputRoot ?? paths.PackagesRoot);
        DisputeContinuityMatrixContracts.EnsurePathUnder(paths.WorkspaceRoot, root, "FEAT-165 package output root");
        var packageRoot = Path.GetFullPath(Path.Combine(
            root,
            DisputeContinuityMatrixPromotionPaths.PackageFamilyFolder,
            DisputeContinuityMatrixPromotionPaths.DefaultMatrixRunId));
        DisputeContinuityMatrixContracts.EnsurePathUnder(root, packageRoot, "FEAT-165 package root");
        return packageRoot;
    }

    public static string ResolveRestrictedIndexRoot(DisputeContinuityMatrixPromotionPaths paths)
    {
        var root = Path.GetFullPath(paths.RestrictedEvidenceIndexRoot);
        DisputeContinuityMatrixContracts.EnsurePathUnder(paths.WorkspaceRoot, root, "FEAT-165 restricted evidence index root");
        return root;
    }

    private static DisputeContinuityMatrixArtifact JsonArtifact(string relativePath, JsonObject content) =>
        new(relativePath, DisputeContinuityMatrixContracts.CanonicalJson(content));

    private static DisputeContinuityMatrixArtifact JsonFileArtifact(string relativePath, string sourcePath, string description) =>
        JsonArtifact(relativePath, DisputeContinuityMatrixContracts.ReadJsonObject(sourcePath, description));

    private static string BuildReadme() =>
        """
        # Dispute Continuity Matrix Package

        This package is the public-safe FEAT-165 dispute continuity matrix validation output.

        It proves technical continuity checks for void, failed-finalize, finalized-with-anomaly,
        replacement-publication, verifier challenge, customer remedy boundary, and matrix currentness
        scenarios. It does not publish customer legal details, dispute bodies, anomaly or challenge
        payloads, voter material, trustee material, credentials, or operational secrets.

        Non-claims:

        - This package does not decide customer legal remedy sufficiency.
        - This package does not manage an AGM or customer governance process.
        - This package does not certify public or state election readiness.
        - This package does not approve production rollout.
        - This package does not mutate the canonical readiness register.

        The private restricted index is generated separately as refs and hashes only. Payload bodies
        are not copied into this public package.
        """;

    private static JsonObject BuildPackageIndex(JsonObject source, string generatedAt, bool publicOnly)
    {
        var restrictedPolicy = DisputeContinuityMatrixContracts.RequireObject(source, "restrictedEvidencePolicy");
        return new JsonObject
        {
            ["schemaVersion"] = "dispute-continuity-matrix-package/v1",
            ["featureId"] = DisputeContinuityMatrixContracts.FeatureId,
            ["packageId"] = DisputeContinuityMatrixPromotionPaths.DefaultMatrixRunId,
            ["status"] = "accepted",
            ["generatedAt"] = generatedAt,
            ["publicOnlyValidation"] = publicOnly,
            ["source"] = new JsonObject
            {
                ["repository"] = "https://github.com/Hushnetwork-social/Dispute-Continuity-Matrix",
                ["branch"] = "main",
                ["sourcePath"] = "examples/release-baseline/dispute-continuity-matrix-source.json",
                ["sourceSha256"] = DisputeContinuityMatrixContracts.Sha256Hex(DisputeContinuityMatrixContracts.CanonicalJson(source)),
            },
            ["manifestRef"] = ManifestPath,
            ["validationRefs"] = DisputeContinuityMatrixContracts.ToStringArray([
                ReadinessBaselineCurrentnessSummaryPath,
                UpstreamCurrentnessSummaryPath,
                ScenarioCoverageSummaryPath,
                ReplacementPublicationSummaryPath,
                VerifierChallengeSummaryPath,
                CustomerRemedyBoundarySummaryPath,
                PublicOnlyValidationSummaryPath,
                NoSecretScanResultPath,
            ]),
            ["readinessRefs"] = DisputeContinuityMatrixContracts.ToStringArray([
                ReadinessFragmentPath,
                ScoreProposalPath,
            ]),
            ["handoffRefs"] = DisputeContinuityMatrixContracts.ToStringArray([
                DownstreamHandoffPath,
                ClaimBoundaryReviewPath,
            ]),
            ["restrictedEvidenceRefs"] = BuildRestrictedEvidenceRefs(restrictedPolicy),
            ["proposalOnly"] = new JsonObject
            {
                ["dimension"] = DisputeContinuityMatrixContracts.TargetDimensionId,
                ["movement"] = DisputeContinuityMatrixContracts.AllowedScoreMovement,
                ["directRegisterMutation"] = false,
            },
        };
    }

    private static JsonObject BuildManifest(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<DisputeContinuityMatrixArtifact> artifacts)
    {
        var restrictedPolicy = DisputeContinuityMatrixContracts.RequireObject(source, "restrictedEvidencePolicy");
        return new JsonObject
        {
            ["schemaVersion"] = "dispute-continuity-matrix-package-manifest/v1",
            ["featureId"] = DisputeContinuityMatrixContracts.FeatureId,
            ["packageId"] = DisputeContinuityMatrixPromotionPaths.DefaultMatrixRunId,
            ["generatedAt"] = generatedAt,
            ["sourceRef"] = new JsonObject
            {
                ["path"] = "examples/release-baseline/dispute-continuity-matrix-source.json",
                ["sha256"] = DisputeContinuityMatrixContracts.Sha256Hex(DisputeContinuityMatrixContracts.CanonicalJson(source)),
                ["visibility"] = "public",
                ["description"] = "Public release-baseline source fixture.",
            },
            ["artifactRefs"] = BuildArtifactRefs(artifacts),
            ["validationRefs"] = BuildArtifactRefs(artifacts.Where(artifact => artifact.RelativePath.StartsWith("validation/", StringComparison.Ordinal)).ToArray()),
            ["readinessProposal"] = new JsonObject
            {
                ["dimension"] = DisputeContinuityMatrixContracts.TargetDimensionId,
                ["movement"] = DisputeContinuityMatrixContracts.AllowedScoreMovement,
                ["directRegisterMutation"] = false,
                ["proposalRef"] = ArtifactRef(artifacts.First(artifact => artifact.RelativePath == ReadinessBaselineCurrentnessSummaryPath)),
            },
            ["restrictedEvidenceRefs"] = BuildRestrictedEvidenceRefs(restrictedPolicy),
            ["manifestSelfHashPolicy"] = "This manifest lists public child artifacts and excludes its own mutable hash.",
        };
    }

    private static JsonObject BuildReadinessBaselineCurrentnessSummary(JsonObject source, string generatedAt, bool publicOnly)
    {
        var baseline = DisputeContinuityMatrixContracts.RequireObject(source, "readinessBaseline");
        var proposal = DisputeContinuityMatrixContracts.RequireObject(source, "scoreProposal");
        return new JsonObject
        {
            ["schemaVersion"] = "dispute-continuity-matrix-readiness-baseline-currentness-summary/v1",
            ["featureId"] = DisputeContinuityMatrixContracts.FeatureId,
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["publicOnlyValidation"] = publicOnly,
            ["registerVersion"] = DisputeContinuityMatrixContracts.GetString(baseline, "registerVersion"),
            ["dimension"] = DisputeContinuityMatrixContracts.GetString(baseline, "dimension"),
            ["blocker"] = DisputeContinuityMatrixContracts.GetString(baseline, "blocker"),
            ["currentScore"] = baseline["currentScore"]!.GetValue<int>(),
            ["targetScore"] = baseline["targetScore"]!.GetValue<int>(),
            ["scoreMovement"] = DisputeContinuityMatrixContracts.GetString(proposal, "movement"),
            ["directRegisterMutation"] = proposal["directRegisterMutation"]!.GetValue<bool>(),
        };
    }

    private static JsonObject BuildUpstreamCurrentnessSummary(JsonObject source, string generatedAt, bool publicOnly)
    {
        var upstream = DisputeContinuityMatrixContracts.RequireArray(source, "upstreamEvidence");
        var summaries = new JsonArray();
        foreach (var item in upstream.OfType<JsonObject>().OrderBy(item => DisputeContinuityMatrixContracts.GetString(item, "featureId"), StringComparer.Ordinal))
        {
            summaries.Add(new JsonObject
            {
                ["featureId"] = DisputeContinuityMatrixContracts.GetString(item, "featureId"),
                ["role"] = DisputeContinuityMatrixContracts.GetString(item, "role"),
                ["status"] = DisputeContinuityMatrixContracts.GetString(item, "status"),
                ["freshness"] = DisputeContinuityMatrixContracts.GetString(item, "freshness"),
                ["evidenceRefCount"] = DisputeContinuityMatrixContracts.RequireArray(item, "evidenceRefs").Count,
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "dispute-continuity-matrix-upstream-currentness-summary/v1",
            ["featureId"] = DisputeContinuityMatrixContracts.FeatureId,
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["publicOnlyValidation"] = publicOnly,
            ["readinessBaseline"] = new JsonObject
            {
                ["registerVersion"] = DisputeContinuityMatrixContracts.CurrentRegisterVersionId,
                ["dimension"] = DisputeContinuityMatrixContracts.TargetDimensionId,
                ["blocker"] = DisputeContinuityMatrixContracts.TargetBlockerId,
                ["currentScore"] = 8,
                ["targetScore"] = 9,
            },
            ["scoreProposal"] = new JsonObject
            {
                ["movement"] = DisputeContinuityMatrixContracts.AllowedScoreMovement,
                ["proposalOnly"] = true,
                ["directRegisterMutation"] = false,
            },
            ["upstreamEvidence"] = summaries,
            ["staleEvidenceGates"] = new JsonArray(
                new JsonObject
                {
                    ["featureId"] = "FEAT-139",
                    ["status"] = "historical_or_blocked_only",
                    ["blockedWhen"] = "accepted_current_while_stale_after_feat146",
                    ["resultCode"] = "stale_feat139_accepted_as_current",
                }),
        };
    }

    private static JsonObject BuildScenarioCoverageSummary(JsonObject source, string generatedAt, bool publicOnly)
    {
        var families = DisputeContinuityMatrixContracts.RequireArray(source, "scenarioFamilies");
        var summaries = new JsonArray();
        foreach (var family in families.OfType<JsonObject>().OrderBy(family => DisputeContinuityMatrixContracts.GetString(family, "family"), StringComparer.Ordinal))
        {
            summaries.Add(new JsonObject
            {
                ["family"] = DisputeContinuityMatrixContracts.GetString(family, "family"),
                ["status"] = "accepted",
                ["required"] = true,
                ["expectedResultCodes"] = CopyStringArray(family, "expectedResultCodes"),
                ["failureResultCodes"] = CopyStringArray(family, "failureResultCodes"),
                ["evidenceRefCount"] = DisputeContinuityMatrixContracts.RequireArray(family, "evidenceRefs").Count,
                ["publicSafeSummaryHash"] = DisputeContinuityMatrixContracts.Sha256Hex(DisputeContinuityMatrixContracts.GetString(family, "publicSafeSummary")),
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "dispute-continuity-matrix-scenario-coverage-summary/v1",
            ["featureId"] = DisputeContinuityMatrixContracts.FeatureId,
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["publicOnlyValidation"] = publicOnly,
            ["requiredFamilies"] = DisputeContinuityMatrixContracts.ToStringArray(DisputeContinuityMatrixContracts.RequiredScenarioFamilies),
            ["coveredFamilyCount"] = summaries.Count,
            ["missingFamilies"] = new JsonArray(),
            ["families"] = summaries,
        };
    }

    private static JsonObject BuildReplacementPublicationSummary(JsonObject source, string generatedAt, bool publicOnly)
    {
        var family = FindScenarioFamily(source, "replacement-publication");
        return new JsonObject
        {
            ["schemaVersion"] = "dispute-continuity-matrix-replacement-publication-summary/v1",
            ["featureId"] = DisputeContinuityMatrixContracts.FeatureId,
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["publicOnlyValidation"] = publicOnly,
            ["voidReplacementRefsValidated"] = true,
            ["supersededPackageNotCurrent"] = HasCode(family, "expectedResultCodes", "superseded_package_not_current"),
            ["supersededPackageStillCurrentFailsClosed"] = HasCode(family, "failureResultCodes", "superseded_package_still_current"),
            ["replayBindingValid"] = HasCode(family, "expectedResultCodes", "replay_binding_valid"),
            ["replayMismatchFailsClosed"] = HasCode(family, "failureResultCodes", "replay_binding_mismatch"),
            ["evidenceRefCount"] = DisputeContinuityMatrixContracts.RequireArray(family, "evidenceRefs").Count,
        };
    }

    private static JsonObject BuildVerifierChallengeSummary(
        DisputeContinuityMatrixPromotionPaths paths,
        JsonObject source,
        string generatedAt,
        bool publicOnly)
    {
        var family = FindScenarioFamily(source, "verifier-challenge");
        var resultCodeSeverities = DisputeContinuityMatrixContracts.LoadResultCodeSeverities(paths);
        var severityCoverage = new JsonArray();
        foreach (var severity in DisputeContinuityMatrixContracts.RequiredResultCodeSeverities)
        {
            severityCoverage.Add(new JsonObject
            {
                ["severity"] = severity,
                ["present"] = resultCodeSeverities.Values.Contains(severity, StringComparer.Ordinal),
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "dispute-continuity-matrix-verifier-challenge-summary/v1",
            ["featureId"] = DisputeContinuityMatrixContracts.FeatureId,
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["publicOnlyValidation"] = publicOnly,
            ["resultCodeSeverityCoverage"] = severityCoverage,
            ["acceptedResultPresent"] = HasCode(family, "expectedResultCodes", "verifier_challenge_accepted"),
            ["limitedResultPresent"] = HasCode(family, "expectedResultCodes", "verifier_challenge_limited"),
            ["unknownResultFailsClosed"] = HasCode(family, "failureResultCodes", "verifier_challenge_result_unknown"),
            ["replayMismatchFailsClosed"] = HasCode(family, "failureResultCodes", "verifier_challenge_replay_mismatch"),
            ["packageBoundEvidenceRefCount"] = DisputeContinuityMatrixContracts.RequireArray(family, "evidenceRefs").Count,
        };
    }

    private static JsonObject BuildCustomerRemedyBoundarySummary(JsonObject source, string generatedAt, bool publicOnly)
    {
        var family = FindScenarioFamily(source, "customer-remedy-boundary");
        return new JsonObject
        {
            ["schemaVersion"] = "dispute-continuity-matrix-customer-remedy-boundary-summary/v1",
            ["featureId"] = DisputeContinuityMatrixContracts.FeatureId,
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["publicOnlyValidation"] = publicOnly,
            ["customerOwnedRemedyBoundaryPresent"] = HasCode(family, "expectedResultCodes", "customer_remedy_boundary_present"),
            ["legalSufficiencyNotClaimed"] = HasCode(family, "expectedResultCodes", "legal_sufficiency_not_claimed"),
            ["legalSufficiencyOverclaimFailsClosed"] = HasCode(family, "failureResultCodes", "legal_sufficiency_overclaim"),
            ["customerDecisionPayloadPublishedFailsClosed"] = HasCode(family, "failureResultCodes", "customer_decision_payload_published"),
            ["restrictedLegalContentRequired"] = false,
            ["publicSafeSummaryHash"] = DisputeContinuityMatrixContracts.Sha256Hex(DisputeContinuityMatrixContracts.GetString(family, "publicSafeSummary")),
        };
    }

    private static JsonObject BuildPublicOnlyValidationSummary(JsonObject source, string generatedAt, bool publicOnly)
    {
        var boundary = DisputeContinuityMatrixContracts.RequireObject(source, "publicBoundary");
        var policy = DisputeContinuityMatrixContracts.RequireObject(source, "restrictedEvidencePolicy");
        return new JsonObject
        {
            ["schemaVersion"] = "dispute-continuity-matrix-public-only-validation-summary/v1",
            ["featureId"] = DisputeContinuityMatrixContracts.FeatureId,
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["publicOnlyFlag"] = publicOnly,
            ["sourcePublicOnlyValidation"] = boundary["publicOnlyValidation"]!.GetValue<bool>(),
            ["privateCheckoutRequired"] = false,
            ["credentialsRequired"] = false,
            ["restrictedPayloadsRequired"] = false,
            ["restrictedPayloadsPublishedHere"] = policy["payloadsPublishedHere"]!.GetValue<bool>(),
            ["publicRefsOnly"] = policy["publicRefsOnly"]!.GetValue<bool>(),
        };
    }

    private static JsonObject BuildNoSecretScanResult(string generatedAt, int scannedArtifactCount) =>
        new()
        {
            ["schemaVersion"] = "dispute-continuity-matrix-no-secret-scan-result/v1",
            ["featureId"] = DisputeContinuityMatrixContracts.FeatureId,
            ["generatedAt"] = generatedAt,
            ["status"] = "pass",
            ["scannedPublicArtifactCount"] = scannedArtifactCount,
            ["unexpectedFindingCount"] = 0,
            ["forbiddenNeedleCount"] = PublicArtifactForbiddenNeedles.Length,
        };

    private static JsonObject BuildReadinessFragment(JsonObject source, string generatedAt)
    {
        var baseline = DisputeContinuityMatrixContracts.RequireObject(source, "readinessBaseline");
        return new JsonObject
        {
            ["schemaVersion"] = "dispute-continuity-matrix-readiness-fragment/v1",
            ["featureId"] = DisputeContinuityMatrixContracts.FeatureId,
            ["targetFeature"] = "FEAT-130",
            ["generatedAt"] = generatedAt,
            ["status"] = "proposal-only",
            ["registerVersion"] = DisputeContinuityMatrixContracts.GetString(baseline, "registerVersion"),
            ["dimension"] = DisputeContinuityMatrixContracts.TargetDimensionId,
            ["blocker"] = DisputeContinuityMatrixContracts.TargetBlockerId,
            ["currentScore"] = 8,
            ["proposedScore"] = 9,
            ["directRegisterMutation"] = false,
            ["acceptedScenarioPackageRefs"] = DisputeContinuityMatrixContracts.ToStringArray([
                ManifestPath,
                PackageIndexPath,
                ScenarioCoverageSummaryPath,
                ReplacementPublicationSummaryPath,
                VerifierChallengeSummaryPath,
                CustomerRemedyBoundarySummaryPath,
                NoSecretScanResultPath,
            ]),
            ["promotionInstruction"] = "Review through FEAT-130 promotion flow only; this fragment does not mutate the canonical readiness register.",
        };
    }

    private static JsonObject BuildScoreProposal(JsonObject source, string generatedAt)
    {
        var proposal = DisputeContinuityMatrixContracts.RequireObject(source, "scoreProposal");
        return new JsonObject
        {
            ["schemaVersion"] = "dispute-continuity-matrix-score-proposal/v1",
            ["featureId"] = DisputeContinuityMatrixContracts.FeatureId,
            ["targetFeature"] = "FEAT-130",
            ["generatedAt"] = generatedAt,
            ["status"] = "proposal-only",
            ["dimension"] = DisputeContinuityMatrixContracts.GetString(proposal, "dimension"),
            ["movement"] = DisputeContinuityMatrixContracts.GetString(proposal, "movement"),
            ["directRegisterMutation"] = false,
            ["blockedUnlessEveryRequiredScenarioGatePasses"] = true,
            ["notReplayedScoreMovements"] = DisputeContinuityMatrixContracts.ToStringArray([
                "FEAT-138 owner void 4 -> 6",
                "FEAT-155 failed-finalize continuity 6 -> 8",
            ]),
            ["forbiddenMovements"] = DisputeContinuityMatrixContracts.ToStringArray([
                "8 -> 10",
                "direct register mutation",
            ]),
            ["evidenceRefs"] = DisputeContinuityMatrixContracts.ToStringArray([
                ScenarioCoverageSummaryPath,
                ReplacementPublicationSummaryPath,
                VerifierChallengeSummaryPath,
                CustomerRemedyBoundarySummaryPath,
                PublicOnlyValidationSummaryPath,
                NoSecretScanResultPath,
            ]),
        };
    }

    private static JsonObject BuildDownstreamHandoff(JsonObject source, string generatedAt)
    {
        var policy = DisputeContinuityMatrixContracts.RequireObject(source, "restrictedEvidencePolicy");
        return new JsonObject
        {
            ["schemaVersion"] = "dispute-continuity-matrix-downstream-handoff/v1",
            ["featureId"] = DisputeContinuityMatrixContracts.FeatureId,
            ["targetFeature"] = "FEAT-166",
            ["promotionOwner"] = "FEAT-130",
            ["generatedAt"] = generatedAt,
            ["status"] = "ready-for-review",
            ["directRegisterMutation"] = false,
            ["publicPackageRefs"] = DisputeContinuityMatrixContracts.ToStringArray([
                ManifestPath,
                PackageIndexPath,
                ReadinessFragmentPath,
                ScoreProposalPath,
                ClaimBoundaryReviewPath,
            ]),
            ["restrictedNoPayloadRefs"] = BuildRestrictedEvidenceRefs(policy),
            ["resultCodeCatalogRef"] = ResultCodesPath,
            ["residualRisks"] = DisputeContinuityMatrixContracts.ToStringArray([
                "Customer remedy decisions remain customer-owned.",
                "Legal sufficiency is outside this technical proof.",
                "Public/state election readiness remains outside this package.",
                "Production rollout approval remains outside this package.",
            ]),
            ["handoffInstructions"] = new JsonObject
            {
                ["technicalProofValidity"] = "Use public package refs and hashes for FEAT-166 governance/customer handoff review.",
                ["customerRemedyBoundary"] = "Do not treat this package as customer legal remedy, AGM management, certification, external audit, public/state, or production rollout acceptance.",
                ["readinessPromotion"] = "Use FEAT-130 promotion flow only; no direct register mutation is authorized.",
            },
        };
    }

    private static JsonObject BuildClaimBoundaryReview(string generatedAt) =>
        new()
        {
            ["schemaVersion"] = "dispute-continuity-matrix-claim-boundary-review/v1",
            ["featureId"] = DisputeContinuityMatrixContracts.FeatureId,
            ["generatedAt"] = generatedAt,
            ["status"] = "pass",
            ["publicStateElectionReadinessClaimed"] = false,
            ["productionRolloutApprovalClaimed"] = false,
            ["legalSufficiencyClaimed"] = false,
            ["agmManagementClaimed"] = false,
            ["externalAuditAcceptanceClaimed"] = false,
            ["certificationClaimed"] = false,
            ["directRegisterMutationAuthorized"] = false,
        };

    private static string BuildRestrictedEvidenceIndexNote() =>
        """
        # Restricted Evidence Index Schema Note

        The private restricted evidence index is generated outside this public package.

        Public artifacts may reference only stable restricted ids, expected hashes, visibility, and
        payload publication flags. Customer legal details, dispute bodies, anomaly or challenge
        payloads, voter material, trustee material, credentials, and operational secrets are not
        copied into public outputs.
        """;

    private static JsonObject BuildPrivateRestrictedEvidenceIndex(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<DisputeContinuityMatrixArtifact> publicArtifacts)
    {
        var policy = DisputeContinuityMatrixContracts.RequireObject(source, "restrictedEvidencePolicy");
        return new JsonObject
        {
            ["schemaVersion"] = "dispute-continuity-matrix-restricted-evidence-index/v1",
            ["featureId"] = DisputeContinuityMatrixContracts.FeatureId,
            ["indexId"] = "FEAT165-RESTRICTED-EVIDENCE-INDEX-001",
            ["packageId"] = DisputeContinuityMatrixPromotionPaths.DefaultMatrixRunId,
            ["generatedAt"] = generatedAt,
            ["payloadPolicy"] = "Refs and hashes only. Payload bodies are not copied.",
            ["payloadsPublishedHere"] = false,
            ["publicManifestRef"] = ArtifactRef(publicArtifacts.First(artifact => artifact.RelativePath == ManifestPath)),
            ["restrictedRefs"] = BuildRestrictedEvidenceRefs(policy),
        };
    }

    private static JsonArray BuildArtifactRefs(IEnumerable<DisputeContinuityMatrixArtifact> artifacts)
    {
        var refs = new JsonArray();
        foreach (var artifact in artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal))
        {
            refs.Add(ArtifactRef(artifact));
        }

        return refs;
    }

    private static JsonObject ArtifactRef(DisputeContinuityMatrixArtifact artifact) =>
        new()
        {
            ["path"] = artifact.RelativePath,
            ["sha256"] = artifact.Sha256Hash,
            ["visibility"] = "public",
            ["description"] = artifact.RelativePath.EndsWith(".md", StringComparison.Ordinal)
                ? "Public-safe markdown artifact."
                : "Public-safe JSON artifact.",
        };

    private static JsonArray BuildRestrictedEvidenceRefs(JsonObject policy)
    {
        var refs = new JsonArray();
        if (!policy.TryGetPropertyValue("restrictedEvidenceRefs", out var node) || node is not JsonArray sourceRefs)
        {
            return refs;
        }

        foreach (var item in sourceRefs.OfType<JsonObject>().OrderBy(item => DisputeContinuityMatrixContracts.GetString(item, "id"), StringComparer.Ordinal))
        {
            refs.Add(new JsonObject
            {
                ["id"] = DisputeContinuityMatrixContracts.GetString(item, "id"),
                ["visibility"] = "restricted-ref-only",
                ["expectedHash"] = DisputeContinuityMatrixContracts.GetString(item, "expectedHash"),
                ["payloadPublished"] = false,
                ["note"] = DisputeContinuityMatrixContracts.GetString(item, "note"),
            });
        }

        return refs;
    }

    private static IReadOnlyList<string> ScanPublicArtifacts(IReadOnlyList<DisputeContinuityMatrixArtifact> artifacts)
    {
        var findings = new List<string>();
        foreach (var artifact in artifacts)
        {
            if (IsSchemaContractArtifact(artifact))
            {
                continue;
            }

            foreach (var needle in PublicArtifactForbiddenNeedles)
            {
                if (artifact.Content.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add($"Public artifact contains forbidden material marker: {artifact.RelativePath} -> {needle}");
                }
            }
        }

        return findings;
    }

    private static int CountScannedPublicArtifacts(IEnumerable<DisputeContinuityMatrixArtifact> artifacts) =>
        artifacts.Count(artifact => !IsSchemaContractArtifact(artifact));

    private static bool IsSchemaContractArtifact(DisputeContinuityMatrixArtifact artifact) =>
        artifact.RelativePath.StartsWith("schemas/", StringComparison.Ordinal);

    private static JsonObject FindScenarioFamily(JsonObject source, string familyName) =>
        DisputeContinuityMatrixContracts.RequireArray(source, "scenarioFamilies")
            .OfType<JsonObject>()
            .First(family => DisputeContinuityMatrixContracts.GetString(family, "family") == familyName);

    private static bool HasCode(JsonObject family, string propertyName, string code) =>
        DisputeContinuityMatrixContracts.RequireArray(family, propertyName)
            .Select(item => item?.GetValue<string>())
            .Contains(code, StringComparer.Ordinal);

    private static JsonArray CopyStringArray(JsonObject obj, string propertyName)
    {
        var copy = new JsonArray();
        foreach (var value in DisputeContinuityMatrixContracts.RequireArray(obj, propertyName).Select(item => item?.GetValue<string>()))
        {
            copy.Add(value);
        }

        return copy;
    }
}
