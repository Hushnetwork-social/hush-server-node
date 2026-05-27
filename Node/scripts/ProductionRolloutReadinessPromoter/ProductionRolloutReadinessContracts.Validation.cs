using System.Text.Json.Nodes;

namespace ProductionRolloutReadinessPromoter;

public static partial class ProductionRolloutReadinessContracts
{
    private static void ValidateBaselineRegister(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "baselineRegister", errors) is not { } register)
        {
            return;
        }

        RequireValue(register, "registerVersionId", CurrentRegisterId, errors, "baselineRegister");
        RequireValue(register, "strongestAllowedClaim", "friendly_organization_pilot", errors, "baselineRegister");
        if (GetInt(register, "totalScore") != 71)
        {
            errors.Add("baselineRegister.totalScore must match RDY-REG-v0.1.4 score 71.");
        }

        ValidateBlockerState(register, "productionBlocker", ProductionBlockerId, errors);
        ValidateBlockerState(register, "publicStateBlocker", PublicStateBlockerId, errors);
    }

    private static void ValidateBlockerState(
        JsonObject source,
        string property,
        string expectedBlockerId,
        List<string> errors)
    {
        if (TryObject(source, property, errors) is not { } blocker)
        {
            return;
        }

        RequireValue(blocker, "blockerId", expectedBlockerId, errors, property);
        if (string.IsNullOrWhiteSpace(GetString(blocker, "severity")) ||
            string.IsNullOrWhiteSpace(GetString(blocker, "status")))
        {
            errors.Add($"{property} must include severity and status.");
        }
    }

    private static void ValidateScorePolicy(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "scorePolicy", errors) is not { } policy)
        {
            return;
        }

        if (GetInt(policy, "allowedWithLimitationsMinimum") != AmberMinimumScore)
        {
            errors.Add("scorePolicy.allowedWithLimitationsMinimum must be 80.");
        }

        if (GetInt(policy, "allowedMinimumRecommended") != FullAllowedRecommendedScore)
        {
            errors.Add("scorePolicy.allowedMinimumRecommended must be 85.");
        }

        if (GetBool(policy, "directRegisterMutation", true))
        {
            errors.Add("scorePolicy.directRegisterMutation must be false.");
        }

        RequireValue(policy, "registerPromotionOwner", "FEAT-130", errors, "scorePolicy");
    }

    private static void ValidateEvidenceGroups(JsonObject source, List<string> errors)
    {
        foreach (var groupName in new[]
        {
            "runEvidence",
            "operationalEvidence",
            "deploymentProofEvidence",
            "webClientProofEvidence",
            "governedOutcomeEvidence",
        })
        {
            if (TryObject(source, groupName, errors) is not { } group)
            {
                continue;
            }

            foreach (var required in new[] { "status", "claimImpact", "evidenceRefs", "blockerIds" })
            {
                if (!group.ContainsKey(required))
                {
                    errors.Add($"{groupName} is missing required property {required}.");
                }
            }
        }

        var upstream = TryArray(source, "upstreamEvidence", errors);
        if (upstream is null)
        {
            return;
        }

        var upstreamFeatures = upstream
            .OfType<JsonObject>()
            .Select(item => GetString(item, "featureSlice"))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var requiredFeature in new[] { "FEAT-133", "FEAT-141", "FEAT-143", "FEAT-144", "FEAT-146", "FEAT-147" })
        {
            if (!upstreamFeatures.Contains(requiredFeature))
            {
                errors.Add($"Missing upstream evidence for {requiredFeature}.");
            }
        }
    }

    private static void ValidateClaimPolicy(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "claimPolicy", errors) is not { } policy)
        {
            return;
        }

        RequireValue(policy, "publicStateClaimState", "blocked", errors, "claimPolicy");
        var nonClaims = GetStringArray(policy, "nonClaims");
        foreach (var required in new[]
        {
            "Not public or state election readiness",
            "Not legal sufficiency validation",
            "Not independent certification",
            "Not full AGM software validation",
        })
        {
            if (!nonClaims.Contains(required, StringComparer.Ordinal))
            {
                errors.Add($"claimPolicy.nonClaims must include {required}.");
            }
        }
    }

    private static void ValidateBlockerDecisions(JsonObject source, List<string> errors)
    {
        var decisionsSource = TryArray(source, "blockerDecisions", errors);
        if (decisionsSource is null)
        {
            return;
        }

        var decisions = decisionsSource
            .OfType<JsonObject>()
            .ToArray();
        foreach (var blockerId in new[] { ProductionBlockerId, PublicStateBlockerId })
        {
            if (decisions.All(decision => GetString(decision, "blockerId") != blockerId))
            {
                errors.Add($"Missing blocker decision for {blockerId}.");
            }
        }
    }

    private static void ValidateRestrictedRefs(JsonObject source, List<string> errors)
    {
        var references = TryArray(source, "restrictedEvidenceRefs", errors);
        if (references is null)
        {
            return;
        }

        foreach (var reference in references.OfType<JsonObject>())
        {
            if (GetBool(reference, "payloadCopied", true))
            {
                errors.Add($"{GetString(reference, "refId", "restricted evidence")} must not copy payload bodies.");
            }
        }
    }

    private static void ValidatePublicSamples(JsonObject source, List<string> errors)
    {
        var samples = TryArray(source, "publicArtifactSamples", errors);
        if (samples is null)
        {
            return;
        }

        foreach (var sample in samples.OfType<JsonObject>())
        {
            var content = GetString(sample, "content");
            foreach (var forbidden in ProductionRolloutReadinessGateChecker.ForbiddenPublicMaterialNeedles)
            {
                if (content.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"publicArtifactSamples includes forbidden material '{forbidden}'.");
                }
            }
        }
    }

    private static JsonObject? TryObject(JsonObject source, string property, List<string> errors)
    {
        if (source.TryGetPropertyValue(property, out var node) && node is JsonObject obj)
        {
            return obj;
        }

        errors.Add($"{property} must be an object.");
        return null;
    }

    private static JsonArray? TryArray(JsonObject source, string property, List<string> errors)
    {
        if (source.TryGetPropertyValue(property, out var node) && node is JsonArray array)
        {
            return array;
        }

        errors.Add($"{property} must be an array.");
        return null;
    }

    private static void RequireValue(
        JsonObject value,
        string property,
        string expected,
        List<string> errors,
        string? prefix = null)
    {
        if (!string.Equals(GetString(value, property), expected, StringComparison.Ordinal))
        {
            errors.Add($"{(prefix is null ? property : $"{prefix}.{property}")} must be {expected}.");
        }
    }
}
