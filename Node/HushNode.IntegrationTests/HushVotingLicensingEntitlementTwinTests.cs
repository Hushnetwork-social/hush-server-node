using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.HushVoting.Licensing.Model;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-013 Task 3.4 real-PostgreSQL TwinTests for authoritative GetOrProvision and expiry
/// normalization: new/legacy lazy provenance, no-mutation resolution, upper-exclusive expiry
/// (expired_to_default), idempotent retry after a committed response loss, retired-plan pinned
/// terms, 100-way same- and distinct-subject concurrency, and storage-unavailable failure.
/// </summary>
[Collection("FEAT-013 Licensing PostgreSQL")]
[Trait("Category", "FEAT-013")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicensingEntitlementTwinTests
{
    private const string V1 = "hushvoting-licence-catalogue/v1.0.0";
    private const string V1Schema = "hushvoting-licence-catalogue/v1";
    private static readonly string DigestA = new('A', 64);

    // A fixed "now" far in the future of the 2020 backdate floor and after the test rollout.
    private static readonly DateTime FixedNowUtc = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly LicensingPostgresFixture _fixture;

    public HushVotingLicensingEntitlementTwinTests(LicensingPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // ------------------------------------------------------------------ helpers

    private sealed class FixedTimeProvider(DateTime fixedUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(DateTime.SpecifyKind(fixedUtc, DateTimeKind.Utc));
    }

    private static LicenceReleaseInstallSpec Spec() => new(V1, DigestA, V1Schema, "hush-server-node-test", "feat013-entitlement-twin");

    private static LicenceServiceConfiguration Configuration(
        HushVotingLicenceCatalogue? catalogue = null) =>
        LicenceServiceConfiguration.CreateDefault(DigestA, catalogue);

    private async Task<string> NewDatabaseAsync(long watermark = 10_000)
    {
        var databaseName = $"feat013_ent_{Guid.NewGuid():N}";
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

    private async Task<AuthenticatedIdentitySubject> NewIdentityAsync(
        string databaseName,
        long creationBlock,
        string? address = null)
    {
        var canonical = address ?? $"identity-{Guid.NewGuid():N}";
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
        LicenceServiceConfiguration configuration,
        AuthenticatedIdentitySubject subject,
        DateTime nowUtc,
        LicenceFailureInjection? injection = null) =>
        await LicenceEntitlementCoordinator.ResolveOrProvisionAsync(
            () => fixture.CreateContext(databaseName),
            configuration,
            subject,
            new FixedTimeProvider(nowUtc),
            telemetry: null,
            CancellationToken.None,
            injection);

    private static async Task SeedActiveAnnualVeritasAsync(
        LicensingPostgresFixture fixture,
        string databaseName,
        AuthenticatedIdentitySubject subject,
        DateTime effectiveFromUtc,
        DateTime expiresAtUtc,
        DateTime? identityCreatedAtUtc = null)
    {
        await using var db = fixture.CreateContext(databaseName);

        var subjectRow = new LicenceSubjectEntity
        {
            LicenceSubjectId = Guid.CreateVersion7(),
            SubjectType = subject.SubjectType,
            CanonicalPublicSigningAddress = subject.CanonicalPublicSigningAddress,
            IdentityCreationBlockIndex = subject.IdentityCreationBlockIndex,
            CreatedAtUtc = identityCreatedAtUtc ?? DateTime.UtcNow,
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

    private async Task AssertSingleDirectFreeStateAsync(
        string databaseName,
        AuthenticatedIdentitySubject identity,
        string expectedSource,
        long expectedRevision,
        Guid? expectedAssignmentId = null,
        DateTime? expectedEffectiveFromUtc = null)
    {
        await using var db = _fixture.CreateContext(databaseName);

        var subjectRow = await db.Set<LicenceSubjectEntity>()
            .SingleAsync(s => s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress);

        subjectRow.EntitlementRevision.Should().Be(expectedRevision);
        subjectRow.SubjectType.Should().Be(LicencePersistenceVocabulary.SubjectTypeIdentity);

        (await db.Set<LicenceAssignmentEntity>().CountAsync(a =>
            a.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(1);

        var active = await db.Set<LicenceAssignmentEntity>().SingleAsync(a =>
            a.LicenceSubjectId == subjectRow.LicenceSubjectId
            && a.LifecycleStatus == LicencePersistenceVocabulary.LifecycleActive);

        active.PlanId.Should().Be(HushVotingLicencePlanId.DirectFree.Value);
        active.Source.Should().Be(expectedSource);
        active.TermKind.Should().Be(LicencePersistenceVocabulary.TermPerpetual);
        active.ExpiresAtUtc.Should().BeNull();
        if (expectedAssignmentId is not null)
        {
            active.LicenceAssignmentId.Should().Be(expectedAssignmentId.Value);
        }

        if (expectedEffectiveFromUtc is not null)
        {
            active.EffectiveFromUtc.Should().Be(expectedEffectiveFromUtc);
        }

        var events = await db.Set<LicenceTransitionEventEntity>()
            .Where(e => e.LicenceSubjectId == subjectRow.LicenceSubjectId)
            .OrderBy(e => e.EventSequence)
            .ToListAsync();

        events.Should().ContainSingle();
        events[0].EventType.Should().Be(LicencePersistenceVocabulary.EventTypeCreated);
        events[0].SubjectRevision.Should().Be(expectedRevision);
        events[0].SourceOrReason.Should().Be(expectedSource);
    }

    // ------------------------------------------------------------------ provisioning & provenance

    [Fact]
    public async Task New_identity_after_rollout_provisions_direct_free_exactly_once()
    {
        var databaseName = await NewDatabaseAsync(watermark: 10_000);
        try
        {
            var identity = await NewIdentityAsync(databaseName, creationBlock: 20_000);
            var result = await ResolveAsync(_fixture, databaseName, Configuration(), identity, FixedNowUtc);

            result.IsSuccess.Should().BeTrue();
            result.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedDefault);
            result.Entitlement!.PlanId.Should().Be(HushVotingLicencePlanId.DirectFree.Value);
            result.Entitlement.EntitlementRevision.Should().Be(1);
            result.Entitlement.EffectiveFromUtc.Should().Be(FixedNowUtc);
            result.Entitlement.ExpiresAtUtc.Should().BeNull();

            await AssertSingleDirectFreeStateAsync(
                databaseName, identity, LicencePersistenceVocabulary.SourceDefaultFree, expectedRevision: 1,
                expectedEffectiveFromUtc: FixedNowUtc);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Legacy_identity_at_or_before_rollout_is_provisioned_migration_lazy_without_backfill()
    {
        var databaseName = await NewDatabaseAsync(watermark: 10_000);
        try
        {
            var legacy = await NewIdentityAsync(databaseName, creationBlock: 10_000, address: "legacy-identity-1");
            var boundary = await NewIdentityAsync(databaseName, creationBlock: 9_999, address: "legacy-identity-2");
            var untouched = await NewIdentityAsync(databaseName, creationBlock: 5_000, address: "legacy-untouched");

            var legacyResult = await ResolveAsync(_fixture, databaseName, Configuration(), legacy, FixedNowUtc);
            legacyResult.IsSuccess.Should().BeTrue();
            legacyResult.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedMigrationDefault);
            await AssertSingleDirectFreeStateAsync(
                databaseName, legacy, LicencePersistenceVocabulary.SourceMigrationLazyDefault, expectedRevision: 1);

            var boundaryResult = await ResolveAsync(_fixture, databaseName, Configuration(), boundary, FixedNowUtc);
            boundaryResult.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedMigrationDefault);

            // No eager backfill: the untouched legacy identity still has no subject or assignment.
            await using var db = _fixture.CreateContext(databaseName);
            (await db.Set<LicenceSubjectEntity>().CountAsync(s =>
                s.CanonicalPublicSigningAddress == untouched.CanonicalPublicSigningAddress)).Should().Be(0);
            (await db.Set<LicenceSubjectEntity>().CountAsync()).Should().Be(2);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    // ------------------------------------------------------------------ existing resolution & expiry

    [Fact]
    public async Task Existing_unexpired_assignment_resolves_without_mutation()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await NewIdentityAsync(databaseName, creationBlock: 20_000, address: "existing-assignment");
            var effectiveFrom = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            await SeedActiveAnnualVeritasAsync(
                _fixture, databaseName, identity,
                effectiveFrom, expiresAtUtc: effectiveFrom.AddYears(1));

            var result = await ResolveAsync(_fixture, databaseName, Configuration(), identity, new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));

            result.IsSuccess.Should().BeTrue();
            result.Outcome.Should().Be(LicenceResolutionOutcome.ResolvedExisting);
            result.Entitlement!.PlanId.Should().Be(HushVotingLicencePlanId.Veritas500.Value);
            result.Entitlement.EntitlementRevision.Should().Be(1);

            // No mutation: assignment count, event count and revision are unchanged after a second call.
            var second = await ResolveAsync(_fixture, databaseName, Configuration(), identity, new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            second.Entitlement!.LicenceAssignmentId.Should().Be(result.Entitlement.LicenceAssignmentId);

            await using var db = _fixture.CreateContext(databaseName);
            var subjectRow = await db.Set<LicenceSubjectEntity>()
                .SingleAsync(s => s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress);
            subjectRow.EntitlementRevision.Should().Be(1);
            (await db.Set<LicenceAssignmentEntity>().CountAsync(a => a.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(1);
            (await db.Set<LicenceTransitionEventEntity>().CountAsync(e => e.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(1);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Annual_assignment_at_upper_exclusive_boundary_expires_to_direct_free_atomically()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await NewIdentityAsync(databaseName, creationBlock: 20_000, address: "expiring-annual");
            var effectiveFrom = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            var expiresAt = effectiveFrom.AddYears(1); // 2026-01-15T12:00:00Z
            await SeedActiveAnnualVeritasAsync(_fixture, databaseName, identity, effectiveFrom, expiresAt);

            // now == expiresAt triggers the upper-exclusive expiry.
            var result = await ResolveAsync(_fixture, databaseName, Configuration(), identity, expiresAt);

            result.IsSuccess.Should().BeTrue();
            result.Outcome.Should().Be(LicenceResolutionOutcome.ExpiredToDefault);
            result.Entitlement!.PlanId.Should().Be(HushVotingLicencePlanId.DirectFree.Value);
            result.Entitlement.Source.Should().Be(LicencePersistenceVocabulary.SourceAutomaticExpiry);
            result.Entitlement.EntitlementRevision.Should().Be(2);

            await using var db = _fixture.CreateContext(databaseName);
            var subjectRow = await db.Set<LicenceSubjectEntity>()
                .SingleAsync(s => s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress);
            subjectRow.EntitlementRevision.Should().Be(2);

            var assignments = await db.Set<LicenceAssignmentEntity>()
                .Where(a => a.LicenceSubjectId == subjectRow.LicenceSubjectId)
                .OrderBy(a => a.EffectiveFromUtc)
                .ToListAsync();
            assignments.Should().HaveCount(2);

            var expired = assignments[0];
            expired.LifecycleStatus.Should().Be(LicencePersistenceVocabulary.LifecycleExpired);
            expired.LifecycleChangedAtUtc.Should().Be(expiresAt);
            expired.LifecycleReason.Should().Be(LicenceEntitlementDecisions.ReasonAnnualExpiry);
            expired.ExpiresAtUtc.Should().Be(expiresAt);

            var directFree = assignments[1];
            directFree.LifecycleStatus.Should().Be(LicencePersistenceVocabulary.LifecycleActive);
            directFree.Source.Should().Be(LicencePersistenceVocabulary.SourceAutomaticExpiry);
            directFree.TermKind.Should().Be(LicencePersistenceVocabulary.TermPerpetual);
            directFree.ExpiresAtUtc.Should().BeNull();
            directFree.EffectiveFromUtc.Should().Be(expiresAt);

            // Orderly append-only evidence: created(1) + expired(2) + created(3), all at revision 2.
            var events = await db.Set<LicenceTransitionEventEntity>()
                .Where(e => e.LicenceSubjectId == subjectRow.LicenceSubjectId)
                .OrderBy(e => e.EventSequence)
                .ToListAsync();
            events.Select(e => e.EventSequence).Should().Equal(1, 2, 3);
            events[1].EventType.Should().Be(LicencePersistenceVocabulary.EventTypeExpired);
            events[1].SubjectRevision.Should().Be(2);
            events[2].EventType.Should().Be(LicencePersistenceVocabulary.EventTypeCreated);
            events[2].SubjectRevision.Should().Be(2);

            // A follow-up resolution converges on the effective Direct Free with no extra mutation.
            var followUp = await ResolveAsync(_fixture, databaseName, Configuration(), identity, expiresAt);
            followUp.Outcome.Should().Be(LicenceResolutionOutcome.ResolvedExisting);
            followUp.Entitlement!.LicenceAssignmentId.Should().Be(directFree.LicenceAssignmentId);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Annual_assignment_before_the_boundary_is_not_expired()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await NewIdentityAsync(databaseName, creationBlock: 20_000, address: "not-yet-expiring");
            var effectiveFrom = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            var expiresAt = effectiveFrom.AddYears(1);
            await SeedActiveAnnualVeritasAsync(_fixture, databaseName, identity, effectiveFrom, expiresAt);

            var result = await ResolveAsync(
                _fixture, databaseName, Configuration(), identity, expiresAt.AddSeconds(-1));

            result.Outcome.Should().Be(LicenceResolutionOutcome.ResolvedExisting);
            result.Entitlement!.PlanId.Should().Be(HushVotingLicencePlanId.Veritas500.Value);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    // ------------------------------------------------------------------ retry / idempotent convergence

    [Fact]
    public async Task Retry_after_a_committed_provision_returns_the_same_assignment_without_duplicates()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await NewIdentityAsync(databaseName, creationBlock: 20_000, address: "retry-committed");
            var first = await ResolveAsync(_fixture, databaseName, Configuration(), identity, FixedNowUtc);
            first.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedDefault);

            var retry = await ResolveAsync(_fixture, databaseName, Configuration(), identity, FixedNowUtc);
            retry.IsSuccess.Should().BeTrue();
            retry.Outcome.Should().Be(LicenceResolutionOutcome.ResolvedExisting);
            retry.Entitlement!.LicenceAssignmentId.Should().Be(first.Entitlement!.LicenceAssignmentId);

            await AssertSingleDirectFreeStateAsync(
                databaseName, identity, LicencePersistenceVocabulary.SourceDefaultFree, expectedRevision: 1,
                expectedAssignmentId: first.Entitlement.LicenceAssignmentId);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Ambiguous_commit_after_success_discloses_the_committed_assignment_without_duplicates()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await NewIdentityAsync(databaseName, creationBlock: 20_000, address: "ambiguous-committed");

            // AfterCommit throws: the commit landed but the response is lost. Reconciliation must
            // discover the committed Direct Free and never create a duplicate.
            var injection = new LicenceFailureInjection
            {
                AfterCommitAsync = (attempt, _) => attempt == 1
                    ? throw new InvalidOperationException("simulated lost response after commit")
                    : Task.CompletedTask,
            };

            var result = await ResolveAsync(_fixture, databaseName, Configuration(), identity, FixedNowUtc, injection);

            result.IsSuccess.Should().BeTrue();
            result.Outcome.Should().Be(LicenceResolutionOutcome.ResolvedExisting);
            result.Entitlement!.EntitlementRevision.Should().Be(1);

            await AssertSingleDirectFreeStateAsync(
                databaseName, identity, LicencePersistenceVocabulary.SourceDefaultFree, expectedRevision: 1,
                expectedAssignmentId: result.Entitlement.LicenceAssignmentId);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Ambiguous_commit_before_commit_redoes_when_absence_is_authoritative()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await NewIdentityAsync(databaseName, creationBlock: 20_000, address: "ambiguous-absent");

            // BeforeCommit throws: the transaction never commits; reconciliation proves absence and
            // the bounded executor re-executes the attempt to completion.
            var injection = new LicenceFailureInjection
            {
                BeforeCommitAsync = (attempt, _) => attempt == 1
                    ? throw new InvalidOperationException("simulated commit boundary failure")
                    : Task.CompletedTask,
            };

            var result = await ResolveAsync(_fixture, databaseName, Configuration(), identity, FixedNowUtc, injection);

            result.IsSuccess.Should().BeTrue();
            result.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedDefault);
            result.Entitlement!.EntitlementRevision.Should().Be(1);

            await AssertSingleDirectFreeStateAsync(
                databaseName, identity, LicencePersistenceVocabulary.SourceDefaultFree, expectedRevision: 1);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Database_outage_returns_storage_unavailable_and_never_invents_entitlement()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var subject = await NewIdentityAsync(databaseName, creationBlock: 20_000);

            // A context that cannot reach PostgreSQL: connection refused on a closed port.
            static DbContext UnreachableContext()
            {
                var builder = new NpgsqlConnectionStringBuilder
                {
                    Host = "127.0.0.1",
                    Port = 1,
                    Database = "unreachable",
                    Username = "x",
                    Password = "x",
                    Timeout = 1,
                };
                var options = new DbContextOptionsBuilder<HushNodeDbContext>()
                    .UseNpgsql(builder.ConnectionString)
                    .Options;
                return new HushNodeDbContext(Array.Empty<HushNode.Interfaces.IDbContextConfigurator>(), options);
            }

            var result = await LicenceEntitlementCoordinator.ResolveOrProvisionAsync(
                UnreachableContext,
                Configuration(),
                subject,
                new FixedTimeProvider(FixedNowUtc),
                telemetry: null,
                CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.StableErrorCode.Should().Be(LicenceEntitlementFailureCodes.StorageUnavailable);
            result.Entitlement.Should().BeNull();
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    // ------------------------------------------------------------------ retired pinned terms

    [Fact]
    public async Task Retired_plan_retains_pinned_operative_terms_until_expiry()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await NewIdentityAsync(databaseName, creationBlock: 20_000, address: "retired-plan");
            var effectiveFrom = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            await SeedActiveAnnualVeritasAsync(
                _fixture, databaseName, identity, effectiveFrom, effectiveFrom.AddYears(1));

            // The running server's catalogue no longer contains Veritas 500 (retired/removed).
            var catalogueWithoutVeritas500 = new HushVotingLicenceCatalogue(
                HushVotingLicenceCatalogueVersion.V1,
                HushVotingLicenceCatalogueV1.CreatePlans()
                    .Where(p => p.Id != HushVotingLicencePlanId.Veritas500)
                    .ToArray(),
                HushVotingProfileCompatibilityV1.Entries);

            var result = await ResolveAsync(
                _fixture, databaseName, Configuration(catalogueWithoutVeritas500), identity,
                new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));

            result.IsSuccess.Should().BeTrue();
            result.Outcome.Should().Be(LicenceResolutionOutcome.ResolvedExisting);
            result.Entitlement!.PlanId.Should().Be(HushVotingLicencePlanId.Veritas500.Value);
            result.Entitlement.EligibleVoterCap.Should().Be(500);
            result.Entitlement.UpgradeRank.Should().Be(1000);
            result.Entitlement.AllowedGovernanceOptionIds.Should().BeEquivalentTo(
            [
                HushVotingGovernanceOptionId.NoCustomerTrustees.Value,
                HushVotingGovernanceOptionId.Trustees3Of5.Value,
            ]);
            result.Entitlement.ExpiresAtUtc.Should().NotBeNull();
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    // ------------------------------------------------------------------ concurrency

    [Fact]
    public async Task Concurrent_first_resolution_100_way_yields_one_subject_one_assignment_one_event()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await NewIdentityAsync(databaseName, creationBlock: 20_000, address: "concurrent-same-identity");

            var attempts = Enumerable.Range(0, 100)
                .Select(_ => Task.Run(() => ResolveAsync(
                    _fixture, databaseName, Configuration(), identity, FixedNowUtc)))
                .ToArray();

            var results = await Task.WhenAll(attempts);

            results.Should().OnlyContain(r => r.IsSuccess);
            var assignmentIds = results.Select(r => r.Entitlement!.LicenceAssignmentId).Distinct().ToArray();
            assignmentIds.Should().ContainSingle();
            results.Select(r => r.Entitlement!.EntitlementRevision).Distinct().Should().BeEquivalentTo([1L]);

            await using var db = _fixture.CreateContext(databaseName);
            var subjectRow = await db.Set<LicenceSubjectEntity>().SingleAsync(s =>
                s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress);
            (await db.Set<LicenceAssignmentEntity>().CountAsync(a =>
                a.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(1);
            (await db.Set<LicenceTransitionEventEntity>().CountAsync(e =>
                e.LicenceSubjectId == subjectRow.LicenceSubjectId)).Should().Be(1);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Concurrent_first_resolution_100_distinct_identities_complete_without_cross_blocking()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identities = new List<AuthenticatedIdentitySubject>(100);
            for (var i = 0; i < 100; i++)
            {
                var identity = await NewIdentityAsync(databaseName, creationBlock: 20_000 + i, address: $"distinct-{i:D3}");
                identities.Add(identity);
            }

            var attempts = identities
                .Select(identity => Task.Run(() => ResolveAsync(
                    _fixture, databaseName, Configuration(), identity, FixedNowUtc)))
                .ToArray();

            var results = await Task.WhenAll(attempts);
            results.Should().OnlyContain(r => r.IsSuccess);
            results.Should().OnlyContain(r => r.Outcome == LicenceResolutionOutcome.ProvisionedDefault);

            await using var db = _fixture.CreateContext(databaseName);
            (await db.Set<LicenceSubjectEntity>().CountAsync()).Should().Be(100);
            (await db.Set<LicenceAssignmentEntity>().CountAsync(a =>
                a.LifecycleStatus == LicencePersistenceVocabulary.LifecycleActive)).Should().Be(100);
            (await db.Set<LicenceTransitionEventEntity>().CountAsync()).Should().Be(100);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }
}
