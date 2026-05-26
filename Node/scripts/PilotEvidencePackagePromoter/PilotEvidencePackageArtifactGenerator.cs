using System.Text;
using System.Text.Json.Nodes;

namespace PilotEvidencePackagePromoter;

public static class PilotEvidencePackageArtifactGenerator
{
    public const string PackagePath = "pilot-evidence-package.json";
    public const string PackageManifestPath = "pilot-evidence-package-manifest.json";
    public const string ReadinessFragmentPath = "pilot-evidence-readiness-fragment.json";
    public const string PublicSafeSummaryPath = "pilot-evidence-public-safe-summary.md";
    public const string RestrictedIndexPath = "pilot-evidence-restricted-index.json";
    public const string DownstreamHandoffPath = "pilot-evidence-downstream-handoff.json";
    public const string ExceptionRecordsPath = "pilot-evidence-exception-records.json";
    public const string PublicArtifactScanPath = "pilot-evidence-public-artifact-scan.json";
    public const string PackageHashValidationPath = "pilot-evidence-package-hash-validation.json";

    public static PilotEvidenceGeneratedPackage Generate(
        PilotEvidencePackagePromotionPaths paths,
        string? sourceInput = null,
        DateTimeOffset? generatedAt = null)
    {
        var source = PilotEvidencePackageContracts.LoadSource(paths, sourceInput);
        var validationErrors = PilotEvidencePackageContracts.ValidateSource(source);
        if (validationErrors.Count > 0)
        {
            throw new PilotEvidencePackagePromotionException(
                "FEAT-141 pilot evidence source validation failed.",
                validationErrors);
        }

        var effectiveGeneratedAt = generatedAt ?? DateTimeOffset.UtcNow;
        var initialStatus = ResolvePackageStatus(source, [], BuildDowngrades(source));
        var publicSummary = BuildPublicSafeSummary(source, initialStatus, effectiveGeneratedAt);
        var initialPublicFindings = PilotEvidencePackageContracts.ScanForbiddenPublicMaterial(
            source,
            [(PublicSafeSummaryPath, publicSummary)]);
        var blockers = BuildBlockers(source, initialPublicFindings);
        var downgrades = BuildDowngrades(source);
        var status = ResolvePackageStatus(source, blockers, downgrades);
        publicSummary = BuildPublicSafeSummary(source, status, effectiveGeneratedAt);
        var publicFindings = PilotEvidencePackageContracts.ScanForbiddenPublicMaterial(
            source,
            [(PublicSafeSummaryPath, publicSummary)]);
        blockers = BuildBlockers(source, publicFindings);
        status = ResolvePackageStatus(source, blockers, downgrades);

        var exceptionRecordsArtifact = JsonArtifact(
            ExceptionRecordsPath,
            BuildExceptionRecords(source, effectiveGeneratedAt));
        var restrictedIndexArtifact = JsonArtifact(
            RestrictedIndexPath,
            BuildRestrictedIndex(source, effectiveGeneratedAt));
        var publicSummaryArtifact = TextArtifact(PublicSafeSummaryPath, publicSummary);
        var publicScanArtifact = JsonArtifact(
            PublicArtifactScanPath,
            BuildPublicArtifactScan(publicFindings, blockers, effectiveGeneratedAt));
        var readinessArtifact = JsonArtifact(
            ReadinessFragmentPath,
            BuildReadinessFragment(
                source,
                status,
                blockers,
                downgrades,
                [exceptionRecordsArtifact, restrictedIndexArtifact, publicSummaryArtifact, publicScanArtifact],
                effectiveGeneratedAt));
        var packageArtifact = JsonArtifact(
            PackagePath,
            BuildPackage(
                source,
                status,
                blockers,
                downgrades,
                [exceptionRecordsArtifact, restrictedIndexArtifact, publicSummaryArtifact, publicScanArtifact, readinessArtifact],
                effectiveGeneratedAt));
        var downstreamHandoffArtifact = JsonArtifact(
            DownstreamHandoffPath,
            BuildDownstreamHandoff(
                source,
                status,
                blockers,
                downgrades,
                packageArtifact,
                readinessArtifact,
                publicSummaryArtifact,
                restrictedIndexArtifact,
                exceptionRecordsArtifact,
                publicScanArtifact,
                effectiveGeneratedAt));
        var manifestArtifact = JsonArtifact(
            PackageManifestPath,
            BuildManifest(
                source,
                status,
                [
                    downstreamHandoffArtifact,
                    exceptionRecordsArtifact,
                    packageArtifact,
                    publicScanArtifact,
                    publicSummaryArtifact,
                    readinessArtifact,
                    restrictedIndexArtifact,
                ],
                effectiveGeneratedAt));
        var hashValidationArtifact = JsonArtifact(
            PackageHashValidationPath,
            BuildPackageHashValidation(
                source,
                [
                    downstreamHandoffArtifact,
                    exceptionRecordsArtifact,
                    manifestArtifact,
                    packageArtifact,
                    publicScanArtifact,
                    publicSummaryArtifact,
                    readinessArtifact,
                    restrictedIndexArtifact,
                ],
                publicFindings,
                blockers,
                effectiveGeneratedAt));

        var artifacts = new[]
        {
            downstreamHandoffArtifact,
            exceptionRecordsArtifact,
            hashValidationArtifact,
            manifestArtifact,
            packageArtifact,
            publicScanArtifact,
            publicSummaryArtifact,
            readinessArtifact,
            restrictedIndexArtifact,
        }
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();

        return new PilotEvidenceGeneratedPackage(
            status,
            artifacts,
            publicFindings,
            blockers,
            downgrades);
    }

    private static JsonObject BuildPackage(
        JsonObject source,
        string status,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> downgrades,
        IReadOnlyCollection<PilotEvidenceGeneratedArtifact> artifactRefs,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "pilot-evidence-package.v1",
            ["packageId"] = PilotEvidencePackageContracts.GetString(source, "packageId"),
            ["featureSlice"] = PilotEvidencePackageContracts.FeatureId,
            ["acceptanceGate"] = PilotEvidencePackageContracts.AcceptanceGate,
            ["status"] = status,
            ["generatedAt"] = PilotEvidencePackageContracts.FormatTimestamp(generatedAt),
            ["identity"] = new JsonObject
            {
                ["sourceId"] = PilotEvidencePackageContracts.GetString(source, "sourceId"),
                ["generator"] = "PilotEvidencePackagePromoter",
                ["generatorVersion"] = "feat141.v1",
                ["canonicalizationVersion"] = PilotEvidencePackageContracts.CanonicalizationVersion,
            },
            ["profile"] = new JsonObject
            {
                ["profile"] = PilotEvidencePackageContracts.GetString(source, "profile"),
                ["scenario"] = PilotEvidencePackageContracts.Clone(source["scenario"]),
                ["organizationScope"] = PilotEvidencePackageContracts.Clone(source["organizationScope"]),
                ["participantModel"] = PilotEvidencePackageContracts.Clone(source["participantModel"]),
                ["timeline"] = PilotEvidencePackageContracts.Clone(source["timeline"]),
                ["rehearsalDecision"] = PilotEvidencePackageContracts.Clone(source["rehearsalDecision"]),
            },
            ["registerBinding"] = BuildRegisterBinding(source),
            ["upstreamEvidence"] = BuildUpstreamEvidence(source),
            ["observedRunEvidence"] = PilotEvidencePackageContracts.Clone(source["observedRunEvidence"]),
            ["runtimeProofEvidence"] = PilotEvidencePackageContracts.Clone(source["runtimeEvidence"]),
            ["claimDecisions"] = BuildClaimDecisions(source),
            ["exceptions"] = PilotEvidencePackageContracts.Clone(source["exceptions"]) ?? new JsonArray(),
            ["publicSafety"] = new JsonObject
            {
                ["redactionPolicy"] = PilotEvidencePackageContracts.Clone(source["redactionPolicy"]),
                ["publicScanRef"] = ArtifactRef(artifactRefs.Single(artifact => artifact.RelativePath == PublicArtifactScanPath)),
                ["publicSummaryRef"] = ArtifactRef(artifactRefs.Single(artifact => artifact.RelativePath == PublicSafeSummaryPath)),
                ["restrictedPayloadsExcluded"] = true,
            },
            ["signoff"] = PilotEvidencePackageContracts.Clone(source["signoff"]),
            ["blockers"] = PilotEvidencePackageContracts.ToJsonArray(blockers),
            ["unresolvedClaimBlockers"] = PilotEvidencePackageContracts.ToJsonArray(BuildClaimBlockers(source)),
            ["downgrades"] = PilotEvidencePackageContracts.ToJsonArray(downgrades),
            ["artifactRefs"] = new JsonArray(artifactRefs
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .Select(ArtifactRef)
                .ToArray<JsonNode?>()),
        };

    private static JsonObject BuildRegisterBinding(JsonObject source)
    {
        var before = PilotEvidencePackageContracts.RequireObject(source, "beforeRegister");
        var after = PilotEvidencePackageContracts.RequireObject(source, "afterRegister");
        return new JsonObject
        {
            ["beforeRegister"] = PilotEvidencePackageContracts.Clone(before),
            ["afterRegister"] = PilotEvidencePackageContracts.Clone(after),
            ["scoreDelta"] = PilotEvidencePackageContracts.GetInt(after, "totalScore") -
                PilotEvidencePackageContracts.GetInt(before, "totalScore"),
            ["directRegisterMutation"] = false,
            ["registerPromotionOwner"] = "FEAT-130",
            ["promotionPolicy"] = PilotEvidencePackageContracts.Clone(source["promotionPolicy"]),
        };
    }

    private static JsonArray BuildUpstreamEvidence(JsonObject source) =>
        new(PilotEvidencePackageContracts
            .RequireArray(source, "upstreamEvidence")
            .OfType<JsonObject>()
            .OrderBy(evidence => PilotEvidencePackageContracts.GetString(evidence, "featureSlice"), StringComparer.Ordinal)
            .Select(evidence => evidence.DeepClone())
            .ToArray<JsonNode?>());

    private static JsonArray BuildClaimDecisions(JsonObject source) =>
        new(PilotEvidencePackageContracts
            .RequireArray(source, "claimDecisions")
            .OfType<JsonObject>()
            .OrderBy(claim => PilotEvidencePackageContracts.GetString(claim, "claimId"), StringComparer.Ordinal)
            .Select(claim => claim.DeepClone())
            .ToArray<JsonNode?>());

    private static JsonObject BuildReadinessFragment(
        JsonObject source,
        string status,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> downgrades,
        IReadOnlyCollection<PilotEvidenceGeneratedArtifact> artifactRefs,
        DateTimeOffset generatedAt)
    {
        var policy = PilotEvidencePackageContracts.RequireObject(source, "promotionPolicy");
        return new JsonObject
        {
            ["schemaVersion"] = "pilot-evidence-readiness-fragment.v1",
            ["fragmentId"] = PilotEvidencePackageContracts.ReadinessFragmentId,
            ["featureSlice"] = PilotEvidencePackageContracts.FeatureId,
            ["sourceGap"] = PilotEvidencePackageContracts.SourceGap,
            ["acceptanceGate"] = PilotEvidencePackageContracts.AcceptanceGate,
            ["dimensionId"] = PilotEvidencePackageContracts.DimensionId,
            ["status"] = status,
            ["generatedAt"] = PilotEvidencePackageContracts.FormatTimestamp(generatedAt),
            ["directRegisterMutation"] = false,
            ["doesNotMutateRegister"] = true,
            ["registerPromotionOwner"] = "FEAT-130",
            ["scoreEffect"] = new JsonObject
            {
                ["currentTotalScore"] = PilotEvidencePackageContracts.GetInt(policy, "currentTotalScore"),
                ["candidateTotalScoreWhenAccepted"] = PilotEvidencePackageContracts.GetInt(policy, "candidateTotalScoreWhenAccepted"),
                ["appliedTotalScore"] = status == "blocked"
                    ? PilotEvidencePackageContracts.GetInt(policy, "currentTotalScore")
                    : PilotEvidencePackageContracts.GetInt(policy, "candidateTotalScoreWhenAccepted"),
                ["scoreIncreaseRequiresFeat130Promotion"] = PilotEvidencePackageContracts.GetBool(policy, "scoreIncreaseRequiresFeat130Promotion"),
            },
            ["blockers"] = PilotEvidencePackageContracts.ToJsonArray(blockers),
            ["unresolvedClaimBlockers"] = PilotEvidencePackageContracts.ToJsonArray(BuildClaimBlockers(source)),
            ["downgrades"] = PilotEvidencePackageContracts.ToJsonArray(downgrades),
            ["claimEffect"] = BuildClaimStateSummary(source),
            ["artifactRefs"] = new JsonArray(artifactRefs
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .Select(ArtifactRef)
                .ToArray<JsonNode?>()),
            ["promotionInstructions"] = "FEAT-130 may ingest this fragment later. FEAT-141 does not mutate the canonical readiness register directly.",
        };
    }

    private static JsonObject BuildRestrictedIndex(JsonObject source, DateTimeOffset generatedAt)
    {
        var observed = PilotEvidencePackageContracts.RequireObject(source, "observedRunEvidence");
        var restrictedRefs = new List<JsonObject>();
        AddObservedRef("exportPackage");
        AddObservedRef("verifierOutput");
        AddObservedRef("supportOrNoIncidentStatement");
        AddObservedRef("acceptanceNotes");
        AddObservedRef("postmortem");

        foreach (var evidence in PilotEvidencePackageContracts.RequireArray(source, "upstreamEvidence").OfType<JsonObject>())
        {
            restrictedRefs.Add(new JsonObject
            {
                ["refId"] = $"{PilotEvidencePackageContracts.GetString(evidence, "featureSlice")}-restricted-ref",
                ["featureSlice"] = PilotEvidencePackageContracts.GetString(evidence, "featureSlice"),
                ["restrictedRef"] = PilotEvidencePackageContracts.GetString(evidence, "restrictedRef"),
                ["sha256Hash"] = PilotEvidencePackageContracts.GetString(evidence, "sha256Hash"),
                ["payloadCopied"] = false,
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "pilot-evidence-restricted-index.v1",
            ["indexId"] = "PILOT-EVIDENCE-RESTRICTED-INDEX-FEAT-141-001",
            ["packageId"] = PilotEvidencePackageContracts.GetString(source, "packageId"),
            ["generatedAt"] = PilotEvidencePackageContracts.FormatTimestamp(generatedAt),
            ["sourceId"] = PilotEvidencePackageContracts.GetString(source, "sourceId"),
            ["restrictedRefs"] = new JsonArray(restrictedRefs
                .OrderBy(item => PilotEvidencePackageContracts.GetString(item, "refId"), StringComparer.Ordinal)
                .ToArray<JsonNode?>()),
            ["redactionPolicy"] = PilotEvidencePackageContracts.Clone(source["redactionPolicy"]),
            ["payloadPolicy"] = "Restricted index stores refs and hashes only. Private payload bodies are not copied.",
        };

        void AddObservedRef(string property)
        {
            if (observed.TryGetPropertyValue(property, out var node) && node is JsonObject evidence)
            {
                restrictedRefs.Add(new JsonObject
                {
                    ["refId"] = PilotEvidencePackageContracts.GetString(evidence, "evidenceId", property),
                    ["featureSlice"] = PilotEvidencePackageContracts.FeatureId,
                    ["restrictedRef"] = PilotEvidencePackageContracts.GetString(evidence, "restrictedRef"),
                    ["sha256Hash"] = PilotEvidencePackageContracts.GetString(evidence, "sha256Hash"),
                    ["payloadCopied"] = false,
                });
            }
        }
    }

    private static JsonObject BuildExceptionRecords(JsonObject source, DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "pilot-evidence-exception-records.v1",
            ["packageId"] = PilotEvidencePackageContracts.GetString(source, "packageId"),
            ["generatedAt"] = PilotEvidencePackageContracts.FormatTimestamp(generatedAt),
            ["exceptions"] = new JsonArray(PilotEvidencePackageContracts
                .RequireArray(source, "exceptions")
                .OfType<JsonObject>()
                .OrderBy(exception => PilotEvidencePackageContracts.GetString(exception, "exceptionId"), StringComparer.Ordinal)
                .Select(exception => exception.DeepClone())
                .ToArray<JsonNode?>()),
        };

    private static JsonObject BuildPublicArtifactScan(
        IReadOnlyList<PilotEvidenceMaterialFinding> findings,
        IReadOnlyList<string> blockers,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "pilot-evidence-public-artifact-scan.v1",
            ["scanId"] = "PILOT-EVIDENCE-PUBLIC-SCAN-FEAT-141-001",
            ["status"] = findings.Count == 0 ? "passed" : "blocked",
            ["generatedAt"] = PilotEvidencePackageContracts.FormatTimestamp(generatedAt),
            ["blockers"] = PilotEvidencePackageContracts.ToJsonArray(blockers),
            ["forbiddenFindings"] = new JsonArray(findings
                .Select(finding => new JsonObject
                {
                    ["path"] = finding.RelativePath,
                    ["category"] = finding.Category,
                    ["evidence"] = finding.Evidence,
                })
                .ToArray<JsonNode?>()),
        };

    private static JsonObject BuildDownstreamHandoff(
        JsonObject source,
        string status,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> downgrades,
        PilotEvidenceGeneratedArtifact packageArtifact,
        PilotEvidenceGeneratedArtifact readinessArtifact,
        PilotEvidenceGeneratedArtifact publicSummaryArtifact,
        PilotEvidenceGeneratedArtifact restrictedIndexArtifact,
        PilotEvidenceGeneratedArtifact exceptionRecordsArtifact,
        PilotEvidenceGeneratedArtifact publicScanArtifact,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "pilot-evidence-downstream-handoff.v1",
            ["handoffId"] = "PILOT-EVIDENCE-DOWNSTREAM-HANDOFF-FEAT-141-001",
            ["producerFeature"] = PilotEvidencePackageContracts.FeatureId,
            ["status"] = status,
            ["generatedAt"] = PilotEvidencePackageContracts.FormatTimestamp(generatedAt),
            ["packageRef"] = ArtifactRef(packageArtifact),
            ["readinessFragmentRef"] = ArtifactRef(readinessArtifact),
            ["publicSummaryRef"] = ArtifactRef(publicSummaryArtifact),
            ["restrictedIndexRef"] = ArtifactRef(restrictedIndexArtifact),
            ["exceptionRecordsRef"] = ArtifactRef(exceptionRecordsArtifact),
            ["publicArtifactScanRef"] = ArtifactRef(publicScanArtifact),
            ["beforeRegister"] = PilotEvidencePackageContracts.Clone(source["beforeRegister"]),
            ["afterRegister"] = PilotEvidencePackageContracts.Clone(source["afterRegister"]),
            ["claimStateSummary"] = BuildClaimStateSummary(source),
            ["blockers"] = PilotEvidencePackageContracts.ToJsonArray(blockers),
            ["unresolvedClaimBlockers"] = PilotEvidencePackageContracts.ToJsonArray(BuildClaimBlockers(source)),
            ["downgrades"] = PilotEvidencePackageContracts.ToJsonArray(downgrades),
            ["promotionInstructions"] = new JsonObject
            {
                ["targetFeature"] = "FEAT-130",
                ["acceptanceGate"] = PilotEvidencePackageContracts.AcceptanceGate,
                ["dimensionId"] = PilotEvidencePackageContracts.DimensionId,
                ["directRegisterMutation"] = false,
                ["instruction"] = "Review and promote through FEAT-130 only; FEAT-141 does not mutate the canonical readiness register.",
            },
            ["publicSafeWordingKeys"] = new JsonArray(PilotEvidencePackageContracts
                .RequireArray(source, "claimDecisions")
                .OfType<JsonObject>()
                .Select(claim => JsonValue.Create(PilotEvidencePackageContracts.GetString(claim, "wordingKey")))
                .ToArray<JsonNode?>()),
        };

    private static JsonObject BuildManifest(
        JsonObject source,
        string status,
        IReadOnlyCollection<PilotEvidenceGeneratedArtifact> artifacts,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "pilot-evidence-package-manifest.v1",
            ["manifestId"] = "PILOT-EVIDENCE-MANIFEST-FEAT-141-001",
            ["packageId"] = PilotEvidencePackageContracts.GetString(source, "packageId"),
            ["status"] = status,
            ["generatedAt"] = PilotEvidencePackageContracts.FormatTimestamp(generatedAt),
            ["canonicalizationVersion"] = PilotEvidencePackageContracts.CanonicalizationVersion,
            ["artifactHashes"] = new JsonArray(artifacts
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .Select(artifact => new JsonObject
                {
                    ["path"] = artifact.RelativePath,
                    ["sha256Hash"] = artifact.Sha256Hash,
                    ["sizeBytes"] = Encoding.UTF8.GetByteCount(artifact.Content),
                    ["mediaType"] = artifact.RelativePath.EndsWith(".md", StringComparison.Ordinal)
                        ? "text/markdown"
                        : "application/json",
                })
                .ToArray<JsonNode?>()),
        };

    private static JsonObject BuildPackageHashValidation(
        JsonObject source,
        IReadOnlyCollection<PilotEvidenceGeneratedArtifact> artifacts,
        IReadOnlyList<PilotEvidenceMaterialFinding> publicFindings,
        IReadOnlyList<string> blockers,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "pilot-evidence-package-hash-validation.v1",
            ["validationId"] = "PILOT-EVIDENCE-HASH-FEAT-141-001",
            ["status"] = blockers.Count == 0 ? "passed" : "blocked",
            ["generatedAt"] = PilotEvidencePackageContracts.FormatTimestamp(generatedAt),
            ["canonicalizationVersion"] = PilotEvidencePackageContracts.CanonicalizationVersion,
            ["sourceId"] = PilotEvidencePackageContracts.GetString(source, "sourceId"),
            ["publicForbiddenFindings"] = new JsonArray(publicFindings
                .Select(finding => new JsonObject
                {
                    ["path"] = finding.RelativePath,
                    ["category"] = finding.Category,
                    ["evidence"] = finding.Evidence,
                })
                .ToArray<JsonNode?>()),
            ["generatedArtifactHashes"] = new JsonArray(artifacts
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .Select(ArtifactRef)
                .ToArray<JsonNode?>()),
        };

    private static string BuildPublicSafeSummary(JsonObject source, string status, DateTimeOffset generatedAt)
    {
        var summary = PilotEvidencePackageContracts.RequireObject(source, "publicSummary");
        var builder = new StringBuilder();
        builder.AppendLine($"# {PilotEvidencePackageContracts.GetString(summary, "title")}");
        builder.AppendLine();
        builder.AppendLine($"Generated: {PilotEvidencePackageContracts.FormatTimestamp(generatedAt)}");
        builder.AppendLine($"Status: {status}");
        builder.AppendLine($"Profile: {PilotEvidencePackageContracts.GetString(source, "profile")}");
        builder.AppendLine();
        builder.AppendLine(PilotEvidencePackageContracts.GetString(summary, "statusWording"));
        builder.AppendLine();
        builder.AppendLine("## Approved Claims");
        AppendStringList(builder, summary, "approvedClaims");
        builder.AppendLine();
        builder.AppendLine("## Blocked Claims");
        AppendStringList(builder, summary, "blockedClaims");
        builder.AppendLine();
        builder.AppendLine("## Claim States");
        foreach (var claim in PilotEvidencePackageContracts.RequireArray(source, "claimDecisions").OfType<JsonObject>())
        {
            builder.Append("- ");
            builder.Append(PilotEvidencePackageContracts.GetString(claim, "claimId"));
            builder.Append(": ");
            builder.AppendLine(PilotEvidencePackageContracts.GetString(claim, "state"));
        }

        builder.AppendLine();
        builder.AppendLine("## Residual Risks");
        AppendStringList(builder, summary, "residualRisks");
        builder.AppendLine();
        builder.Append("Review path: ");
        builder.AppendLine(PilotEvidencePackageContracts.GetString(summary, "contactPath"));
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> BuildBlockers(
        JsonObject source,
        IReadOnlyList<PilotEvidenceMaterialFinding> publicFindings)
    {
        var blockers = new SortedSet<string>(StringComparer.Ordinal);
        if (PilotEvidencePackageContracts.GetString(source, "status") == "blocked")
        {
            blockers.Add("FEAT141-SOURCE-STATUS-BLOCKED");
        }

        foreach (var exception in PilotEvidencePackageContracts.RequireArray(source, "exceptions").OfType<JsonObject>())
        {
            if (PilotEvidencePackageContracts.GetBool(exception, "packageBlocking"))
            {
                blockers.Add(PilotEvidencePackageContracts.GetString(exception, "exceptionId"));
            }
        }

        if (publicFindings.Count > 0)
        {
            blockers.Add("FEAT141-PUBLIC-FORBIDDEN-MATERIAL");
        }

        return blockers.ToArray();
    }

    private static IReadOnlyList<string> BuildDowngrades(JsonObject source)
    {
        var downgrades = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var claim in PilotEvidencePackageContracts.RequireArray(source, "claimDecisions").OfType<JsonObject>())
        {
            var state = PilotEvidencePackageContracts.GetString(claim, "state");
            if (state is "allowed_with_limitations" or "downgraded")
            {
                foreach (var downgrade in PilotEvidencePackageContracts.GetStringArray(claim, "downgradeIds"))
                {
                    downgrades.Add(downgrade);
                }
            }
        }

        foreach (var exception in PilotEvidencePackageContracts.RequireArray(source, "exceptions").OfType<JsonObject>())
        {
            var scoreImpact = PilotEvidencePackageContracts.GetString(exception, "scoreImpact");
            if (scoreImpact is "limited_claim_only" or "downgrade")
            {
                downgrades.Add(PilotEvidencePackageContracts.GetString(exception, "exceptionId"));
            }
        }

        if (downgrades.Count == 0 && PilotEvidencePackageContracts.GetString(source, "status") == "accepted_with_limitations")
        {
            downgrades.Add("FEAT141-ACCEPTED-WITH-LIMITATIONS");
        }

        return downgrades.ToArray();
    }

    private static IReadOnlyList<string> BuildClaimBlockers(JsonObject source)
    {
        var blockers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var claim in PilotEvidencePackageContracts.RequireArray(source, "claimDecisions").OfType<JsonObject>())
        {
            foreach (var blocker in PilotEvidencePackageContracts.GetStringArray(claim, "blockerIds"))
            {
                blockers.Add(blocker);
            }
        }

        foreach (var upstream in PilotEvidencePackageContracts.RequireArray(source, "upstreamEvidence").OfType<JsonObject>())
        {
            foreach (var blocker in PilotEvidencePackageContracts.GetStringArray(upstream, "blockerIds"))
            {
                blockers.Add(blocker);
            }
        }

        return blockers.ToArray();
    }

    private static string ResolvePackageStatus(
        JsonObject source,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> downgrades)
    {
        if (blockers.Count > 0)
        {
            return "blocked";
        }

        if (PilotEvidencePackageContracts.GetString(source, "status") == "accepted_with_limitations" ||
            downgrades.Count > 0 ||
            PilotEvidencePackageContracts.RequireArray(source, "claimDecisions").OfType<JsonObject>()
                .Any(claim => PilotEvidencePackageContracts.GetString(claim, "state") is "blocked" or "downgraded"))
        {
            return "accepted_with_limitations";
        }

        return "accepted";
    }

    private static JsonObject BuildClaimStateSummary(JsonObject source)
    {
        var claims = PilotEvidencePackageContracts.RequireArray(source, "claimDecisions")
            .OfType<JsonObject>()
            .ToArray();
        return new JsonObject
        {
            ["allowed"] = claims.Count(claim => PilotEvidencePackageContracts.GetString(claim, "state") == "allowed"),
            ["allowedWithLimitations"] = claims.Count(claim => PilotEvidencePackageContracts.GetString(claim, "state") == "allowed_with_limitations"),
            ["downgraded"] = claims.Count(claim => PilotEvidencePackageContracts.GetString(claim, "state") == "downgraded"),
            ["blocked"] = claims.Count(claim => PilotEvidencePackageContracts.GetString(claim, "state") == "blocked"),
            ["notInScope"] = claims.Count(claim => PilotEvidencePackageContracts.GetString(claim, "state") == "not_in_scope"),
        };
    }

    private static void AppendStringList(StringBuilder builder, JsonObject source, string property)
    {
        foreach (var item in PilotEvidencePackageContracts.GetStringArray(source, property))
        {
            builder.Append("- ");
            builder.AppendLine(item);
        }
    }

    private static PilotEvidenceGeneratedArtifact JsonArtifact(string relativePath, JsonObject content)
    {
        var text = PilotEvidencePackageContracts.CanonicalJson(content);
        return new PilotEvidenceGeneratedArtifact(
            relativePath,
            text,
            PilotEvidencePackageContracts.Sha256Hex(text));
    }

    private static PilotEvidenceGeneratedArtifact TextArtifact(string relativePath, string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.EndsWith('\n'))
        {
            normalized += "\n";
        }

        return new PilotEvidenceGeneratedArtifact(
            relativePath,
            normalized,
            PilotEvidencePackageContracts.Sha256Hex(normalized));
    }

    private static JsonObject ArtifactRef(PilotEvidenceGeneratedArtifact artifact) =>
        new()
        {
            ["path"] = artifact.RelativePath,
            ["sha256Hash"] = artifact.Sha256Hash,
            ["hashFormat"] = "sha256-hex",
        };
}
