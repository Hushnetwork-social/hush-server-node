using System.Text.Json.Nodes;

namespace ProductionLikeOperationalRunPromoter;

public static partial class ProductionLikeOperationalRunContracts
{
    private static void ValidateBaselineRegister(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "baselineRegister", errors) is not { } register)
        {
            return;
        }

        RequireValue(register, "registerVersionId", CurrentRegisterId, errors, "baselineRegister");
        RequireValue(register, "registerStatus", "AcceptedInternal", errors, "baselineRegister");
        RequireValue(register, "dimensionId", DimensionId, errors, "baselineRegister");
        RequireValue(register, "strongestAllowedClaim", "friendly_organization_pilot", errors, "baselineRegister");

        if (GetInt(register, "totalScore") != 71 ||
            GetInt(register, "currentScore") != 6 ||
            GetInt(register, "targetScore") != 8)
        {
            errors.Add("baselineRegister must preserve RDY-REG-v0.1.5 score 71 and RDY-DIM-007 6 -> 8 target.");
        }
    }

    private static void ValidateRunProfile(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "runProfile", errors) is not { } profile)
        {
            return;
        }

        RequireValue(profile, "profileId", "controlled-hush-managed-staging-aws-like-v1", errors, "runProfile");
        RequireValue(profile, "environmentClass", "controlled_hush_managed_staging_aws_like", errors, "runProfile");
        RequireValue(profile, "deploymentProfile", "hush_saas_v1", errors, "runProfile");

        if (GetBool(profile, "localOnly") ||
            GetBool(profile, "privateChainOnly") ||
            GetBool(profile, "uncontrolledProduction") ||
            !GetBool(profile, "syntheticOrNonConfidentialData"))
        {
            errors.Add("runProfile must use controlled Hush-managed staging/AWS-like infrastructure with non-confidential data.");
        }
    }

    private static void ValidateDataScope(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "dataScope", errors) is not { } dataScope)
        {
            return;
        }

        if (GetBool(dataScope, "containsRealVoterPersonalData") ||
            GetBool(dataScope, "containsVoteChoiceData"))
        {
            errors.Add("dataScope cannot include real voter personal data or vote-choice data.");
        }
    }

    private static void ValidateReadinessProposal(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "readinessProposal", errors) is not { } proposal)
        {
            return;
        }

        RequireValue(proposal, "dimensionId", DimensionId, errors, "readinessProposal");
        RequireValue(proposal, "promotionOwner", PromotionOwner, errors, "readinessProposal");

        if (GetInt(proposal, "proposedScoreFrom") != 6 ||
            GetInt(proposal, "proposedScoreTo") != 8 ||
            !GetBool(proposal, "doesNotMutateRegister") ||
            GetBool(proposal, "directRegisterMutation", true) ||
            !GetBool(proposal, "scoreChangeRequiresPromotion"))
        {
            errors.Add("readinessProposal must preserve RDY-DIM-007 6 -> 8 with no direct register mutation.");
        }
    }

    private static void ValidateDownstreamHandoff(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "downstreamHandoff", errors) is not { } handoff)
        {
            return;
        }

        var targetFeatures = GetStringArray(handoff, "targetFeatures").ToHashSet(StringComparer.Ordinal);
        foreach (var required in new[] { "FEAT-148", "FEAT-155", "FEAT-156" })
        {
            if (!targetFeatures.Contains(required))
            {
                errors.Add($"downstreamHandoff.targetFeatures must include {required}.");
            }
        }
    }

    private static void ValidateRestrictedRefs(JsonObject source, List<string> errors)
    {
        if (TryArray(source, "restrictedEvidenceRefs", errors) is not { } refs)
        {
            return;
        }

        foreach (var reference in refs.OfType<JsonObject>())
        {
            if (GetBool(reference, "payloadCopied", true))
            {
                errors.Add($"{GetString(reference, "refId", "restricted evidence")} must not copy payload bodies.");
            }
        }
    }

    private static void ValidatePublicSafety(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "publicSafety", errors) is not { } publicSafety)
        {
            return;
        }

        RequireValue(publicSafety, "visibility", "public_safe", errors, "publicSafety");
        if (GetInt(publicSafety, "expectedFindingCountInGeneratedPublicOutputs") != 0)
        {
            errors.Add("publicSafety.expectedFindingCountInGeneratedPublicOutputs must be 0.");
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
