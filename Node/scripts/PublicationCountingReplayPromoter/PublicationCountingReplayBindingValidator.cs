using System.Text.Json.Nodes;

namespace PublicationCountingReplayPromoter;

public static class PublicationCountingReplayBindingValidator
{
    private static readonly string[] RequiredCaseBindingTypes =
    [
        "package-hash",
        "tally-output",
        "package-verifier-output",
        "runtime-verifier-output",
    ];

    private static readonly string[] RequiredGeneratedReportRefs =
    [
        PublicationCountingReplayArtifactGenerator.GoodProfileReplaySummaryPath,
        PublicationCountingReplayArtifactGenerator.GoodProfileNormalizedOutputHashesPath,
        PublicationCountingReplayArtifactGenerator.TamperReplaySummaryPath,
        PublicationCountingReplayArtifactGenerator.StaleReferenceCheckSummaryPath,
        PublicationCountingReplayArtifactGenerator.PublicCiReplayEvidencePath,
    ];

    public static IReadOnlyList<string> ValidateGeneratedPackageBindings(
        PublicationCountingReplayGeneratedPackage generated)
    {
        var errors = new List<string>();
        var artifacts = generated.Artifacts.ToDictionary(item => item.RelativePath, StringComparer.Ordinal);
        ValidateGoodProfileReplaySummary(artifacts, errors);
        ValidateNormalizedOutputHashes(artifacts, errors);
        ValidateTamperReplaySummary(artifacts, errors);
        ValidatePublicCiReplayEvidence(artifacts, errors);
        ValidateGeneratedReportBindings(artifacts, errors);
        return errors;
    }

    private static void ValidateGoodProfileReplaySummary(
        IReadOnlyDictionary<string, PublicationCountingReplayArtifact> artifacts,
        List<string> errors)
    {
        var summary = ReadArtifactObject(artifacts, PublicationCountingReplayArtifactGenerator.GoodProfileReplaySummaryPath, errors);
        if (summary is null)
        {
            return;
        }

        if (!string.Equals(PublicationCountingReplayContracts.GetString(summary, "status"), "pass", StringComparison.Ordinal))
        {
            errors.Add("good-profile replay summary must have pass status.");
        }

        foreach (var item in PublicationCountingReplayContracts.RequireArray(summary, "cases").OfType<JsonObject>())
        {
            var fixtureId = PublicationCountingReplayContracts.GetString(item, "fixtureId");
            RequireSha256(item, "packageHash", $"{fixtureId}.packageHash", errors);
            RequireSha256(item, "observedPackageHash", $"{fixtureId}.observedPackageHash", errors);
            RequireSha256(item, "normalizedOutputHash", $"{fixtureId}.normalizedOutputHash", errors);
            RequireSha256(item, "expectedNormalizedOutputHash", $"{fixtureId}.expectedNormalizedOutputHash", errors);

            var bindings = PublicationCountingReplayContracts.RequireArray(item, "artifactBindings")
                .OfType<JsonObject>()
                .ToArray();
            foreach (var bindingType in RequiredCaseBindingTypes)
            {
                if (!bindings.Any(binding => string.Equals(
                        PublicationCountingReplayContracts.GetString(binding, "bindingType"),
                        bindingType,
                        StringComparison.Ordinal)))
                {
                    errors.Add($"{fixtureId} missing required {bindingType} binding.");
                }
            }

            foreach (var binding in bindings)
            {
                RequireSha256(
                    binding,
                    "sha256Hash",
                    $"{fixtureId}.{PublicationCountingReplayContracts.GetString(binding, "path")}",
                    errors);
            }
        }
    }

    private static void ValidateNormalizedOutputHashes(
        IReadOnlyDictionary<string, PublicationCountingReplayArtifact> artifacts,
        List<string> errors)
    {
        var hashes = ReadArtifactObject(artifacts, PublicationCountingReplayArtifactGenerator.GoodProfileNormalizedOutputHashesPath, errors);
        if (hashes is null)
        {
            return;
        }

        foreach (var item in PublicationCountingReplayContracts.RequireArray(hashes, "hashes").OfType<JsonObject>())
        {
            var fixtureId = PublicationCountingReplayContracts.GetString(item, "fixtureId");
            RequireSha256(item, "expectedNormalizedOutputHash", $"{fixtureId}.expectedNormalizedOutputHash", errors);
            RequireSha256(item, "normalizedOutputHash", $"{fixtureId}.normalizedOutputHash", errors);
        }
    }

    private static void ValidateGeneratedReportBindings(
        IReadOnlyDictionary<string, PublicationCountingReplayArtifact> artifacts,
        List<string> errors)
    {
        var summary = ReadArtifactObject(artifacts, PublicationCountingReplayArtifactGenerator.GeneratedReportBindingSummaryPath, errors);
        if (summary is null)
        {
            return;
        }

        var requiredTypes = PublicationCountingReplayContracts.RequireArray(summary, "requiredBindingTypes")
            .Select(item => item?.GetValue<string>() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        if (!requiredTypes.Contains("generated-report"))
        {
            errors.Add("generated report binding summary missing generated-report binding type.");
        }

        var boundRefs = PublicationCountingReplayContracts.RequireArray(summary, "boundArtifacts")
            .OfType<JsonObject>()
            .Select(item => PublicationCountingReplayContracts.GetString(item, "path"))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var requiredRef in RequiredGeneratedReportRefs)
        {
            if (!boundRefs.Contains(requiredRef))
            {
                errors.Add($"generated report binding summary missing {requiredRef}.");
            }
        }
    }

    private static void ValidateTamperReplaySummary(
        IReadOnlyDictionary<string, PublicationCountingReplayArtifact> artifacts,
        List<string> errors)
    {
        var summary = ReadArtifactObject(artifacts, PublicationCountingReplayArtifactGenerator.TamperReplaySummaryPath, errors);
        if (summary is null)
        {
            return;
        }

        if (!string.Equals(PublicationCountingReplayContracts.GetString(summary, "status"), "pass", StringComparison.Ordinal))
        {
            errors.Add("tamper replay summary must have pass status.");
        }

        foreach (var item in PublicationCountingReplayContracts.RequireArray(summary, "cases").OfType<JsonObject>())
        {
            var fixtureId = PublicationCountingReplayContracts.GetString(item, "fixtureId");
            RequireNonEmpty(item, "expectedPrimaryResultCode", $"{fixtureId}.expectedPrimaryResultCode", errors);
            RequireNonEmpty(item, "observedPrimaryResultCode", $"{fixtureId}.observedPrimaryResultCode", errors);
            RequireNonEmpty(item, "changedArtifactOrCondition", $"{fixtureId}.changedArtifactOrCondition", errors);
            RequireSha256(item, "packageHash", $"{fixtureId}.packageHash", errors);
            RequireSha256(item, "expectedNormalizedOutputHash", $"{fixtureId}.expectedNormalizedOutputHash", errors);
            RequireSha256(item, "normalizedOutputHash", $"{fixtureId}.normalizedOutputHash", errors);
            if (!PublicationCountingReplayContracts.GetBool(item, "blocksScoreMovement"))
            {
                errors.Add($"{fixtureId}.blocksScoreMovement must be true.");
            }

            if (PublicationCountingReplayContracts.RequireArray(item, "changedArtifactRefs").Count == 0)
            {
                errors.Add($"{fixtureId} missing changed-artifact references.");
            }
        }
    }

    private static void ValidatePublicCiReplayEvidence(
        IReadOnlyDictionary<string, PublicationCountingReplayArtifact> artifacts,
        List<string> errors)
    {
        var evidence = ReadArtifactObject(artifacts, PublicationCountingReplayArtifactGenerator.PublicCiReplayEvidencePath, errors);
        if (evidence is null)
        {
            return;
        }

        if (!string.Equals(PublicationCountingReplayContracts.GetString(evidence, "status"), "pass", StringComparison.Ordinal))
        {
            errors.Add("public CI replay evidence must have pass status.");
        }

        var gate = PublicationCountingReplayContracts.RequireObject(evidence, "scoreProposalGate");
        if (!PublicationCountingReplayContracts.GetBool(gate, "missingPublicReplayEvidenceBlocksFinalScoreProposal"))
        {
            errors.Add("public CI replay evidence must block final score proposal when missing.");
        }

        if (PublicationCountingReplayContracts.RequireArray(evidence, "goodProfileFixtures").Count == 0)
        {
            errors.Add("public CI replay evidence must include good-profile fixtures.");
        }

        if (PublicationCountingReplayContracts.RequireArray(evidence, "tamperFixtures").Count == 0)
        {
            errors.Add("public CI replay evidence must include tamper fixtures.");
        }
    }

    private static JsonObject? ReadArtifactObject(
        IReadOnlyDictionary<string, PublicationCountingReplayArtifact> artifacts,
        string relativePath,
        List<string> errors)
    {
        if (!artifacts.TryGetValue(relativePath, out var artifact))
        {
            errors.Add($"missing generated artifact {relativePath}.");
            return null;
        }

        var node = JsonNode.Parse(artifact.Content);
        if (node is JsonObject obj)
        {
            return obj;
        }

        errors.Add($"generated artifact {relativePath} is not a JSON object.");
        return null;
    }

    private static void RequireSha256(
        JsonObject value,
        string property,
        string label,
        List<string> errors)
    {
        var observed = PublicationCountingReplayContracts.GetString(value, property);
        if (!observed.StartsWith("sha256:", StringComparison.Ordinal) ||
            observed.Length != 71 ||
            observed["sha256:".Length..].Any(character => !Uri.IsHexDigit(character)))
        {
            errors.Add($"{label} must be a sha256:<64 hex> value.");
        }
    }

    private static void RequireNonEmpty(
        JsonObject value,
        string property,
        string label,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(PublicationCountingReplayContracts.GetString(value, property)))
        {
            errors.Add($"{label} must not be empty.");
        }
    }
}
