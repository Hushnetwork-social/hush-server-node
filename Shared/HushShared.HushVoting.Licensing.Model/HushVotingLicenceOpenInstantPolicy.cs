namespace HushShared.HushVoting.Licensing.Model;

/// <summary>FEAT-018 DD018-06: validity at actual canonical Open. Scheduled Close has no
/// role in this decision; a persisted valid capture governs permitted later completion.</summary>
public static class HushVotingLicenceOpenInstantPolicy
{
    public static bool IsEffectiveAt(DateTime effectiveFromUtc, DateTime? expiresAtUtc, DateTime openBlockTimeUtc) =>
        effectiveFromUtc.Kind == DateTimeKind.Utc && openBlockTimeUtc.Kind == DateTimeKind.Utc &&
        openBlockTimeUtc >= effectiveFromUtc &&
        (expiresAtUtc is null || (expiresAtUtc.Value.Kind == DateTimeKind.Utc &&
            expiresAtUtc > effectiveFromUtc && openBlockTimeUtc < expiresAtUtc));
}
