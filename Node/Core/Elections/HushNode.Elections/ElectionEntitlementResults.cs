using HushShared.Elections.Model;

namespace HushNode.Elections;

/// <summary>One mapping for expected licence rejections; no raw infrastructure/identity text.</summary>
public static class ElectionEntitlementResults
{
    public static ElectionCommandResult Reject(ElectionEntitlementReason reason)
    {
        var (category, message) = reason switch
        {
            ElectionEntitlementReason.NotActive => (ElectionCommandErrorCode.ValidationFailed, "An active licence is required for this operation."),
            ElectionEntitlementReason.LimitExceeded => (ElectionCommandErrorCode.ValidationFailed, "The resulting eligible roster exceeds the licence limit."),
            ElectionEntitlementReason.ProfileNotAllowed => (ElectionCommandErrorCode.ValidationFailed, "The selected election profile is not permitted by the licence."),
            ElectionEntitlementReason.AuthorityUnavailable => (ElectionCommandErrorCode.DependencyBlocked, "Licence authority is temporarily unavailable. Retry later."),
            ElectionEntitlementReason.CaptureUnavailable => (ElectionCommandErrorCode.DependencyBlocked, "This election's authorization evidence is unavailable. Retry later."),
            ElectionEntitlementReason.RosterReplacementAfterLink => (ElectionCommandErrorCode.Conflict, "The roster cannot be replaced after a voter has linked."),
            _ => (ElectionCommandErrorCode.NotSupported, "This operation requires compatible licence semantics."),
        };
        var safeReason = Enum.IsDefined(reason) && reason != ElectionEntitlementReason.None
            ? reason : ElectionEntitlementReason.SemanticsUnsupported;
        return ElectionCommandResult.Failure(category, message) with { EntitlementReason = safeReason };
    }
}
