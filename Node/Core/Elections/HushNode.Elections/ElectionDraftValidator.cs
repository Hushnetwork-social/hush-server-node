using HushShared.Elections.Model;

namespace HushNode.Elections;

internal static class ElectionDraftValidator
{
    public static List<string> ValidateDraftRequest(
        string actorPublicAddress,
        string snapshotReason,
        ElectionDraftSpecification draft)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(actorPublicAddress))
        {
            errors.Add("Actor public address is required.");
        }

        if (string.IsNullOrWhiteSpace(snapshotReason))
        {
            errors.Add("Snapshot reason is required.");
        }

        if (string.IsNullOrWhiteSpace(draft.Title))
        {
            errors.Add("Election title is required.");
        }

        if (string.IsNullOrWhiteSpace(draft.ProtocolOmegaVersion))
        {
            errors.Add("Protocol Omega version is required.");
        }

        if (string.IsNullOrWhiteSpace(draft.SelectedProfileId))
        {
            errors.Add("A selected circuit/profile id is required.");
        }

        if (draft.ElectionClass != ElectionClass.OrganizationalRemoteVoting)
        {
            errors.Add("FEAT-094 only supports organizational remote voting elections.");
        }

        if (draft.DisclosureMode != ElectionDisclosureMode.FinalResultsOnly)
        {
            errors.Add("FEAT-094 only supports the final-results-only disclosure mode.");
        }

        if (draft.ParticipationPrivacyMode != ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice)
        {
            errors.Add("FEAT-094 only supports the phase-one participation privacy mode.");
        }

        if (draft.VoteUpdatePolicy != VoteUpdatePolicy.SingleSubmissionOnly)
        {
            errors.Add("FEAT-094 only supports the single-submission-only vote update policy.");
        }

        if (draft.EligibilitySourceType != EligibilitySourceType.OrganizationImportedRoster)
        {
            errors.Add("FEAT-094 only supports the organization-imported-roster eligibility source.");
        }

        if (draft.EligibilityMutationPolicy != EligibilityMutationPolicy.FrozenAtOpen &&
            draft.EligibilityMutationPolicy != EligibilityMutationPolicy.LateActivationForRosteredVotersOnly)
        {
            errors.Add(
                "The eligibility mutation policy must be frozen-at-open or late-activation-for-rostered-voters-only.");
        }

        if (draft.ReportingPolicy != ReportingPolicy.DefaultPhaseOnePackage)
        {
            errors.Add("FEAT-094 only supports the default phase-one reporting policy.");
        }

        if (draft.GovernanceMode == ElectionGovernanceMode.AdminOnly && draft.RequiredApprovalCount.HasValue)
        {
            errors.Add("Admin-only elections must not set a required approval count.");
        }

        if (draft.GovernanceMode == ElectionGovernanceMode.TrusteeThreshold &&
            (!draft.RequiredApprovalCount.HasValue || draft.RequiredApprovalCount.Value < 1))
        {
            errors.Add("Trustee-threshold elections require a required approval count of at least 1.");
        }

        errors.AddRange(ValidateOutcomeRule(draft.OutcomeRule));
        errors.AddRange(ValidateOwnerOptions(draft.OwnerOptions));

        return errors;
    }

    private static IReadOnlyList<string> ValidateOutcomeRule(OutcomeRuleDefinition outcomeRule)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(outcomeRule.TemplateKey))
        {
            errors.Add("Outcome rule template key is required.");
        }

        if (string.IsNullOrWhiteSpace(outcomeRule.TieResolutionRule))
        {
            errors.Add("Outcome rule tie-resolution policy is required.");
        }

        if (string.IsNullOrWhiteSpace(outcomeRule.CalculationBasis))
        {
            errors.Add("Outcome rule calculation basis is required.");
        }

        switch (outcomeRule.Kind)
        {
            case OutcomeRuleKind.SingleWinner:
                if (outcomeRule.SeatCount != 1)
                {
                    errors.Add("Single-winner elections must use seat count 1.");
                }
                break;

            case OutcomeRuleKind.PassFail:
                if (outcomeRule.SeatCount != 1)
                {
                    errors.Add("Pass/fail elections must use seat count 1.");
                }
                if (!outcomeRule.BlankVoteExcludedFromThresholdDenominator)
                {
                    errors.Add("Pass/fail elections must exclude blank votes from the threshold denominator.");
                }
                break;

            default:
                errors.Add("FEAT-094 does not support this outcome rule kind.");
                break;
        }

        if (!outcomeRule.BlankVoteCountsForTurnout)
        {
            errors.Add("Blank vote turnout accounting must remain enabled in FEAT-094.");
        }

        if (!outcomeRule.BlankVoteExcludedFromWinnerSelection)
        {
            errors.Add("Blank vote must remain excluded from winner selection in FEAT-094.");
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateOwnerOptions(IReadOnlyList<ElectionOptionDefinition> ownerOptions)
    {
        var errors = new List<string>();
        if (ownerOptions is null)
        {
            errors.Add("Owner options are required.");
            return errors;
        }

        foreach (var option in ownerOptions)
        {
            if (string.IsNullOrWhiteSpace(option.OptionId))
            {
                errors.Add("Each election option must have a stable option id.");
            }

            if (string.IsNullOrWhiteSpace(option.DisplayLabel))
            {
                errors.Add("Each election option must have a display label.");
            }

            if (option.BallotOrder < 0)
            {
                errors.Add("Election option ballot order must be zero or greater.");
            }

            if (option.IsBlankOption)
            {
                errors.Add("Owner options must not mark themselves as the reserved blank vote option.");
            }

            if (string.Equals(option.OptionId, ElectionOptionDefinition.ReservedBlankOptionId, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Owner options must not reuse the reserved blank option id.");
            }
        }

        if (ownerOptions.GroupBy(x => x.OptionId, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
        {
            errors.Add("Election option ids must be unique.");
        }

        if (ownerOptions.GroupBy(x => x.BallotOrder).Any(x => x.Count() > 1))
        {
            errors.Add("Election option ballot order must be unique.");
        }

        return errors;
    }
}
