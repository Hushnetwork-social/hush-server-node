using HushNode.HushVoting.Licensing.Storage;

namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// FEAT-015 serving authority resolver: resolves the current effective entitlement strictly from the
/// indexed projection. It never provisions Direct Free, never activates a higher plan, never expires
/// a licence, and never writes licence state. Verified indexed absence maps to a success-with-no-
/// entitlement result so the display path can distinguish no-active from infrastructure failure.
/// </summary>
public sealed class LicenceIndexedEntitlementAuthorityResolver : IEntitlementAuthorityResolver
{
    public const string UnavailableCode = "licence_index_unavailable";

    private readonly ILicenceIndexedProjectionReader _reader;
    private readonly Func<DateTime> _utcNow;

    public LicenceIndexedEntitlementAuthorityResolver(
        ILicenceIndexedProjectionReader reader,
        Func<DateTime>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public async Task<LicenceResolutionResult> ResolveEffectiveEntitlementAsync(
        AuthenticatedIdentitySubject subject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        cancellationToken.ThrowIfCancellationRequested();

        var read = await _reader.ResolveEffectiveAsync(subject, _utcNow(), cancellationToken)
            .ConfigureAwait(false);

        return read.Outcome switch
        {
            IndexedEntitlementReadOutcome.Active => LicenceResolutionResult.Ok(
                LicenceResolutionOutcome.ResolvedExisting,
                read.Entitlement!),
            IndexedEntitlementReadOutcome.NoActive => LicenceResolutionResult.Absent(),
            _ => LicenceResolutionResult.Fail(
                read.StableErrorCode ?? UnavailableCode,
                read.SafeErrorReason ?? "the licence chain index is temporarily unavailable"),
        };
    }
}
