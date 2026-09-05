using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-014 Phase 7 Task 7.3 real-PostgreSQL + real-Redis TwinTests: transactional outbox rows,
/// lease-safe claim arbitration between two dispatcher instances, crash-safe reclaim of abandoned
/// leases, idempotent latest-revision convergence through Redis CAS, and retention that never drops
/// pending work. Two cache-service instances share the same Redis and PostgreSQL.
/// </summary>
[Collection("FEAT-014 Redis+PostgreSQL")]
[Trait("Category", "FEAT-014")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicenceCacheOutboxConvergenceTwinTests
{
    private const string CatalogueVersion = "hushvoting-licence-catalogue/v1.0.0";
    private static readonly string CatalogueDigest = new('A', 64);
    private static readonly DateTime NowUtc = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private static readonly LicenceCacheOptions Options = new();
    private static readonly LicenceCacheEnvelopeCodec Codec = new();
    private static readonly LicenceCacheKeyRing KeyRing = LicenceCacheKeyRing.TryCreate(
        LicenceCacheMasterKey.Create("v2", Enumerable.Range(1, 32).Select(i => (byte)i).ToArray(), NowUtc, Options, out _),
        LicenceCacheMasterKey.Create("v1", Enumerable.Range(90, 32).Select(i => (byte)i).ToArray(), new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc), Options, out _),
        Options,
        out _)!;

    private readonly LicenceCacheRedisPostgresFixture _fixture;

    public HushVotingLicenceCacheOutboxConvergenceTwinTests(LicenceCacheRedisPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private static LicenceSubjectEntity SubjectRow(string address, Guid subjectId) =>
        new()
        {
            LicenceSubjectId = subjectId,
            SubjectType = LicencePersistenceVocabulary.SubjectTypeIdentity,
            // Production always persists the canonical address; mirror that so the digest the
            // dispatcher recomputes matches the address-derived digest asserted by the test.
            CanonicalPublicSigningAddress = Subject(address).CanonicalPublicSigningAddress,
            IdentityCreationBlockIndex = 7,
            CreatedAtUtc = NowUtc,
            EntitlementRevision = 9,
        };

    private static AuthenticatedIdentitySubject Subject(string address) =>
        AuthenticatedIdentitySubject.TryCreate(
            LicencePersistenceVocabulary.SubjectTypeIdentity, address, 7, out var s, out _) ? s! : throw new InvalidOperationException();

    private static EffectiveLicenceEntitlement Entitlement(Guid subjectId, long revision) =>
        new(
            subjectId,
            Guid.NewGuid(),
            "hushvoting.standard",
            "Standard",
            1,
            null,
            false,
            "annual",
            1,
            new[] { "proposal", "vote" },
            "licence-assignment",
            NowUtc,
            NowUtc.AddYears(1),
            CatalogueVersion,
            CatalogueDigest,
            revision);

    private sealed class StubCatalogue : ICurrentLicenceCatalogueProvider
    {
        public (string Version, string DigestSha256) Current => (CatalogueVersion, CatalogueDigest);
    }

    private sealed class SubjectAwareAuthority : IEntitlementAuthorityResolver
    {
        private readonly Guid _subjectId;

        public SubjectAwareAuthority(Guid subjectId)
        {
            _subjectId = subjectId;
        }

        public Task<LicenceResolutionResult> ResolveEffectiveEntitlementAsync(
            AuthenticatedIdentitySubject subject,
            CancellationToken cancellationToken) =>
            Task.FromResult(LicenceResolutionResult.Ok(LicenceResolutionOutcome.ResolvedExisting, Entitlement(_subjectId, 9)));
    }

    private async Task<(string Db, LicenceCacheOutboxStore Store, LicenceCacheRedisWriter Writer)> PrepareAsync()
    {
        await _fixture.FlushRedisAsync();
        var db = "feat014_outbox_" + Guid.NewGuid().ToString("N")[..12];
        await _fixture.CreateDatabaseAsync(db);
        await _fixture.MigrateToHeadAsync(db);

        var store = new LicenceCacheOutboxStore(() => _fixture.CreateContext(db), Options);
        var writer = new LicenceCacheRedisWriter(
            new RedisEntitlementProjectionStore(_fixture.RedisDatabase, Options, Codec),
            Codec,
            Options,
            KeyRing,
            new StubCatalogue(),
            new LicenceCacheTelemetry(),
            () => NowUtc,
            _fixture.InstancePrefix);
        return (db, store, writer);
    }

    private LicenceCacheOutboxDispatcherService NewDispatcher(
        string db,
        LicenceCacheOutboxStore store,
        LicenceCacheRedisWriter writer,
        Guid subjectId) =>
        new(
            store,
            () => _fixture.CreateContext(db),
            new SubjectAwareAuthority(subjectId),
            writer,
            Options,
            new LicenceCacheTelemetry());

    private async Task SeedSubjectAsync(string db, Guid subjectId, string address)
    {
        await using var context = _fixture.CreateContext(db);
        context.Set<LicenceSubjectEntity>().Add(SubjectRow(address, subjectId));
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Two_instances_share_redis_and_postgres_and_only_one_claims_each_row()
    {
        var (db, store, writer) = await PrepareAsync();
        var subjectId = Guid.NewGuid();
        const string address = "NXc1cYVmGJZcbzF5wY2tT9DP5cFpFT9k";
        await SeedSubjectAsync(db, subjectId, address);

        var rowId = await store.EnqueueAsync(subjectId, 9, LicenceCacheOutboxChangeKinds.ActivatedHigherPlan, NowUtc, CancellationToken.None);

        // Two dispatcher instances race for the same row over the same PG lease table and Redis.
        var instanceA = NewDispatcher(db, store, writer, subjectId);
        var instanceB = NewDispatcher(db, store, writer, subjectId);
        var results = await Task.WhenAll(
            instanceA.ProcessOnceAsync(CancellationToken.None),
            instanceB.ProcessOnceAsync(CancellationToken.None));

        results.Sum().Should().Be(1, "a lease-safe claim grants each row to exactly one dispatcher");

        await using var context = _fixture.CreateContext(db);
        var row = await context.Set<LicenceCacheOutboxEntity>().SingleAsync(e => e.Id == rowId);
        row.DeliveredUtc.Should().NotBeNull();
        row.LeaseOwnerToken.Should().BeNull("the lease is released after delivery");

        // Redis now holds the latest authoritative revision (rev 9) visible to both instances.
        var canonical = Subject(address).CanonicalPublicSigningAddress;
        var digest = LicenceCacheKeyDerivation.ComputeSubjectDigest(
            LicenceCacheKeyDerivation.DeriveSubjectKey(KeyRing.Current.SecretBytes), canonical);
        var key = LicenceCacheKeyBuilder.BuildProjectionKey(
            _fixture.InstancePrefix,
            LicenceCacheKeyBuilder.BuildCatalogueToken(CatalogueVersion, CatalogueDigest),
            KeyRing.Current.KeyId,
            digest);
        var read = await new RedisEntitlementProjectionStore(_fixture.RedisDatabase, Options, Codec)
            .ReadCurrentAsync(key, CancellationToken.None);
        read.Found.Should().BeTrue("row state: {0}", $"{row.DeliveredUtc is not null}(delivered), attempts={row.AttemptCount}, lastError={row.LastSafeErrorCode ?? "none"}");
        Codec.TryDeserialize(read.CanonicalEnvelopeBytes, out var envelope, out _).Should().BeTrue();
        envelope!.EntitlementRevision.Should().Be(9);

        await _fixture.DropDatabaseAsync(db);
    }

    [Fact]
    public async Task Crash_before_delivery_leaves_claim_reclaimable_after_lease_expiry_and_converges_idempotently()
    {
        var (db, store, writer) = await PrepareAsync();
        var subjectId = Guid.NewGuid();
        const string address = "PYq2dZQoVhAKiU5fLkDxRwvjS9yDkM1Q";
        await SeedSubjectAsync(db, subjectId, address);

        var rowId = await store.EnqueueAsync(subjectId, 9, LicenceCacheOutboxChangeKinds.ActivatedHigherPlan, NowUtc, CancellationToken.None);

        // Instance A claims the row then "crashes" before marking delivery (simulated by simply
        // releasing the claim with an expired lease, matching what the database sees after 60 s).
        var staleOwner = "dead-instance-owner";
        await store.ClaimBatchAsync(staleOwner, NowUtc.AddMinutes(-1), NowUtc.AddMinutes(-2), 1, CancellationToken.None);

        // Instance B processes after the lease expires and converges durably and idempotently.
        var instanceB = NewDispatcher(db, store, writer, subjectId);
        var processed = await instanceB.ProcessOnceAsync(CancellationToken.None);
        processed.Should().Be(1);

        await using var context = _fixture.CreateContext(db);
        var row = await context.Set<LicenceCacheOutboxEntity>().SingleAsync(e => e.Id == rowId);
        row.DeliveredUtc.Should().NotBeNull();
        row.AttemptCount.Should().Be(0, "first successful delivery marks the row without a failure attempt");

        await _fixture.DropDatabaseAsync(db);
    }

    [Fact]
    public async Task Retention_purges_only_delivered_rows_and_never_drops_pending_work()
    {
        var (db, store, _) = await PrepareAsync();
        var subjectId = Guid.NewGuid();
        await SeedSubjectAsync(db, subjectId, "Kq9nXjF2hPqZfRk2YmQ1w5LvNx7yGmHt");

        var deliveredOldId = await store.EnqueueAsync(subjectId, 3, LicenceCacheOutboxChangeKinds.ActivatedHigherPlan, NowUtc.AddDays(-40), CancellationToken.None);
        var pendingOldId = await store.EnqueueAsync(subjectId, 9, LicenceCacheOutboxChangeKinds.ActivatedHigherPlan, NowUtc.AddDays(-40), CancellationToken.None);

        // Mark the delivered row delivered 40 days ago (older than the 30-day retention window).
        await using (var context = _fixture.CreateContext(db))
        {
            var delivered = await context.Set<LicenceCacheOutboxEntity>().SingleAsync(e => e.Id == deliveredOldId);
            delivered.DeliveredUtc = NowUtc.AddDays(-40);
            await context.SaveChangesAsync();
        }

        var purged = await store.PurgeDeliveredAsync(NowUtc.AddDays(-31), 100, CancellationToken.None);
        purged.Should().Be(1);

        await using var verify = _fixture.CreateContext(db);
        verify.Set<LicenceCacheOutboxEntity>().AnyAsync(e => e.Id == deliveredOldId).Result.Should().BeFalse();
        var pending = await verify.Set<LicenceCacheOutboxEntity>().SingleAsync(e => e.Id == pendingOldId);
        pending.DeliveredUtc.Should().BeNull("pending work is preserved regardless of age");

        await _fixture.DropDatabaseAsync(db);
    }
}
