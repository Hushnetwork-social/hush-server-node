using HushShared.HushVoting.Licensing.Model;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Transaction coordinator for the authoritative entitlement operations. Each operation:
///   1. validates the trusted typed subject/bounded command before opening a transaction;
///   2. atomically upserts and locks the subject row (FOR UPDATE);
///   3. reloads authoritative assignment/operation state under the lock;
///   4. normalizes annual expiry using one captured UTC instant;
///   5. validates against FEAT-012 policy;
///   6. writes assignments/status/operation outcomes/events and the single revision increment
///      atomically;
///   7. commits before publishing success.
/// PostgreSQL constraints remain the final guard. Recognized races and ambiguous commits flow
/// through <see cref="LicenceBoundedExecutor"/>; expected business and authority failures are
/// typed results, never exception text.
/// </summary>
public static partial class LicenceEntitlementCoordinator
{
    private const string OperationResolve = "resolve";

    // ---------------------------------------------------------------------------------------
    // GetOrProvision
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves the effective entitlement or atomically provisions Direct Free exactly once,
    /// normalizing an annual expiry boundary first with one captured server instant.
    /// </summary>
    public static async Task<LicenceResolutionResult> ResolveOrProvisionAsync(
        Func<DbContext> contextFactory,
        LicenceServiceConfiguration configuration,
        AuthenticatedIdentitySubject subject,
        TimeProvider timeProvider,
        LicenceTelemetry? telemetry,
        CancellationToken cancellationToken,
        LicenceFailureInjection? failureInjection = null)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var startedAt = timeProvider.GetTimestamp();
        var attemptNumber = 0;

        try
        {
            var result = await LicenceBoundedExecutor.ExecuteAsync(
                attemptAsync: async ct =>
                {
                    attemptNumber++;
                    return await RunResolveAttemptAsync(
                        contextFactory, configuration, subject, nowUtc, attemptNumber, telemetry, failureInjection, ct);
                },
                reconcileCommittedAsync: ct => ReconcileResolveAsync(
                    contextFactory, subject, ct),
                OperationResolve,
                telemetry,
                cancellationToken);

            RecordResolution(result, telemetry, timeProvider, startedAt);
            return result;
        }
        catch (LicenceExecutionExhaustedException exception)
        {
            var outcomeName = exception.StableCode;
            telemetry?.RecordResolutionOutcome(outcomeName);
            telemetry?.RecordOperationDuration(OperationResolve, outcomeName, timeProvider.GetElapsedTime(startedAt));
            telemetry?.LogAuthorityUnavailable(OperationResolve, outcomeName);
            return LicenceResolutionResult.Fail(exception.StableCode, exception.Message);
        }
    }

    private static void RecordResolution(
        LicenceResolutionResult result,
        LicenceTelemetry? telemetry,
        TimeProvider timeProvider,
        long startedAt)
    {
        var outcomeName = result.IsSuccess
            ? LicenceEntitlementOutcomeNames.ToWireName(result.Outcome!.Value)
            : result.StableErrorCode ?? "unknown";

        telemetry?.RecordResolutionOutcome(outcomeName);
        telemetry?.RecordOperationDuration(OperationResolve, outcomeName, timeProvider.GetElapsedTime(startedAt));
    }

    private static async Task<LicenceResolutionResult> RunResolveAttemptAsync(
        Func<DbContext> contextFactory,
        LicenceServiceConfiguration configuration,
        AuthenticatedIdentitySubject subject,
        DateTime nowUtc,
        int attemptNumber,
        LicenceTelemetry? telemetry,
        LicenceFailureInjection? injection,
        CancellationToken cancellationToken)
    {
        if (injection?.BeforeAttemptAsync is { } beforeAttempt)
        {
            await beforeAttempt(attemptNumber, cancellationToken);
        }

        await using var db = contextFactory();

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var subjectRow = await LockOrCreateSubjectAsync(db, subject, nowUtc, cancellationToken);

            var expiry = await NormalizeExpiryIfDueAsync(db, configuration, subjectRow, nowUtc, cancellationToken);

            LicenceResolutionResult result;
            if (expiry.DidExpire)
            {
                telemetry?.RecordExpiryNormalized();
                result = LicenceResolutionResult.Ok(
                    LicenceResolutionOutcome.ExpiredToDefault,
                    ToEntitlement(expiry.ActiveAssignment!, subjectRow.EntitlementRevision));
            }
            else if (expiry.ActiveAssignment is not null)
            {
                result = LicenceResolutionResult.Ok(
                    LicenceResolutionOutcome.ResolvedExisting,
                    ToEntitlement(expiry.ActiveAssignment, subjectRow.EntitlementRevision));
            }
            else
            {
                var watermark = await LicenceCatalogueLedgerCoordinator.ReadRolloutWatermarkAsync(db, cancellationToken);
                if (watermark is null)
                {
                    result = LicenceResolutionResult.Fail(
                        LicenceEntitlementFailureCodes.StorageUnavailable,
                        "Licence rollout watermark unavailable; provisioning authority cannot be established.");
                }
                else
                {
                    var provisioned = await ProvisionDirectFreeAsync(
                        db, configuration, subjectRow, watermark.Value, nowUtc, cancellationToken);

                    result = LicenceResolutionResult.Ok(
                        string.Equals(
                            provisioned.Source,
                            LicencePersistenceVocabulary.SourceMigrationLazyDefault,
                            StringComparison.Ordinal)
                            ? LicenceResolutionOutcome.ProvisionedMigrationDefault
                            : LicenceResolutionOutcome.ProvisionedDefault,
                        ToEntitlement(provisioned.Assignment, subjectRow.EntitlementRevision));
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            // Commit boundary: failures here have an unknown outcome until reconciled.
            try
            {
                if (injection?.BeforeCommitAsync is { } beforeCommit)
                {
                    await beforeCommit(attemptNumber, cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                if (injection?.AfterCommitAsync is { } afterCommit)
                {
                    await afterCommit(attemptNumber, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new LicenceAmbiguousCommitException(
                    "Commit outcome unknown; reconciliation required before any new mutation.",
                    exception);
            }

            var outcomeName = result.IsSuccess
                ? LicenceEntitlementOutcomeNames.ToWireName(result.Outcome!.Value)
                : result.StableErrorCode ?? "unknown";
            telemetry?.LogOperationCompleted(OperationResolve, subjectRow.LicenceSubjectId, outcomeName);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateException exception)
        {
            return ClassifyResolveDbFailure(exception);
        }
        catch (PostgresException exception)
        {
            return ClassifyResolveDbFailure(exception);
        }
        catch (NpgsqlException exception)
        {
            return LicenceResolutionResult.Fail(
                LicenceEntitlementFailureCodes.StorageUnavailable,
                $"Resolution storage unavailable: {exception.GetType().Name}");
        }
        catch (Exception exception) when (LicencePostgresFailureClassifier.IsStorageUnavailable(exception))
        {
            return LicenceResolutionResult.Fail(
                LicenceEntitlementFailureCodes.StorageUnavailable,
                $"Resolution storage unavailable: {exception.GetType().Name}");
        }
    }

    private static async Task<LicenceResolutionResult?> ReconcileResolveAsync(
        Func<DbContext> contextFactory,
        AuthenticatedIdentitySubject subject,
        CancellationToken cancellationToken)
    {
        await using var db = contextFactory();

        var subjectRow = await db.Set<LicenceSubjectEntity>()
            .SingleOrDefaultAsync(s =>
                s.SubjectType == subject.SubjectType
                && s.CanonicalPublicSigningAddress == subject.CanonicalPublicSigningAddress,
                cancellationToken);

        if (subjectRow is null)
        {
            // Authoritative absence: the resolution transaction did not commit.
            return null;
        }

        var active = await LoadActiveAssignmentAsync(db, subjectRow.LicenceSubjectId, cancellationToken);
        if (active is not null)
        {
            return LicenceResolutionResult.Ok(
                LicenceResolutionOutcome.ResolvedExisting,
                ToEntitlement(active, subjectRow.EntitlementRevision));
        }

        // Subject exists but the operation effect (an active assignment) is absent.
        return null;
    }

    private static LicenceResolutionResult ClassifyResolveDbFailure(DbUpdateException exception)
    {
        if (LicencePostgresFailureClassifier.IsRecognizedTransient(exception))
        {
            throw new LicenceTransientConflictException(
                "Recognized transient PostgreSQL race during resolution.",
                exception);
        }

        if (LicencePostgresFailureClassifier.IsPersistenceInvariantViolation(exception))
        {
            return LicenceResolutionResult.Fail(
                LicenceEntitlementFailureCodes.PersistenceInvariantViolation,
                "PostgreSQL rejected a resolution write that violates a persistence invariant.");
        }

        return LicenceResolutionResult.Fail(
            LicenceEntitlementFailureCodes.StorageUnavailable,
            "Unclassified database write failure during resolution; no entitlement invented.");
    }

    private static LicenceResolutionResult ClassifyResolveDbFailure(PostgresException exception)
    {
        if (LicencePostgresFailureClassifier.IsRecognizedTransient(exception))
        {
            throw new LicenceTransientConflictException(
                "Recognized transient PostgreSQL race during resolution.",
                exception);
        }

        if (LicencePostgresFailureClassifier.IsPersistenceInvariantViolation(exception))
        {
            return LicenceResolutionResult.Fail(
                LicenceEntitlementFailureCodes.PersistenceInvariantViolation,
                "PostgreSQL rejected a resolution write that violates a persistence invariant.");
        }

        return LicenceResolutionResult.Fail(
            LicenceEntitlementFailureCodes.StorageUnavailable,
            $"PostgreSQL failure during resolution: {exception.SqlState}");
    }

    // ---------------------------------------------------------------------------------------
    // Shared single-attempt infrastructure
    // ---------------------------------------------------------------------------------------

    private static async Task<LicenceSubjectEntity> LockOrCreateSubjectAsync(
        DbContext db,
        AuthenticatedIdentitySubject subject,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var existing = await db.Set<LicenceSubjectEntity>()
            .FromSqlRaw(
                """
                SELECT * FROM "HushVoting"."LicenceSubject"
                WHERE "SubjectType" = {0} AND "CanonicalPublicSigningAddress" = {1} FOR UPDATE
                """,
                subject.SubjectType,
                subject.CanonicalPublicSigningAddress)
            .SingleOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var created = new LicenceSubjectEntity
        {
            LicenceSubjectId = Guid.CreateVersion7(),
            SubjectType = subject.SubjectType,
            CanonicalPublicSigningAddress = subject.CanonicalPublicSigningAddress,
            IdentityCreationBlockIndex = subject.IdentityCreationBlockIndex,
            CreatedAtUtc = nowUtc,
            EntitlementRevision = 0,
        };

        db.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        return created;
    }

    private static async Task<LicenceAssignmentEntity?> LoadActiveAssignmentAsync(
        DbContext db,
        Guid subjectId,
        CancellationToken cancellationToken) =>
        await db.Set<LicenceAssignmentEntity>()
            .Where(a => a.LicenceSubjectId == subjectId
                        && a.LifecycleStatus == LicencePersistenceVocabulary.LifecycleActive)
            .SingleOrDefaultAsync(cancellationToken);

    private static async Task<long> NextEventSequenceAsync(
        DbContext db,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        var max = await db.Set<LicenceTransitionEventEntity>()
            .Where(e => e.LicenceSubjectId == subjectId)
            .Select(e => (long?)e.EventSequence)
            .MaxAsync(cancellationToken);

        return (max ?? 0L) + 1L;
    }

    private static LicenceTransitionEventEntity NewTransitionEvent(
        Guid subjectId,
        long sequence,
        string eventType,
        long subjectRevision,
        Guid? assignmentId,
        string planId,
        string catalogueDecisionVersion,
        string? sourceOrReason,
        Guid? operationReferenceId,
        DateTime occurredAtUtc) =>
        new()
        {
            LicenceTransitionEventId = Guid.CreateVersion7(),
            LicenceSubjectId = subjectId,
            EventSequence = sequence,
            EventType = eventType,
            SubjectRevision = subjectRevision,
            AssignmentId = assignmentId,
            PlanId = planId,
            CatalogueDecisionVersion = catalogueDecisionVersion,
            SourceOrReason = sourceOrReason,
            OperationReferenceId = operationReferenceId,
            OccurredAtUtc = occurredAtUtc,
        };

    private static LicenceAssignmentEntity NewAssignment(
        Guid subjectId,
        HushVotingLicencePlan plan,
        LicenceServiceConfiguration configuration,
        string source,
        DateTime effectiveFromUtc,
        DateTime? expiresAtUtc,
        Guid? createdByOperationId,
        string? creationCorrelationId)
    {
        var snapshot = LicenceEntitlementDecisions.ToOperativeSnapshot(plan);

        return new LicenceAssignmentEntity
        {
            LicenceAssignmentId = Guid.CreateVersion7(),
            LicenceSubjectId = subjectId,
            PlanId = plan.Id.Value,
            AssignedCatalogueVersion = configuration.CatalogueVersion,
            AssignedCatalogueDigestSha256 = configuration.ReleaseDigestSha256,
            LifecycleStatus = LicencePersistenceVocabulary.LifecycleActive,
            Source = source,
            EffectiveFromUtc = effectiveFromUtc,
            ExpiresAtUtc = expiresAtUtc,
            LifecycleChangedAtUtc = null,
            LifecycleReason = null,
            PlanFamily = snapshot.PlanFamily,
            UpgradeRank = snapshot.UpgradeRank,
            EligibleVoterCap = snapshot.EligibleVoterCap,
            UnlimitedElectionPolicy = snapshot.UnlimitedElectionPolicy,
            TermKind = snapshot.TermKind,
            TermYears = snapshot.TermYears,
            AllowedGovernanceOptionIds = snapshot.AllowedGovernanceOptionIds.ToArray(),
            CreationCorrelationId = creationCorrelationId,
            CreatedByOperationId = createdByOperationId,
        };
    }

    /// <summary>
    /// Normalizes an annual assignment that is at or past its upper-exclusive expiry instant:
    /// the annual assignment becomes expired, a Direct Free assignment becomes active, both
    /// transition events are appended, and the subject revision increments exactly once.
    /// Returns the (possibly new) active assignment state.
    /// </summary>
    private static async Task<ExpiryNormalization> NormalizeExpiryIfDueAsync(
        DbContext db,
        LicenceServiceConfiguration configuration,
        LicenceSubjectEntity subjectRow,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var active = await LoadActiveAssignmentAsync(db, subjectRow.LicenceSubjectId, cancellationToken);
        if (active is null || !LicenceEntitlementDecisions.IsExpired(active, nowUtc))
        {
            return new ExpiryNormalization(DidExpire: false, active);
        }

        var expiredAtUtc = nowUtc;
        var nextRevision = subjectRow.EntitlementRevision + 1L;
        subjectRow.EntitlementRevision = nextRevision;

        active.LifecycleStatus = LicencePersistenceVocabulary.LifecycleExpired;
        active.LifecycleChangedAtUtc = expiredAtUtc;
        active.LifecycleReason = LicenceEntitlementDecisions.ReasonAnnualExpiry;

        var sequence = await NextEventSequenceAsync(db, subjectRow.LicenceSubjectId, cancellationToken);

        db.Add(NewTransitionEvent(
            subjectRow.LicenceSubjectId,
            sequence,
            LicencePersistenceVocabulary.EventTypeExpired,
            nextRevision,
            active.LicenceAssignmentId,
            active.PlanId,
            active.AssignedCatalogueVersion,
            LicenceEntitlementDecisions.ReasonAnnualExpiry,
            operationReferenceId: null,
            expiredAtUtc));

        var directFreePlan = configuration.Catalogue.FindPlan(HushVotingLicencePlanId.DirectFree)
            ?? throw new InvalidOperationException(
                "Configured licence catalogue must contain the Direct Free plan.");

        var directFree = NewAssignment(
            subjectRow.LicenceSubjectId,
            directFreePlan,
            configuration,
            LicencePersistenceVocabulary.SourceAutomaticExpiry,
            effectiveFromUtc: expiredAtUtc,
            expiresAtUtc: null,
            createdByOperationId: null,
            creationCorrelationId: null);

        db.Add(directFree);

        db.Add(NewTransitionEvent(
            subjectRow.LicenceSubjectId,
            sequence + 1L,
            LicencePersistenceVocabulary.EventTypeCreated,
            nextRevision,
            directFree.LicenceAssignmentId,
            directFree.PlanId,
            configuration.CatalogueVersion,
            LicencePersistenceVocabulary.SourceAutomaticExpiry,
            operationReferenceId: null,
            expiredAtUtc));

        return new ExpiryNormalization(DidExpire: true, directFree);
    }

    /// <summary>Provisions Direct Free for an identity with no effective assignment.</summary>
    private static async Task<ProvisioningOutcome> ProvisionDirectFreeAsync(
        DbContext db,
        LicenceServiceConfiguration configuration,
        LicenceSubjectEntity subjectRow,
        long rolloutWatermarkBlockHeight,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var source = LicenceEntitlementDecisions.DecideProvisionSource(
            subjectRow.IdentityCreationBlockIndex,
            rolloutWatermarkBlockHeight);

        var directFreePlan = configuration.Catalogue.FindPlan(HushVotingLicencePlanId.DirectFree)
            ?? throw new InvalidOperationException(
                "Configured licence catalogue must contain the Direct Free plan.");

        var nextRevision = subjectRow.EntitlementRevision + 1L;
        subjectRow.EntitlementRevision = nextRevision;

        var assignment = NewAssignment(
            subjectRow.LicenceSubjectId,
            directFreePlan,
            configuration,
            source,
            effectiveFromUtc: nowUtc,
            expiresAtUtc: null,
            createdByOperationId: null,
            creationCorrelationId: null);

        db.Add(assignment);

        var sequence = await NextEventSequenceAsync(db, subjectRow.LicenceSubjectId, cancellationToken);
        db.Add(NewTransitionEvent(
            subjectRow.LicenceSubjectId,
            sequence,
            LicencePersistenceVocabulary.EventTypeCreated,
            nextRevision,
            assignment.LicenceAssignmentId,
            assignment.PlanId,
            configuration.CatalogueVersion,
            source,
            operationReferenceId: null,
            nowUtc));

        return new ProvisioningOutcome(assignment, source);
    }

    /// <summary>Builds the stable typed projection from an assignment row and the subject revision.</summary>
    private static EffectiveLicenceEntitlement ToEntitlement(
        LicenceAssignmentEntity assignment,
        long entitlementRevision)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        return new EffectiveLicenceEntitlement(
            assignment.LicenceSubjectId,
            assignment.LicenceAssignmentId,
            assignment.PlanId,
            assignment.PlanFamily,
            assignment.UpgradeRank,
            assignment.EligibleVoterCap,
            assignment.UnlimitedElectionPolicy,
            assignment.TermKind,
            assignment.TermYears,
            assignment.AllowedGovernanceOptionIds,
            assignment.Source,
            assignment.EffectiveFromUtc,
            assignment.ExpiresAtUtc,
            assignment.AssignedCatalogueVersion,
            assignment.AssignedCatalogueDigestSha256,
            entitlementRevision);
    }

    // Expiry telemetry hook set by the public coordinator entry points (records expiry once).

    private sealed record ExpiryNormalization(bool DidExpire, LicenceAssignmentEntity? ActiveAssignment);

    private sealed record ProvisioningOutcome(LicenceAssignmentEntity Assignment, string Source);
}
