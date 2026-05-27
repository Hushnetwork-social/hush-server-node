using System.Text.Json.Nodes;

namespace PublicationCountingHardeningPromoter;

public static class PublicationCountingReadinessOutputValidator
{
    private static readonly string[] RequiredEvidenceRefs =
    [
        PublicationCountingHardeningArtifactGenerator.ManifestPath,
        PublicationCountingHardeningArtifactGenerator.PackageVerifierReplaySummaryPath,
        PublicationCountingHardeningArtifactGenerator.AcceptedToPublishedBindingSummaryPath,
        PublicationCountingHardeningArtifactGenerator.TallyReplayBindingSummaryPath,
        PublicationCountingHardeningArtifactGenerator.TamperStaleReplaySummaryPath,
        PublicationCountingHardeningArtifactGenerator.PackageHashCurrentnessSummaryPath,
        PublicationCountingHardeningArtifactGenerator.NoSecretScanResultPath,
    ];

    public static IReadOnlyList<string> Validate(PublicationCountingHardeningGeneratedPackage generated)
    {
        var errors = new List<string>();
        var fragment = ArtifactJson(generated, PublicationCountingHardeningArtifactGenerator.ReadinessFragmentPath);
        var scoreProposal = ArtifactJson(generated, PublicationCountingHardeningArtifactGenerator.ScoreProposalPath);
        var expectedStatus = generated.Status == "accepted" ? "accepted" : "blocked";

        RequireValue(fragment, "schemaVersion", "publication-counting-readiness-fragment.v1", errors);
        RequireValue(fragment, "producerFeature", PublicationCountingHardeningContracts.FeatureId, errors);
        RequireValue(fragment, "dimensionId", PublicationCountingHardeningContracts.TargetDimensionId, errors);
        RequireValue(fragment, "status", expectedStatus, errors);
        RequireValue(PublicationCountingHardeningContracts.RequireObject(fragment, "scoreEffect"), "doesNotMutateRegister", true, errors);
        RequireValue(PublicationCountingHardeningContracts.RequireObject(fragment, "scoreEffect"), "proposedScoreFrom", 7, errors);
        RequireValue(PublicationCountingHardeningContracts.RequireObject(fragment, "scoreEffect"), "proposedScoreTo", 8, errors);
        ValidateEvidenceRefs(fragment, errors);
        ValidateArtifactHashes(fragment, errors);

        RequireValue(scoreProposal, "schemaVersion", "publication-counting-score-proposal.v1", errors);
        RequireValue(scoreProposal, "producerFeature", PublicationCountingHardeningContracts.FeatureId, errors);
        RequireValue(scoreProposal, "status", expectedStatus, errors);
        RequireValue(scoreProposal, "registerMutation", "not_performed", errors);
        RequireValue(PublicationCountingHardeningContracts.RequireObject(scoreProposal, "proposal"), "dimensionId", PublicationCountingHardeningContracts.TargetDimensionId, errors);
        RequireValue(PublicationCountingHardeningContracts.RequireObject(scoreProposal, "proposal"), "proposedScoreFrom", 7, errors);
        RequireValue(PublicationCountingHardeningContracts.RequireObject(scoreProposal, "proposal"), "proposedScoreTo", 8, errors);
        RequireValue(PublicationCountingHardeningContracts.RequireObject(scoreProposal, "proposal"), "doesNotMutateRegister", true, errors);
        ValidateArtifactHashes(scoreProposal, errors);
        ValidateNonClaims(PublicationCountingHardeningContracts.RequireObject(scoreProposal, "proposal"), errors);

        return errors;
    }

    private static JsonObject ArtifactJson(PublicationCountingHardeningGeneratedPackage generated, string relativePath)
    {
        var artifact = generated.Artifacts.SingleOrDefault(item => item.RelativePath == relativePath)
            ?? throw new PublicationCountingHardeningPromotionException($"Missing generated artifact: {relativePath}");
        return JsonNode.Parse(artifact.Content)!.AsObject();
    }

    private static void ValidateEvidenceRefs(JsonObject fragment, List<string> errors)
    {
        var refs = PublicationCountingHardeningContracts.RequireArray(fragment, "evidenceRefs")
            .Select(item => item?.GetValue<string>() ?? "")
            .ToHashSet(StringComparer.Ordinal);
        foreach (var required in RequiredEvidenceRefs)
        {
            if (!refs.Contains(required))
            {
                errors.Add($"Readiness fragment is missing evidence ref {required}.");
            }
        }
    }

    private static void ValidateArtifactHashes(JsonObject source, List<string> errors)
    {
        var hashes = PublicationCountingHardeningContracts.RequireArray(source, "evidenceArtifactHashes");
        if (hashes.Count == 0)
        {
            errors.Add("Readiness output must include evidenceArtifactHashes.");
            return;
        }

        foreach (var item in hashes.OfType<JsonObject>())
        {
            var path = PublicationCountingHardeningContracts.GetString(item, "path");
            var hash = PublicationCountingHardeningContracts.GetString(item, "sha256Hash");
            if (string.IsNullOrWhiteSpace(path))
            {
                errors.Add("Readiness output contains an evidence hash without a path.");
            }

            if (!hash.StartsWith("sha256:", StringComparison.Ordinal) || hash.Length != 71)
            {
                errors.Add($"Readiness output evidence hash for {path} must be sha256:<64 hex>.");
            }
        }
    }

    private static void ValidateNonClaims(JsonObject proposal, List<string> errors)
    {
        var nonClaims = PublicationCountingHardeningContracts.RequireArray(proposal, "nonClaims")
            .Select(item => item?.GetValue<string>() ?? "")
            .ToArray();
        if (!nonClaims.Any(item => item.Contains("No production rollout claim", StringComparison.Ordinal)) ||
            !nonClaims.Any(item => item.Contains("No public or state election readiness claim", StringComparison.Ordinal)))
        {
            errors.Add("Score proposal must preserve production and public/state non-claim language.");
        }
    }

    private static void RequireValue(JsonObject value, string property, string expected, List<string> errors)
    {
        var observed = PublicationCountingHardeningContracts.GetString(value, property);
        if (!string.Equals(observed, expected, StringComparison.Ordinal))
        {
            errors.Add($"{property} must be {expected}; observed {observed}.");
        }
    }

    private static void RequireValue(JsonObject value, string property, int expected, List<string> errors)
    {
        var observed = PublicationCountingHardeningContracts.GetInt(value, property, int.MinValue);
        if (observed != expected)
        {
            errors.Add($"{property} must be {expected}; observed {observed}.");
        }
    }

    private static void RequireValue(JsonObject value, string property, bool expected, List<string> errors)
    {
        var observed = PublicationCountingHardeningContracts.GetBool(value, property, !expected);
        if (observed != expected)
        {
            errors.Add($"{property} must be {expected.ToString().ToLowerInvariant()}.");
        }
    }
}
