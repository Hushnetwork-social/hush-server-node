using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Authorization read enlisted in the caller's write transaction. Locks the subject before
/// the caller locks its election/roster. Never opens a second connection, commits, provisions
/// or changes an assignment. Serialization/deadlock faults escape for whole-operation retry.
/// </summary>
public static class LicenceEnlistedProjectionReader
{
    public static async Task<IndexedEntitlementReadResult> LockAndReadAsync(
        DbContext writeContext, string canonicalOwnerAddress, DateTime executionUtc,
        CancellationToken cancellationToken = default)
    {
        var address = AuthenticatedIdentitySubject.NormalizeCanonicalAddress(canonicalOwnerAddress);
        if (address is null || executionUtc.Kind != DateTimeKind.Utc || writeContext.Database.CurrentTransaction is null)
            return Unavailable();

        try
        {
            var options = new DbContextOptionsBuilder<EnlistedContext>()
                .UseNpgsql(writeContext.Database.GetDbConnection()).Options;
            await using var db = new EnlistedContext(options);
            await db.Database.UseTransactionAsync(writeContext.Database.CurrentTransaction.GetDbTransaction(), cancellationToken);
            var subject = await db.Set<LicenceSubjectEntity>().FromSqlInterpolated($"""
                SELECT * FROM "HushVoting"."LicenceSubject"
                WHERE "SubjectType" = {LicencePersistenceVocabulary.SubjectTypeIdentity}
                  AND "CanonicalPublicSigningAddress" = {address} FOR UPDATE
                """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (subject is null) return IndexedEntitlementReadResult.NoActive();
            subject.Assignments = await db.Set<LicenceAssignmentEntity>().AsNoTracking()
                .Where(a => a.LicenceSubjectId == subject.LicenceSubjectId
                    && a.LifecycleStatus == LicencePersistenceVocabulary.LifecycleActive)
                .ToListAsync(cancellationToken);
            return LicenceIndexedProjectionEvaluator.Evaluate(subject, executionUtc);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (LicenceDatabaseFailures.IsSerializationConflict(ex)) { throw; }
        catch (Exception) { return Unavailable(); }
    }

    private static IndexedEntitlementReadResult Unavailable() => IndexedEntitlementReadResult.Unavailable(
        "licence_index_unavailable", "The transactional licence authority is unavailable.");

    private sealed class EnlistedContext(DbContextOptions<EnlistedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder) => new LicensingDbContextConfigurator().Configure(builder);
    }
}
