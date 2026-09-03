using HushShared.HushVoting.Licensing.Model;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Higher-plan activation half of <see cref="LicenceEntitlementCoordinator"/>. Applies the FEAT-012
/// transition policy behind a server-only boundary using the caller's expected plan/revision,
/// a canonical payload fingerprint, per-subject UUID idempotency, and atomic assignment/history
/// mutation. Every database-evaluated outcome is durable; replays return the original outcome;
/// key reuse with a different payload returns idempotency_payload_mismatch without mutation.
/// </summary>
public static partial class LicenceEntitlementCoordinator
{
    private const string OperationActivate = "activate";

    /// <summary>
    /// Activates a higher Veritas plan for an initialized entitlement. Requires the previously
    /// returned current plan id and entitlement revision (precondition); never initializes an
    /// entitlement and never activates Enterprise.
    /// </summary>
    public static async Task<LicenceActivationResult> ActivateHigherPlanAsync(
        Func<DbContext> contextFactory,
        LicenceServiceConfiguration configuration,
        AuthenticatedIdentitySubject subject,
        LicenceActivationCommand command,
        TimeProvider timeProvider,
        LicenceTelemetry? telemetry,
        CancellationToken cancellationToken,
        LicenceFailureInjection? failureInjection = null)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(command);
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
                    return await RunActivateAttemptAsync(
                        contextFactory, configuration, subject, command, nowUtc, attemptNumber,
                        telemetry, failureInjection, ct);
                },
                reconcileCommittedAsync: ct => ReconcileActivateAsync(
                    contextFactory, subject, command, ct),
                OperationActivate,
                telemetry,
                cancellationToken);

            RecordActivation(result, telemetry, timeProvider, startedAt);
            return result;
        }
        catch (LicenceExecutionExhaustedException exception)
        {
            var outcomeName = exception.StableCode;
            telemetry?.RecordActivationOutcome(outcomeName);
            telemetry?.RecordOperationDuration(OperationActivate, outcomeName, timeProvider.GetElapsedTime(startedAt));
            telemetry?.LogAuthorityUnavailable(OperationActivate, outcomeName);
            return LicenceActivationResult.Fail(exception.StableCode, exception.Message);
        }
    }

    private static void RecordActivation(
        LicenceActivationResult result,
        LicenceTelemetry? telemetry,
        TimeProvider timeProvider,
        long startedAt)
    {
        var outcomeName = result.IsSuccess
            ? LicenceEntitlementOutcomeNames.ToWireName(result.Outcome!.Value)
            : result.StableErrorCode ?? "unknown";

        telemetry?.RecordActivationOutcome(outcomeName);
        telemetry?.RecordOperationDuration(OperationActivate, outcomeName, timeProvider.GetElapsedTime(startedAt));
    }

    private static async Task<LicenceActivationResult> RunActivateAttemptAsync(
        Func<DbContext> contextFactory,
        LicenceServiceConfiguration configuration,
        AuthenticatedIdentitySubject subject,
        LicenceActivationCommand command,
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

            var fingerprint = LicenceEntitlementDecisions.CanonicalActivationFingerprint(
                command.ExpectedCurrentPlanId,
                command.ExpectedEntitlementRevision,
                command.RequestedTargetPlanId);

            var existing = await db.Set<LicenceActivationOperationEntity>()
                .SingleOrDefaultAsync(o =>
                    o.LicenceSubjectId == subjectRow.LicenceSubjectId
                    && o.IdempotencyKey == command.IdempotencyKey,
                    cancellationToken);

            if (existing is not null)
            {
                var replayOrMismatch = string.Equals(
                    existing.CanonicalPayloadFingerprintSha256,
                    fingerprint,
                    StringComparison.OrdinalIgnoreCase)
                    ? await ReplayOperationAsync(db, existing, cancellationToken)
                    : LicenceActivationResult.Ok(
                        LicenceActivationOutcome.IdempotencyPayloadMismatch,
                        entitlement: null,
                        "Idempotency key reuse with a different payload is rejected without mutation.");

                await CommitWithInjectionAsync(db, transaction, injection, attemptNumber, cancellationToken);

                telemetry?.LogOperationCompleted(
                    OperationActivate, subjectRow.LicenceSubjectId,
                    LicenceEntitlementOutcomeNames.ToWireName(replayOrMismatch.Outcome!.Value));
                return replayOrMismatch;
            }

            var expiry = await NormalizeExpiryIfDueAsync(db, configuration, subjectRow, nowUtc, cancellationToken);
            if (expiry.DidExpire)
            {
                telemetry?.RecordExpiryNormalized();
            }

            var operation = NewActivationOperationRow(
                subjectRow.LicenceSubjectId,
                configuration.CatalogueVersion,
                command,
                fingerprint,
                nowUtc);

            var active = expiry.ActiveAssignment;
            LicenceActivationOutcome outcome;
            LicenceAssignmentEntity? resultingAssignment = null;
            long? resultingRevision = null;

            if (active is null)
            {
                outcome = LicenceActivationOutcome.EntitlementNotInitialized;
                operation.DurableResult = LicenceEntitlementOutcomeNames.ToWireName(outcome);
            }
            else if (!string.Equals(active.PlanId, command.ExpectedCurrentPlanId, StringComparison.Ordinal)
                     || subjectRow.EntitlementRevision != command.ExpectedEntitlementRevision)
            {
                outcome = LicenceActivationOutcome.PreconditionConflict;
                operation.DurableResult = LicenceEntitlementOutcomeNames.ToWireName(outcome);
            }
            else
            {
                var currentPlanId = HushVotingLicencePlanId.TryGetKnown(active.PlanId);
                var targetPlanId = HushVotingLicencePlanId.TryGetKnown(command.RequestedTargetPlanId);
                if (currentPlanId is null || targetPlanId is null)
                {
                    outcome = LicenceActivationOutcome.PlanUnknown;
                    operation.DurableResult = LicenceEntitlementOutcomeNames.ToWireName(outcome);
                }
                else
                {
                    var evaluation = HushVotingLicenceUpgradeEvaluator.Evaluate(
                        configuration.Catalogue, currentPlanId, targetPlanId);

                    if (!evaluation.Allowed)
                    {
                        outcome = LicenceEntitlementDecisions.MapUpgradeEvaluationToDurableResult(evaluation);
                        operation.DurableResult = LicenceEntitlementOutcomeNames.ToWireName(outcome);
                    }
                    else
                    {
                        var targetPlan = configuration.Catalogue.FindPlan(targetPlanId)
                            ?? throw new InvalidOperationException(
                                "The FEAT-012 evaluator allowed a target that is absent from the configured catalogue.");

                        var nextRevision = subjectRow.EntitlementRevision + 1L;
                        subjectRow.EntitlementRevision = nextRevision;

                        var expiresAtUtc = LicenceEntitlementDecisions.ComputeExpiryInstant(nowUtc, targetPlan.Term);

                        active.LifecycleStatus = LicencePersistenceVocabulary.LifecycleSuperseded;
                        active.LifecycleChangedAtUtc = nowUtc;
                        active.LifecycleReason = LicenceEntitlementDecisions.ReasonSupersededByAutomaticUpgrade;

                        var assignment = NewAssignment(
                            subjectRow.LicenceSubjectId,
                            targetPlan,
                            configuration,
                            LicencePersistenceVocabulary.SourceAutomaticUpgrade,
                            effectiveFromUtc: nowUtc,
                            expiresAtUtc,
                            createdByOperationId: operation.LicenceActivationOperationId,
                            creationCorrelationId: command.RequestCorrelationId);

                        db.Add(assignment);

                        var sequence = await NextEventSequenceAsync(db, subjectRow.LicenceSubjectId, cancellationToken);

                        db.Add(NewTransitionEvent(
                            subjectRow.LicenceSubjectId,
                            sequence,
                            LicencePersistenceVocabulary.EventTypeSuperseded,
                            nextRevision,
                            active.LicenceAssignmentId,
                            active.PlanId,
                            active.AssignedCatalogueVersion,
                            LicenceEntitlementDecisions.ReasonSupersededByAutomaticUpgrade,
                            operationReferenceId: operation.LicenceActivationOperationId,
                            nowUtc));

                        db.Add(NewTransitionEvent(
                            subjectRow.LicenceSubjectId,
                            sequence + 1L,
                            LicencePersistenceVocabulary.EventTypeCreated,
                            nextRevision,
                            assignment.LicenceAssignmentId,
                            assignment.PlanId,
                            configuration.CatalogueVersion,
                            LicencePersistenceVocabulary.SourceAutomaticUpgrade,
                            operationReferenceId: operation.LicenceActivationOperationId,
                            nowUtc));

                        outcome = LicenceActivationOutcome.Activated;
                        operation.DurableResult = LicenceEntitlementOutcomeNames.ToWireName(outcome);
                        resultingAssignment = assignment;
                        resultingRevision = nextRevision;
                    }
                }
            }

            operation.CompletedAtUtc = nowUtc;
            db.Add(operation);

            // Avoid a circular-insert ordering between LicenceAssignment.CreatedByOperationId and
            // LicenceActivationOperation.ResultingAssignmentId: persist the operation/transition
            // first, then attach the resulting-assignment reference in the same transaction.
            await db.SaveChangesAsync(cancellationToken);
            if (resultingAssignment is not null)
            {
                operation.ResultingAssignmentId = resultingAssignment.LicenceAssignmentId;
                operation.ResultingEntitlementRevision = resultingRevision;
                await db.SaveChangesAsync(cancellationToken);
            }

            await CommitWithInjectionAsync(db, transaction, injection, attemptNumber, cancellationToken);

            var entitlement = resultingAssignment is not null
                ? ToEntitlement(resultingAssignment, subjectRow.EntitlementRevision)
                : null;

            telemetry?.LogOperationCompleted(
                OperationActivate, subjectRow.LicenceSubjectId,
                LicenceEntitlementOutcomeNames.ToWireName(outcome));

            return LicenceActivationResult.Ok(outcome, entitlement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateException exception)
        {
            return ClassifyActivateDbFailure(exception);
        }
        catch (PostgresException exception)
        {
            return ClassifyActivateDbFailure(exception);
        }
        catch (NpgsqlException exception)
        {
            return LicenceActivationResult.Fail(
                LicenceEntitlementFailureCodes.StorageUnavailable,
                $"Activation storage unavailable: {exception.GetType().Name}");
        }
        catch (Exception exception) when (LicencePostgresFailureClassifier.IsStorageUnavailable(exception))
        {
            return LicenceActivationResult.Fail(
                LicenceEntitlementFailureCodes.StorageUnavailable,
                $"Activation storage unavailable: {exception.GetType().Name}");
        }
    }

    private static async Task<LicenceActivationResult?> ReconcileActivateAsync(
        Func<DbContext> contextFactory,
        AuthenticatedIdentitySubject subject,
        LicenceActivationCommand command,
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
            // Authoritative absence: the activation transaction did not commit.
            return null;
        }

        var operation = await db.Set<LicenceActivationOperationEntity>()
            .SingleOrDefaultAsync(o =>
                o.LicenceSubjectId == subjectRow.LicenceSubjectId
                && o.IdempotencyKey == command.IdempotencyKey,
                cancellationToken);

        return operation is null
            ? null
            : await ReplayOperationAsync(db, operation, cancellationToken);
    }

    private static async Task<LicenceActivationResult> ReplayOperationAsync(
        DbContext db,
        LicenceActivationOperationEntity operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.DurableResult is not { } durableResult)
        {
            return LicenceActivationResult.Fail(
                LicenceEntitlementFailureCodes.PersistenceInvariantViolation,
                "A committed activation operation row has no durable result.");
        }

        var outcome = LicenceEntitlementOutcomeNames.FromDurableResultString(durableResult);
        if (outcome != LicenceActivationOutcome.Activated)
        {
            return LicenceActivationResult.Ok(outcome, entitlement: null);
        }

        if (operation.ResultingAssignmentId is not { } assignmentId
            || operation.ResultingEntitlementRevision is not { } resultingRevision)
        {
            return LicenceActivationResult.Fail(
                LicenceEntitlementFailureCodes.PersistenceInvariantViolation,
                "A committed activated operation row is missing its resulting assignment reference.");
        }

        var assignment = await db.Set<LicenceAssignmentEntity>()
            .SingleOrDefaultAsync(a => a.LicenceAssignmentId == assignmentId, cancellationToken);

        if (assignment is null)
        {
            return LicenceActivationResult.Fail(
                LicenceEntitlementFailureCodes.PersistenceInvariantViolation,
                "A committed activated operation references a missing assignment.");
        }

        return LicenceActivationResult.Ok(
            LicenceActivationOutcome.Activated,
            ToEntitlement(assignment, resultingRevision));
    }

    private static LicenceActivationOperationEntity NewActivationOperationRow(
        Guid subjectId,
        string evaluatedCatalogueVersion,
        LicenceActivationCommand command,
        string fingerprint,
        DateTime nowUtc) =>
        new()
        {
            LicenceActivationOperationId = Guid.CreateVersion7(),
            LicenceSubjectId = subjectId,
            IdempotencyKey = command.IdempotencyKey,
            CanonicalPayloadFingerprintSha256 = fingerprint,
            ExpectedCurrentPlanId = command.ExpectedCurrentPlanId,
            ExpectedEntitlementRevision = command.ExpectedEntitlementRevision,
            RequestedTargetPlanId = command.RequestedTargetPlanId,
            EvaluatedCatalogueVersion = evaluatedCatalogueVersion,
            DurableResult = null,
            ResultingAssignmentId = null,
            ResultingEntitlementRevision = null,
            CreatedAtUtc = nowUtc,
            CompletedAtUtc = null,
            RequestCorrelationId = command.RequestCorrelationId,
        };

    private static LicenceActivationResult ClassifyActivateDbFailure(DbUpdateException exception)
    {
        if (LicencePostgresFailureClassifier.IsRecognizedTransient(exception))
        {
            throw new LicenceTransientConflictException(
                "Recognized transient PostgreSQL race during activation.",
                exception);
        }

        if (LicencePostgresFailureClassifier.IsPersistenceInvariantViolation(exception))
        {
            return LicenceActivationResult.Fail(
                LicenceEntitlementFailureCodes.PersistenceInvariantViolation,
                "PostgreSQL rejected an activation write that violates a persistence invariant.");
        }

        return LicenceActivationResult.Fail(
            LicenceEntitlementFailureCodes.StorageUnavailable,
            "Unclassified database write failure during activation; no unintended transition committed.");
    }

    private static LicenceActivationResult ClassifyActivateDbFailure(PostgresException exception)
    {
        if (LicencePostgresFailureClassifier.IsRecognizedTransient(exception))
        {
            throw new LicenceTransientConflictException(
                "Recognized transient PostgreSQL race during activation.",
                exception);
        }

        if (LicencePostgresFailureClassifier.IsPersistenceInvariantViolation(exception))
        {
            return LicenceActivationResult.Fail(
                LicenceEntitlementFailureCodes.PersistenceInvariantViolation,
                "PostgreSQL rejected an activation write that violates a persistence invariant.");
        }

        return LicenceActivationResult.Fail(
            LicenceEntitlementFailureCodes.StorageUnavailable,
            $"PostgreSQL failure during activation: {exception.SqlState}");
    }

    private static async Task CommitWithInjectionAsync(
        DbContext db,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        LicenceFailureInjection? injection,
        int attemptNumber,
        CancellationToken cancellationToken)
    {
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
    }
}
