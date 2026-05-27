using System.Text.Json.Nodes;

namespace PublicationCountingHardeningPromoter;

public sealed record PublicationCountingTamperStaleCaseRequirement(
    string CaseId,
    string Category,
    string ChangedArtifact,
    string ExpectedPrimaryResultCode,
    string Enforcement);

public sealed record PublicationCountingTamperStaleMatrixResult(
    string Status,
    IReadOnlyList<string> Errors,
    JsonObject Diagnostics)
{
    public bool Passed => Errors.Count == 0;
}

public static class PublicationCountingTamperStaleMatrix
{
    public static readonly PublicationCountingTamperStaleCaseRequirement[] RequiredCases =
    [
        new("TM-STALE-MANIFEST-HASH", "stale_currentness", "corpus-manifest.json", "source_release_manifest_hash_mismatch", "source-currentness-validation"),
        new("TM-STALE-VERIFIER-SOURCE-REF", "stale_currentness", "corpus-manifest.json", "verifier_source_ref_mismatch", "source-currentness-validation"),
        new("TM-STALE-VERIFIER-BINARY-HASH", "stale_currentness", "corpus-manifest.json", "verifier_binary_hash_mismatch", "source-currentness-validation"),
        new("TM-STALE-PACKAGE-HASH", "stale_currentness", "packageRefs.packageHash", "package_hash_mismatch", "source-currentness-validation"),
        new("TM-EXPECTED-RESULT-DRIFT", "stale_currentness", "expected-results/sample-good-finalized-election.json", "expected_result_hash_mismatch", "source-currentness-validation"),
        new("TM-MISSING-PUBLICATION-PROOF", "accepted_to_published", "artifacts/election-record/publication-proof-transcript.json", "missing_publication_proof_transcript", "binding-check-replay"),
        new("TM-PUBLISHED-COUNT-MISMATCH", "accepted_to_published", "artifacts/election-record/published-ballot-stream.json", "published_ballot_count_mismatch", "binding-check-replay"),
        new("TM-PUBLISHED-DUPLICATE", "accepted_to_published", "artifacts/election-record/published-ballot-stream.json", "published_ballot_duplicate", "binding-check-replay"),
        new("TM-PUBLISHED-REMOVAL", "accepted_to_published", "artifacts/election-record/published-ballot-stream.json", "published_ballot_removed", "binding-check-replay"),
        new("TM-PUBLISHED-INSERTION", "accepted_to_published", "artifacts/election-record/published-ballot-stream.json", "published_ballot_inserted", "binding-check-replay"),
        new("TM-PUBLISHED-REPLACEMENT", "accepted_to_published", "artifacts/election-record/published-ballot-stream.json", "published_ballot_replaced", "binding-check-replay"),
        new("TM-WRONG-ELECTION", "accepted_to_published", "artifacts/election-record/published-ballot-stream.json", "election_id_mismatch", "binding-check-replay"),
        new("TM-WRONG-TALLY-TARGET", "tally_replay", "artifacts/election-record/tally-replay.json", "tally_target_mismatch", "binding-check-replay"),
    ];

    public static PublicationCountingTamperStaleMatrixResult Evaluate(
        JsonObject source,
        PublicationCountingBindingCheckResult acceptedToPublished,
        PublicationCountingBindingCheckResult tallyReplay)
    {
        var errors = new List<string>();
        var sourceCases = PublicationCountingHardeningContracts.RequireArray(source, "tamperAndStaleMatrix")
            .OfType<JsonObject>()
            .ToArray();
        var sourceById = sourceCases
            .GroupBy(item => PublicationCountingHardeningContracts.GetString(item, "caseId"), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var requiredCaseSummaries = new JsonArray();
        foreach (var required in RequiredCases)
        {
            if (!sourceById.TryGetValue(required.CaseId, out var sourceCase))
            {
                errors.Add($"tamperAndStaleMatrix is missing required case {required.CaseId}.");
                requiredCaseSummaries.Add(RequiredCaseSummary(required, "missing"));
                continue;
            }

            ValidateCase(required, sourceCase, errors);
            requiredCaseSummaries.Add(RequiredCaseSummary(required, "covered"));
        }

        foreach (var sourceCase in sourceCases)
        {
            if (!PublicationCountingHardeningContracts.GetBool(sourceCase, "blocksScoreMovement"))
            {
                errors.Add($"{PublicationCountingHardeningContracts.GetString(sourceCase, "caseId")} must block score movement.");
            }

            if (PublicationCountingHardeningContracts.GetInt(sourceCase, "expectedExitCode") == 0)
            {
                errors.Add($"{PublicationCountingHardeningContracts.GetString(sourceCase, "caseId")} must have a non-zero expected exit code.");
            }

            if (string.IsNullOrWhiteSpace(PublicationCountingHardeningContracts.GetString(sourceCase, "expectedPrimaryResultCode")))
            {
                errors.Add($"{PublicationCountingHardeningContracts.GetString(sourceCase, "caseId")} must have an expected primary result code.");
            }
        }

        if (!acceptedToPublished.Passed)
        {
            errors.Add("Current accepted-to-published binding must pass before tamper/stale matrix can be accepted.");
        }

        if (!tallyReplay.Passed)
        {
            errors.Add("Current tally replay binding must pass before tamper/stale matrix can be accepted.");
        }

        var limitations = new JsonArray();
        AppendStrings(limitations, PublicationCountingHardeningContracts.RequireArray(
            PublicationCountingHardeningContracts.RequireObject(source, "readinessProposal"),
            "nonClaims"));
        AppendStrings(limitations, PublicationCountingHardeningContracts.RequireArray(source, "residualRisks"));

        return new PublicationCountingTamperStaleMatrixResult(
            errors.Count == 0 ? "accepted" : "blocked",
            errors,
            new JsonObject
            {
                ["requiredCaseCount"] = RequiredCases.Length,
                ["sourceCaseCount"] = sourceCases.Length,
                ["requiredCases"] = requiredCaseSummaries,
                ["residualLimitations"] = limitations,
            });
    }

    private static void ValidateCase(
        PublicationCountingTamperStaleCaseRequirement required,
        JsonObject sourceCase,
        List<string> errors)
    {
        var observedCategory = PublicationCountingHardeningContracts.GetString(sourceCase, "category");
        if (!string.Equals(observedCategory, required.Category, StringComparison.Ordinal))
        {
            errors.Add($"{required.CaseId} category must be {required.Category}; observed {observedCategory}.");
        }

        var observedArtifact = PublicationCountingHardeningContracts.GetString(sourceCase, "changedArtifact");
        if (!string.Equals(observedArtifact, required.ChangedArtifact, StringComparison.Ordinal))
        {
            errors.Add($"{required.CaseId} changedArtifact must be {required.ChangedArtifact}; observed {observedArtifact}.");
        }

        var observedCode = PublicationCountingHardeningContracts.GetString(sourceCase, "expectedPrimaryResultCode");
        if (!string.Equals(observedCode, required.ExpectedPrimaryResultCode, StringComparison.Ordinal))
        {
            errors.Add($"{required.CaseId} expectedPrimaryResultCode must be {required.ExpectedPrimaryResultCode}; observed {observedCode}.");
        }

        if (!string.Equals(
                PublicationCountingHardeningContracts.GetString(sourceCase, "expectedOverallStatus"),
                "fail",
                StringComparison.Ordinal))
        {
            errors.Add($"{required.CaseId} expectedOverallStatus must be fail.");
        }
    }

    private static JsonObject RequiredCaseSummary(PublicationCountingTamperStaleCaseRequirement required, string coverageStatus) =>
        new()
        {
            ["caseId"] = required.CaseId,
            ["category"] = required.Category,
            ["changedArtifact"] = required.ChangedArtifact,
            ["expectedPrimaryResultCode"] = required.ExpectedPrimaryResultCode,
            ["enforcement"] = required.Enforcement,
            ["coverageStatus"] = coverageStatus,
        };

    private static void AppendStrings(JsonArray target, JsonArray source)
    {
        foreach (var item in source)
        {
            if (item is not null)
            {
                target.Add(JsonValue.Create(item.GetValue<string>()));
            }
        }
    }
}
