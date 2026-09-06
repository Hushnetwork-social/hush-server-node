using Microsoft.EntityFrameworkCore;

namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>FEAT-015: outcome of a strictly read-only indexed projection resolution.</summary>
public enum IndexedEntitlementReadOutcome
{
    /// <summary>An assignment is effective at the evaluation instant.</summary>
    Active = 0,

    /// <summary>Verified indexed absence: no assignment is currently effective. No write was performed.</summary>
    NoActive = 1,

    /// <summary>The chain index could not be read (storage unavailable). Never reported as absence.</summary>
    IndexUnavailable = 2,
}

/// <summary>Typed, read-only resolution result. Expected outcomes are data, never exceptions.</summary>
public sealed record IndexedEntitlementReadResult(
    bool IsSuccess,
    IndexedEntitlementReadOutcome Outcome,
    EffectiveLicenceEntitlement? Entitlement,
    string? StableErrorCode,
    string? SafeErrorReason)
{
    public static IndexedEntitlementReadResult Active(EffectiveLicenceEntitlement entitlement) =>
        new(true, IndexedEntitlementReadOutcome.Active, entitlement, null, null);

    public static IndexedEntitlementReadResult NoActive() =>
        new(true, IndexedEntitlementReadOutcome.NoActive, null, null, null);

    public static IndexedEntitlementReadResult Unavailable(string stableErrorCode, string safeErrorReason) =>
        new(false, IndexedEntitlementReadOutcome.IndexUnavailable, null, stableErrorCode, safeErrorReason);
}

/// <summary>
/// Port for resolving the current effective indexed entitlement of a canonical identity subject.
/// Strictly observational: it never provisions, activates, expires, or writes any licence state.
/// </summary>
public interface ILicenceIndexedProjectionReader
{
    Task<IndexedEntitlementReadResult> ResolveEffectiveAsync(
        AuthenticatedIdentitySubject subject,
        DateTime evaluationUtc,
        CancellationToken cancellationToken);
}

/// <summary>
/// FEAT-015 read-only projection reader over the FEAT-013 subject/assignment projection.
/// The partial-unique single active assignment is evaluated for interval membership at the
/// evaluation instant; an annual assignment that has lapsed is simply not effective (observational
/// expiry — no expiry transaction or status write is generated here).
/// </summary>
public sealed class LicenceIndexedProjectionReader : ILicenceIndexedProjectionReader
{
    private readonly Func<DbContext> _contextFactory;

    public LicenceIndexedProjectionReader(Func<DbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<IndexedEntitlementReadResult> ResolveEffectiveAsync(
        AuthenticatedIdentitySubject subject,
        DateTime evaluationUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var db = _contextFactory();
            var subjectRow = await db.Set<LicenceSubjectEntity>()
                .AsNoTracking()
                .Include(s => s.Assignments)
                .SingleOrDefaultAsync(
                    s => s.SubjectType == subject.SubjectType
                         && s.CanonicalPublicSigningAddress == subject.CanonicalPublicSigningAddress,
                    cancellationToken)
                .ConfigureAwait(false);

            if (subjectRow is null)
            {
                // The identity has never had a licence: verified indexed absence.
                return IndexedEntitlementReadResult.NoActive();
            }

            var evaluation = LicenceIndexedProjectionEvaluator.Evaluate(subjectRow, evaluationUtc);
            return evaluation.Outcome switch
            {
                IndexedEntitlementReadOutcome.Active => IndexedEntitlementReadResult.Active(evaluation.Entitlement!),
                _ => IndexedEntitlementReadResult.NoActive(),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Infrastructure failure is never absence and never fabricates a licence.
            return IndexedEntitlementReadResult.Unavailable(
                "licence_index_unavailable",
                "the licence chain index is temporarily unavailable");
        }
    }
}
