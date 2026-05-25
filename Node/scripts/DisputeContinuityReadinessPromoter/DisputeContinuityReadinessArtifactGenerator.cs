using System.Text;
using System.Text.Json.Nodes;

namespace DisputeContinuityReadinessPromoter;

public static class DisputeContinuityReadinessArtifactGenerator
{
    public const string ReadinessFragmentPath = "dispute-continuity-readiness-fragment.json";
    public const string EvidenceIndexPath = "dispute-continuity-evidence-index.json";
    public const string ClaimDecisionMatrixPath = "dispute-continuity-claim-decision-matrix.json";
    public const string PublicSafeSummaryPath = "dispute-continuity-public-safe-summary.md";
    public const string DownstreamHandoffPath = "dispute-continuity-downstream-handoff.json";
    public const string PackageHashValidationPath = "dispute-continuity-package-hash-validation.json";

    public static DisputeContinuityGeneratedPackage Generate(
        DisputeContinuityReadinessPromotionPaths paths,
        string? sourceInput = null,
        DateTimeOffset? generatedAt = null)
    {
        var source = DisputeContinuityReadinessContracts.LoadSource(paths, sourceInput);
        var validationErrors = DisputeContinuityReadinessContracts.ValidateSource(source);
        if (validationErrors.Count > 0)
        {
            throw new DisputeContinuityReadinessPromotionException(
                "FEAT-139 dispute continuity readiness source validation failed.",
                validationErrors);
        }

        var effectiveGeneratedAt = generatedAt ?? DateTimeOffset.UtcNow;
        var evidenceIndex = BuildEvidenceIndex(source, effectiveGeneratedAt);
        var claimMatrix = BuildClaimDecisionMatrix(source, effectiveGeneratedAt);
        var initialBlockers = BuildBlockers(source, claimMatrix, []);
        var initialStatus = ResolvePackageStatus(claimMatrix, initialBlockers);
        var publicSummary = BuildPublicSafeSummary(source, claimMatrix, initialStatus, effectiveGeneratedAt);
        var publicFindings = DisputeContinuityReadinessContracts.ScanForbiddenPublicMaterial(
            source,
            [(PublicSafeSummaryPath, publicSummary)]);
        var blockers = BuildBlockers(source, claimMatrix, publicFindings);
        var status = ResolvePackageStatus(claimMatrix, blockers);
        publicSummary = BuildPublicSafeSummary(source, claimMatrix, status, effectiveGeneratedAt);
        publicFindings = DisputeContinuityReadinessContracts.ScanForbiddenPublicMaterial(
            source,
            [(PublicSafeSummaryPath, publicSummary)]);
        blockers = BuildBlockers(source, claimMatrix, publicFindings);
        status = ResolvePackageStatus(claimMatrix, blockers);

        var evidenceIndexArtifact = JsonArtifact(EvidenceIndexPath, evidenceIndex);
        var claimMatrixArtifact = JsonArtifact(ClaimDecisionMatrixPath, claimMatrix);
        var publicSummaryArtifact = TextArtifact(PublicSafeSummaryPath, publicSummary);
        var readiness = BuildReadinessFragment(
            source,
            status,
            blockers,
            evidenceIndexArtifact,
            claimMatrixArtifact,
            publicSummaryArtifact,
            effectiveGeneratedAt);
        var readinessArtifact = JsonArtifact(ReadinessFragmentPath, readiness);
        var handoff = BuildDownstreamHandoff(
            source,
            status,
            blockers,
            readinessArtifact,
            evidenceIndexArtifact,
            claimMatrixArtifact,
            publicSummaryArtifact,
            effectiveGeneratedAt);
        var handoffArtifact = JsonArtifact(DownstreamHandoffPath, handoff);
        var artifactsForHashValidation = new[]
        {
            claimMatrixArtifact,
            evidenceIndexArtifact,
            handoffArtifact,
            publicSummaryArtifact,
            readinessArtifact,
        };
        var hashValidation = BuildPackageHashValidation(source, artifactsForHashValidation, effectiveGeneratedAt);

        var artifacts = artifactsForHashValidation
            .Append(JsonArtifact(PackageHashValidationPath, hashValidation))
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();

        return new DisputeContinuityGeneratedPackage(
            status,
            artifacts,
            publicFindings,
            blockers);
    }

    private static JsonObject BuildEvidenceIndex(JsonObject source, DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "dispute-continuity-evidence-index.v1",
            ["indexId"] = "DISPUTE-CONTINUITY-EVIDENCE-FEAT-139-001",
            ["featureSlice"] = DisputeContinuityReadinessContracts.FeatureId,
            ["generatedAt"] = DisputeContinuityReadinessContracts.FormatTimestamp(generatedAt),
            ["sourceRefs"] = DisputeContinuityReadinessContracts.Clone(source["evidenceRefs"]),
            ["currentReadinessRegister"] = DisputeContinuityReadinessContracts.Clone(source["currentReadinessRegister"]),
            ["anomalyEvidence"] = DisputeContinuityReadinessContracts.Clone(source["anomalyEvidence"]),
            ["voidEvidence"] = DisputeContinuityReadinessContracts.Clone(source["voidEvidence"]),
            ["governedOutcomeEvidence"] = DisputeContinuityReadinessContracts.Clone(source["governedOutcomeEvidence"]),
            ["privacyBoundary"] = new JsonObject
            {
                ["indexPolicy"] = "Refs and hashes only; no private anomaly bodies, support records, voter material, trustee shares, or tally material are embedded.",
                ["publicOutputPolicy"] = "Public summary is generated from allowlisted high-level claim status and artifact hashes.",
            },
        };

    private static JsonObject BuildClaimDecisionMatrix(JsonObject source, DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "dispute-continuity-claim-decision-matrix.v1",
            ["matrixId"] = "DISPUTE-CONTINUITY-CLAIM-MATRIX-FEAT-139-001",
            ["featureSlice"] = DisputeContinuityReadinessContracts.FeatureId,
            ["generatedAt"] = DisputeContinuityReadinessContracts.FormatTimestamp(generatedAt),
            ["scenarioDecisions"] = new JsonArray(DisputeContinuityReadinessContracts
                .RequireArray(source, "scenarioDecisions")
                .OfType<JsonObject>()
                .OrderBy(scenario => DisputeContinuityReadinessContracts.GetString(scenario, "scenarioId"), StringComparer.Ordinal)
                .Select(BuildScenarioDecision)
                .ToArray<JsonNode?>()),
        };

    private static JsonObject BuildScenarioDecision(JsonObject scenario)
    {
        var effectiveDecision = ResolveScenarioDecision(scenario);
        return new JsonObject
        {
            ["scenarioId"] = DisputeContinuityReadinessContracts.GetString(scenario, "scenarioId"),
            ["label"] = DisputeContinuityReadinessContracts.GetString(scenario, "label"),
            ["sourceDecision"] = DisputeContinuityReadinessContracts.GetString(scenario, "decision"),
            ["effectiveDecision"] = effectiveDecision,
            ["evidenceStates"] = DisputeContinuityReadinessContracts.Clone(scenario["evidenceStates"]),
            ["affectedClaims"] = DisputeContinuityReadinessContracts.Clone(scenario["affectedClaims"]),
            ["blockerIds"] = DisputeContinuityReadinessContracts.Clone(scenario["blockerIds"]) ?? new JsonArray(),
            ["publicWordingKey"] = DisputeContinuityReadinessContracts.GetString(scenario, "publicWordingKey"),
            ["residualRisk"] = DisputeContinuityReadinessContracts.GetString(scenario, "residualRisk"),
            ["decisionRule"] = "missing_required, blocked, or stale_or_superseded evidence forces block; limitations downgrade public claims.",
        };
    }

    private static JsonObject BuildReadinessFragment(
        JsonObject source,
        string status,
        IReadOnlyList<string> blockers,
        DisputeContinuityGeneratedArtifact evidenceIndexArtifact,
        DisputeContinuityGeneratedArtifact claimMatrixArtifact,
        DisputeContinuityGeneratedArtifact publicSummaryArtifact,
        DateTimeOffset generatedAt)
    {
        var register = DisputeContinuityReadinessContracts.RequireObject(source, "currentReadinessRegister");
        var currentScore = DisputeContinuityReadinessContracts.GetInt(register, "currentScore");
        var candidateScore = DisputeContinuityReadinessContracts.GetInt(register, "candidateScoreWhenAccepted", currentScore);
        return new JsonObject
        {
            ["schemaVersion"] = "dispute-continuity-readiness-fragment.v1",
            ["fragmentId"] = DisputeContinuityReadinessContracts.ReadinessFragmentId,
            ["featureSlice"] = DisputeContinuityReadinessContracts.FeatureId,
            ["sourceGap"] = DisputeContinuityReadinessContracts.GetString(source, "sourceGap"),
            ["acceptanceGate"] = DisputeContinuityReadinessContracts.AcceptanceGate,
            ["dimensionId"] = DisputeContinuityReadinessContracts.GetString(register, "dimensionId"),
            ["status"] = status,
            ["directRegisterMutation"] = false,
            ["doesNotMutateRegister"] = true,
            ["registerPromotionOwner"] = "FEAT-130",
            ["generatedAt"] = DisputeContinuityReadinessContracts.FormatTimestamp(generatedAt),
            ["scoreEffect"] = new JsonObject
            {
                ["currentScore"] = currentScore,
                ["candidateScoreWhenAccepted"] = candidateScore,
                ["appliedScore"] = status == "accepted" ? candidateScore : currentScore,
                ["targetScoreBeforeReviewPilot"] = DisputeContinuityReadinessContracts.GetInt(register, "targetScoreBeforeReviewPilot"),
                ["scoreIncreaseRequiredForFeatureAcceptance"] = false,
            },
            ["blockers"] = ToJsonArray(blockers),
            ["claimEffect"] = BuildClaimEffect(source),
            ["artifactRefs"] = new JsonArray(
                ArtifactRef(evidenceIndexArtifact),
                ArtifactRef(claimMatrixArtifact),
                ArtifactRef(publicSummaryArtifact)),
            ["promotionInstructions"] = "FEAT-130 may promote this fragment later. FEAT-139 never mutates the canonical readiness register directly.",
        };
    }

    private static JsonObject BuildDownstreamHandoff(
        JsonObject source,
        string status,
        IReadOnlyList<string> blockers,
        DisputeContinuityGeneratedArtifact readinessArtifact,
        DisputeContinuityGeneratedArtifact evidenceIndexArtifact,
        DisputeContinuityGeneratedArtifact claimMatrixArtifact,
        DisputeContinuityGeneratedArtifact publicSummaryArtifact,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "dispute-continuity-downstream-handoff.v1",
            ["handoffId"] = "DISPUTE-CONTINUITY-HANDOFF-FEAT-139-001",
            ["producerFeature"] = DisputeContinuityReadinessContracts.FeatureId,
            ["status"] = status,
            ["generatedAt"] = DisputeContinuityReadinessContracts.FormatTimestamp(generatedAt),
            ["blockers"] = ToJsonArray(blockers),
            ["feat130Handoff"] = new JsonObject
            {
                ["targetFeature"] = "FEAT-130",
                ["readinessFragmentRef"] = readinessArtifact.RelativePath,
                ["readinessFragmentHash"] = readinessArtifact.Sha256Hash,
                ["acceptanceGate"] = DisputeContinuityReadinessContracts.AcceptanceGate,
                ["dimensionId"] = DisputeContinuityReadinessContracts.DimensionId,
                ["directRegisterMutation"] = false,
                ["promotionPreconditions"] = new JsonArray(
                    "package hash validation passed",
                    "public forbidden-material findings empty",
                    "producer blockers represented honestly in claim decisions"),
            },
            ["feat141Handoff"] = new JsonObject
            {
                ["targetFeature"] = "FEAT-141",
                ["publicSafeSummaryRef"] = publicSummaryArtifact.RelativePath,
                ["publicSafeSummaryHash"] = publicSummaryArtifact.Sha256Hash,
                ["claimDecisionMatrixRef"] = claimMatrixArtifact.RelativePath,
                ["claimDecisionMatrixHash"] = claimMatrixArtifact.Sha256Hash,
                ["evidenceIndexRef"] = evidenceIndexArtifact.RelativePath,
                ["evidenceIndexHash"] = evidenceIndexArtifact.Sha256Hash,
                ["consumerAction"] = "Use public-safe refs, claim decisions, blockers, and residual risks when assembling pilot evidence.",
            },
            ["privacyBoundary"] = new JsonObject
            {
                ["publicHandoffAllowed"] = new JsonArray(
                    "artifact hashes",
                    "claim status",
                    "blocker ids",
                    "residual risk wording",
                    "public package refs"),
                ["restrictedPayloadsExcluded"] = true,
            },
            ["voidEvidenceInput"] = DisputeContinuityReadinessContracts.Clone(source["voidEvidence"]),
        };

    private static JsonObject BuildPackageHashValidation(
        JsonObject source,
        IReadOnlyCollection<DisputeContinuityGeneratedArtifact> generatedArtifacts,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "dispute-continuity-package-hash-validation.v1",
            ["validationId"] = "DISPUTE-CONTINUITY-HASH-FEAT-139-001",
            ["generatedAt"] = DisputeContinuityReadinessContracts.FormatTimestamp(generatedAt),
            ["status"] = "passed",
            ["canonicalizationVersion"] = DisputeContinuityReadinessContracts.CanonicalizationVersion,
            ["sourceEvidenceRefs"] = new JsonArray(DisputeContinuityReadinessContracts
                .RequireArray(source, "evidenceRefs")
                .OfType<JsonObject>()
                .Select(evidence => new JsonObject
                {
                    ["evidenceId"] = DisputeContinuityReadinessContracts.GetString(evidence, "evidenceId"),
                    ["path"] = DisputeContinuityReadinessContracts.GetString(evidence, "path"),
                    ["declaredSha256Hash"] = DisputeContinuityReadinessContracts.GetString(evidence, "sha256Hash"),
                    ["hashFormat"] = "sha256-hex-or-source-controlled",
                })
                .ToArray<JsonNode?>()),
            ["generatedArtifactHashes"] = new JsonArray(generatedArtifacts
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .Select(artifact => ArtifactRef(artifact))
                .ToArray<JsonNode?>()),
        };

    private static string BuildPublicSafeSummary(
        JsonObject source,
        JsonObject claimMatrix,
        string status,
        DateTimeOffset generatedAt)
    {
        var publicSummary = DisputeContinuityReadinessContracts.RequireObject(source, "publicSummary");
        var decisions = DisputeContinuityReadinessContracts.RequireArray(claimMatrix, "scenarioDecisions")
            .OfType<JsonObject>()
            .ToArray();
        var builder = new StringBuilder();
        builder.AppendLine($"# {DisputeContinuityReadinessContracts.GetString(publicSummary, "title")}");
        builder.AppendLine();
        builder.AppendLine($"Generated: {DisputeContinuityReadinessContracts.FormatTimestamp(generatedAt)}");
        builder.AppendLine($"Status: {status}");
        builder.AppendLine();
        builder.AppendLine(DisputeContinuityReadinessContracts.GetString(publicSummary, "statusWording"));
        builder.AppendLine();
        builder.AppendLine("## Claim Decisions");
        foreach (var decision in decisions)
        {
            builder.Append("- ");
            builder.Append(DisputeContinuityReadinessContracts.GetString(decision, "label"));
            builder.Append(": ");
            builder.AppendLine(DisputeContinuityReadinessContracts.GetString(decision, "effectiveDecision"));
        }

        builder.AppendLine();
        builder.AppendLine("## Allowed Claims");
        foreach (var claim in DisputeContinuityReadinessContracts.GetStringArray(publicSummary, "allowedClaims"))
        {
            builder.Append("- ");
            builder.AppendLine(claim);
        }

        builder.AppendLine();
        builder.AppendLine("## Blocked Claims");
        foreach (var claim in DisputeContinuityReadinessContracts.GetStringArray(publicSummary, "blockedClaims"))
        {
            builder.Append("- ");
            builder.AppendLine(claim);
        }

        builder.AppendLine();
        builder.AppendLine("## Residual Risks");
        foreach (var risk in DisputeContinuityReadinessContracts.GetStringArray(publicSummary, "residualRisks"))
        {
            builder.Append("- ");
            builder.AppendLine(risk);
        }

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> BuildBlockers(
        JsonObject source,
        JsonObject claimMatrix,
        IReadOnlyList<DisputeContinuityMaterialFinding> publicFindings)
    {
        var blockers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var scenario in DisputeContinuityReadinessContracts
            .RequireArray(claimMatrix, "scenarioDecisions")
            .OfType<JsonObject>())
        {
            if (DisputeContinuityReadinessContracts.GetString(scenario, "effectiveDecision") != "block")
            {
                continue;
            }

            var scenarioBlockers = DisputeContinuityReadinessContracts.GetStringArray(scenario, "blockerIds");
            if (scenarioBlockers.Count == 0)
            {
                blockers.Add($"FEAT139-{DisputeContinuityReadinessContracts.GetString(scenario, "scenarioId").ToUpperInvariant()}");
            }
            else
            {
                foreach (var blocker in scenarioBlockers)
                {
                    blockers.Add(blocker);
                }
            }
        }

        var voidEvidence = DisputeContinuityReadinessContracts.RequireObject(source, "voidEvidence");
        if (IsBlockingState(DisputeContinuityReadinessContracts.GetString(voidEvidence, "state")))
        {
            foreach (var blocker in DisputeContinuityReadinessContracts.GetStringArray(voidEvidence, "blockerIds"))
            {
                blockers.Add(blocker);
            }
        }

        foreach (var outcome in DisputeContinuityReadinessContracts
            .RequireArray(source, "governedOutcomeEvidence")
            .OfType<JsonObject>())
        {
            if (!IsBlockingState(DisputeContinuityReadinessContracts.GetString(outcome, "state")))
            {
                continue;
            }

            foreach (var blocker in DisputeContinuityReadinessContracts.GetStringArray(outcome, "blockerIds"))
            {
                blockers.Add(blocker);
            }
        }

        if (publicFindings.Count > 0)
        {
            blockers.Add("FEAT139-PUBLIC-FORBIDDEN-MATERIAL");
        }

        return blockers.ToArray();
    }

    private static string ResolvePackageStatus(JsonObject claimMatrix, IReadOnlyList<string> blockers)
    {
        if (blockers.Count > 0)
        {
            return "blocked";
        }

        var decisions = DisputeContinuityReadinessContracts
            .RequireArray(claimMatrix, "scenarioDecisions")
            .OfType<JsonObject>()
            .Select(decision => DisputeContinuityReadinessContracts.GetString(decision, "effectiveDecision"))
            .ToArray();
        if (decisions.Any(decision => decision == "block"))
        {
            return "blocked";
        }

        return decisions.Any(decision => decision is "allow_with_limitations" or "downgrade")
            ? "accepted_with_limitations"
            : "accepted";
    }

    private static string ResolveScenarioDecision(JsonObject scenario)
    {
        var states = DisputeContinuityReadinessContracts.GetStringArray(scenario, "evidenceStates");
        if (states.Any(IsBlockingState))
        {
            return "block";
        }

        var decision = DisputeContinuityReadinessContracts.GetString(scenario, "decision");
        return decision == "downgrade"
            ? "downgrade"
            : states.Contains("accepted_with_limitations", StringComparer.Ordinal)
                ? "allow_with_limitations"
                : decision;
    }

    private static bool IsBlockingState(string state) =>
        state is "missing_required" or "blocked" or "stale_or_superseded";

    private static JsonObject BuildClaimEffect(JsonObject source)
    {
        var scenarios = DisputeContinuityReadinessContracts
            .RequireArray(source, "scenarioDecisions")
            .OfType<JsonObject>()
            .Select(BuildScenarioDecision)
            .ToArray();
        return new JsonObject
        {
            ["allowed"] = scenarios.Count(s => DisputeContinuityReadinessContracts.GetString(s, "effectiveDecision") == "allow"),
            ["limited"] = scenarios.Count(s => DisputeContinuityReadinessContracts.GetString(s, "effectiveDecision") == "allow_with_limitations"),
            ["downgraded"] = scenarios.Count(s => DisputeContinuityReadinessContracts.GetString(s, "effectiveDecision") == "downgrade"),
            ["blocked"] = scenarios.Count(s => DisputeContinuityReadinessContracts.GetString(s, "effectiveDecision") == "block"),
        };
    }

    private static DisputeContinuityGeneratedArtifact JsonArtifact(string relativePath, JsonObject content)
    {
        var text = DisputeContinuityReadinessContracts.CanonicalJson(content);
        return new DisputeContinuityGeneratedArtifact(
            relativePath,
            text,
            DisputeContinuityReadinessContracts.Sha256Hex(text));
    }

    private static DisputeContinuityGeneratedArtifact TextArtifact(string relativePath, string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.EndsWith('\n'))
        {
            normalized += "\n";
        }

        return new DisputeContinuityGeneratedArtifact(
            relativePath,
            normalized,
            DisputeContinuityReadinessContracts.Sha256Hex(normalized));
    }

    private static JsonObject ArtifactRef(DisputeContinuityGeneratedArtifact artifact) =>
        new()
        {
            ["path"] = artifact.RelativePath,
            ["sha256Hash"] = artifact.Sha256Hash,
            ["hashFormat"] = "sha256-hex",
        };

    private static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());
}
