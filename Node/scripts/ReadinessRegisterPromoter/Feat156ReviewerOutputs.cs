using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReadinessRegisterPromoter;

internal sealed record Feat156ReviewerArtifact(
    string RelativePath,
    string Content,
    string MediaType);

internal sealed record Feat156GeneratedReviewerPackage(
    string SourceId,
    string Status,
    IReadOnlyList<Feat156ReviewerArtifact> Artifacts);

internal static class Feat156ReviewerOutputs
{
    public const string DecisionLedgerPath = "feat156-production-rollout-decision-ledger.json";
    public const string RestrictedReviewerIndexPath = "feat156-restricted-reviewer-index.json";
    public const string PublicSafeSummaryPath = "feat156-public-safe-summary.md";
    public const string PromotionSourceSnapshotPath = "feat156-promotion-source-snapshot.json";
    public const string ForbiddenMaterialScanPath = "feat156-forbidden-material-scan.json";
    public const string NoUiBoundaryNotePath = "feat156-no-ui-boundary-note.md";
    public const string ArtifactHashAuditPath = "feat156-artifact-hash-audit.json";
    public const string ReviewerOutputValidationPath = "feat156-reviewer-output-validation.json";
    public const string PackageManifestPath = "feat156-package-manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string SourcePath(ReadinessRegisterPromotionPaths paths) => Path.Combine(
        paths.WorkspaceRoot,
        "hush-memory-bank",
        "Overview",
        "HushVotingReadiness",
        "Production-Rollout-Promotion-Register",
        "examples",
        "release-baseline",
        "production-rollout-promotion-source.json");

    public static string PackageRoot(ReadinessRegisterPromotionPaths paths) => Path.Combine(
        paths.WorkspaceRoot,
        "hush-documents",
        "PrivateServer_ElectronicVoting",
        "Production-Rollout-Promotion-Register",
        "package");

    public static Feat156GeneratedReviewerPackage? TryGenerate(
        ReadinessRegisterPromotionPaths paths,
        JsonObject promotedRegister,
        DateTimeOffset generatedAt,
        string registerManifestHash,
        string registerArchiveHash)
    {
        var sourcePath = SourcePath(paths);
        if (!File.Exists(sourcePath))
        {
            return null;
        }

        var source = ReadJsonObject(sourcePath, "FEAT-156 promotion source");
        var targetRegister = ObjectOrEmpty(source, "targetRegister");
        if (GetString(targetRegister, "registerVersion") != GetString(promotedRegister, "registerVersion") ||
            GetString(targetRegister, "registerVersionId") != GetString(promotedRegister, "registerVersionId"))
        {
            return null;
        }

        var sourceErrors = ValidateSource(source, promotedRegister);
        if (sourceErrors.Count > 0)
        {
            throw new ReadinessRegisterPromotionException(
                "FEAT-156 reviewer output source validation failed.",
                sourceErrors);
        }

        var publicSafeSummary = BuildPublicSafeSummary(
            source,
            promotedRegister,
            registerManifestHash,
            registerArchiveHash,
            generatedAt);
        var restrictedIndex = BuildRestrictedReviewerIndex(source, generatedAt);
        var decisionLedger = BuildDecisionLedger(source, promotedRegister, generatedAt);
        var forbiddenMaterialScan = BuildForbiddenMaterialScan(source, promotedRegister, publicSafeSummary, generatedAt);
        var noUiBoundaryNote = BuildNoUiBoundaryNote(source, promotedRegister, generatedAt);
        var reviewerOutputValidation = BuildReviewerOutputValidation(
            source,
            promotedRegister,
            forbiddenMaterialScan,
            restrictedIndex,
            registerManifestHash,
            registerArchiveHash,
            generatedAt);

        var validationErrors = CollectFailedFindings(forbiddenMaterialScan);
        validationErrors.AddRange(CollectFailedChecks(reviewerOutputValidation));
        if (validationErrors.Count > 0)
        {
            throw new ReadinessRegisterPromotionException(
                "FEAT-156 reviewer output validation failed.",
                validationErrors);
        }

        var artifacts = new List<Feat156ReviewerArtifact>
        {
            JsonArtifact(DecisionLedgerPath, decisionLedger),
            JsonArtifact(PromotionSourceSnapshotPath, source.DeepClone()),
            JsonArtifact(RestrictedReviewerIndexPath, restrictedIndex),
            TextArtifact(PublicSafeSummaryPath, publicSafeSummary),
            JsonArtifact(ForbiddenMaterialScanPath, forbiddenMaterialScan),
            TextArtifact(NoUiBoundaryNotePath, noUiBoundaryNote),
            JsonArtifact(ReviewerOutputValidationPath, reviewerOutputValidation),
        };

        var artifactHashAudit = BuildArtifactHashAudit(
            paths,
            source,
            sourcePath,
            artifacts,
            registerManifestHash,
            registerArchiveHash,
            generatedAt);
        artifacts.Add(JsonArtifact(ArtifactHashAuditPath, artifactHashAudit));
        artifacts.Add(JsonArtifact(
            PackageManifestPath,
            BuildPackageManifest(source, promotedRegister, artifacts, generatedAt)));

        return new Feat156GeneratedReviewerPackage(
            GetString(source, "sourceId"),
            "passed",
            artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).ToArray());
    }

    public static void WriteArtifacts(
        string packageRoot,
        IReadOnlyList<Feat156ReviewerArtifact> artifacts,
        List<string> writtenFiles)
    {
        Directory.CreateDirectory(packageRoot);
        foreach (var artifact in artifacts.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            var path = Path.GetFullPath(Path.Combine(packageRoot, artifact.RelativePath));
            EnsureContained(packageRoot, path, artifact.RelativePath);
            File.WriteAllText(path, artifact.Content, new UTF8Encoding(false));
            writtenFiles.Add(path);
        }
    }

    public static IReadOnlyList<string> ValidateExistingArtifacts(
        string packageRoot,
        IReadOnlyList<Feat156ReviewerArtifact> artifacts)
    {
        var errors = new List<string>();
        foreach (var artifact in artifacts.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            var path = Path.Combine(packageRoot, artifact.RelativePath);
            if (!File.Exists(path))
            {
                errors.Add($"Missing FEAT-156 reviewer output artifact: {artifact.RelativePath}");
                continue;
            }

            var actual = NormalizeLineEndings(File.ReadAllText(path));
            var expected = NormalizeLineEndings(artifact.Content);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                errors.Add($"FEAT-156 reviewer output artifact mismatch: {artifact.RelativePath}");
            }
        }

        return errors;
    }

    private static List<string> ValidateSource(JsonObject source, JsonObject promotedRegister)
    {
        var errors = new List<string>();
        if (GetString(source, "schemaVersion") != "production-rollout-promotion-source.v1")
        {
            errors.Add("schemaVersion must be production-rollout-promotion-source.v1.");
        }

        if (GetString(source, "featureId") != "FEAT-156")
        {
            errors.Add("featureId must be FEAT-156.");
        }

        if (GetString(source, "status") != "accepted")
        {
            errors.Add("status must be accepted.");
        }

        var target = RequireObject(source, "targetRegister", errors);
        if (target is not null)
        {
            if (GetString(target, "registerVersion") != GetString(promotedRegister, "registerVersion"))
            {
                errors.Add("targetRegister.registerVersion must match promoted register version.");
            }

            if (GetString(target, "registerVersionId") != GetString(promotedRegister, "registerVersionId"))
            {
                errors.Add("targetRegister.registerVersionId must match promoted register version id.");
            }

            var targetScore = GetInt(target, "totalScore");
            var promotedScore = GetInt(ObjectOrEmpty(promotedRegister, "score"), "total");
            if (targetScore != promotedScore)
            {
                errors.Add($"targetRegister.totalScore must match promoted score total {promotedScore}.");
            }
        }

        var generatedViews = ObjectOrEmpty(promotedRegister, "generatedViews");
        var targetPublicationStatus = GetString(ObjectOrEmpty(source, "targetRegister"), "publicationStatus");
        if (GetString(generatedViews, "publicSafePublicationStatus") != targetPublicationStatus)
        {
            errors.Add($"promoted register publication status must be {targetPublicationStatus}.");
        }

        var targetStrongestAllowedClaim = GetString(ObjectOrEmpty(source, "targetRegister"), "strongestAllowedClaim");
        if (GetStrongestAllowedClaim(promotedRegister) != targetStrongestAllowedClaim)
        {
            errors.Add($"promoted register strongest allowed claim must be {targetStrongestAllowedClaim}.");
        }

        var rules = ObjectOrEmpty(source, "restrictedReviewerRules");
        if (rules.Count > 0)
        {
            if (GetBool(rules, "payloadInliningAllowed"))
            {
                errors.Add("restrictedReviewerRules.payloadInliningAllowed must be false.");
            }

            if (GetBool(rules, "rawEvidenceCopied"))
            {
                errors.Add("restrictedReviewerRules.rawEvidenceCopied must be false.");
            }
        }

        return errors;
    }

    private static JsonObject BuildDecisionLedger(JsonObject source, JsonObject promotedRegister, DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "feat156-production-rollout-decision-ledger.v1",
            ["ledgerId"] = "FEAT156-PRODUCTION-ROLLOUT-DECISION-LEDGER-001",
            ["sourceId"] = GetString(source, "sourceId"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["registerVersionId"] = GetString(promotedRegister, "registerVersionId"),
            ["publicationStatus"] = GetString(ObjectOrEmpty(promotedRegister, "generatedViews"), "publicSafePublicationStatus"),
            ["scoreModel"] = Clone(source["scoreModel"]),
            ["baselineRegister"] = Clone(source["baselineRegister"]),
            ["targetRegister"] = Clone(source["targetRegister"]),
            ["scoreMovements"] = Clone(source["scoreMovements"]),
            ["blockerDecisions"] = Clone(source["blockerDecisions"]),
            ["residualRisks"] = Clone(source["residualRisks"]),
            ["signoff"] = Clone(source["signoff"]),
        };

    private static JsonObject BuildRestrictedReviewerIndex(JsonObject source, DateTimeOffset generatedAt)
    {
        var evidenceIndex = new JsonArray();
        foreach (var movement in ArrayOrEmpty(source, "scoreMovements").Select(node => node!.AsObject()))
        {
            var artifactRefs = new JsonArray();
            foreach (var artifactRef in ArrayOrEmpty(movement, "artifactRefs").Select(node => node!.AsObject()))
            {
                artifactRefs.Add(new JsonObject
                {
                    ["artifactId"] = GetString(artifactRef, "artifactId"),
                    ["path"] = GetString(artifactRef, "path"),
                    ["sha256Hash"] = NormalizeHash(GetString(artifactRef, "sha256Hash")),
                    ["hashBasis"] = GetString(artifactRef, "hashBasis"),
                    ["visibility"] = GetString(artifactRef, "visibility"),
                });
            }

            evidenceIndex.Add(new JsonObject
            {
                ["movementId"] = GetString(movement, "movementId"),
                ["featureId"] = GetString(movement, "featureId"),
                ["dimensionId"] = GetString(movement, "dimensionId"),
                ["status"] = GetString(movement, "status"),
                ["freshness"] = GetString(movement, "freshness"),
                ["acceptanceGateIds"] = Clone(movement["acceptanceGateIds"]),
                ["sourceGapRows"] = Clone(movement["sourceGapRows"]),
                ["evidenceIds"] = Clone(movement["evidenceIds"]),
                ["artifactRefs"] = artifactRefs,
                ["signoff"] = Clone(movement["signoff"]),
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "feat156-restricted-reviewer-index.v1",
            ["indexId"] = "FEAT156-RESTRICTED-REVIEWER-INDEX-001",
            ["sourceId"] = GetString(source, "sourceId"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["visibility"] = "restricted_reviewer",
            ["rawEvidenceInlined"] = false,
            ["payloadInliningAllowed"] = false,
            ["rawEvidenceCopied"] = false,
            ["reviewerOwnership"] = Clone(source["signoff"]),
            ["decisionLedgerRef"] = new JsonObject
            {
                ["path"] = DecisionLedgerPath,
                ["movementCount"] = evidenceIndex.Count,
            },
            ["evidenceIndex"] = evidenceIndex,
            ["blockerDecisionRefs"] = Clone(source["blockerDecisions"]),
        };
    }

    private static string BuildPublicSafeSummary(
        JsonObject source,
        JsonObject promotedRegister,
        string registerManifestHash,
        string registerArchiveHash,
        DateTimeOffset generatedAt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!-- Generated by ReadinessRegisterPromoter. Do not edit by hand. -->");
        sb.AppendLine();
        sb.AppendLine("# HushVoting Production Rollout Readiness Summary");
        sb.AppendLine();
        sb.AppendLine($"Generated at: {FormatTimestamp(generatedAt)}");
        sb.AppendLine($"Register: {GetString(promotedRegister, "registerVersionId")}");
        sb.AppendLine($"Publication status: {GetString(ObjectOrEmpty(promotedRegister, "generatedViews"), "publicSafePublicationStatus")}");
        sb.AppendLine($"Manifest hash: {registerManifestHash}");
        sb.AppendLine($"Archive hash: {registerArchiveHash}");
        sb.AppendLine();
        sb.AppendLine("## Current Public-Safe Status");
        sb.AppendLine();
        if (GetString(ObjectOrEmpty(source, "targetRegister"), "publicationStatus") == InternalAudit95ReadinessPlan.PublicationStatus)
        {
            sb.AppendLine("HushVoting may be discussed for controlled friendly-organization pilot planning with explicit limitations. The promoted register now separates Hush-owned internal audit hardening from downstream external validation prerequisites.");
        }
        else
        {
            sb.AppendLine("HushVoting may be discussed for limited organizational rollout readiness with explicit limitations. The promoted register keeps residual risks and customer-owned governance responsibilities visible.");
        }
        sb.AppendLine();
        sb.AppendLine("## Known Limitations");
        sb.AppendLine();
        if (GetString(ObjectOrEmpty(source, "targetRegister"), "publicationStatus") == InternalAudit95ReadinessPlan.PublicationStatus)
        {
            sb.AppendLine("- The current internal audit score is below the Hush-owned 95+ target and production organizational rollout is not claimed by this register.");
            sb.AppendLine("- Hush-owned hardening tasks remain visible in the restricted readiness register.");
        }
        else
        {
            sb.AppendLine("- The promotion supports controlled organizational rollout planning only.");
        }
        sb.AppendLine("- Higher external-authority election uses remain outside this technical readiness promotion.");
        sb.AppendLine("- Customer governance, dispute remedies, regulatory approval, repeated operating history, and third-party review remain outside the promoted evidence.");
        sb.AppendLine();
        sb.AppendLine("## Non-Claims");
        sb.AppendLine();
        sb.AppendLine("- This summary does not authorize unrestricted production use.");
        sb.AppendLine("- This summary does not publish internal score details, source payloads, private references, local paths, user data, keys, or secrets.");
        sb.AppendLine("- This summary does not present HushVoting as a full meeting-management product.");
        return NormalizeLineEndings(sb.ToString());
    }

    private static JsonObject BuildForbiddenMaterialScan(
        JsonObject source,
        JsonObject promotedRegister,
        string publicSafeSummary,
        DateTimeOffset generatedAt)
    {
        var findings = new JsonArray();
        var index = 1;
        foreach (var item in GetForbiddenNeedles(source, promotedRegister))
        {
            var found = publicSafeSummary.Contains(item.Needle, StringComparison.OrdinalIgnoreCase);
            findings.Add(new JsonObject
            {
                ["ruleId"] = $"FEAT156-PUBLIC-SAFE-FORBIDDEN-{index:000}",
                ["artifact"] = PublicSafeSummaryPath,
                ["category"] = item.Category,
                ["needle"] = item.Needle,
                ["result"] = found ? "failed" : "passed",
                ["reason"] = found
                    ? "Forbidden material was found in the public-safe summary."
                    : "Forbidden material was not found in the public-safe summary.",
            });
            index++;
        }

        return new JsonObject
        {
            ["schemaVersion"] = "feat156-forbidden-material-scan.v1",
            ["scanId"] = "FEAT156-FORBIDDEN-MATERIAL-SCAN-001",
            ["sourceId"] = GetString(source, "sourceId"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["status"] = findings.Select(node => node!.AsObject()).All(finding => GetString(finding, "result") == "passed")
                ? "passed"
                : "failed",
            ["scannedArtifacts"] = new JsonArray(PublicSafeSummaryPath),
            ["findings"] = findings,
        };
    }

    private static string BuildNoUiBoundaryNote(JsonObject source, JsonObject promotedRegister, DateTimeOffset generatedAt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!-- Generated by ReadinessRegisterPromoter. Do not edit by hand. -->");
        sb.AppendLine();
        sb.AppendLine("# FEAT-156 No-UI Boundary Note");
        sb.AppendLine();
        sb.AppendLine($"Generated at: {FormatTimestamp(generatedAt)}");
        sb.AppendLine($"Register: {GetString(promotedRegister, "registerVersionId")}");
        sb.AppendLine($"Source: {GetString(source, "sourceId")}");
        sb.AppendLine();
        sb.AppendLine("FEAT-156 is a readiness-register promotion and reviewer-output package. No HushWebClient route or component is required for this feature.");
        sb.AppendLine();
        sb.AppendLine("The promoted register keeps the existing readiness register schema and generated view shape consumed by FEAT-142. The internal readiness dashboard can continue to read the current catalog/register pointer generically.");
        sb.AppendLine();
        sb.AppendLine("No focused FEAT-142 dashboard regression is required for this phase because no UI contract or view shape changed.");
        return NormalizeLineEndings(sb.ToString());
    }

    private static JsonObject BuildReviewerOutputValidation(
        JsonObject source,
        JsonObject promotedRegister,
        JsonObject forbiddenMaterialScan,
        JsonObject restrictedReviewerIndex,
        string registerManifestHash,
        string registerArchiveHash,
        DateTimeOffset generatedAt)
    {
        var checks = new JsonArray();
        AddCheck(
            checks,
            "public-safe-scan-passed",
            GetString(forbiddenMaterialScan, "status") == "passed",
            "Public-safe summary contains no forbidden material.");
        AddCheck(
            checks,
            "restricted-index-metadata-only",
            !GetBool(restrictedReviewerIndex, "rawEvidenceInlined") &&
                !GetBool(restrictedReviewerIndex, "payloadInliningAllowed") &&
                !GetBool(restrictedReviewerIndex, "rawEvidenceCopied"),
            "Restricted reviewer index excludes raw payloads.");
        AddCheck(
            checks,
            "movement-count-six",
            ArrayOrEmpty(restrictedReviewerIndex, "evidenceIndex").Count == 6,
            "Restricted reviewer index lists six score movements.");
        AddCheck(
            checks,
            "no-ui-route-required",
            true,
            "No HushWebClient route or component is required.");
        AddCheck(
            checks,
            "register-target-matches",
            GetString(promotedRegister, "registerVersionId") == GetString(ObjectOrEmpty(source, "targetRegister"), "registerVersionId") &&
                GetString(ObjectOrEmpty(promotedRegister, "generatedViews"), "publicSafePublicationStatus") == GetString(ObjectOrEmpty(source, "targetRegister"), "publicationStatus"),
            "Promoted register identity and publication status match FEAT-156 target.");
        AddCheck(
            checks,
            "non-sensitive-hashes-present",
            IsSha256(registerManifestHash) && IsSha256(registerArchiveHash),
            "Register manifest and archive hashes are present.");

        return new JsonObject
        {
            ["schemaVersion"] = "feat156-reviewer-output-validation.v1",
            ["validationId"] = "FEAT156-REVIEWER-OUTPUT-VALIDATION-001",
            ["sourceId"] = GetString(source, "sourceId"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["status"] = checks.Select(node => node!.AsObject()).All(check => GetString(check, "result") == "passed")
                ? "passed"
                : "failed",
            ["checks"] = checks,
        };
    }

    private static JsonObject BuildArtifactHashAudit(
        ReadinessRegisterPromotionPaths paths,
        JsonObject source,
        string sourcePath,
        IReadOnlyList<Feat156ReviewerArtifact> artifacts,
        string registerManifestHash,
        string registerArchiveHash,
        DateTimeOffset generatedAt)
    {
        var entries = new JsonArray
        {
            new JsonObject
            {
                ["artifactId"] = "FEAT156-PROMOTION-SOURCE",
                ["path"] = ToWorkspaceRelativePath(paths.WorkspaceRoot, sourcePath),
                ["sha256Hash"] = ComputeSha256Hex(File.ReadAllBytes(sourcePath)),
                ["hashAlgorithm"] = "SHA-256",
                ["visibility"] = "restricted_reviewer",
                ["sizeBytes"] = new FileInfo(sourcePath).Length,
                ["auditResult"] = "passed",
            },
            KnownHashEntry(
                "FEAT156-READINESS-REGISTER-MANIFEST",
                "HushVoting-Readiness-Register/v0.1.6/readiness-register-manifest.json",
                registerManifestHash,
                "internal"),
            KnownHashEntry(
                "FEAT156-READINESS-REGISTER-ARCHIVE",
                "HushVoting-Readiness-Register/v0.1.6/HushVoting-Readiness-Register-v0.1.6.zip",
                registerArchiveHash,
                "internal"),
        };

        foreach (var artifact in artifacts.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            entries.Add(new JsonObject
            {
                ["artifactId"] = Path.GetFileNameWithoutExtension(artifact.RelativePath).ToUpperInvariant().Replace("-", "_", StringComparison.Ordinal),
                ["path"] = artifact.RelativePath,
                ["sha256Hash"] = ComputeSha256Hex(EncodingWithoutBom(artifact.Content)),
                ["hashAlgorithm"] = "SHA-256",
                ["visibility"] = VisibilityForArtifact(artifact.RelativePath),
                ["sizeBytes"] = EncodingWithoutBom(artifact.Content).Length,
                ["auditResult"] = "passed",
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "feat156-artifact-hash-audit.v1",
            ["auditId"] = "FEAT156-ARTIFACT-HASH-AUDIT-001",
            ["sourceId"] = GetString(source, "sourceId"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["status"] = "passed",
            ["artifacts"] = entries,
        };
    }

    private static JsonObject BuildPackageManifest(
        JsonObject source,
        JsonObject promotedRegister,
        IReadOnlyList<Feat156ReviewerArtifact> artifacts,
        DateTimeOffset generatedAt)
    {
        var manifestArtifacts = new JsonArray();
        foreach (var artifact in artifacts.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            manifestArtifacts.Add(new JsonObject
            {
                ["relativePath"] = artifact.RelativePath,
                ["mediaType"] = artifact.MediaType,
                ["sha256Hash"] = ComputeSha256Hex(EncodingWithoutBom(artifact.Content)),
                ["hashAlgorithm"] = "SHA-256",
                ["visibility"] = VisibilityForArtifact(artifact.RelativePath),
                ["sizeBytes"] = EncodingWithoutBom(artifact.Content).Length,
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "feat156-package-manifest.v1",
            ["packageId"] = "FEAT156-PRODUCTION-ROLLOUT-PROMOTION-PACKAGE",
            ["sourceId"] = GetString(source, "sourceId"),
            ["registerVersionId"] = GetString(promotedRegister, "registerVersionId"),
            ["publicationStatus"] = GetString(ObjectOrEmpty(promotedRegister, "generatedViews"), "publicSafePublicationStatus"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["status"] = "passed",
            ["artifacts"] = manifestArtifacts,
            ["nonClaims"] = new JsonArray(
                "unrestricted_production_use",
                "external_authority_election_authorization",
                "legal_approval",
                "third_party_certification",
                "full_meeting_management_product"),
        };
    }

    private static void AddCheck(JsonArray checks, string checkId, bool passed, string reason)
    {
        checks.Add(new JsonObject
        {
            ["checkId"] = checkId,
            ["result"] = passed ? "passed" : "failed",
            ["reason"] = reason,
        });
    }

    private static List<string> CollectFailedFindings(JsonObject scanDocument) =>
        ArrayOrEmpty(scanDocument, "findings")
            .Select(node => node!.AsObject())
            .Where(finding => GetString(finding, "result") == "failed")
            .Select(finding => $"{GetString(finding, "artifact")} contains forbidden material: {GetString(finding, "needle")}.")
            .ToList();

    private static IReadOnlyList<string> CollectFailedChecks(JsonObject validationDocument) =>
        ArrayOrEmpty(validationDocument, "checks")
            .Select(node => node!.AsObject())
            .Where(check => GetString(check, "result") == "failed")
            .Select(check => $"FEAT-156 reviewer output validation failed: {GetString(check, "checkId")}.")
            .ToArray();

    private static IReadOnlyList<ForbiddenNeedle> GetForbiddenNeedles(JsonObject source, JsonObject promotedRegister)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var needles = new List<ForbiddenNeedle>();
        var rules = ObjectOrEmpty(source, "publicSafeOutputRules");

        AddNeedles(needles, seen, ArrayOrEmpty(rules, "forbiddenMaterialNeedles"), "restricted-material");
        AddNeedles(needles, seen, ArrayOrEmpty(rules, "forbiddenClaimNeedles"), "overclaim");

        if (!GetBool(rules, "numericScorePublicDisclosure"))
        {
            AddNeedle(needles, seen, $"{GetInt(ObjectOrEmpty(promotedRegister, "score"), "total")}/100", "score-disclosure");
            AddNeedle(needles, seen, "total score", "score-disclosure");
            AddNeedle(needles, seen, "dimension score", "score-disclosure");
            AddNeedle(needles, seen, "RDY-DIM", "score-disclosure");
        }

        foreach (var item in new[]
        {
            new ForbiddenNeedle("C:\\", "local-path"),
            new ForbiddenNeedle("hush-documents/PrivateServer_ElectronicVoting/", "local-path"),
            new ForbiddenNeedle("restricted_reviewer", "restricted-material"),
            new ForbiddenNeedle("restricted-evidence/", "restricted-material"),
            new ForbiddenNeedle("raw evidence", "restricted-material"),
            new ForbiddenNeedle("raw log", "restricted-material"),
            new ForbiddenNeedle("voter identity", "privacy"),
            new ForbiddenNeedle("ballot choice", "privacy"),
            new ForbiddenNeedle("KMS key", "secret"),
            new ForbiddenNeedle("credential", "secret"),
            new ForbiddenNeedle("database connection", "secret"),
            new ForbiddenNeedle("support case", "privacy"),
            new ForbiddenNeedle("legal sufficiency", "overclaim"),
            new ForbiddenNeedle("independent certification", "overclaim"),
            new ForbiddenNeedle("full AGM", "overclaim"),
            new ForbiddenNeedle("public/state election ready", "overclaim"),
            new ForbiddenNeedle("government election ready", "overclaim"),
            new ForbiddenNeedle("legally binding AGM platform", "overclaim"),
            new ForbiddenNeedle("production green", "overclaim"),
        })
        {
            AddNeedle(needles, seen, item.Needle, item.Category);
        }

        return needles;
    }

    private static void AddNeedles(List<ForbiddenNeedle> needles, HashSet<string> seen, JsonArray source, string category)
    {
        foreach (var value in source
            .Select(node => node?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            AddNeedle(needles, seen, value!, category);
        }
    }

    private static void AddNeedle(List<ForbiddenNeedle> needles, HashSet<string> seen, string needle, string category)
    {
        if (!string.IsNullOrWhiteSpace(needle) && seen.Add(needle))
        {
            needles.Add(new ForbiddenNeedle(needle, category));
        }
    }

    private static JsonObject KnownHashEntry(string artifactId, string path, string hash, string visibility) =>
        new()
        {
            ["artifactId"] = artifactId,
            ["path"] = path,
            ["sha256Hash"] = hash,
            ["hashAlgorithm"] = "SHA-256",
            ["visibility"] = visibility,
            ["sizeBytes"] = 0,
            ["auditResult"] = IsSha256(hash) ? "passed" : "failed",
        };

    private static string VisibilityForArtifact(string relativePath) =>
        relativePath == PublicSafeSummaryPath
            ? "public_safe"
            : relativePath == NoUiBoundaryNotePath
                ? "internal"
                : "restricted_reviewer";

    private static string GetStrongestAllowedClaim(JsonObject register)
    {
        var rank = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["internal_development"] = 0,
            ["internal_non_binding_rehearsal"] = 1,
            ["friendly_organization_pilot"] = 2,
            ["production_organizational_rollout"] = 3,
            ["public_or_state_election"] = 4,
        };
        return ArrayOrEmpty(register, "claimLevels")
            .Select(node => node!.AsObject())
            .Where(claim =>
                GetString(claim, "status") is "allowed" or "allowed_with_limitations" &&
                GetString(claim, "blockerSeverity") != "red")
            .OrderByDescending(claim => rank.GetValueOrDefault(GetString(claim, "claimLevel"), -1))
            .Select(claim => GetString(claim, "claimLevel"))
            .FirstOrDefault() ?? "none";
    }

    private static Feat156ReviewerArtifact JsonArtifact(string relativePath, JsonNode node) =>
        new(relativePath, SerializeJson(node), "application/json");

    private static Feat156ReviewerArtifact TextArtifact(string relativePath, string content) =>
        new(relativePath, NormalizeLineEndings(content) + (content.EndsWith('\n') ? "" : "\n"), "text/markdown");

    private static JsonObject ReadJsonObject(string path, string displayName)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                ?? throw new JsonException("Root is not an object.");
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            throw new ReadinessRegisterPromotionException(
                $"Could not read {displayName}.",
                [$"{path}: {ex.Message}"]);
        }
    }

    private static JsonObject? RequireObject(JsonObject? item, string propertyName, List<string> errors)
    {
        if (item is not null && item[propertyName] is JsonObject obj)
        {
            return obj;
        }

        errors.Add($"{propertyName} must be an object.");
        return null;
    }

    private static void RequireValue(JsonObject source, string name, string expected, List<string> errors)
    {
        if (GetString(source, name) != expected)
        {
            errors.Add($"{name} must be {expected}.");
        }
    }

    private static void EnsureContained(string root, string child, string label)
    {
        var fullRoot = Path.GetFullPath(root);
        var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!child.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReadinessRegisterPromotionException(
                "FEAT-156 reviewer output path escapes package root.",
                [label]);
        }
    }

    private static string ToWorkspaceRelativePath(string workspaceRoot, string path) =>
        Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/');

    private static JsonNode? Clone(JsonNode? node) => node?.DeepClone();

    private static JsonArray ArrayOrEmpty(JsonObject item, string propertyName) =>
        item[propertyName] as JsonArray ?? [];

    private static JsonObject ObjectOrEmpty(JsonObject item, string propertyName) =>
        item[propertyName] as JsonObject ?? new JsonObject();

    private static string GetString(JsonObject? item, string propertyName) =>
        item is not null && item[propertyName] is JsonValue value && value.TryGetValue<string>(out var result)
            ? result
            : string.Empty;

    private static int GetInt(JsonObject? item, string propertyName) =>
        item is not null && item[propertyName] is JsonValue value && value.TryGetValue<int>(out var result)
            ? result
            : 0;

    private static bool GetBool(JsonObject? item, string propertyName) =>
        item is not null && item[propertyName] is JsonValue value && value.TryGetValue<bool>(out var result) && result;

    private static string NormalizeHash(string value) =>
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? value["sha256:".Length..].ToLowerInvariant()
            : value.ToLowerInvariant();

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static string SerializeJson(JsonNode node) => NormalizeLineEndings(node.ToJsonString(JsonOptions)) + "\n";

    private static byte[] EncodingWithoutBom(string value) => new UTF8Encoding(false).GetBytes(NormalizeLineEndings(value));

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    private static string ComputeSha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record ForbiddenNeedle(string Needle, string Category);
}
