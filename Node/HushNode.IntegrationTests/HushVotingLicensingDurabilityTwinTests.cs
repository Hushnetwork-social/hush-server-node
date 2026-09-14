using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.HushVoting.Licensing.Model;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-013 Phase 7 real-PostgreSQL durability TwinTests: the composed internal-service journey
/// (resolve -> activate -> calendar expiry -> resolve) with exact authoritative counts, retention
/// (history never deleted), and restrict-FK refusal of destructive cascades.
/// </summary>
[Collection("FEAT-013 Licensing PostgreSQL")]
[Trait("Category", "FEAT-013")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicensingDurabilityTwinTests
{
    private const string V1 = "hushvoting-licence-catalogue/v1.0.0";
    private const string V1Schema = "hushvoting-licence-catalogue/v1";
    private static readonly string DigestA = new('A', 64);
    private static readonly DateTime JourneyStartUtc = new(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly LicensingPostgresFixture _fixture;

    public HushVotingLicensingDurabilityTwinTests(LicensingPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class FixedTimeProvider(DateTime fixedUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(DateTime.SpecifyKind(fixedUtc, DateTimeKind.Utc));
    }

    private async Task<(string DatabaseName, LicenceEntitlementService Service)> NewJourneyAsync()
    {
        var databaseName = $"feat013_durability_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(databaseName);
        await _fixture.MigrateToHeadAsync(databaseName);

        await using var context = _fixture.CreateContext(databaseName);
        var state = await LicenceCatalogueLedgerCoordinator.ReconcileAsync(
            context,
            new LicenceReleaseInstallSpec(V1, DigestA, V1Schema, "hush-server-node-test", "feat013-durability-twin"),
            _ => Task.FromResult(10_000L),
            CancellationToken.None);
        state.Ready.Should().BeTrue();

        var service = new LicenceEntitlementService(
            () => _fixture.CreateContext(databaseName),
            LicenceServiceConfiguration.CreateDefault(DigestA),
            new FixedTimeProvider(JourneyStartUtc));
        return (databaseName, service);
    }

    private static AuthenticatedIdentitySubject NewIdentity(string address)
    {
        var created = AuthenticatedIdentitySubject.TryCreate(
            LicencePersistenceVocabulary.SubjectTypeIdentity, address, 20_000, out var subject, out var error);
        created.Should().BeTrue(error);
        return subject!;
    }

    [Fact]
    public async Task Composed_service_journey_resolve_activate_calendar_expiry_resolve_is_exact()
    {
        var (databaseName, service) = await NewJourneyAsync();
        try
        {
            var identity = NewIdentity("journey-identity-1");

            var resolution = await service.GetOrProvisionAsync(identity, CancellationToken.None);
            resolution.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedDefault);
            resolution.Entitlement!.EntitlementRevision.Should().Be(1);

            var activationCommand = LicenceActivationCommand.TryCreate(
                Guid.NewGuid(),
                HushVotingLicencePlanId.DirectFree.Value,
                1,
                HushVotingLicencePlanId.Veritas500.Value,
                "journey-upgrade",
                out var command,
                out var error);
            activationCommand.Should().BeTrue(error);

            var activation = await service.ActivateHigherPlanAsync(identity, command!, CancellationToken.None);
            activation.Outcome.Should().Be(LicenceActivationOutcome.Activated);
            activation.Entitlement!.EntitlementRevision.Should().Be(2);
            activation.Entitlement.ExpiresAtUtc.Should().Be(JourneyStartUtc.AddYears(1));

            // At the upper-exclusive calendar boundary the next authoritative resolution expires
            // the Veritas assignment and creates Direct Free in one transaction (revision 3).
            var expiryService = new LicenceEntitlementService(
                () => _fixture.CreateContext(databaseName),
                LicenceServiceConfiguration.CreateDefault(DigestA),
                new FixedTimeProvider(JourneyStartUtc.AddYears(1)));

            var afterExpiry = await expiryService.GetOrProvisionAsync(identity, CancellationToken.None);
            afterExpiry.Outcome.Should().Be(LicenceResolutionOutcome.ExpiredToDefault);
            afterExpiry.Entitlement!.PlanId.Should().Be(HushVotingLicencePlanId.DirectFree.Value);
            afterExpiry.Entitlement.Source.Should().Be(LicencePersistenceVocabulary.SourceAutomaticExpiry);
            afterExpiry.Entitlement.EntitlementRevision.Should().Be(3);

            await using var db = _fixture.CreateContext(databaseName);
            var subjectRow = await db.Set<LicenceSubjectEntity>()
                .SingleAsync(s => s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress);
            subjectRow.EntitlementRevision.Should().Be(3);

            var assignments = await db.Set<LicenceAssignmentEntity>()
                .Where(a => a.LicenceSubjectId == subjectRow.LicenceSubjectId)
                .ToListAsync();
            assignments.Should().HaveCount(3);
            assignments.Should().ContainSingle(a =>
                a.PlanId == HushVotingLicencePlanId.DirectFree.Value
                && a.Source == LicencePersistenceVocabulary.SourceDefaultFree
                && a.LifecycleStatus == LicencePersistenceVocabulary.LifecycleSuperseded);
            assignments.Should().ContainSingle(a =>
                a.PlanId == HushVotingLicencePlanId.Veritas500.Value
                && a.LifecycleStatus == LicencePersistenceVocabulary.LifecycleExpired);
            assignments.Should().ContainSingle(a =>
                a.PlanId == HushVotingLicencePlanId.DirectFree.Value
                && a.Source == LicencePersistenceVocabulary.SourceAutomaticExpiry
                && a.LifecycleStatus == LicencePersistenceVocabulary.LifecycleActive);

            var events = await db.Set<LicenceTransitionEventEntity>()
                .Where(e => e.LicenceSubjectId == subjectRow.LicenceSubjectId)
                .OrderBy(e => e.EventSequence)
                .ToListAsync();
            events.Select(e => e.EventType).Should().Equal(
                LicencePersistenceVocabulary.EventTypeCreated,
                LicencePersistenceVocabulary.EventTypeSuperseded,
                LicencePersistenceVocabulary.EventTypeCreated,
                LicencePersistenceVocabulary.EventTypeExpired,
                LicencePersistenceVocabulary.EventTypeCreated);
            events.Should().OnlyContain(e => e.SubjectRevision >= 1 && e.SubjectRevision <= 3);

            var operations = await db.Set<LicenceActivationOperationEntity>()
                .Where(o => o.LicenceSubjectId == subjectRow.LicenceSubjectId)
                .ToListAsync();
            operations.Should().ContainSingle(o =>
                o.DurableResult == LicencePersistenceVocabulary.OperationResultActivated);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Licence_history_is_never_deleted_and_restrict_fks_refuse_destructive_cascades()
    {
        var (databaseName, service) = await NewJourneyAsync();
        try
        {
            var identity = NewIdentity("history-identity-1");
            var resolution = await service.GetOrProvisionAsync(identity, CancellationToken.None);
            resolution.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedDefault);

            await using var db = _fixture.CreateContext(databaseName);
            var subjectRow = await db.Set<LicenceSubjectEntity>()
                .SingleAsync(s => s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress);

            // Attempting to delete the subject (or its history) must be refused by restrict FKs.
            db.Remove(subjectRow);
            var subjectDelete = async () => await db.SaveChangesAsync();
            (await subjectDelete.Should().ThrowAsync<DbUpdateException>())
                .And.InnerException.Should().BeOfType<PostgresException>();

            // Roll the failed context away and prove every row survives unchanged.
            await using var verify = _fixture.CreateContext(databaseName);
            (await verify.Set<LicenceSubjectEntity>().CountAsync()).Should().Be(1);
            (await verify.Set<LicenceAssignmentEntity>().CountAsync()).Should().Be(1);
            (await verify.Set<LicenceTransitionEventEntity>().CountAsync()).Should().Be(1);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }
}
