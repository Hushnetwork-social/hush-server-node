// FEAT-015 Task 6.1/6.2 — GetMyEntitlement response mapping.
//
// Maps the typed application result to the additive proto response. The mapping excludes
// subject/identity, history, database keys, catalogue digest, cache provenance, outbox state,
// signatures, and internal error text. Timestamps are UTC ISO-8601 strings. Active responses
// carry the licence reference + safe current detail + strictly higher options + informational
// Enterprise; no-active carries exactly one Direct Free template.

using System.Globalization;
using HushNetwork.proto;
using HushNode.HushVoting.Licence.Transactions;

namespace HushNode.HushVoting.Licence.gRPC;

public static class LicenceQueryResponseMappings
{
    public static GetMyEntitlementResponse ToProto(LicenceEntitlementQueryApplicationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        switch (result.State)
        {
            case HushVotingLicenceEntitlementQueryState.Active:
                return ToActiveResponse(result.Active!);
            case HushVotingLicenceEntitlementQueryState.NoActive:
                return ToNoActiveResponse(result.DirectFreeTemplate!);
            default:
                return new GetMyEntitlementResponse
                {
                    State = LicenceEntitlementState.Unspecified,
                    UnavailableCode = result.UnavailableCode ?? "licence_index_unavailable",
                };
        }
    }

    private static GetMyEntitlementResponse ToActiveResponse(HushVotingLicenceActiveView view)
    {
        var proto = new GetMyEntitlementResponse
        {
            State = LicenceEntitlementState.Active,
            Active = new LicenceActiveEntitlementView
            {
                LicenceReference = view.LicenceReference,
                PlanId = view.PlanId,
                PlanFamily = view.PlanFamily,
                DisplayName = view.DisplayName,
                SafeDescription = view.SafeDescription,
                EligibleVoterCap = view.EligibleVoterCap ?? 0,
                UnlimitedElections = view.UnlimitedElections,
                TermKind = view.TermKind,
                TermYears = view.TermYears,
                EffectiveFromUtc = ToUtcIso(view.EffectiveFromUtc),
                AssignedCatalogueVersion = view.AssignedCatalogueVersion,
            },
        };

        proto.Active.AllowedGovernanceOptionIds.AddRange(view.AllowedGovernanceOptionIds);

        if (view.ExpiresAtUtc is { } expiresAtUtc)
        {
            proto.Active.ExpiresAtUtc = ToUtcIso(expiresAtUtc);
        }

        foreach (var option in view.HigherOptions)
        {
            proto.Active.HigherOptions.Add(new LicenceHigherOptionView
            {
                PlanId = option.PlanId,
                DisplayName = option.DisplayName,
                SafeDescription = option.SafeDescription,
                EligibleVoterCap = option.EligibleVoterCap ?? 0,
                UnlimitedElections = option.UnlimitedElections,
                TermKind = option.TermKind,
                TermYears = option.TermYears,
            });
        }

        if (view.Enterprise is { } enterprise)
        {
            proto.Active.Enterprise = new LicenceEnterpriseView
            {
                PlanId = enterprise.PlanId,
                DisplayName = enterprise.DisplayName,
                SafeDescription = enterprise.SafeDescription,
            };
        }

        return proto;
    }

    private static GetMyEntitlementResponse ToNoActiveResponse(HushVotingLicenceDirectFreeTemplate template) =>
        new()
        {
            State = LicenceEntitlementState.NoActive,
            DirectFreeTemplate = new LicenceDirectFreeTemplate
            {
                TransitionIntent = template.TransitionIntent,
                RequestedPlanId = template.RequestedPlanId,
                ObservedCatalogueVersion = template.ObservedCatalogueVersion,
            },
        };

    private static string ToUtcIso(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
