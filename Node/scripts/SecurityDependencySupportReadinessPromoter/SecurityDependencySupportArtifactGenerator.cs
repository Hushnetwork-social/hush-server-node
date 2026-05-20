using System.Text.Json.Nodes;

namespace SecurityDependencySupportReadinessPromoter;

public static class SecurityDependencySupportArtifactGenerator
{
    public const string DependencyInventoryPath = "dependency-inventory.json";
    public const string LicenseScanPath = "license-scan-normalized.json";
    public const string VulnerabilityScanPath = "vulnerability-scan-normalized.json";
    public const string DisclosureProcessPath = "disclosure-process.json";
    public const string AccessibilityEvidencePath = "accessibility-evidence-index.json";
    public const string VoterClientGuidanceMarkdownPath = "voter-client-integrity-guidance.md";
    public const string SupportReadinessPath = "support-readiness-index.json";
    public const string SupportExportPrivacyProofPath = "support-export-privacy-proof.json";
    public const string ExceptionsPath = "security-dependency-support-exceptions.json";
    public const string ReadinessFragmentPath = "security-dependency-support-readiness-fragment.json";
    public const string SecuritySupportHandoffPath = "security-support-handoff.json";
    public const string RestrictedEvidenceIndexPath = "restricted-security-dependency-support-evidence-index.md";
    public const string PublicSafeSummaryPath = "public-safe-security-dependency-support-summary.md";
    public const string ManifestPath = "security-dependency-support-manifest.json";
    public const string ExternalPackageFolder = "packages";
    public const string ExternalReadinessEvidenceId = "SECURITY-DEPENDENCY-SUPPORT-READINESS-001";
    public const string ExternalSecuritySupportHandoffId = "SECURITY-SUPPORT-HANDOFF-001";

    public static SecurityDependencySupportGeneratedPackage Generate(
        SecurityDependencySupportPromotionPaths paths,
        string? sourceInput,
        string releaseId,
        string version,
        DateTimeOffset generatedAt,
        string publicationStatus)
    {
        var sources = SecurityDependencySupportContracts.LoadSources(paths, sourceInput);
        var checkResult = SecurityDependencySupportChecker.Evaluate(paths, sources, generatedAt);
        var package = BuildPackage(sources.Package, releaseId, version, generatedAt, publicationStatus, checkResult);
        var packageId = SecurityDependencySupportContracts.GetString(package, "packageId");

        var artifacts = new List<SecurityDependencySupportGeneratedArtifact>
        {
            BuildArtifact(SecurityDependencySupportPromotionPaths.PackageFileName, package),
            BuildArtifact(DependencyInventoryPath, SanitizeForPackage(sources.DependencyInventory)!.AsObject()),
            BuildArtifact(LicenseScanPath, SanitizeForPackage(sources.LicenseScan)!.AsObject()),
            BuildArtifact(VulnerabilityScanPath, SanitizeForPackage(sources.VulnerabilityScan)!.AsObject()),
            BuildArtifact(DisclosureProcessPath, SanitizeForPackage(sources.DisclosureProcess)!.AsObject()),
            BuildArtifact(AccessibilityEvidencePath, SanitizeForPackage(sources.AccessibilityEvidence)!.AsObject()),
            BuildArtifact(VoterClientGuidanceMarkdownPath, RenderVoterClientGuidance(sources.VoterClientGuidance)),
            BuildArtifact(SupportReadinessPath, SanitizeForPackage(sources.SupportReadiness)!.AsObject()),
            BuildArtifact(SupportExportPrivacyProofPath, SanitizeForPackage(sources.SupportExportPrivacyProof)!.AsObject()),
            BuildArtifact(ExceptionsPath, SanitizeForPackage(sources.Exceptions)!.AsObject()),
        };

        var readinessFragment = BuildReadinessFragment(sources, checkResult, generatedAt);
        artifacts.Add(BuildArtifact(ReadinessFragmentPath, readinessFragment));
        var handoff = BuildSecuritySupportHandoff(sources, checkResult, generatedAt);
        artifacts.Add(BuildArtifact(SecuritySupportHandoffPath, handoff));
        artifacts.Add(BuildArtifact(RestrictedEvidenceIndexPath, RenderRestrictedEvidenceIndex(package, checkResult, artifacts)));
        artifacts.Add(BuildArtifact(PublicSafeSummaryPath, RenderPublicSafeSummary(package, sources, checkResult)));

        var scanFindings = artifacts
            .Where(artifact => artifact.RelativePath is PublicSafeSummaryPath or RestrictedEvidenceIndexPath or VoterClientGuidanceMarkdownPath)
            .SelectMany(artifact => SecurityDependencySupportContracts.ScanForbiddenMaterial(
                artifact.Content,
                artifact.RelativePath == PublicSafeSummaryPath ? "public" : "restricted",
                artifact.RelativePath))
            .Concat(checkResult.ForbiddenMaterialFindings)
            .ToArray();

        var manifest = BuildManifest(
            packageId,
            releaseId,
            version,
            generatedAt,
            artifacts,
            paths,
            checkResult,
            scanFindings);
        artifacts.Add(BuildArtifact(ManifestPath, manifest));

        return new SecurityDependencySupportGeneratedPackage(
            scanFindings.Length > 0 ? "blocked" : checkResult.Status,
            checkResult,
            artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).ToArray(),
            scanFindings);
    }

    private static JsonObject BuildPackage(
        JsonObject source,
        string releaseId,
        string version,
        DateTimeOffset generatedAt,
        string publicationStatus,
        SecurityDependencySupportCheckSet checkResult)
    {
        var package = source.DeepClone().AsObject();
        var releaseScope = SecurityDependencySupportContracts.RequireObject(package, "releaseScope");
        releaseScope["releaseId"] = releaseId;
        releaseScope["releaseScopeId"] = releaseId;
        releaseScope["version"] = version;
        releaseScope["artifactRefs"] = new JsonArray("deployment proof package refs", "operational evidence refs");
        package["generatedAt"] = SecurityDependencySupportContracts.FormatTimestamp(generatedAt);
        package["generatedBy"] = "Security dependency support package generator";
        package["status"] = checkResult.Status;
        package["publicationStatus"] = publicationStatus;
        package.Remove("sourceRefs");
        package["dependencyInventoryRef"] = DependencyInventoryPath;
        package["licenseScanRef"] = LicenseScanPath;
        package["vulnerabilityScanRef"] = VulnerabilityScanPath;
        package["disclosureProcessRef"] = DisclosureProcessPath;
        package["accessibilityEvidenceRef"] = AccessibilityEvidencePath;
        package["voterClientGuidanceRef"] = VoterClientGuidanceMarkdownPath;
        package["supportReadinessRef"] = SupportReadinessPath;
        package["supportExportPrivacyProofRef"] = SupportExportPrivacyProofPath;
        package["validationResults"] = new JsonObject
        {
            ["status"] = checkResult.Status,
            ["blockers"] = ToJsonArray(checkResult.Blockers),
            ["warnings"] = ToJsonArray(checkResult.Warnings),
            ["notApplicable"] = ToJsonArray(checkResult.NotApplicable),
            ["checkIds"] = ToJsonArray(checkResult.Checks.Select(check => check.CheckId)),
        };
        return SanitizeForPackage(package)!.AsObject();
    }

    private static JsonObject BuildReadinessFragment(
        SecurityDependencySupportSourceSet sources,
        SecurityDependencySupportCheckSet checkResult,
        DateTimeOffset generatedAt)
    {
        return new JsonObject
        {
            ["evidenceId"] = ExternalReadinessEvidenceId,
            ["evidenceScope"] = "security_dependency_support_readiness",
            ["sourceGap"] = SecurityDependencySupportContracts.GetString(sources.Package, "sourceGap"),
            ["readinessAreas"] = new JsonArray("dependency_license", "vulnerability_freshness", "disclosure_process", "accessibility", "voter_client_integrity", "support_privacy"),
            ["evidenceRefs"] = new JsonArray(
                SecurityDependencySupportPromotionPaths.PackageFileName,
                DependencyInventoryPath,
                LicenseScanPath,
                VulnerabilityScanPath,
                DisclosureProcessPath,
                AccessibilityEvidencePath,
                VoterClientGuidanceMarkdownPath,
                SupportReadinessPath,
                SupportExportPrivacyProofPath),
            ["blockerChanges"] = ToJsonArray(checkResult.Blockers),
            ["warnings"] = ToJsonArray(checkResult.Warnings),
            ["freshness"] = sources.Package["freshness"]?.DeepClone(),
            ["claimEffect"] = new JsonObject
            {
                ["status"] = checkResult.Status,
                ["acceptedScore"] = checkResult.Status == "accepted" ? 8 : checkResult.Status == "accepted_with_warnings" ? 7 : 5,
                ["targetGapSize"] = checkResult.Status == "accepted" ? 1 : checkResult.Status == "accepted_with_warnings" ? 2 : 3,
                ["claimImpact"] = SanitizeForPackage(sources.Package["claimImpact"]),
            },
            ["residualRisk"] = new JsonObject
            {
                ["notAvailableV1"] = new JsonArray("qr_cross_device_verification", "native_mobile_app_integrity"),
                ["commercialLicensingOutOfScope"] = true,
                ["scopeBoundary"] = "technical_and_delivery_readiness_only",
            },
            ["signoff"] = new JsonObject
            {
                ["generatedAt"] = SecurityDependencySupportContracts.FormatTimestamp(generatedAt),
                ["owner"] = "AboimPinto Consulting security maintainer",
            },
            ["useInstructions"] = "Use this evidence after owner review. Do not infer stronger claims than the evidence supports.",
        };
    }

    private static JsonObject BuildSecuritySupportHandoff(
        SecurityDependencySupportSourceSet sources,
        SecurityDependencySupportCheckSet checkResult,
        DateTimeOffset generatedAt)
    {
        var exceptionIds = SecurityDependencySupportContracts.RequireArray(sources.Exceptions, "exceptions")
            .OfType<JsonObject>()
            .Select(record => SecurityDependencySupportContracts.GetString(record, "exceptionId"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();

        return new JsonObject
        {
            ["handoffId"] = ExternalSecuritySupportHandoffId,
            ["producer"] = "security_dependency_support_readiness",
            ["status"] = checkResult.Status,
            ["releaseScope"] = SanitizeForPackage(SecurityDependencySupportContracts.RequireObject(sources.Package, "releaseScope")),
            ["acceptedEvidenceIds"] = new JsonArray(
                ExternalReadinessEvidenceId,
                "SDS-INV-HV-REL-BASELINE-2026-05",
                "SDS-LIC-HV-REL-BASELINE-2026-05",
                "SDS-VULN-HV-REL-BASELINE-2026-05"),
            ["unresolvedBlockers"] = ToJsonArray(checkResult.Blockers),
            ["acceptedExceptions"] = ToJsonArray(exceptionIds),
            ["claimImpact"] = SanitizeForPackage(sources.Package["claimImpact"]),
            ["freshness"] = SanitizeForPackage(sources.Package["freshness"]),
            ["publicSafeSummaryPath"] = PublicSafeSummaryPath,
            ["restrictedEvidenceIndexPath"] = RestrictedEvidenceIndexPath,
            ["consumerInstructions"] = new JsonObject
            {
                ["readinessRegister"] = "Import this readiness evidence only after owner review; do not infer stronger claims than the evidence supports.",
                ["pilotEvidencePackage"] = "Use this handoff to bind pilot evidence, unresolved blockers, and accepted exceptions.",
                ["readinessDashboard"] = "Display warning and blocker ids only after promotion through the readiness register; do not expose restricted findings.",
            },
            ["generatedAt"] = SecurityDependencySupportContracts.FormatTimestamp(generatedAt),
        };
    }

    private static JsonObject BuildManifest(
        string packageId,
        string releaseId,
        string version,
        DateTimeOffset generatedAt,
        IReadOnlyList<SecurityDependencySupportGeneratedArtifact> artifacts,
        SecurityDependencySupportPromotionPaths paths,
        SecurityDependencySupportCheckSet checkResult,
        IReadOnlyList<SecurityDependencySupportMaterialFinding> scanFindings)
    {
        var manifest = new JsonObject
        {
            ["manifestId"] = $"SDS-MANIFEST-{releaseId}-{version}",
            ["packageId"] = packageId,
            ["releaseScopeId"] = releaseId,
            ["version"] = version,
            ["generatedAt"] = SecurityDependencySupportContracts.FormatTimestamp(generatedAt),
            ["canonicalizationVersion"] = SecurityDependencySupportContracts.CanonicalizationVersion,
            ["files"] = new JsonArray(artifacts
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .Select(artifact => new JsonObject
                {
                    ["path"] = artifact.RelativePath,
                    ["sha256Hash"] = artifact.Sha256Hash,
                })
                .ToArray<JsonNode?>()),
            ["hashes"] = new JsonObject
            {
                ["packageHash"] = artifacts.Single(artifact => artifact.RelativePath == SecurityDependencySupportPromotionPaths.PackageFileName).Sha256Hash,
                ["manifestHash"] = "computed_after_manifest_finalization",
            },
            ["schemaHashes"] = new JsonArray(SecurityDependencySupportContracts.RequiredSchemaFiles
                .Select(schemaFile => new JsonObject
                {
                    ["path"] = $"schemas/{schemaFile}",
                    ["sha256Hash"] = File.Exists(Path.Combine(paths.SchemasRoot, schemaFile))
                        ? SecurityDependencySupportContracts.Sha256FileHex(Path.Combine(paths.SchemasRoot, schemaFile))
                        : "missing",
                })
                .ToArray<JsonNode?>()),
            ["archive"] = new JsonObject
            {
                ["path"] = $"archives/{releaseId}-{version}-security-dependency-support.zip",
                ["sha256Hash"] = "computed_after_archive_generation"
            },
            ["sourceRefs"] = artifacts.Single(artifact => artifact.RelativePath == SecurityDependencySupportPromotionPaths.PackageFileName)
                .Content.Contains("\"sourceRefs\"", StringComparison.Ordinal)
                    ? JsonNode.Parse(artifacts.Single(artifact => artifact.RelativePath == SecurityDependencySupportPromotionPaths.PackageFileName).Content)!.AsObject()["sourceRefs"]!.DeepClone()
                    : new JsonObject(),
            ["validationResults"] = new JsonObject
            {
                ["status"] = checkResult.Status,
                ["blockers"] = ToJsonArray(checkResult.Blockers),
                ["warnings"] = ToJsonArray(checkResult.Warnings),
                ["checks"] = new JsonArray(checkResult.Checks
                    .Select(check => new JsonObject
                    {
                        ["checkId"] = check.CheckId,
                        ["name"] = check.Name,
                        ["status"] = check.Status,
                        ["reason"] = check.Reason,
                    })
                    .ToArray<JsonNode?>()),
            },
            ["redactionScanResults"] = new JsonArray(scanFindings
                .Select(finding => new JsonObject
                {
                    ["boundary"] = finding.Boundary,
                    ["path"] = finding.RelativePath,
                    ["category"] = finding.Category,
                    ["evidence"] = finding.Evidence,
                })
                .ToArray<JsonNode?>()),
        };
        return SanitizeForPackage(manifest)!.AsObject();
    }

    private static string RenderVoterClientGuidance(JsonObject guidance)
    {
        var notAvailable = SecurityDependencySupportContracts.RequireArray(guidance, "notAvailableV1Paths")
            .OfType<JsonObject>()
            .Select(item => $"- {SecurityDependencySupportContracts.GetString(item, "path")}: {SecurityDependencySupportContracts.GetString(item, "reason")}")
            .ToArray();
        var limitations = SecurityDependencySupportContracts.RequireArray(guidance, "sameDeviceLimitations")
            .OfType<JsonValue>()
            .Select(item => $"- {item.GetValue<string>()}")
            .ToArray();

        return NormalizeMarkdown($"""
            # Voter Client Integrity Guidance

            Guidance id: {SecurityDependencySupportContracts.GetString(guidance, "guidanceId")}

            Claim-bearing paths:
            {RenderBulletList(SecurityDependencySupportContracts.RequireArray(guidance, "claimBearingPaths"))}

            Supported v1 paths:
            {RenderBulletList(SecurityDependencySupportContracts.RequireArray(guidance, "supportedPaths"))}

            Same-device limitations:
            {string.Join('\n', limitations.Select(SanitizeText))}

            Not available in v1:
            {string.Join('\n', notAvailable.Select(SanitizeText))}
            """);
    }

    private static string RenderRestrictedEvidenceIndex(
        JsonObject package,
        SecurityDependencySupportCheckSet checkResult,
        IReadOnlyList<SecurityDependencySupportGeneratedArtifact> artifacts)
    {
        var files = string.Join(
            '\n',
            artifacts
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .Select(artifact => $"- `{artifact.RelativePath}` `{artifact.Sha256Hash}`"));

        return NormalizeMarkdown($"""
            # Restricted Security Dependency Support Evidence Index

            Package: {SecurityDependencySupportContracts.GetString(package, "packageId")}
            Status: {checkResult.Status}

            Reviewer files:
            {files}

            SDS blockers:
            {RenderStringList(checkResult.Blockers)}

            SDS warnings:
            {RenderStringList(checkResult.Warnings)}

            Review instructions:
            Keep detailed security findings, private scanner reports, operator contact data, tenant-specific evidence, and support case records restricted. This package is for technical and delivery readiness review.
            """);
    }

    private static string RenderPublicSafeSummary(
        JsonObject package,
        SecurityDependencySupportSourceSet sources,
        SecurityDependencySupportCheckSet checkResult)
    {
        var licenseFindings = SecurityDependencySupportContracts.RequireArray(sources.LicenseScan, "licenseFindings").Count;
        var vulnerabilityFindings = SecurityDependencySupportContracts.RequireArray(sources.VulnerabilityScan, "findings").Count;
        var releaseScope = SecurityDependencySupportContracts.RequireObject(package, "releaseScope");
        return NormalizeMarkdown($"""
            # Public-Safe Security Dependency Support Summary

            Package: {SecurityDependencySupportContracts.GetString(package, "packageId")}
            Release scope: {SecurityDependencySupportContracts.GetString(releaseScope, "releaseId")}
            Status: {checkResult.Status}

            Summary counts:
            - SDS checks: {checkResult.Checks.Count}
            - Blocking checks: {checkResult.Blockers.Count}
            - Warning checks: {checkResult.Warnings.Count}
            - License findings: {licenseFindings}
            - Vulnerability findings: {vulnerabilityFindings}

            Claim boundaries:
            - Desktop web and mobile web are the v1 claim-bearing client paths.
            - QR/cross-device verification and native mobile app integrity are not available in v1.
            - Commercial provider/customer licensing is outside this readiness package.
            - This package is technical and delivery readiness evidence; it does not make claims beyond the listed scope.
            """);
    }

    private static SecurityDependencySupportGeneratedArtifact BuildArtifact(string relativePath, JsonNode content)
    {
        var text = SecurityDependencySupportContracts.CanonicalJson(SanitizeForPackage(content) ?? new JsonObject());
        return new SecurityDependencySupportGeneratedArtifact(
            relativePath,
            text,
            SecurityDependencySupportContracts.Sha256Hex(text));
    }

    private static SecurityDependencySupportGeneratedArtifact BuildArtifact(string relativePath, string content)
    {
        var text = NormalizeMarkdown(content);
        return new SecurityDependencySupportGeneratedArtifact(
            relativePath,
            text,
            SecurityDependencySupportContracts.Sha256Hex(text));
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());

    private static string RenderBulletList(JsonArray values) =>
        string.Join('\n', values.OfType<JsonValue>().Select(value => $"- {value.GetValue<string>()}"));

    private static string RenderStringList(IEnumerable<string> values)
    {
        var items = values.ToArray();
        return items.Length == 0
            ? "- none"
            : string.Join('\n', items.Select(value => $"- {value}"));
    }

    private static string NormalizeMarkdown(string value)
    {
        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var nonEmpty = lines.Where(line => line.Length > 0).ToArray();
        var leadingSpaces = nonEmpty.Length == 0
            ? 0
            : nonEmpty
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.TakeWhile(char.IsWhiteSpace).Count())
                .DefaultIfEmpty(0)
                .Min();
        var normalized = string.Join('\n', lines.Select(line => line.Length >= leadingSpaces ? line[leadingSpaces..].TrimEnd() : line.TrimEnd())).Trim();
        return normalized + "\n";
    }

    private static JsonNode? SanitizeForPackage(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject obj:
                var cleanObject = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    var cleanKey = SanitizeKey(key);
                    if (cleanKey is null)
                    {
                        continue;
                    }

                    cleanObject[cleanKey] = SanitizeForPackage(value);
                }

                return cleanObject;
            case JsonArray array:
                return new JsonArray(array.Select(SanitizeForPackage).ToArray());
            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(SanitizeText(text));
            default:
                return node.DeepClone();
        }
    }

    private static string? SanitizeKey(string key)
    {
        return key switch
        {
            "featureId" => null,
            "featureSlice" => null,
            "acceptanceGate" => null,
            "dimensionIds" => null,
            "doesNotMutateRegister" => null,
            "registerPromotionOwner" => null,
            "producerFeature" => null,
            "promotionInstructions" => null,
            "feat103EvidenceRefs" => "accessibilityEvidenceRefs",
            _ => key,
        };
    }

    private static string SanitizeText(string value)
    {
        return value
            .Replace(SecurityDependencySupportContracts.ReadinessFragmentId, ExternalReadinessEvidenceId, StringComparison.Ordinal)
            .Replace("FEAT-132 deployment proof", "deployment proof", StringComparison.Ordinal)
            .Replace("FEAT-133 operational evidence", "operational evidence", StringComparison.Ordinal)
            .Replace("FEAT-134", "security dependency support readiness", StringComparison.Ordinal)
            .Replace("FEAT-130", "readiness register", StringComparison.Ordinal)
            .Replace("FEAT-141", "pilot evidence package", StringComparison.Ordinal)
            .Replace("FEAT-142", "readiness dashboard", StringComparison.Ordinal)
            .Replace("FEAT-132", "deployment proof", StringComparison.Ordinal)
            .Replace("FEAT-133", "operational evidence", StringComparison.Ordinal)
            .Replace("FEAT-121", "mobile platform evidence", StringComparison.Ordinal)
            .Replace("FEAT-103", "accessibility evidence", StringComparison.Ordinal)
            .Replace("EPIC-015", "technical delivery readiness", StringComparison.Ordinal)
            .Replace("AT-RDY-014", "security dependency support readiness", StringComparison.Ordinal)
            .Replace("RDY-DIM-008", "dependency-support-readiness", StringComparison.Ordinal)
            .Replace("RDY-DIM-009", "support-privacy-readiness", StringComparison.Ordinal)
            .Replace("later feature delivers it", "later delivery provides it", StringComparison.Ordinal);
    }
}
