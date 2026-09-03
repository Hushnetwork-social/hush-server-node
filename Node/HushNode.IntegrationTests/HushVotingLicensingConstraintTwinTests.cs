using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-013 real-PostgreSQL constraint TwinTests (Task 2.4). Database CHECKs, partial
/// unique indexes, per-subject uniqueness, and restrict FKs are the final guard, proven
/// here against an isolated postgres:16 container.
/// </summary>
[Collection("FEAT-013 Licensing PostgreSQL")]
[Trait("Category", "FEAT-013")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicensingConstraintTwinTests : IAsyncLifetime
{
    private const string ConstraintViolation = "23514";
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";

    private readonly LicensingPostgresFixture _fixture;
    private readonly string _databaseName;
    private long _addressCounter;

    public HushVotingLicensingConstraintTwinTests(LicensingPostgresFixture fixture)
    {
        _fixture = fixture;
        _databaseName = $"feat013_con_{Guid.NewGuid():N}";
    }

    public async Task InitializeAsync()
    {
        await _fixture.CreateDatabaseAsync(_databaseName);
        await _fixture.MigrateToHeadAsync(_databaseName);
    }

    public async Task DisposeAsync()
    {
        await _fixture.DropDatabaseAsync(_databaseName);
    }

    private string NextAddress() => $"feat013-constraint-{Interlocked.Increment(ref _addressCounter):D4}";

    [Fact]
    public async Task Duplicate_subject_for_same_canonical_identity_is_rejected()
    {
        await using var context = _fixture.CreateContext(_databaseName);
        var address = NextAddress();
        context.Set<LicenceSubjectEntity>().Add(NewSubject(address));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.Set<LicenceSubjectEntity>().Add(NewSubject(address));
        var act = async () => await context.SaveChangesAsync();
        (await CapturePostgresExceptionAsync(act)).SqlState.Should().Be(UniqueViolation);
    }

    [Fact]
    public async Task Unknown_lifecycle_status_is_rejected_by_check()
    {
        var assignment = NewActiveAssignment(NextAddress());
        assignment.LifecycleStatus = "unknown";
        await ExpectCheckViolationAsync(assignment);
    }

    [Fact]
    public async Task Unknown_source_is_rejected_by_check()
    {
        var assignment = NewActiveAssignment(NextAddress());
        assignment.Source = "customer_choice";
        await ExpectCheckViolationAsync(assignment);
    }

    [Fact]
    public async Task Annual_assignment_without_expiry_is_rejected()
    {
        var assignment = NewActiveAssignment(NextAddress());
        assignment.TermKind = LicencePersistenceVocabulary.TermAnnual;
        assignment.TermYears = 1;
        assignment.ExpiresAtUtc = null;
        await ExpectCheckViolationAsync(assignment);
    }

    [Fact]
    public async Task Perpetual_assignment_with_expiry_is_rejected()
    {
        var assignment = NewActiveAssignment(NextAddress());
        assignment.ExpiresAtUtc = DateTime.UtcNow.AddYears(1);
        await ExpectCheckViolationAsync(assignment);
    }

    [Fact]
    public async Task Only_one_active_assignment_may_exist_per_subject()
    {
        await using var context = _fixture.CreateContext(_databaseName);
        var subject = NewSubject(NextAddress());
        context.Set<LicenceSubjectEntity>().Add(subject);
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        context.Set<LicenceAssignmentEntity>().Add(NewActiveAssignmentFor(subject.LicenceSubjectId, "hushvoting.direct.free"));
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        context.Set<LicenceAssignmentEntity>().Add(NewActiveAssignmentFor(subject.LicenceSubjectId, "hushvoting.veritas.500"));

        var act = async () => await context.SaveChangesAsync();
        (await CapturePostgresExceptionAsync(act)).SqlState.Should().Be(UniqueViolation);
    }

    [Fact]
    public async Task Duplicate_event_sequence_per_subject_is_rejected()
    {
        await using var context = _fixture.CreateContext(_databaseName);
        var subject = NewSubject(NextAddress());
        context.Set<LicenceSubjectEntity>().Add(subject);
        await context.SaveChangesAsync();

        var now = DateTime.UtcNow;
        context.Set<LicenceTransitionEventEntity>().AddRange(
            NewEvent(subject.LicenceSubjectId, sequence: 1, now),
            NewEvent(subject.LicenceSubjectId, sequence: 1, now.AddSeconds(1)));

        var act = async () => await context.SaveChangesAsync();
        (await CapturePostgresExceptionAsync(act)).SqlState.Should().Be(UniqueViolation);
    }

    [Fact]
    public async Task Idempotency_key_is_unique_per_subject_but_reusable_across_subjects()
    {
        await using var context = _fixture.CreateContext(_databaseName);
        var first = NewSubject(NextAddress());
        var second = NewSubject(NextAddress());
        context.Set<LicenceSubjectEntity>().AddRange(first, second);
        await context.SaveChangesAsync();

        var sharedKey = Guid.NewGuid();
        context.Set<LicenceActivationOperationEntity>().AddRange(
            NewOperation(first.LicenceSubjectId, sharedKey),
            NewOperation(second.LicenceSubjectId, sharedKey));
        await context.SaveChangesAsync(); // same caller key on different subjects is allowed

        context.ChangeTracker.Clear();
        context.Set<LicenceActivationOperationEntity>().Add(NewOperation(first.LicenceSubjectId, sharedKey));

        var act = async () => await context.SaveChangesAsync();
        (await CapturePostgresExceptionAsync(act)).SqlState.Should().Be(UniqueViolation);
    }

    [Fact]
    public async Task Deleting_a_subject_with_history_is_refused_and_history_is_retained()
    {
        await using var context = _fixture.CreateContext(_databaseName);
        var subject = NewSubject(NextAddress());
        context.Set<LicenceSubjectEntity>().Add(subject);
        await context.SaveChangesAsync();

        var assignment = NewActiveAssignmentFor(subject.LicenceSubjectId, "hushvoting.direct.free");
        context.Set<LicenceAssignmentEntity>().Add(assignment);
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        var tracked = context.Set<LicenceSubjectEntity>().Single(s => s.LicenceSubjectId == subject.LicenceSubjectId);
        context.Set<LicenceSubjectEntity>().Remove(tracked);

        var act = async () => await context.SaveChangesAsync();
        (await CapturePostgresExceptionAsync(act)).SqlState.Should().Be(ForeignKeyViolation);

        (await context.Set<LicenceSubjectEntity>().CountAsync()).Should().Be(1);
        (await context.Set<LicenceAssignmentEntity>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Release_ledger_allows_only_one_current_release()
    {
        await using var context = _fixture.CreateContext(_databaseName);
        var now = DateTime.UtcNow;
        context.Set<LicenceCatalogueReleaseEntity>().AddRange(
            NewRelease(now, isCurrent: true),
            NewRelease(now.AddSeconds(1), isCurrent: true));

        var act = async () => await context.SaveChangesAsync();
        (await CapturePostgresExceptionAsync(act)).SqlState.Should().Be(UniqueViolation);
    }

    private async Task ExpectCheckViolationAsync(LicenceAssignmentEntity assignment)
    {
        await using var context = _fixture.CreateContext(_databaseName);
        var subject = NewSubject(NextAddress());
        context.Set<LicenceSubjectEntity>().Add(subject);
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        assignment.LicenceSubjectId = subject.LicenceSubjectId;
        context.Set<LicenceAssignmentEntity>().Add(assignment);

        var act = async () => await context.SaveChangesAsync();
        (await CapturePostgresExceptionAsync(act)).SqlState.Should().Be(ConstraintViolation);
    }

    private static async Task<PostgresException> CapturePostgresExceptionAsync(Func<Task> act)
    {
        var exception = await Record.ExceptionAsync(act);
        exception.Should().BeOfType<DbUpdateException>();
        var postgres = exception!.InnerException as PostgresException;
        postgres.Should().NotBeNull();
        return postgres!;
    }

    private static LicenceSubjectEntity NewSubject(string address) => new()
    {
        LicenceSubjectId = Guid.CreateVersion7(),
        SubjectType = LicencePersistenceVocabulary.SubjectTypeIdentity,
        CanonicalPublicSigningAddress = address,
        IdentityCreationBlockIndex = 123,
        CreatedAtUtc = DateTime.UtcNow,
        EntitlementRevision = 0
    };

    private static LicenceAssignmentEntity NewActiveAssignment(string address) =>
        NewActiveAssignmentFor(NewSubject(address).LicenceSubjectId, "hushvoting.direct.free");

    private static LicenceAssignmentEntity NewActiveAssignmentFor(Guid subjectId, string planId) => new()
    {
        LicenceAssignmentId = Guid.CreateVersion7(),
        LicenceSubjectId = subjectId,
        PlanId = planId,
        AssignedCatalogueVersion = "hushvoting-licence-catalogue/v1.0.0",
        AssignedCatalogueDigestSha256 = new string('A', 64),
        LifecycleStatus = LicencePersistenceVocabulary.LifecycleActive,
        Source = planId == "hushvoting.direct.free"
            ? LicencePersistenceVocabulary.SourceDefaultFree
            : LicencePersistenceVocabulary.SourceAutomaticUpgrade,
        EffectiveFromUtc = DateTime.UtcNow,
        ExpiresAtUtc = null,
        PlanFamily = planId == "hushvoting.direct.free"
            ? LicencePersistenceVocabulary.PlanFamilyDirect
            : LicencePersistenceVocabulary.PlanFamilyVeritas,
        UpgradeRank = 0,
        EligibleVoterCap = null,
        UnlimitedElectionPolicy = false,
        TermKind = LicencePersistenceVocabulary.TermPerpetual,
        TermYears = 0,
        AllowedGovernanceOptionIds = Array.Empty<string>()
    };

    private static LicenceTransitionEventEntity NewEvent(Guid subjectId, long sequence, DateTime at) => new()
    {
        LicenceTransitionEventId = Guid.CreateVersion7(),
        LicenceSubjectId = subjectId,
        EventSequence = sequence,
        EventType = LicencePersistenceVocabulary.EventTypeCreated,
        SubjectRevision = 1,
        PlanId = "hushvoting.direct.free",
        CatalogueDecisionVersion = "hushvoting-licence-catalogue/v1.0.0",
        SourceOrReason = LicencePersistenceVocabulary.SourceDefaultFree,
        OccurredAtUtc = at
    };

    private static LicenceActivationOperationEntity NewOperation(Guid subjectId, Guid idempotencyKey) => new()
    {
        LicenceActivationOperationId = Guid.CreateVersion7(),
        LicenceSubjectId = subjectId,
        IdempotencyKey = idempotencyKey,
        CanonicalPayloadFingerprintSha256 = new string('B', 64),
        ExpectedCurrentPlanId = "hushvoting.direct.free",
        ExpectedEntitlementRevision = 1,
        RequestedTargetPlanId = "hushvoting.veritas.500",
        EvaluatedCatalogueVersion = "hushvoting-licence-catalogue/v1.0.0",
        CreatedAtUtc = DateTime.UtcNow
    };

    private static LicenceCatalogueReleaseEntity NewRelease(DateTime at, bool isCurrent) => new()
    {
        LicenceCatalogueReleaseId = Guid.CreateVersion7(),
        CatalogueVersion = "hushvoting-licence-catalogue/v1.0.0",
        ReleaseDigestSha256 = new string('C', 64),
        SchemaVersion = "hushvoting-licence-catalogue/v1",
        InstalledByServerRelease = "hush-server-node-test",
        InstalledByServerHost = "feat013-twin-test",
        InstalledAtUtc = at,
        IsCurrent = isCurrent,
        RolloutWatermarkBlockHeight = null
    };
}
