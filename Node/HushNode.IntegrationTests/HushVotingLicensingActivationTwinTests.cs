using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.HushVoting.Licensing.Model;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-013 Task 3.6 real-PostgreSQL TwinTests for durable higher-plan activation and idempotency:
/// every activation outcome, exact operation/event/revision counts, per-subject key semantics,
/// expiry-before-activation races, and no unintended transition on rejection or replay.
/// </summary>
[Collection("FEAT-013 Licensing PostgreSQL")]
[Trait("Category", "FEAT-013")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicensingActivationTwinTests
{
    private const string V1 = "hushvoting-licence-catalogue/v1.0.0";
    private const string V1Schema = "hushvoting-licence-catalogue/v1";
    private static readonly string DigestA = new('A', 64);

    private static readonly DateTime FixedNowUtc = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly LicensingPostgresFixture _fixture;

    public HushVotingLicensingActivationTwinTests(LicensingPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class FixedTimeProvider(DateTime fixedUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(DateTime.SpecifyKind(fixedUtc, DateTimeKind.Utc));
    }

    private static LicenceReleaseInstallSpec Spec() => new(V1, DigestA, V1Schema, "hush-server-node-test", "feat013-activation-twin");

    private static LicenceServiceConfiguration Configuration() =>
        LicenceServiceConfiguration.CreateDefault(DigestA);

    private async Task<string> NewDatabaseAsync(long watermark = 10_000)
    {
        var databaseName = $"feat013_act_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(databaseName);
        await _fixture.MigrateToHeadAsync(databaseName);

        await using var context = _fixture.CreateContext(databaseName);
        var state = await LicenceCatalogueLedgerCoordinator.ReconcileAsync(
            context,
            Spec(),
            _ => Task.FromResult(watermark),
            CancellationToken.None);
        state.Ready.Should().BeTrue();

        return databaseName;
    }

    private static async Task<AuthenticatedIdentitySubject> NewSubjectAsync(
        LicensingPostgresFixture fixture,
        string databaseName,
        long creationBlock = 20_000)
    {
        var canonical = $"identity-{Guid.NewGuid():N}";
        var created = AuthenticatedIdentitySubject.TryCreate(
            LicencePersistenceVocabulary.SubjectTypeIdentity,
            canonical,
            creationBlock,
            out var subject,
            out var error);
        created.Should().BeTrue(error);
        return subject!;
    }

    private static async Task<LicenceResolutionResult> ResolveAsync(
        LicensingPostgresFixture fixture,
        string databaseName,
        AuthenticatedIdentitySubject identity,
        DateTime nowUtc) =>
        await LicenceEntitlementCoordinator.ResolveOrProvisionAsync(
            () => fixture.CreateContext(databaseName),
            Configuration(),
            identity,
            new FixedTimeProvider(nowUtc),
            telemetry: null,
            CancellationToken.None);

    private static async Task<LicenceActivationResult> ActivateAsync(
        LicensingPostgresFixture fixture,
        string databaseName,
        AuthenticatedIdentitySubject identity,
        LicenceActivationCommand command,
        DateTime nowUtc) =>
        await LicenceEntitlementCoordinator.ActivateHigherPlanAsync(
            () => fixture.CreateContext(databaseName),
            Configuration(),
            identity,
            command,
            new FixedTimeProvider(nowUtc),
            telemetry: null,
            CancellationToken.None);

    private static LicenceActivationCommand Command(
        string expectedPlan,
        long expectedRevision,
        string targetPlan,
        Guid? key = null,
        string? correlation = null)
    {
        var created = LicenceActivationCommand.TryCreate(
            key ?? Guid.NewGuid(),
            expectedPlan,
            expectedRevision,
            targetPlan,
            correlation,
            out var command,
            out var error);
        created.Should().BeTrue(error);
        return command!;
    }

    private static async Task<AuthenticatedIdentitySubject> SeedDirectFreeAsync(
        LicensingPostgresFixture fixture,
        string databaseName,
        long creationBlock = 20_000)
    {
        var identity = await NewSubjectAsync(fixture, databaseName, creationBlock);
        var result = await ResolveAsync(fixture, databaseName, identity, FixedNowUtc);
        result.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedDefault);
        return identity;
    }

    private static async Task SeedActiveAnnualVeritasAsync(
        LicensingPostgresFixture fixture,
        string databaseName,
        AuthenticatedIdentitySubject subject,
        DateTime effectiveFromUtc,
        DateTime expiresAtUtc)
    {
        await using var db = fixture.CreateContext(databaseName);

        var subjectRow = new LicenceSubjectEntity
        {
            LicenceSubjectId = Guid.CreateVersion7(),
            SubjectType = subject.SubjectType,
            CanonicalPublicSigningAddress = subject.CanonicalPublicSigningAddress,
            IdentityCreationBlockIndex = subject.IdentityCreationBlockIndex,
            CreatedAtUtc = effectiveFromUtc,
            EntitlementRevision = 1,
        };
        db.Add(subjectRow);

        var assignment = new LicenceAssignmentEntity
        {
            LicenceAssignmentId = Guid.CreateVersion7(),
            LicenceSubjectId = subjectRow.LicenceSubjectId,
            PlanId = HushVotingLicencePlanId.Veritas500.Value,
            AssignedCatalogueVersion = V1,
            AssignedCatalogueDigestSha256 = DigestA,
            LifecycleStatus = LicencePersistenceVocabulary.LifecycleActive,
            Source = LicencePersistenceVocabulary.SourceAutomaticUpgrade,
            EffectiveFromUtc = effectiveFromUtc,
            ExpiresAtUtc = expiresAtUtc,
            LifecycleChangedAtUtc = null,
            LifecycleReason = null,
            PlanFamily = LicencePersistenceVocabulary.PlanFamilyVeritas,
            UpgradeRank = 1000,
            EligibleVoterCap = 500,
            UnlimitedElectionPolicy = true,
            TermKind = LicencePersistenceVocabulary.TermAnnual,
            TermYears = 1,
            AllowedGovernanceOptionIds =
            [
                HushVotingGovernanceOptionId.NoCustomerTrustees.Value,
                HushVotingGovernanceOptionId.Trustees3Of5.Value,
            ],
            CreationCorrelationId = null,
            CreatedByOperationId = null,
        };
        db.Add(assignment);

        db.Add(new LicenceTransitionEventEntity
        {
            LicenceTransitionEventId = Guid.CreateVersion7(),
            LicenceSubjectId = subjectRow.LicenceSubjectId,
            EventSequence = 1,
            EventType = LicencePersistenceVocabulary.EventTypeCreated,
            SubjectRevision = 1,
            AssignmentId = assignment.LicenceAssignmentId,
            PlanId = assignment.PlanId,
            CatalogueDecisionVersion = V1,
            SourceOrReason = LicencePersistenceVocabulary.SourceAutomaticUpgrade,
            OperationReferenceId = null,
            OccurredAtUtc = effectiveFromUtc,
        });

        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ valid transitions

    [Fact]
    public async Task Valid_activation_supersedes_direct_free_and_starts_one_annual_veritas()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await SeedDirectFreeAsync(_fixture, databaseName);

            var result = await ActivateAsync(
                _fixture, databaseName, identity,
                Command(HushVotingLicencePlanId.DirectFree.Value, 1, HushVotingLicencePlanId.Veritas500.Value),
                FixedNowUtc);

            result.IsSuccess.Should().BeTrue();
            result.Outcome.Should().Be(LicenceActivationOutcome.Activated);
            result.Entitlement!.PlanId.Should().Be(HushVotingLicencePlanId.Veritas500.Value);
            result.Entitlement.Source.Should().Be(LicencePersistenceVocabulary.SourceAutomaticUpgrade);
            result.Entitlement.TermKind.Should().Be(LicencePersistenceVocabulary.TermAnnual);
            result.Entitlement.ExpiresAtUtc.Should().Be(FixedNowUtc.AddYears(1));
            result.Entitlement.EntitlementRevision.Should().Be(2);

            await using var db = _fixture.CreateContext(databaseName);
            var subjectRow = await db.Set<LicenceSubjectEntity>()
                .SingleAsync(s => s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress);
            subjectRow.EntitlementRevision.Should().Be(2);

            var assignments = await db.Set<LicenceAssignmentEntity>()
                .Where(a => a.LicenceSubjectId == subjectRow.LicenceSubjectId)
                .OrderBy(a => a.EffectiveFromUtc)
                .ThenBy(a => a.PlanId)
                .ToListAsync();
            assignments.Should().HaveCount(2);

            var directFree = assignments.Single(a => a.PlanId == HushVotingLicencePlanId.DirectFree.Value);
            var veritas = assignments.Single(a => a.PlanId == HushVotingLicencePlanId.Veritas500.Value);
            directFree.LifecycleStatus.Should().Be(LicencePersistenceVocabulary.LifecycleSuperseded);
            directFree.LifecycleChangedAtUtc.Should().Be(FixedNowUtc);
            directFree.LifecycleReason.Should().Be(LicenceEntitlementDecisions.ReasonSupersededByAutomaticUpgrade);
            veritas.LifecycleStatus.Should().Be(LicencePersistenceVocabulary.LifecycleActive);
            veritas.CreatedByOperationId.Should().NotBeNull();
            veritas.CreationCorrelationId.Should().BeNull();

            var events = await db.Set<LicenceTransitionEventEntity>()
                .Where(e => e.LicenceSubjectId == subjectRow.LicenceSubjectId)
                .OrderBy(e => e.EventSequence)
                .ToListAsync();
            events.Select(e => e.EventSequence).Should().Equal(1, 2, 3);
            events[1].EventType.Should().Be(LicencePersistenceVocabulary.EventTypeSuperseded);
            events[1].SubjectRevision.Should().Be(2);
            events[1].OperationReferenceId.Should().NotBeNull();
            events[2].EventType.Should().Be(LicencePersistenceVocabulary.EventTypeCreated);
            events[2].SubjectRevision.Should().Be(2);
            events[2].PlanId.Should().Be(HushVotingLicencePlanId.Veritas500.Value);

            var operation = await db.Set<LicenceActivationOperationEntity>()
                .SingleAsync(o => o.LicenceSubjectId == subjectRow.LicenceSubjectId);
            operation.DurableResult.Should().Be(LicencePersistenceVocabulary.OperationResultActivated);
            operation.CompletedAtUtc.Should().Be(FixedNowUtc);
            operation.ResultingAssignmentId.Should().Be(veritas.LicenceAssignmentId);
            operation.ResultingEntitlementRevision.Should().Be(2);
            operation.EvaluatedCatalogueVersion.Should().Be(V1);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Activation_chain_direct_free_500_2000_10000_supersedes_each_once()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await SeedDirectFreeAsync(_fixture, databaseName);

            // Direct Free -> Veritas 500 (rev 1 -> 2)
            var to500 = await ActivateAsync(
                _fixture, databaseName, identity,
                Command(HushVotingLicencePlanId.DirectFree.Value, 1, HushVotingLicencePlanId.Veritas500.Value, correlation: "upgrade-1"),
                FixedNowUtc);
            to500.Outcome.Should().Be(LicenceActivationOutcome.Activated);

            // Veritas 500 -> Veritas 2000 (rev 2 -> 3)
            var to2000 = await ActivateAsync(
                _fixture, databaseName, identity,
                Command(HushVotingLicencePlanId.Veritas500.Value, 2, HushVotingLicencePlanId.Veritas2000.Value, correlation: "upgrade-2"),
                FixedNowUtc);
            to2000.Outcome.Should().Be(LicenceActivationOutcome.Activated);
            to2000.Entitlement!.EligibleVoterCap.Should().Be(2000);

            // Veritas 2000 -> Veritas 10000 (rev 3 -> 4)
            var to10000 = await ActivateAsync(
                _fixture, databaseName, identity,
                Command(HushVotingLicencePlanId.Veritas2000.Value, 3, HushVotingLicencePlanId.Veritas10000.Value, correlation: "upgrade-3"),
                FixedNowUtc);
            to10000.Outcome.Should().Be(LicenceActivationOutcome.Activated);
            to10000.Entitlement!.EligibleVoterCap.Should().Be(10000);

            await using var db = _fixture.CreateContext(databaseName);
            var subjectRow = await db.Set<LicenceSubjectEntity>()
                .SingleAsync(s => s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress);
            subjectRow.EntitlementRevision.Should().Be(4);

            var assignments = await db.Set<LicenceAssignmentEntity>()
                .Where(a => a.LicenceSubjectId == subjectRow.LicenceSubjectId)
                .ToListAsync();
            assignments.Should().HaveCount(4);
            assignments.Should().ContainSingle(a =>
                a.LifecycleStatus == LicencePersistenceVocabulary.LifecycleActive
                && a.PlanId == HushVotingLicencePlanId.Veritas10000.Value);
            assignments.Count(a => a.LifecycleStatus == LicencePersistenceVocabulary.LifecycleSuperseded).Should().Be(3);

            (await db.Set<LicenceTransitionEventEntity>().CountAsync(e =>
                e.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(7);

            var operations = await db.Set<LicenceActivationOperationEntity>()
                .Where(o => o.LicenceSubjectId == subjectRow.LicenceSubjectId)
                .OrderBy(o => o.CompletedAtUtc)
                .ToListAsync();
            operations.Should().HaveCount(3);
            operations.Should().OnlyContain(o => o.DurableResult == LicencePersistenceVocabulary.OperationResultActivated);
            operations.Select(o => o.RequestCorrelationId).Should().BeEquivalentTo("upgrade-1", "upgrade-2", "upgrade-3");
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Direct_free_may_activate_veritas_10000_directly()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await SeedDirectFreeAsync(_fixture, databaseName);

            var result = await ActivateAsync(
                _fixture, databaseName, identity,
                Command(HushVotingLicencePlanId.DirectFree.Value, 1, HushVotingLicencePlanId.Veritas10000.Value),
                FixedNowUtc);

            result.Outcome.Should().Be(LicenceActivationOutcome.Activated);
            result.Entitlement!.EligibleVoterCap.Should().Be(10000);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    // ------------------------------------------------------------------ idempotency semantics

    [Fact]
    public async Task Identical_command_replay_returns_the_original_outcome_without_mutation()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await SeedDirectFreeAsync(_fixture, databaseName);
            var key = Guid.NewGuid();

            var first = await ActivateAsync(
                _fixture, databaseName, identity,
                Command(HushVotingLicencePlanId.DirectFree.Value, 1, HushVotingLicencePlanId.Veritas500.Value, key: key),
                FixedNowUtc);
            first.Outcome.Should().Be(LicenceActivationOutcome.Activated);

            var replay = await ActivateAsync(
                _fixture, databaseName, identity,
                Command(HushVotingLicencePlanId.DirectFree.Value, 1, HushVotingLicencePlanId.Veritas500.Value, key: key),
                FixedNowUtc);

            replay.IsSuccess.Should().BeTrue();
            replay.Outcome.Should().Be(LicenceActivationOutcome.Activated);
            replay.Entitlement!.LicenceAssignmentId.Should().Be(first.Entitlement!.LicenceAssignmentId);
            replay.Entitlement.EntitlementRevision.Should().Be(2);

            await using var db = _fixture.CreateContext(databaseName);
            var subjectRow = await db.Set<LicenceSubjectEntity>()
                .SingleAsync(s => s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress);
            subjectRow.EntitlementRevision.Should().Be(2);
            (await db.Set<LicenceAssignmentEntity>().CountAsync(a =>
                a.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(2);
            (await db.Set<LicenceActivationOperationEntity>().CountAsync(o =>
                o.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(1);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Key_reuse_with_a_different_payload_returns_mismatch_without_mutation()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await SeedDirectFreeAsync(_fixture, databaseName);
            var key = Guid.NewGuid();

            var first = await ActivateAsync(
                _fixture, databaseName, identity,
                Command(HushVotingLicencePlanId.DirectFree.Value, 1, HushVotingLicencePlanId.Veritas500.Value, key: key),
                FixedNowUtc);
            first.Outcome.Should().Be(LicenceActivationOutcome.Activated);

            var mismatch = await ActivateAsync(
                _fixture, databaseName, identity,
                Command(HushVotingLicencePlanId.DirectFree.Value, 1, HushVotingLicencePlanId.Veritas2000.Value, key: key),
                FixedNowUtc);

            mismatch.Outcome.Should().Be(LicenceActivationOutcome.IdempotencyPayloadMismatch);
            mismatch.Entitlement.Should().BeNull();

            await using var db = _fixture.CreateContext(databaseName);
            var subjectRow = await db.Set<LicenceSubjectEntity>()
                .SingleAsync(s => s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress);
            subjectRow.EntitlementRevision.Should().Be(2);
            (await db.Set<LicenceAssignmentEntity>().CountAsync(a =>
                a.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(2);
            (await db.Set<LicenceActivationOperationEntity>().CountAsync(o =>
                o.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(1);

            // The original operation is unchanged.
            var operation = await db.Set<LicenceActivationOperationEntity>().SingleAsync(o =>
                o.LicenceSubjectId == subjectRow.LicenceSubjectId);
            operation.DurableResult.Should().Be(LicencePersistenceVocabulary.OperationResultActivated);
            operation.RequestedTargetPlanId.Should().Be(HushVotingLicencePlanId.Veritas500.Value);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Same_caller_key_may_be_reused_on_a_different_subject()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identityA = await SeedDirectFreeAsync(_fixture, databaseName);
            var identityB = await SeedDirectFreeAsync(_fixture, databaseName);
            var sharedKey = Guid.NewGuid();

            var resultA = await ActivateAsync(
                _fixture, databaseName, identityA,
                Command(HushVotingLicencePlanId.DirectFree.Value, 1, HushVotingLicencePlanId.Veritas500.Value, key: sharedKey),
                FixedNowUtc);
            var resultB = await ActivateAsync(
                _fixture, databaseName, identityB,
                Command(HushVotingLicencePlanId.DirectFree.Value, 1, HushVotingLicencePlanId.Veritas500.Value, key: sharedKey),
                FixedNowUtc);

            resultA.Outcome.Should().Be(LicenceActivationOutcome.Activated);
            resultB.Outcome.Should().Be(LicenceActivationOutcome.Activated);
            resultB.Entitlement!.LicenceAssignmentId.Should().NotBe(resultA.Entitlement!.LicenceAssignmentId);

            await using var db = _fixture.CreateContext(databaseName);
            (await db.Set<LicenceActivationOperationEntity>().CountAsync()).Should().Be(2);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    // ------------------------------------------------------------------ durable rejections

    [Fact]
    public async Task Same_plan_activation_returns_transition_unchanged_durably()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await SeedDirectFreeAsync(_fixture, databaseName);

            var result = await ActivateAsync(
                _fixture, databaseName, identity,
                Command(HushVotingLicencePlanId.DirectFree.Value, 1, HushVotingLicencePlanId.DirectFree.Value),
                FixedNowUtc);

            result.Outcome.Should().Be(LicenceActivationOutcome.TransitionUnchanged);

            await using var db = _fixture.CreateContext(databaseName);
            var subjectRow = await db.Set<LicenceSubjectEntity>()
                .SingleAsync(s => s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress);
            subjectRow.EntitlementRevision.Should().Be(1);
            (await db.Set<LicenceAssignmentEntity>().CountAsync(a =>
                a.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(1);
            (await db.Set<LicenceTransitionEventEntity>().CountAsync(e =>
                e.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(1);

            var operation = await db.Set<LicenceActivationOperationEntity>().SingleAsync(o =>
                o.LicenceSubjectId == subjectRow.LicenceSubjectId);
            operation.DurableResult.Should().Be(LicencePersistenceVocabulary.OperationResultTransitionUnchanged);
            operation.ResultingAssignmentId.Should().BeNull();
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Downgrade_target_returns_transition_not_higher_durably()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await NewSubjectAsync(_fixture, databaseName);
            var effectiveFrom = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            await SeedActiveAnnualVeritasAsync(_fixture, databaseName, identity, effectiveFrom, effectiveFrom.AddYears(1));

            var result = await ActivateAsync(
                _fixture, databaseName, identity,
                Command(HushVotingLicencePlanId.Veritas500.Value, 1, HushVotingLicencePlanId.DirectFree.Value),
                new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));

            result.Outcome.Should().Be(LicenceActivationOutcome.TransitionNotHigher);

            await using var db = _fixture.CreateContext(databaseName);
            var subjectRow = await db.Set<LicenceSubjectEntity>()
                .SingleAsync(s => s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress);
            subjectRow.EntitlementRevision.Should().Be(1);
            var operation = await db.Set<LicenceActivationOperationEntity>().SingleAsync(o =>
                o.LicenceSubjectId == subjectRow.LicenceSubjectId);
            operation.DurableResult.Should().Be(LicencePersistenceVocabulary.OperationResultTransitionNotHigher);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Unknown_target_returns_plan_unknown_durably()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await SeedDirectFreeAsync(_fixture, databaseName);

            var result = await ActivateAsync(
                _fixture, databaseName, identity,
                Command(HushVotingLicencePlanId.DirectFree.Value, 1, "hushvoting.veritas.999"),
                FixedNowUtc);

            result.Outcome.Should().Be(LicenceActivationOutcome.PlanUnknown);

            await using var db = _fixture.CreateContext(databaseName);
            var subjectRow = await db.Set<LicenceSubjectEntity>()
                .SingleAsync(s => s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress);
            subjectRow.EntitlementRevision.Should().Be(1);
            (await db.Set<LicenceAssignmentEntity>().CountAsync(a =>
                a.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(1);
            var operation = await db.Set<LicenceActivationOperationEntity>().SingleAsync(o =>
                o.LicenceSubjectId == subjectRow.LicenceSubjectId);
            operation.DurableResult.Should().Be(LicencePersistenceVocabulary.OperationResultPlanUnknown);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Enterprise_target_returns_plan_unavailable_durably()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await SeedDirectFreeAsync(_fixture, databaseName);

            var result = await ActivateAsync(
                _fixture, databaseName, identity,
                Command(HushVotingLicencePlanId.DirectFree.Value, 1, HushVotingLicencePlanId.Enterprise.Value),
                FixedNowUtc);

            result.Outcome.Should().Be(LicenceActivationOutcome.PlanUnavailable);

            await using var db = _fixture.CreateContext(databaseName);
            var subjectRow = await db.Set<LicenceSubjectEntity>()
                .SingleAsync(s => s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress);
            var operation = await db.Set<LicenceActivationOperationEntity>().SingleAsync(o =>
                o.LicenceSubjectId == subjectRow.LicenceSubjectId);
            operation.DurableResult.Should().Be(LicencePersistenceVocabulary.OperationResultPlanUnavailable);
            (await db.Set<LicenceAssignmentEntity>().CountAsync(a =>
                a.LicenceSubjectId == subjectRow.LicenceSubjectId
                && a.PlanId == HushVotingLicencePlanId.Enterprise.Value)).Should().Be(0);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Stale_precondition_returns_precondition_conflict_durably()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await SeedDirectFreeAsync(_fixture, databaseName);

            // Revision is 1, but the caller submits a stale expected revision 0.
            var result = await ActivateAsync(
                _fixture, databaseName, identity,
                Command(HushVotingLicencePlanId.DirectFree.Value, 0, HushVotingLicencePlanId.Veritas500.Value),
                FixedNowUtc);

            result.Outcome.Should().Be(LicenceActivationOutcome.PreconditionConflict);

            await using var db = _fixture.CreateContext(databaseName);
            var subjectRow = await db.Set<LicenceSubjectEntity>()
                .SingleAsync(s => s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress);
            subjectRow.EntitlementRevision.Should().Be(1);
            (await db.Set<LicenceAssignmentEntity>().CountAsync(a =>
                a.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(1);
            (await db.Set<LicenceTransitionEventEntity>().CountAsync(e =>
                e.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(1);
            var operation = await db.Set<LicenceActivationOperationEntity>().SingleAsync(o =>
                o.LicenceSubjectId == subjectRow.LicenceSubjectId);
            operation.DurableResult.Should().Be(LicencePersistenceVocabulary.OperationResultPreconditionConflict);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Activation_without_initialized_entitlement_returns_not_initialized_durably()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await NewSubjectAsync(_fixture, databaseName);

            var result = await ActivateAsync(
                _fixture, databaseName, identity,
                Command(HushVotingLicencePlanId.DirectFree.Value, 0, HushVotingLicencePlanId.Veritas500.Value),
                FixedNowUtc);

            result.Outcome.Should().Be(LicenceActivationOutcome.EntitlementNotInitialized);
            result.Entitlement.Should().BeNull();

            await using var db = _fixture.CreateContext(databaseName);
            var subjectRow = await db.Set<LicenceSubjectEntity>()
                .SingleAsync(s => s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress);
            subjectRow.EntitlementRevision.Should().Be(0);
            (await db.Set<LicenceAssignmentEntity>().CountAsync(a =>
                a.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(0);
            (await db.Set<LicenceTransitionEventEntity>().CountAsync(e =>
                e.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(0);
            var operation = await db.Set<LicenceActivationOperationEntity>().SingleAsync(o =>
                o.LicenceSubjectId == subjectRow.LicenceSubjectId);
            operation.DurableResult.Should().Be(LicencePersistenceVocabulary.OperationResultEntitlementNotInitialized);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    // ------------------------------------------------------------------ expiry versus activation

    [Fact]
    public async Task Expiry_is_normalized_before_a_racing_activation_leaving_one_effective_assignment()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await NewSubjectAsync(_fixture, databaseName);
            var effectiveFrom = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            var expiresAt = effectiveFrom.AddYears(1); // == FixedNowUtc
            await SeedActiveAnnualVeritasAsync(_fixture, databaseName, identity, effectiveFrom, expiresAt);

            var activation = Task.Run(() => ActivateAsync(
                _fixture, databaseName, identity,
                Command(HushVotingLicencePlanId.Veritas500.Value, 1, HushVotingLicencePlanId.Veritas2000.Value),
                FixedNowUtc));
            var resolution = Task.Run(() => ResolveAsync(_fixture, databaseName, identity, FixedNowUtc));

            await Task.WhenAll(activation, resolution);

            // Expiry always wins and the racing activation conflicts deterministically regardless of
            // lock order: whichever operation normalized the expiry first, the activation that carries
            // the old plan/revision records precondition_conflict and exactly one effective assignment
            // remains.
            activation.Result.Outcome.Should().Be(LicenceActivationOutcome.PreconditionConflict);
            resolution.Result.Outcome.Should().BeOneOf(
                LicenceResolutionOutcome.ExpiredToDefault,
                LicenceResolutionOutcome.ResolvedExisting);

            await using var db = _fixture.CreateContext(databaseName);
            var subjectRow = await db.Set<LicenceSubjectEntity>()
                .SingleAsync(s => s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress);
            subjectRow.EntitlementRevision.Should().Be(2);

            var active = await db.Set<LicenceAssignmentEntity>().SingleAsync(a =>
                a.LicenceSubjectId == subjectRow.LicenceSubjectId
                && a.LifecycleStatus == LicencePersistenceVocabulary.LifecycleActive);
            active.PlanId.Should().Be(HushVotingLicencePlanId.DirectFree.Value);
            active.Source.Should().Be(LicencePersistenceVocabulary.SourceAutomaticExpiry);

            (await db.Set<LicenceAssignmentEntity>().CountAsync(a =>
                a.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(2);

            var operation = await db.Set<LicenceActivationOperationEntity>().SingleAsync(o =>
                o.LicenceSubjectId == subjectRow.LicenceSubjectId);
            operation.DurableResult.Should().Be(LicencePersistenceVocabulary.OperationResultPreconditionConflict);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }
}
