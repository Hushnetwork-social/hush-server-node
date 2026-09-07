using FluentAssertions;
using HushNode.HushVoting.Licence.gRPC;
using HushNode.HushVoting.Licence.Transactions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.HushVoting.Licensing.Model;
using HushShared.Blockchain.TransactionModel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-015 real-PostgreSQL TwinTests (Phase 6 Task 6.5) for the licence entitlement query
/// application service. Proves: no-active -> exactly one Direct Free template; active indexed
/// Direct Free/Veritas -> safe active view with strictly-higher options and informational
/// Enterprise; and that query resolution never writes licence state.
/// </summary>
[Collection("FEAT-015 Licensing PostgreSQL")]
[Trait("Category", "FEAT-015")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicenceEntitlementQueryTwinTests : IAsyncLifetime
{
    private readonly LicensingPostgresFixture _fixture;
    private readonly string _databaseName;
    private long _counter;

    public HushVotingLicenceEntitlementQueryTwinTests(LicensingPostgresFixture fixture)
    {
        _fixture = fixture;
        _databaseName = $"feat015_qry_{Guid.NewGuid():N}";
    }

    public async Task InitializeAsync()
    {
        await _fixture.CreateDatabaseAsync(_databaseName);
        await _fixture.MigrateToAsync(_databaseName, "20260906122828_Feat015LicenceIndexProjectionAndReservation");
    }

    public async Task DisposeAsync()
    {
        await _fixture.DropDatabaseAsync(_databaseName);
    }

    private string NextAddress() => $"feat015-qry-{Interlocked.Increment(ref _counter):D4}";

    private static readonly HushVotingLicenceCatalogue Catalogue = HushVotingLicenceCatalogueV1.CreateCatalogue();
    private static readonly LicenceServiceConfiguration Configuration =
        LicenceServiceConfiguration.CreateDefault(catalogue: Catalogue);

    private LicenceEntitlementQueryApplicationService NewService() =>
        new(new LicenceIndexedProjectionReader(() => _fixture.CreateContext(_databaseName)), Configuration);

    private async Task<(string Address, AuthenticatedIdentitySubject Subject)> InsertIndexedSubjectAsync(string? planId = null)
    {
        var address = NextAddress();
        var subject = new LicenceSubjectEntity
        {
            LicenceSubjectId = Guid.CreateVersion7(),
            SubjectType = LicencePersistenceVocabulary.SubjectTypeIdentity,
            CanonicalPublicSigningAddress = address,
            IdentityCreationBlockIndex = 100,
            CreatedAtUtc = DateTime.UtcNow,
            EntitlementRevision = 0,
        };
        await using (var context = _fixture.CreateContext(_databaseName))
        {
            context.Set<LicenceSubjectEntity>().Add(subject);
            await context.SaveChangesAsync();
        }

        var ok = AuthenticatedIdentitySubject.TryCreate(
            LicencePersistenceVocabulary.SubjectTypeIdentity, address, 100, out var trusted, out _);
        ok.Should().BeTrue();
        return (address, trusted!);
    }

    [Fact]
    public async Task No_active_identity_returns_direct_free_template()
    {
        var (address, _) = await InsertIndexedSubjectAsync();
        var service = NewService();

        var result = await service.GetMyEntitlementAsync(address, CancellationToken.None);

        result.State.Should().Be(HushVotingLicenceEntitlementQueryState.NoActive);
        result.DirectFreeTemplate!.TransitionIntent.Should().Be("baseline_free");
        result.DirectFreeTemplate.RequestedPlanId.Should().Be("hushvoting.direct.free");
        result.DirectFreeTemplate.ObservedCatalogueVersion.Should().Be(Catalogue.Version.Value);
    }

    [Fact]
    [Trait("Category", "FEAT-017")]
    [Trait("AcceptanceId", "AT-LIC-004")] // FEAT-017 TwinTest pair (EPIC-002): options page reflects the exact server catalogue in server order
    public async Task Active_veritas_returns_safe_view_with_higher_option_and_enterprise()
    {
        var (address, trusted) = await InsertIndexedSubjectAsync();
        var baselineTx = Guid.Parse("5f2d9e11-3c44-4a80-b8e7-6b2f1a0c9d3e");
        var blockTime = DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime();

        await LicenceBlockIndexWriter.IndexAsync(
            () => _fixture.CreateContext(_databaseName),
            Configuration,
            trusted,
            BuildValidated(baselineTx, new HushVotingLicenceAssignmentPayload(
                HushVotingLicenceTransitionIntent.BaselineFree,
                "hushvoting.direct.free",
                Catalogue.Version.Value)),
            1,
            blockTime,
            null,
            CancellationToken.None);

        var upgradeTx = Guid.Parse("8c6a1b77-4d2e-4f91-a4c0-9e7b2d8f1a55");
        await LicenceBlockIndexWriter.IndexAsync(
            () => _fixture.CreateContext(_databaseName),
            Configuration,
            trusted,
            BuildValidated(upgradeTx, new HushVotingLicenceAssignmentPayload(
                HushVotingLicenceTransitionIntent.ConfirmedUpgrade,
                "hushvoting.veritas.2000",
                Catalogue.Version.Value,
                baselineTx,
                "hushvoting.direct.free")),
            2,
            blockTime.AddDays(30),
            null,
            CancellationToken.None);

        var service = NewService();
        var result = await service.GetMyEntitlementAsync(address, CancellationToken.None);

        result.State.Should().Be(HushVotingLicenceEntitlementQueryState.Active);
        var view = result.Active!;
        view.PlanId.Should().Be("hushvoting.veritas.2000");
        view.LicenceReference.Should().Be(upgradeTx.ToString());
        view.HigherOptions.Select(o => o.PlanId).Should().Equal("hushvoting.veritas.10000");
        view.Enterprise!.PlanId.Should().Be("hushvoting.enterprise");
        view.ExpiresAtUtc.Should().Be(blockTime.AddDays(30).AddYears(1));
    }

    [Fact]
    public async Task Query_resolution_never_writes_licence_state()
    {
        var (address, _) = await InsertIndexedSubjectAsync();
        var service = NewService();

        await service.GetMyEntitlementAsync(address, CancellationToken.None);

        // No assignment/event/outbox row may exist after a pure query.
        await using var context = _fixture.CreateContext(_databaseName);
        (await context.Set<LicenceAssignmentEntity>().CountAsync()).Should().Be(0);
        (await context.Set<LicenceTransitionEventEntity>().CountAsync()).Should().Be(0);
        (await context.Set<LicenceCacheOutboxEntity>().CountAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("Category", "FEAT-016")]
    [Trait("AcceptanceId", "AT-LIC-011")] // FEAT-016 TwinTest pair (EPIC-002): Lock/identity replacement cannot leak entitlement
    public async Task Another_actor_never_observes_the_first_actors_entitlement()
    {
        // Actor A is an indexed identity holding an active Direct Free assignment.
        var (actorA, trustedA) = await InsertIndexedSubjectAsync();
        var baselineTx = Guid.Parse("7f3d2c11-4b4e-4a81-b8e7-6b2f1a0c9d3e");
        var blockTime = DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime();
        await LicenceBlockIndexWriter.IndexAsync(
            () => _fixture.CreateContext(_databaseName),
            Configuration,
            trustedA,
            BuildValidated(baselineTx, new HushVotingLicenceAssignmentPayload(
                HushVotingLicenceTransitionIntent.BaselineFree,
                "hushvoting.direct.free",
                Catalogue.Version.Value)),
            1,
            blockTime,
            null,
            CancellationToken.None);

        // Actor B is a separate indexed identity with no licence (the
        // replacement/later identity on the same device).
        var (actorB, _) = await InsertIndexedSubjectAsync();
        var service = NewService();

        var resultForB = await service.GetMyEntitlementAsync(actorB, CancellationToken.None);

        // B sees its own no-active state with the Direct Free template only —
        // never A's active assignment, plan, or licence reference.
        resultForB.State.Should().Be(HushVotingLicenceEntitlementQueryState.NoActive);
        resultForB.DirectFreeTemplate.Should().NotBeNull();
        resultForB.Active.Should().BeNull();

        var resultForA = await service.GetMyEntitlementAsync(actorA, CancellationToken.None);
        resultForA.State.Should().Be(HushVotingLicenceEntitlementQueryState.Active);
        resultForA.Active.Should().NotBeNull();
        resultForA.Active!.PlanId.Should().Be("hushvoting.direct.free");
        resultForA.Active!.LicenceReference.Should().Be(baselineTx.ToString());
    }

    private static HushShared.Blockchain.TransactionModel.States.ValidatedTransaction<HushVotingLicenceAssignmentPayload>
        BuildValidated(Guid txId, HushVotingLicenceAssignmentPayload payload)
    {
        var unsigned = new HushShared.Blockchain.TransactionModel.States.UnsignedTransaction<HushVotingLicenceAssignmentPayload>(
            new HushShared.Blockchain.TransactionModel.TransactionId(txId),
            HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind,
            new HushShared.Blockchain.Model.Timestamp(
                DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime()),
            payload,
            HushVotingLicenceCanonicalJson.PayloadJsonUtf8Length(payload));
        var signed = new HushShared.Blockchain.TransactionModel.States.SignedTransaction<HushVotingLicenceAssignmentPayload>(
            unsigned,
            new HushShared.Blockchain.Model.SignatureInfo("signatory", "sig"));
        return signed.SignByValidator(new HushShared.Blockchain.Model.SignatureInfo("validator", "vsig"));
    }
}
