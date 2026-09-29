using System.Data.Common;
using System.Text.Json;
using FluentAssertions;
using HushNode.Blockchain.BlockModel.States;
using HushNode.Events;
using HushNode.HushVoting.Licence.Transactions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.Indexing;
using HushNode.Indexing.Interfaces;
using HushNode.Interfaces;
using HushServerNode.HushVotingLicensingIntegration;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.HushVoting.Licensing.Model;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Olimpo;
using Olimpo.KeyDerivation;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.ServerTwins;

[Binding]
[Scope(Tag = "HV-ENTITLEMENT-REPLAY-TWIN")]
internal sealed class EntitlementReplayTwinSteps(HushVotingScenario scenario)
{
    private DerivedKeys _keys = null!;
    private readonly CapturedBlocks _blocks = new();
    private LicenceAssignmentEntity[] _before = [];
    private long _revision;
    private IndexedEntitlementReadResult? _read;
    private string _readSql = "";

    private HushNodeDbContext Db() => HushVotingLicensingIntegrationHostBuild.CreateFreshDbContext(scenario.Node.Services);
    private async Task<LicenceAssignmentEntity[]> Assignments()
    {
        await using var db = Db();
        return await db.Set<LicenceAssignmentEntity>().AsNoTracking().OrderBy(x => x.OriginatingBlockIndex).ToArrayAsync();
    }

    [Given("real historical licence blocks whose annual assignment has now expired")]
    public async Task Prepare()
    {
        scenario.Page.Should().BeNull();
        var words = MnemonicGenerator.GenerateMnemonic();
        _keys = HushVotingTestIdentity.DeriveP01(words);
        await HushVotingArtifactClient.RegisterAsync(words, _keys.SigningPrivateKey, _keys.EncryptPrivateKey,
            _keys.SigningPublicKey, _keys.EncryptPublicKey);
        await HushVotingServerIdentity.RegisterAsync(scenario, _keys, "Replay fixture");
        scenario.Node.Services.GetRequiredService<IEventAggregator>().Subscribe(_blocks);
        var baselineId = await Submit(new(HushVotingLicenceTransitionIntent.BaselineFree,
            HushVotingLicencePlanId.DirectFree.Value, HushVotingLicenceCatalogueVersion.V1Value));
        scenario.HistoricalBlockClock!.AdvanceTo(scenario.HistoricalBlockClock.GetUtcNow().AddSeconds(1));
        await Submit(new(HushVotingLicenceTransitionIntent.ConfirmedUpgrade, HushVotingLicencePlanId.Veritas500.Value,
            HushVotingLicenceCatalogueVersion.V1Value, baselineId, HushVotingLicencePlanId.DirectFree.Value));
        _before = await Assignments();
        _before.Should().HaveCount(2);
        _before[1].ExpiresAtUtc.Should().BeBefore(DateTime.UtcNow);
        _blocks.Values.Should().HaveCount(2);
        await using var db = Db();
        _revision = (await db.Set<LicenceSubjectEntity>().SingleAsync()).EntitlementRevision;
        var query = scenario.Node.Services.GetRequiredService<HushNode.HushVoting.Licence.gRPC.ILicenceEntitlementQueryApplicationService>();
        (await query.GetMyEntitlementAsync(_keys.SigningPublicKey, CancellationToken.None)).State
            .Should().Be(HushVotingLicenceEntitlementQueryState.NoActive);
    }

    private async Task<Guid> Submit(HushVotingLicenceAssignmentPayload payload)
    {
        var id = Guid.NewGuid();
        var unsigned = new UnsignedTransaction<HushVotingLicenceAssignmentPayload>(new TransactionId(id),
            HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind,
            new Timestamp(scenario.HistoricalBlockClock!.GetUtcNow().UtcDateTime), payload,
            HushVotingLicenceCanonicalJson.PayloadJsonUtf8Length(payload));
        var canonical = new HushVotingLicenceCanonicalSerializer().SerializeCanonicalUnsignedJson(
            new SignedTransaction<HushVotingLicenceAssignmentPayload>(unsigned, new SignatureInfo(_keys.SigningPublicKey, "")));
        var signed = new SignedTransaction<HushVotingLicenceAssignmentPayload>(unsigned,
            new SignatureInfo(_keys.SigningPublicKey, DigitalSignature.SignMessageCompactBase64(canonical, _keys.SigningPrivateKey)));
        var wire = JsonSerializer.Serialize(signed);
        await HushVotingArtifactClient.RegisterAsync(wire);
        using (var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(20)))
        {
            var reply = await scenario.Blockchain.SubmitSignedTransactionAsync(new() { SignedTransaction = wire }, deadline: DateTime.UtcNow.AddSeconds(15));
            if (!reply.Successfull) throw new InvalidOperationException("Fixture licence rejected: " + reply.ValidationCode);
            await received.WaitAsync();
        }
        await scenario.Blocks.ProduceBlockAsync();
        return id;
    }

    [When("their real indexing dispatcher replays the retained blocks")]
    public async Task Replay()
    {
        var dispatcher = (IndexingDispatcherService)scenario.Node.Services.GetRequiredService<IIndexingDispatcherService>();
        foreach (var block in _blocks.Values.ToArray()) await dispatcher.HandleAsync(new BlockCreatedEvent(block));
    }

    [Then("the retained licence assignments and revision are unchanged")]
    public async Task Unchanged()
    {
        (await Assignments()).Should().BeEquivalentTo(_before);
        await using var db = Db();
        (await db.Set<LicenceSubjectEntity>().SingleAsync()).EntitlementRevision.Should().Be(_revision);
    }

    [When("the licence projection is rebuilt by replaying the retained blocks")]
    public async Task Rebuild()
    {
        await using (var db = Db())
        {
            // Deliberately discard only this isolated Twin's rebuildable licence projection.
            // No production reset route is introduced; retained blocks and identities survive.
            await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"HushVoting\".\"LicenceAssignment\", \"HushVoting\".\"LicenceTransitionEvent\", \"HushVoting\".\"LicenceActivationOperation\", \"HushVoting\".\"LicencePendingReservation\", \"HushVoting\".\"LicenceCacheOutbox\" CASCADE");
            await db.Database.ExecuteSqlRawAsync("UPDATE \"HushVoting\".\"LicenceSubject\" SET \"EntitlementRevision\" = 0");
        }
        await Replay();
    }

    [Then("rebuilt licence terms and chain references match the original projection")]
    public async Task Rebuilt()
    {
        var rebuilt = await Assignments();
        rebuilt.Should().HaveCount(_before.Length);
        for (var i = 0; i < rebuilt.Length; i++)
            rebuilt[i].Should().BeEquivalentTo(_before[i], options => options
                .Excluding(x => x.LicenceAssignmentId).Excluding(x => x.SupersededByAssignmentId));
        await using var db = Db();
        (await db.Set<LicenceSubjectEntity>().SingleAsync()).EntitlementRevision.Should().Be(_revision);
    }

    [When("a dispatcher cannot resolve the retained approved licence release")]
    public async Task MissingRelease()
    {
        var services = scenario.Node.Services;
        var strategy = new LicenceBlockContextIndexStrategy(
            services.GetRequiredService<IHushVotingLicenceTransactionValidator>(),
            Db, new LicenceCatalogueArchive([]));
        var completed = false;
        // Real dispatcher and index strategy; isolated callback observes the failure boundary.
        var events = new EventAggregator(Microsoft.Extensions.Logging.Abstractions.NullLogger<EventAggregator>.Instance);
        var dispatcher = new IndexingDispatcherService([], events, () => completed = true, [strategy]);
        Func<Task> index = () => dispatcher.HandleAsync(new BlockCreatedEvent(_blocks.Values[0]));
        await index.Should().ThrowAsync<InvalidOperationException>().WithMessage("*retained approved catalogue*");
        completed.Should().BeFalse();
        events.Unsubscribe(dispatcher);
    }

    [Then("indexing fails without advancing its completion callback or changing rights")]
    public Task FailurePreserves() => Unchanged();

    [When("the indexed authority reader queries the assignment at its effective instant")]
    public async Task ReadAtEffectiveTime()
    {
        var recorder = new SqlCapture();
        var services = scenario.Node.Services;
        var options = new DbContextOptionsBuilder<HushNodeDbContext>(services.GetRequiredService<DbContextOptions<HushNodeDbContext>>())
            .AddInterceptors(recorder).Options;
        var reader = new LicenceIndexedProjectionReader(() => new HushNodeDbContext(services.GetServices<IDbContextConfigurator>(), options));
        AuthenticatedIdentitySubject.TryCreate(LicencePersistenceVocabulary.SubjectTypeIdentity, _keys.SigningPublicKey, 1, out var subject, out _).Should().BeTrue();
        _read = await reader.ResolveEffectiveAsync(subject!, _before[1].EffectiveFromUtc, CancellationToken.None);
        _readSql = recorder.Command;
    }

    [Then("PostgreSQL filters the current row and the original assigned terms are returned")]
    public void Bounded()
    {
        _read!.Outcome.Should().Be(IndexedEntitlementReadOutcome.Active);
        _read.Entitlement!.LicenceReference.Should().Be(_before[1].OriginatingTransactionId);
        _read.Entitlement.EligibleVoterCap.Should().Be(_before[1].EligibleVoterCap);
        // Assert the actual SQL sent by EF has the active predicate, not a LINQ-only post-filter.
        _readSql.Contains("\"LifecycleStatus\" = 'active'", StringComparison.Ordinal).Should().BeTrue();
    }

    [AfterScenario(Order = 10)]
    public void StopCapture() => scenario.Node.Services.GetRequiredService<IEventAggregator>().Unsubscribe(_blocks);

    private sealed class CapturedBlocks : IHandleAsync<BlockCreatedEvent>
    {
        public List<FinalizedBlock> Values { get; } = [];
        public Task HandleAsync(BlockCreatedEvent message)
        {
            if (message.Block.Transactions.Any(t => t.PayloadKind == HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind)) Values.Add(message.Block);
            return Task.CompletedTask;
        }
    }

    private sealed class SqlCapture : DbCommandInterceptor
    {
        public string Command { get; private set; } = "";
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Command = command.CommandText;
            return ValueTask.FromResult(result);
        }
    }
}
