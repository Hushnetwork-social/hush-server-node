using FluentAssertions;
using HushNode.HushVoting.Licence.Transactions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-015 real-PostgreSQL TwinTests (Phase 3 Tasks 3.5-3.6) for the DB-backed pending
/// reservation store. Proves: exact retry -> PENDING without a second row; idempotency
/// mismatch on same transaction id with different bytes; one pending per identity (partial
/// unique); higher-valid-rank supersession; first-valid same-rank; resolution transitions.
/// </summary>
[Collection("FEAT-015 Licensing PostgreSQL")]
[Trait("Category", "FEAT-015")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicenceReservationStoreTwinTests : IAsyncLifetime
{
    private readonly LicensingPostgresFixture _fixture;
    private readonly string _databaseName;
    private long _counter;

    public HushVotingLicenceReservationStoreTwinTests(LicensingPostgresFixture fixture)
    {
        _fixture = fixture;
        _databaseName = $"feat015_res_{Guid.NewGuid():N}";
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

    private string NextAddress() => $"feat015-res-{Interlocked.Increment(ref _counter):D4}";

    private HushVotingLicenceReservationStore NewStore() =>
        new(() => _fixture.CreateContext(_databaseName));

    private async Task<Guid> InsertSubjectAsync()
    {
        await using var context = _fixture.CreateContext(_databaseName);
        var subject = new LicenceSubjectEntity
        {
            LicenceSubjectId = Guid.CreateVersion7(),
            SubjectType = LicencePersistenceVocabulary.SubjectTypeIdentity,
            CanonicalPublicSigningAddress = NextAddress(),
            IdentityCreationBlockIndex = 1,
            CreatedAtUtc = DateTime.UtcNow,
            EntitlementRevision = 0,
        };
        context.Set<LicenceSubjectEntity>().Add(subject);
        await context.SaveChangesAsync();
        return subject.LicenceSubjectId;
    }

    private static HushVotingLicenceReservationClaim Claim(
        Guid subjectId,
        Guid transactionId,
        string digest,
        int rank,
        string intent = "confirmed_upgrade",
        string planId = "hushvoting.veritas.2000") =>
        new(
            subjectId,
            transactionId,
            digest,
            intent,
            planId,
            "hushvoting-licence-catalogue/v1.0.0",
            BaselineDirectFreeTransactionId,
            "hushvoting.direct.free",
            rank);

    private static readonly Guid BaselineDirectFreeTransactionId = Guid.Parse("aaaaaaaa-1111-4222-8333-444444444444");

    private static string Digest(char fill) => new(fill, 64);

    [Fact]
    public async Task First_claim_is_accepted_and_exact_retry_is_pending()
    {
        var store = NewStore();
        var subjectId = await InsertSubjectAsync();
        var tx = Guid.CreateVersion7();
        var digest = Digest('a');

        var first = await store.ReserveAsync(Claim(subjectId, tx, digest, rank: 2), CancellationToken.None);
        first.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Accepted);

        var retry = await store.ReserveAsync(Claim(subjectId, tx, digest, rank: 2), CancellationToken.None);
        retry.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Pending);

        await using var context = _fixture.CreateContext(_databaseName);
        (await context.Set<LicencePendingReservationEntity>()
            .CountAsync(r => r.OriginatingTransactionId == tx)).Should().Be(1);
    }

    [Fact]
    public async Task Reused_transaction_id_with_different_bytes_is_idempotency_mismatch()
    {
        var store = NewStore();
        var subjectId = await InsertSubjectAsync();
        var tx = Guid.CreateVersion7();

        var first = await store.ReserveAsync(Claim(subjectId, tx, Digest('a'), rank: 2), CancellationToken.None);
        first.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Accepted);

        var mismatch = await store.ReserveAsync(Claim(subjectId, tx, Digest('b'), rank: 2), CancellationToken.None);
        mismatch.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Rejected);
        mismatch.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.TransactionIdempotencyMismatch);
    }

    [Fact]
    public async Task Higher_valid_rank_supersedes_lower_pending()
    {
        var store = NewStore();
        var subjectId = await InsertSubjectAsync();

        var lower = await store.ReserveAsync(
            Claim(subjectId, Guid.CreateVersion7(), Digest('a'), rank: 1),
            CancellationToken.None);
        lower.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Accepted);

        var higher = await store.ReserveAsync(
            Claim(subjectId, Guid.CreateVersion7(), Digest('b'), rank: 3),
            CancellationToken.None);
        higher.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Accepted);

        await using var context = _fixture.CreateContext(_databaseName);
        var rows = await context.Set<LicencePendingReservationEntity>().ToListAsync();
        rows.Should().HaveCount(2); // append-only: lower superseded, higher pending
        rows.Should().Contain(r =>
            r.LifecycleStatus == LicencePersistenceVocabulary.ReservationLifecyclePending && r.RequestedUpgradeRank == 3);
        rows.Should().Contain(r =>
            r.LifecycleStatus == LicencePersistenceVocabulary.ReservationLifecycleSuperseded && r.RequestedUpgradeRank == 1);
    }

    [Fact]
    public async Task Equal_or_lower_rank_cannot_displace_pending()
    {
        var store = NewStore();
        var subjectId = await InsertSubjectAsync();

        var first = await store.ReserveAsync(
            Claim(subjectId, Guid.CreateVersion7(), Digest('a'), rank: 2),
            CancellationToken.None);
        first.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Accepted);

        var equal = await store.ReserveAsync(
            Claim(subjectId, Guid.CreateVersion7(), Digest('b'), rank: 2),
            CancellationToken.None);
        equal.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Pending);

        var lower = await store.ReserveAsync(
            Claim(subjectId, Guid.CreateVersion7(), Digest('c'), rank: 0),
            CancellationToken.None);
        lower.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Rejected);
        lower.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.TransitionPending);
    }

    [Fact]
    public async Task Resolve_pending_marks_the_row_resolved()
    {
        var store = NewStore();
        var subjectId = await InsertSubjectAsync();
        var tx = Guid.CreateVersion7();

        var accepted = await store.ReserveAsync(Claim(subjectId, tx, Digest('a'), rank: 2), CancellationToken.None);
        accepted.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Accepted);

        var resolved = await store.ResolvePendingAsync(
            subjectId, tx, LicencePersistenceVocabulary.ReservationLifecycleResolved, CancellationToken.None);
        resolved.Should().BeTrue();

        // After resolution, a fresh (different tx) claim for the subject is accepted again.
        var fresh = await store.ReserveAsync(
            Claim(subjectId, Guid.CreateVersion7(), Digest('b'), rank: 2),
            CancellationToken.None);
        fresh.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Accepted);
    }
}
