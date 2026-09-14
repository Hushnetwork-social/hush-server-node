using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.HushVoting.Licensing.Model;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-014 Phase 6 Task 6.4 real-PostgreSQL transaction/failure TwinTests for the FEAT-013
/// outbox row contribution. Every cache-relevant state-changing transaction creates exactly one
/// outbox row inside the same transaction at the committed revision; non-mutating operations create
/// none; rollback leaves nothing; an ambiguous commit reconciles once without duplication; and the
/// best-effort immediate publisher runs only after commit (never before) without changing results.
/// </summary>
[Collection("FEAT-014 Licensing PostgreSQL")]
[Trait("Category", "FEAT-014")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicenceCacheTransactionTwinTests
{
    private const string V1 = "hushvoting-licence-catalogue/v1.0.0";
    private const string V1Schema = "hushvoting-licence-catalogue/v1";
    private static readonly string DigestA = new('A', 64);
    private static readonly DateTime FixedNowUtc = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly LicensingPostgresFixture _fixture;

    public HushVotingLicenceCacheTransactionTwinTests(LicensingPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class FixedTimeProvider(DateTime fixedUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(DateTime.SpecifyKind(fixedUtc, DateTimeKind.Utc));
    }

    private static LicenceReleaseInstallSpec Spec() =>
        new(V1, DigestA, V1Schema, "hush-server-node-test", "feat014-cache-txn-twin");

    private static LicenceServiceConfiguration Configuration() =>
        LicenceServiceConfiguration.CreateDefault(DigestA);

    private async Task<string> NewDatabaseAsync(long watermark = 10_000)
    {
        var databaseName = $"feat014_txn_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(databaseName);
        await _fixture.MigrateToHeadAsync(databaseName);

        await using var context = _fixture.CreateContext(databaseName);
        var state = await LicenceCatalogueLedgerCoordinator.ReconcileAsync(
            context, Spec(), _ => Task.FromResult(watermark), CancellationToken.None);
        state.Ready.Should().BeTrue();

        return databaseName;
    }

    private static AuthenticatedIdentitySubject NewIdentity(long creationBlock = 5_000, string? address = null)
    {
        var canonical = address ?? $"identity-{Guid.NewGuid():N}";
        var created = AuthenticatedIdentitySubject.TryCreate(
            LicencePersistenceVocabulary.SubjectTypeIdentity, canonical, creationBlock, out var subject, out var error);
        created.Should().BeTrue(error);
        return subject!;
    }

    private static async Task<LicenceResolutionResult> ResolveAsync(
        LicensingPostgresFixture fixture,
        string databaseName,
        AuthenticatedIdentitySubject subject,
        DateTime nowUtc,
        LicenceCacheOutboxPolicy cacheOutbox,
        LicenceFailureInjection? injection = null) =>
        await LicenceEntitlementCoordinator.ResolveOrProvisionAsync(
            () => fixture.CreateContext(databaseName),
            Configuration(),
            subject,
            new FixedTimeProvider(nowUtc),
            telemetry: null,
            CancellationToken.None,
            injection,
            cacheOutbox);

    private static async Task<LicenceActivationResult> ActivateAsync(
        LicensingPostgresFixture fixture,
        string databaseName,
        AuthenticatedIdentitySubject subject,
        LicenceActivationCommand command,
        DateTime nowUtc,
        LicenceCacheOutboxPolicy cacheOutbox,
        LicenceFailureInjection? injection = null) =>
        await LicenceEntitlementCoordinator.ActivateHigherPlanAsync(
            () => fixture.CreateContext(databaseName),
            Configuration(),
            subject,
            command,
            new FixedTimeProvider(nowUtc),
            telemetry: null,
            CancellationToken.None,
            injection,
            cacheOutbox);

    private async Task<List<LicenceCacheOutboxEntity>> OutboxRowsAsync(
        string databaseName,
        Guid? licenceSubjectId = null)
    {
        await using var db = _fixture.CreateContext(databaseName);
        var query = db.Set<LicenceCacheOutboxEntity>().AsQueryable();
        if (licenceSubjectId is not null)
        {
            query = query.Where(r => r.LicenceSubjectId == licenceSubjectId);
        }

        return await query.OrderBy(r => r.CommittedRevision).ToListAsync();
    }

    private async Task<Guid> SubjectIdForAsync(string databaseName, AuthenticatedIdentitySubject subject)
    {
        await using var db = _fixture.CreateContext(databaseName);
        var row = await db.Set<LicenceSubjectEntity>()
            .SingleAsync(s => s.CanonicalPublicSigningAddress == subject.CanonicalPublicSigningAddress);
        return row.LicenceSubjectId;
    }

    private static LicenceCacheOutboxPolicy EnabledPolicy(Action? onPublish = null) =>
        new(true, (_, _, _) =>
        {
            onPublish?.Invoke();
            return Task.CompletedTask;
        });

    private static LicenceCacheOutboxPolicy DisabledPolicy() => LicenceCacheOutboxPolicy.Disabled;

    // ------------------------------------------------------------------ provisioning

    [Fact]
    public async Task Default_provisioning_creates_one_outbox_row_at_revision_one()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = NewIdentity(creationBlock: 20_000);
            var published = 0;
            var result = await ResolveAsync(
                _fixture, databaseName, identity, FixedNowUtc, EnabledPolicy(() => published++));

            result.IsSuccess.Should().BeTrue();
            result.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedDefault);
            result.Entitlement!.EntitlementRevision.Should().Be(1);

            var subjectId = await SubjectIdForAsync(databaseName, identity);
            var rows = await OutboxRowsAsync(databaseName, subjectId);
            rows.Should().ContainSingle();
            rows[0].CommittedRevision.Should().Be(1);
            rows[0].ChangeKind.Should().Be(LicenceCacheOutboxChangeKinds.ProvisionedDefault);
            rows[0].DeliveredUtc.Should().BeNull();
            published.Should().Be(1);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Migration_provisioning_creates_one_outbox_row_at_revision_one()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = NewIdentity();
            var result = await ResolveAsync(
                _fixture, databaseName, identity, FixedNowUtc, EnabledPolicy());

            result.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedMigrationDefault);

            var subjectId = await SubjectIdForAsync(databaseName, identity);
            var rows = await OutboxRowsAsync(databaseName, subjectId);
            rows.Should().ContainSingle();
            rows[0].CommittedRevision.Should().Be(1);
            rows[0].ChangeKind.Should().Be(LicenceCacheOutboxChangeKinds.ProvisionedMigrationDefault);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Resolved_existing_creates_no_outbox_row()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = NewIdentity();
            var first = await ResolveAsync(_fixture, databaseName, identity, FixedNowUtc, EnabledPolicy());
            first.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedMigrationDefault);

            var second = await ResolveAsync(_fixture, databaseName, identity, FixedNowUtc, EnabledPolicy());
            second.Outcome.Should().Be(LicenceResolutionOutcome.ResolvedExisting);

            var subjectId = await SubjectIdForAsync(databaseName, identity);
            var rows = await OutboxRowsAsync(databaseName, subjectId);
            rows.Should().ContainSingle(); // only the provisioning row
            rows[0].CommittedRevision.Should().Be(1);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Disabled_cache_outbox_creates_no_rows_and_feat013_is_unchanged()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = NewIdentity();
            var result = await ResolveAsync(_fixture, databaseName, identity, FixedNowUtc, DisabledPolicy());
            result.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedMigrationDefault);

            var rows = await OutboxRowsAsync(databaseName);
            rows.Should().BeEmpty();
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    // ------------------------------------------------------------------ annual expiry to default

    [Fact]
    public async Task Annual_expiry_to_default_creates_one_outbox_row_at_next_revision()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = NewIdentity();
            // Provision Direct Free (rev 1), then activate a one-year Veritas (rev 2) so the next
            // authoritative resolution crosses the upper-exclusive annual expiry boundary.
            var provisioned = await ResolveAsync(_fixture, databaseName, identity, FixedNowUtc, EnabledPolicy());
            provisioned.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedMigrationDefault);

            var command = LicenceActivationCommand.TryCreate(
                Guid.NewGuid(),
                HushVotingLicencePlanId.DirectFree.Value,
                1,
                HushVotingLicencePlanId.Veritas500.Value,
                "txn-annual-expiry",
                out var cmd,
                out var error);
            command.Should().BeTrue(error);

            var activation = await ActivateAsync(
                _fixture, databaseName, identity, cmd!, FixedNowUtc, EnabledPolicy());
            activation.Outcome.Should().Be(LicenceActivationOutcome.Activated);
            activation.Entitlement!.EntitlementRevision.Should().Be(2);

            // Resolve at the upper-exclusive expiry instant: annual expires to Direct Free (rev 3).
            var expiryResult = await ResolveAsync(
                _fixture, databaseName, identity, FixedNowUtc.AddYears(1), EnabledPolicy());
            expiryResult.Outcome.Should().Be(LicenceResolutionOutcome.ExpiredToDefault);
            expiryResult.Entitlement!.EntitlementRevision.Should().Be(3);

            var subjectId = await SubjectIdForAsync(databaseName, identity);
            var rows = await OutboxRowsAsync(databaseName, subjectId);
            rows.Should().HaveCount(3); // provision (1) + activation (2) + expiry (3)
            rows[2].CommittedRevision.Should().Be(3);
            rows[2].ChangeKind.Should().Be(LicenceCacheOutboxChangeKinds.ExpiredToDefault);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    // ------------------------------------------------------------------ activation

    [Fact]
    public async Task Higher_plan_activation_creates_one_outbox_row_at_next_revision()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = NewIdentity();
            var resolved = await ResolveAsync(_fixture, databaseName, identity, FixedNowUtc, EnabledPolicy());
            resolved.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedMigrationDefault);

            var command = LicenceActivationCommand.TryCreate(
                Guid.NewGuid(),
                HushVotingLicencePlanId.DirectFree.Value,
                1,
                HushVotingLicencePlanId.Veritas500.Value,
                "txn-activate",
                out var cmd,
                out var error);
            command.Should().BeTrue(error);

            var activated = await ActivateAsync(
                _fixture, databaseName, identity, cmd!, FixedNowUtc, EnabledPolicy());
            activated.Outcome.Should().Be(LicenceActivationOutcome.Activated);
            activated.Entitlement!.EntitlementRevision.Should().Be(2);

            var subjectId = await SubjectIdForAsync(databaseName, identity);
            var rows = await OutboxRowsAsync(databaseName, subjectId);
            rows.Should().HaveCount(2); // provision (rev 1) + activation (rev 2)
            rows[1].CommittedRevision.Should().Be(2);
            rows[1].ChangeKind.Should().Be(LicenceCacheOutboxChangeKinds.ActivatedHigherPlan);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Rejected_activation_creates_no_additional_outbox_row()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = NewIdentity();
            var resolved = await ResolveAsync(_fixture, databaseName, identity, FixedNowUtc, EnabledPolicy());
            resolved.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedMigrationDefault);

            // Downgrade target is rejected durably; no activation row may appear.
            var command = LicenceActivationCommand.TryCreate(
                Guid.NewGuid(),
                HushVotingLicencePlanId.DirectFree.Value,
                1,
                HushVotingLicencePlanId.Enterprise.Value,
                "txn-reject",
                out var cmd,
                out var error);
            command.Should().BeTrue(error);

            var rejected = await ActivateAsync(
                _fixture, databaseName, identity, cmd!, FixedNowUtc, EnabledPolicy());
            rejected.IsSuccess.Should().BeTrue();
            rejected.Outcome.Should().Be(LicenceActivationOutcome.PlanUnavailable);

            var subjectId = await SubjectIdForAsync(databaseName, identity);
            var rows = await OutboxRowsAsync(databaseName, subjectId);
            rows.Should().ContainSingle(); // only the provisioning row
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    // ------------------------------------------------------------------ rollback / ambiguous commit

    [Fact]
    public async Task Rollback_leaves_no_outbox_row_and_no_publication()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = NewIdentity();
            var published = 0;
            // Fail before the transaction on every attempt: the bounded executor exhausts its retries
            // and returns concurrency_exhausted; nothing is staged or committed, so no outbox row or
            // immediate publication may exist.
            var injection = new LicenceFailureInjection
            {
                BeforeAttemptAsync = (_, _) => throw new LicenceTransientConflictException(
                    "simulated persistent deadlock", new InvalidOperationException("inner")),
            };

            var result = await ResolveAsync(
                _fixture, databaseName, identity, FixedNowUtc, EnabledPolicy(() => published++), injection);

            result.IsSuccess.Should().BeFalse();
            result.StableErrorCode.Should().Be(LicenceEntitlementFailureCodes.ConcurrencyExhausted);

            var rows = await OutboxRowsAsync(databaseName);
            rows.Should().BeEmpty();
            published.Should().Be(0);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Ambiguous_commit_reconciles_once_with_single_outbox_row()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = NewIdentity();
            // Provision first so the subsequent activation commit is the ambiguous one.
            var resolved = await ResolveAsync(_fixture, databaseName, identity, FixedNowUtc, EnabledPolicy());
            resolved.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedMigrationDefault);

            var command = LicenceActivationCommand.TryCreate(
                Guid.NewGuid(),
                HushVotingLicencePlanId.DirectFree.Value,
                1,
                HushVotingLicencePlanId.Veritas500.Value,
                "txn-ambiguous",
                out var cmd,
                out var error);
            command.Should().BeTrue(error);

            // First attempt loses its success response after commit (ambiguous). The reconcile path
            // must discover the committed activation and return it without adding a second outbox row.
            var activated = await ActivateAsync(
                _fixture,
                databaseName,
                identity,
                cmd!,
                FixedNowUtc,
                EnabledPolicy(),
                new LicenceFailureInjection
                {
                    AfterCommitAsync = (_, _) => throw new Npgsql.PostgresException(
                        "connection lost after commit", "08006", "admin_shutdown", "ambiguous"),
                });

            activated.IsSuccess.Should().BeTrue();
            activated.Outcome.Should().Be(LicenceActivationOutcome.Activated);

            var subjectId = await SubjectIdForAsync(databaseName, identity);
            var rows = await OutboxRowsAsync(databaseName, subjectId);
            rows.Should().HaveCount(2); // provisioning rev1 + activation rev2, exactly once each
            rows.Should().OnlyContain(r => r.DeliveredUtc == null);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }
}
