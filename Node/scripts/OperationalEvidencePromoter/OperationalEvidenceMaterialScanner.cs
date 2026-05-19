using System.Text.RegularExpressions;

namespace OperationalEvidencePromoter;

public sealed record OperationalEvidenceMaterialFinding(
    string RelativePath,
    string Boundary,
    string Category,
    string Evidence,
    string ClaimImpact);

public static partial class OperationalEvidenceMaterialScanner
{
    private static readonly (string Category, string Fragment)[] PublicForbiddenFragments =
    [
        ("credential", "BEGIN PRIVATE KEY"),
        ("credential", "aws_secret_access_key"),
        ("credential", "aws_access_key_id"),
        ("credential", "AKIA"),
        ("credential", "password="),
        ("credential", "secret="),
        ("credential", "client_secret"),
        ("credential", "token="),
        ("kms", "arn:aws:kms"),
        ("kms", "kmsKeyId"),
        ("kms", "kms_key_id"),
        ("kms", "kmsAlias"),
        ("kms", "alias/"),
        ("decrypt_authority", "decrypt authority"),
        ("raw_log", "rawLogLine"),
        ("raw_log", "raw log"),
        ("raw_log", "raw support log"),
        ("raw_log", "raw anomaly log"),
        ("operator_contact", "operator contact"),
        ("support_correlation", "support correlation"),
        ("voter_data", "voter data row"),
        ("voter_data", "voterEmail"),
        ("vote_choice", "voteChoice"),
        ("trustee_secret", "trusteeShare"),
        ("scalar_material", "plaintextScalar"),
    ];

    private static readonly (string Category, string Fragment)[] RestrictedForbiddenFragments =
    [
        ("credential", "BEGIN PRIVATE KEY"),
        ("credential", "aws_secret_access_key"),
        ("credential", "aws_access_key_id"),
        ("credential", "AKIA"),
        ("credential", "password="),
        ("credential", "secret="),
        ("credential", "client_secret"),
        ("credential", "token="),
        ("vote_choice", "voteChoice"),
        ("trustee_secret", "trusteeShare"),
        ("scalar_material", "plaintextScalar"),
        ("scalar_material", "plaintext scalar"),
    ];

    public static IReadOnlyList<OperationalEvidenceMaterialFinding> ScanGeneratedArtifacts(
        IEnumerable<OperationalEvidenceGeneratedArtifact> artifacts)
    {
        var findings = new List<OperationalEvidenceMaterialFinding>();
        foreach (var artifact in artifacts)
        {
            if (artifact.Visibility == OperationalEvidenceArtifactVisibility.Public)
            {
                findings.AddRange(ScanText(
                    artifact.RelativePath,
                    "public",
                    artifact.Content,
                    PublicForbiddenFragments,
                    rejectProviderAccountIds: true));
            }
            else if (artifact.Visibility == OperationalEvidenceArtifactVisibility.Restricted)
            {
                findings.AddRange(ScanText(
                    artifact.RelativePath,
                    "restricted",
                    artifact.Content,
                    RestrictedForbiddenFragments,
                    rejectProviderAccountIds: false));
            }
        }

        return findings
            .OrderBy(finding => finding.RelativePath, StringComparer.Ordinal)
            .ThenBy(finding => finding.Category, StringComparer.Ordinal)
            .ThenBy(finding => finding.Evidence, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<OperationalEvidenceMaterialFinding> ScanText(
        string relativePath,
        string boundary,
        string content,
        IReadOnlyList<(string Category, string Fragment)> fragments,
        bool rejectProviderAccountIds)
    {
        foreach (var (category, fragment) in fragments)
        {
            if (content.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                yield return new OperationalEvidenceMaterialFinding(
                    relativePath,
                    boundary,
                    category,
                    fragment,
                    "Accepted FEAT-133 evidence is blocked until generated material is redacted.");
            }
        }

        if (rejectProviderAccountIds && DirectProviderAccountIdRegex().IsMatch(content))
        {
            yield return new OperationalEvidenceMaterialFinding(
                relativePath,
                boundary,
                "provider_account_identifier",
                "12-digit provider account id",
                "Accepted FEAT-133 evidence is blocked until generated material is redacted.");
        }
    }

    [GeneratedRegex(@"\b\d{12}\b", RegexOptions.Compiled)]
    private static partial Regex DirectProviderAccountIdRegex();
}
