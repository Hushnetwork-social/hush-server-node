using System.Text.Json;
using FluentAssertions;
using HushNode.Elections;
using HushNode.HushVoting.Licence.Transactions;
using HushNode.HushVoting.Licensing.Storage;
using HushServerNode.HushVotingLicensingIntegration;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Olimpo;
using StackExchange.Redis;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.ServerTwins;

internal sealed partial class EntitlementEnforcementTwinSteps
{
    private ElectionEntitlementCapture _originalCapture = null!;
    private ElectionRolloutReadinessResult _rollout = null!;
    private ElectionEntitlementRolloutReadiness Rollout(LicenceCatalogueArchive? archive = null) => new(
        () => HushVotingLicensingIntegrationHostBuild.CreateFreshDbContext(scenario.Node.Services),
        archive ?? scenario.Node.Services.GetRequiredService<LicenceCatalogueArchive>(),
        scenario.Node.Services.GetRequiredService<IHushVotingLicenceTransactionValidator>(),
        scenario.Node.Services.GetRequiredService<IElectionEnvelopeCryptoService>());

    [Given("the rollout block clock retains submicrosecond UTC precision")]
    public void SetPreciseBlockClock()
    {
        var now = scenario.HistoricalBlockClock!.GetUtcNow().UtcTicks;
        scenario.HistoricalBlockClock.AdvanceTo(new DateTimeOffset((now / TimeSpan.TicksPerSecond + 1) * TimeSpan.TicksPerSecond + 7, TimeSpan.Zero));
    }

    [Given("a genuinely indexed Open whose licence is now expired")]
    public async Task IndexedOpenForRollout()
    {
        await OpenAtOffset(-1);
        _originalCapture = await Read(db => db.ElectionEntitlementCaptures.AsNoTracking().SingleAsync(c => c.ElectionId == _id));
        scenario.HistoricalBlockClock!.AdvanceTo(_licenceExpiry.AddDays(1));
        var ready = await Rollout().EvaluateAsync();
        ready.Ready.Should().BeTrue(ready.Code);
        ready.BackfilledCaptures.Should().Be(0);
    }

    [When("only its rebuildable capture projection is lost")]
    public async Task LoseCapture()
    {
        await WithRestoredEvidenceFault(async db =>
            await db.Set<ElectionEntitlementCapture>().Where(c => c.ElectionId == _id).ExecuteDeleteAsync());
        _rollout = await Rollout().EvaluateAsync();
    }

    [Then("readiness reconstructs the identical original capture without changing current rights")]
    public async Task Reconstructed()
    {
        _rollout.Ready.Should().BeTrue(_rollout.Code);
        _rollout.BackfilledCaptures.Should().Be(1);
        (await Read(db => db.ElectionEntitlementCaptures.AsNoTracking().SingleAsync(c => c.ElectionId == _id))).Should().Be(_originalCapture);
        (await Rollout().EvaluateAsync()).Should().Be(new ElectionRolloutReadinessResult(true, "election_entitlement_ready"));
        var query = scenario.Node.Services.GetRequiredService<HushNode.HushVoting.Licence.gRPC.ILicenceEntitlementQueryApplicationService>();
        (await query.GetMyEntitlementAsync(_owner.SigningPublicKey, CancellationToken.None)).State
            .Should().Be(HushVotingLicenceEntitlementQueryState.NoActive);
    }

    [When("rollout encounters (incompatible capture|missing checkpoint|missing retained release|unprovable assignment)")]
    public async Task BreakRolloutEvidence(string defect)
    {
        await WithRestoredEvidenceFault(async db =>
        {
        switch (defect)
        {
            case "incompatible capture":
                await db.Set<ElectionEntitlementCapture>().Where(c => c.ElectionId == _id).ExecuteUpdateAsync(s => s.SetProperty(c => c.PolicyVersion, "unsupported/v9"));
                break;
            case "missing checkpoint":
                await db.Set<ElectionIndexCheckpoint>().Where(c => c.BlockHeight == _originalCapture.OpenBlockHeight).ExecuteDeleteAsync();
                break;
            case "unprovable assignment":
                await db.Set<LicenceAssignmentEntity>().Where(a => a.OriginatingTransactionId == _originalCapture.OriginatingLicenceTransactionId)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.EligibleVoterCap, 10000));
                break;
        }
        });
        _rollout = await Rollout(defect == "missing retained release" ? new LicenceCatalogueArchive([]) : null).EvaluateAsync();
    }

    [Then("rollout refuses with (.*) and preserves every existing capture")]
    public async Task Refuses(string code)
    {
        _rollout.Ready.Should().BeFalse();
        _rollout.Code.Should().Be(code);
        _rollout.BackfilledCaptures.Should().Be(0);
        var actual = await Read(db => db.ElectionEntitlementCaptures.AsNoTracking().SingleAsync(c => c.ElectionId == _id));
        actual.Should().Be(code == "election_capture_incompatible" ? _originalCapture with { PolicyVersion = "unsupported/v9" } : _originalCapture);
    }

    [When("the run-owned Redis is unavailable during rollout verification")]
    public async Task RedisUnavailableForRollout()
    {
        await scenario.WithRedisStoppedAsync(async () =>
        {
            var redis = scenario.Node.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
            Func<Task> ping = async () => { await redis.PingAsync(); };
            await ping.Should().ThrowAsync<RedisException>();
            _rollout = await Rollout().EvaluateAsync();
        });
    }

    [Then("the retained PostgreSQL authority remains ready and unchanged")]
    public async Task ReadyWithoutRedis()
    {
        _rollout.Ready.Should().BeTrue(_rollout.Code);
        _rollout.BackfilledCaptures.Should().Be(0);
        (await Read(db => db.ElectionEntitlementCaptures.AsNoTracking().SingleAsync(c => c.ElectionId == _id))).Should().Be(_originalCapture);
    }

    // Scenario-owned restore corruption only. Production evidence remains append-only.
    // Re-enable both database guards before running the real readiness evaluator.
    private async Task WithRestoredEvidenceFault(Func<DbContext, Task> mutate)
    {
        await using var db = HushVotingLicensingIntegrationHostBuild.CreateFreshDbContext(scenario.Node.Services);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Elections"."ElectionEntitlementCapture" DISABLE TRIGGER "ImmutableCapture";
            ALTER TABLE "Elections"."ElectionIndexCheckpoint" DISABLE TRIGGER "ImmutableIndexCheckpoint";
            """);
        await mutate(db);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Elections"."ElectionEntitlementCapture" ENABLE TRIGGER "ImmutableCapture";
            ALTER TABLE "Elections"."ElectionIndexCheckpoint" ENABLE TRIGGER "ImmutableIndexCheckpoint";
            """);
        await transaction.CommitAsync();
    }

    [When("ordinary database writes attempt to change immutable authorization evidence")]
    public async Task VerifyDatabaseImmutability()
    {
        await using var db = HushVotingLicensingIntegrationHostBuild.CreateFreshDbContext(scenario.Node.Services);
        Func<Task> capture = async () => await db.Set<ElectionEntitlementCapture>().Where(c => c.ElectionId == _id).ExecuteDeleteAsync();
        Func<Task> checkpoint = async () => await db.Set<ElectionIndexCheckpoint>().Where(c => c.BlockHeight == _originalCapture.OpenBlockHeight)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.PolicyVersion, "unsupported"));
        await capture.Should().ThrowAsync<Npgsql.PostgresException>().WithMessage("*immutable*");
        await checkpoint.Should().ThrowAsync<Npgsql.PostgresException>().WithMessage("*immutable*");
        _rollout = await Rollout().EvaluateAsync();
    }

    [Given("a genuinely indexed governed Open whose licence is now expired")]
    public async Task GovernedOpenForRollout()
    {
        await GovernedOpenReady();
        await FinalApprovalAtExpiry(-1);
        _originalCapture = await Read(db => db.ElectionEntitlementCaptures.AsNoTracking().SingleAsync(c => c.ElectionId == _id));
        _originalCapture.GovernedProposalId.Should().Be(_governedProposal);
        scenario.HistoricalBlockClock!.AdvanceTo(_licenceExpiry.AddDays(1));
        var ready = await Rollout().EvaluateAsync();
        ready.Ready.Should().BeTrue(ready.Code);
    }

    [When("an operator attempts to remove the populated completion-checkpoint schema")]
    public async Task IncompatibleRollback()
    {
        await using var db = HushVotingLicensingIntegrationHostBuild.CreateFreshDbContext(scenario.Node.Services);
        Func<Task> rollback = () => db.GetService<IMigrator>().MigrateAsync("20260929000633_Feat018RejectedOpenOutcome");
        await rollback.Should().ThrowAsync<Npgsql.PostgresException>().WithMessage("*election_checkpoint_rollback_refused*");
        _rollout = await Rollout().EvaluateAsync();
    }

    [When("the additive checkpoint migration upgrades a populated prior-schema snapshot")]
    public async Task UpgradePopulatedPriorSchema()
    {
        // Only this isolated fixture: represent the immediately preceding schema, which
        // retained signed blocks and captures but had no durable completion table.
        await WithRestoredEvidenceFault(async db => await db.Set<ElectionIndexCheckpoint>().ExecuteDeleteAsync());
        await using var db = HushVotingLicensingIntegrationHostBuild.CreateFreshDbContext(scenario.Node.Services);
        var blockCount = await db.Set<HushNode.Blockchain.Storage.Model.BlockchainBlock>().CountAsync();
        await db.GetService<IMigrator>().MigrateAsync("20260929000633_Feat018RejectedOpenOutcome");
        await db.GetService<IMigrator>().MigrateAsync();
        (await db.Set<HushNode.Blockchain.Storage.Model.BlockchainBlock>().CountAsync()).Should().Be(blockCount);
        (await db.Set<ElectionIndexCheckpoint>().CountAsync()).Should().Be(0, "migration cannot manufacture successful indexing");
        _rollout = await Rollout().EvaluateAsync();
    }

    [When("a correctly signed client uses an unsupported election envelope version")]
    public async Task UnsupportedClient()
    {
        var action = new EncryptedElectionActionEnvelope(EncryptedElectionEnvelopeActionTypes.OpenElection,
            JsonSerializer.SerializeToElement(new OpenElectionActionPayload(_owner.SigningPublicKey,
                [ElectionWarningCode.LowAnonymitySet], null, null, null, null)));
        var unsigned = EncryptedElectionEnvelopePayloadHandler.CreateNewV21(_id,
            EncryptKeys.Encrypt(_electionKeys.PrivateKey, _owner.EncryptPublicKey), _electionKeys.PublicKey,
            EncryptKeys.Encrypt(JsonSerializer.Serialize(action), _electionKeys.PublicKey), action.ActionType, action.ActionPayload);
        unsigned = unsigned with { TransactionTimeStamp = new Timestamp(scenario.HistoricalBlockClock!.GetUtcNow().UtcDateTime) };
        var compatible = new SignedTransaction<EncryptedElectionEnvelopePayload>(unsigned,
            new SignatureInfo(_owner.SigningPublicKey, DigitalSignature.SignMessage(unsigned.ToJson(), _owner.SigningPrivateKey)));
        await HushVotingArtifactClient.RegisterAsync(compatible.ToJson());
        var handler = scenario.Node.Services.GetServices<HushShared.Blockchain.TransactionModel.ITransactionContentHandler>()
            .Single(h => h.CanValidate(unsigned.PayloadKind));
        (await Task.Run(() => handler.ValidateAndSign(compatible)).WaitAsync(TimeSpan.FromSeconds(15)))
            .Should().NotBeNull("the same otherwise-valid operation must pass with the supported version");
        unsigned = unsigned with { Payload = unsigned.Payload with { EnvelopeVersion = "unsupported/legacy" } };
        var signed = new SignedTransaction<EncryptedElectionEnvelopePayload>(unsigned,
            new SignatureInfo(_owner.SigningPublicKey, DigitalSignature.SignMessage(unsigned.ToJson(), _owner.SigningPrivateKey)));
        var wire = signed.ToJson();
        await HushVotingArtifactClient.RegisterAsync(wire);
        var result = await scenario.Blockchain.SubmitSignedTransactionAsync(new() { SignedTransaction = wire }, deadline: DateTime.UtcNow.AddSeconds(15));
        result.Successfull.Should().BeFalse();
        result.ValidationCode.Should().Be("election_envelope_decrypt_failed");
        (await Read(db => db.Elections.AsNoTracking().SingleAsync(e => e.ElectionId == _id))).LifecycleState.Should().Be(ElectionLifecycleState.Draft);
        (await Read(db => db.ElectionEntitlementCaptures.CountAsync(c => c.ElectionId == _id))).Should().Be(0);
    }
}
