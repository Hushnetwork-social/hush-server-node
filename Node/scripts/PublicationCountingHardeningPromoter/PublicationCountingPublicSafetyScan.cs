using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace PublicationCountingHardeningPromoter;

public sealed record PublicationCountingPublicSafetyFinding(
    string ArtifactPath,
    string SignalId,
    string Description);

public sealed record PublicationCountingPublicSafetyScanResult(
    string Status,
    IReadOnlyList<PublicationCountingPublicSafetyFinding> Findings)
{
    public bool Passed => Findings.Count == 0;
}

public static partial class PublicationCountingPublicSafetyScan
{
    private static readonly (string SignalId, Regex Pattern, string Description)[] ForbiddenSignals =
    [
        ("local_absolute_path", WindowsAbsolutePathRegex(), "Local absolute path detected."),
        ("unc_path", UncPathRegex(), "UNC path detected."),
        ("shuffle_map_field", JsonFieldRegex("shuffleMap|shuffle_map|shuffleMaps|shuffle_maps"), "Shuffle map field detected."),
        ("rerandomization_randomness_field", JsonFieldRegex("rerandomizationRandomness|rerandomization_randomness"), "Rerandomization randomness field detected."),
        ("plaintext_choice_field", JsonFieldRegex("plaintextChoice|plaintextChoices|plaintext_choice|plaintext_choices"), "Plaintext choice field detected."),
        ("voter_identity_join_field", JsonFieldRegex("voterIdentityJoin|voter_identity_join|voterIdentityJoins|voter_identity_joins"), "Voter identity join field detected."),
        ("kms_secret_field", JsonFieldRegex("kmsSecret|kmsSecrets|kms_secret|kms_secrets"), "KMS secret field detected."),
        ("support_case_data_field", JsonFieldRegex("supportCaseData|support_case_data"), "Support case data field detected."),
        ("cloud_account_identifier_field", JsonFieldRegex("cloudAccountIdentifier|cloud_account_identifier|cloudAccountId|cloud_account_id"), "Cloud account identifier field detected."),
        ("database_connection_string_field", JsonFieldRegex("databaseConnectionString|database_connection_string"), "Database connection string field detected."),
    ];

    public static PublicationCountingPublicSafetyScanResult Scan(
        IReadOnlyList<PublicationCountingHardeningArtifact> artifacts)
    {
        var findings = new List<PublicationCountingPublicSafetyFinding>();
        foreach (var artifact in artifacts)
        {
            foreach (var signal in ForbiddenSignals)
            {
                if (signal.Pattern.IsMatch(artifact.Content))
                {
                    findings.Add(new PublicationCountingPublicSafetyFinding(
                        artifact.RelativePath,
                        signal.SignalId,
                        signal.Description));
                }
            }
        }

        return new PublicationCountingPublicSafetyScanResult(
            findings.Count == 0 ? "pass" : "fail",
            findings);
    }

    public static JsonObject ToJson(PublicationCountingPublicSafetyScanResult result) =>
        new()
        {
            ["status"] = result.Status,
            ["unexpectedFindingCount"] = result.Findings.Count,
            ["findings"] = new JsonArray(result.Findings
                .Select(finding => new JsonObject
                {
                    ["artifactPath"] = finding.ArtifactPath,
                    ["signalId"] = finding.SignalId,
                    ["description"] = finding.Description,
                })
                .ToArray<JsonNode?>()),
        };

    private static Regex JsonFieldRegex(string fieldAlternation) =>
        new($"\"(?:{fieldAlternation})\"\\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [GeneratedRegex(@"[A-Za-z]:\\", RegexOptions.Compiled)]
    private static partial Regex WindowsAbsolutePathRegex();

    [GeneratedRegex(@"\\\\[A-Za-z0-9_.-]+\\", RegexOptions.Compiled)]
    private static partial Regex UncPathRegex();
}
