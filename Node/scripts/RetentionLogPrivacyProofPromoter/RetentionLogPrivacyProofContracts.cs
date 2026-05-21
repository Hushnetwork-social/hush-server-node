using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RetentionLogPrivacyProofPromoter;

public sealed record RetentionLogPrivacyProofPromotionPaths(
    string WorkspaceRoot,
    string OutputRoot)
{
    public string PackageOutputRoot => Path.Combine(OutputRoot, RetentionLogPrivacyProofContracts.ExternalPackageFolder);

    public static RetentionLogPrivacyProofPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);
        return new RetentionLogPrivacyProofPromotionPaths(
            root,
            Path.Combine(root, "hush-documents", "PrivateServer_ElectronicVoting", "Retention-Log-Privacy-Proof"));
    }
}

public static partial class RetentionLogPrivacyProofContracts
{
    public const string ExternalPackageFolder = "package";
    public const string PackagePath = "retention-log-privacy-proof-package.json";
    public const string RetentionPolicyPath = "retention-policy.json";
    public const string DataClassInventoryPath = "data-class-inventory.json";
    public const string SeparationScanPath = "schema-service-separation-scan.json";
    public const string InMemoryGuardProofPath = "in-memory-operation-guard-proof.json";
    public const string AtomicCastProofPath = "atomic-cast-proof.json";
    public const string LogTraceSupportScanPath = "log-trace-support-scan-results.json";
    public const string LegacyJoinMigrationEvidencePath = "legacy-join-migration-evidence.json";
    public const string ReadinessFragmentPath = "readiness-fragment.json";
    public const string DownstreamHandoffPath = "downstream-handoff.json";
    public const string PublicSummaryPath = "public-safe-retention-log-privacy-summary.md";
    public const string RestrictedEvidenceIndexPath = "restricted-retention-log-privacy-evidence-index.md";

    public static readonly string[] RequiredArtifactPaths =
    [
        PackagePath,
        RetentionPolicyPath,
        DataClassInventoryPath,
        SeparationScanPath,
        InMemoryGuardProofPath,
        AtomicCastProofPath,
        LogTraceSupportScanPath,
        LegacyJoinMigrationEvidencePath,
        ReadinessFragmentPath,
        DownstreamHandoffPath,
        PublicSummaryPath,
        RestrictedEvidenceIndexPath,
    ];

    public static readonly string[] RequiredCheckIds =
    [
        "RLP-000",
        "RLP-001",
        "RLP-002",
        "RLP-003",
        "RLP-004",
        "RLP-005",
        "RLP-006",
        "RLP-007",
        "RLP-008",
    ];

    private static readonly string[] ForbiddenInternalCodeFragments = ["FEAT-", "EPIC-"];
    private static readonly string[] LocalPathFragments = ["C:\\", "C:/", "\\myWork\\", "/home/", "/Users/", "file://"];
    private static readonly string[] SecretFragments =
    [
        "receiptSecret",
        "receipt_secret",
        "receipt secret=",
        "plaintextVote",
        "plaintext choice",
        "privateKey",
        "BEGIN PRIVATE KEY",
        "aws_secret_access_key",
        "client_secret",
    ];

    private static readonly string[] IdentityJoinFragments =
    [
        "organizationVoterId=",
        "linkedActorPublicAddress=",
        "voterEmail=",
        "accountIdentity=",
    ];

    private static readonly string[] BallotJoinFragments =
    [
        "preparedBallotId=",
        "acceptedBallotId=",
        "ballotNullifier=",
        "receiptCommitment=",
        "receiptCapability=",
    ];

    public static string CanonicalJson(JsonObject node) => RetentionLogPrivacyProofCanonicalJson.Serialize(node);

    public static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    public static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.Select(value => (JsonNode?)value).ToArray());

    public static JsonArray ToArtifactHashArray(IEnumerable<RetentionLogPrivacyProofGeneratedArtifact> artifacts) =>
        new(artifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .Select(artifact => new JsonObject
            {
                ["path"] = artifact.RelativePath,
                ["sha256Hash"] = artifact.Sha256Hash,
                ["visibility"] = artifact.Visibility.ToString().ToLowerInvariant(),
                ["mediaType"] = artifact.MediaType,
                ["sizeBytes"] = artifact.SizeBytes,
            })
            .ToArray<JsonNode?>());

    public static IReadOnlyList<RetentionLogPrivacyProofScanFinding> ScanGeneratedArtifacts(
        IEnumerable<RetentionLogPrivacyProofGeneratedArtifact> artifacts)
    {
        return artifacts
            .SelectMany(artifact => ScanText(artifact.RelativePath, artifact.Visibility.ToString().ToLowerInvariant(), artifact.Content))
            .OrderBy(finding => finding.RelativePath, StringComparer.Ordinal)
            .ThenBy(finding => finding.Category, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<RetentionLogPrivacyProofScanFinding> ScanText(
        string relativePath,
        string boundary,
        string content)
    {
        var findings = new List<RetentionLogPrivacyProofScanFinding>();
        foreach (var fragment in SecretFragments)
        {
            if (content.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(Finding(relativePath, boundary, "secret_material", fragment));
            }
        }

        if (IdentityJoinFragments.Any(fragment => content.Contains(fragment, StringComparison.OrdinalIgnoreCase)) &&
            BallotJoinFragments.Any(fragment => content.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(Finding(relativePath, boundary, "identity_to_ballot_join", "identity_field+ballot_field"));
        }

        if (LocalPathPattern().IsMatch(content) ||
            LocalPathFragments.Any(fragment => content.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(Finding(relativePath, boundary, "local_path", "local filesystem path"));
        }

        foreach (var fragment in ForbiddenInternalCodeFragments)
        {
            if (content.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(Finding(relativePath, boundary, "internal_code", fragment));
            }
        }

        return findings;
    }

    public static bool DeliberateForbiddenFixtureIsDetected()
    {
        const string fixture = "organizationVoterId=fixture-voter; receiptCommitment=fixture-receipt";
        return ScanText("fixtures/deliberate-identity-ballot-join.txt", "fixture", fixture)
            .Any(finding => finding.Category == "identity_to_ballot_join");
    }

    public static IReadOnlyList<string> ValidateGeneratedPackage(RetentionLogPrivacyProofGeneratedPackage package)
    {
        var errors = new List<string>();
        var byPath = package.Artifacts.ToDictionary(artifact => artifact.RelativePath, StringComparer.Ordinal);
        foreach (var requiredPath in RequiredArtifactPaths)
        {
            if (!byPath.ContainsKey(requiredPath))
            {
                errors.Add($"Missing required artifact: {requiredPath}");
            }
        }

        var packageArtifact = byPath.GetValueOrDefault(PackagePath);
        if (packageArtifact is not null)
        {
            errors.AddRange(ValidatePackageManifestHashes(packageArtifact, byPath));
        }

        errors.AddRange(ScanGeneratedArtifacts(package.Artifacts)
            .Select(finding => $"Forbidden generated material: {finding.RelativePath}:{finding.Category}:{finding.Evidence}"));

        if (!DeliberateForbiddenFixtureIsDetected())
        {
            errors.Add("Deliberate forbidden-material fixture was not detected.");
        }

        var checkIds = package.CheckResult.Checks.Select(check => check.CheckId).ToHashSet(StringComparer.Ordinal);
        foreach (var checkId in RequiredCheckIds)
        {
            if (!checkIds.Contains(checkId))
            {
                errors.Add($"Missing required check: {checkId}");
            }
        }

        if (package.CheckResult.Blockers.Count > 0)
        {
            errors.AddRange(package.CheckResult.Blockers.Select(blocker => $"Unresolved blocker: {blocker}"));
        }

        return errors;
    }

    private static IEnumerable<string> ValidatePackageManifestHashes(
        RetentionLogPrivacyProofGeneratedArtifact packageArtifact,
        IReadOnlyDictionary<string, RetentionLogPrivacyProofGeneratedArtifact> byPath)
    {
        if (JsonNode.Parse(packageArtifact.Content) is not JsonObject package)
        {
            yield return "Package artifact is not a JSON object.";
            yield break;
        }

        if (package["artifactHashes"] is not JsonArray artifactHashes)
        {
            yield return "Package artifact missing artifactHashes.";
            yield break;
        }

        foreach (var entry in artifactHashes.OfType<JsonObject>())
        {
            var path = entry["path"]?.GetValue<string>() ?? string.Empty;
            var expectedHash = entry["sha256Hash"]?.GetValue<string>() ?? string.Empty;
            if (!byPath.TryGetValue(path, out var artifact))
            {
                yield return $"Package artifact hash references missing path: {path}";
                continue;
            }

            if (!string.Equals(expectedHash, artifact.Sha256Hash, StringComparison.Ordinal))
            {
                yield return $"Artifact hash mismatch for {path}.";
            }
        }
    }

    private static RetentionLogPrivacyProofScanFinding Finding(
        string relativePath,
        string boundary,
        string category,
        string evidence) =>
        new(
            relativePath,
            boundary,
            category,
            evidence,
            "Accepted retention/log privacy evidence is blocked until generated material is redacted.");

    [GeneratedRegex(@"[A-Za-z]:[\\/][^\s`""]+", RegexOptions.Compiled)]
    private static partial Regex LocalPathPattern();
}
