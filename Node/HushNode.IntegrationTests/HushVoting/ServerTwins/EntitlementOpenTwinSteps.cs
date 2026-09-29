using System.Text.Json;
using FluentAssertions;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushNode.Events;
using HushNode.Indexing;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.HushVoting.Licence.Transactions;
using HushShared.HushVoting.Licensing.Model;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Olimpo;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.ServerTwins;

internal sealed partial class EntitlementEnforcementTwinSteps
{
    private EncryptKeys _electionKeys = null!;
    private DateTime _licenceExpiry;
    private int _openOffset;
    private Guid _openTransaction;
    private readonly OpenBlocks _openBlocks = new();
    private bool _upgradeFirst;
    private bool _rollbackObserved;
    private int _orderedOpenPosition;
    private string _restartCaptureDigest = "";

    [When("the recorded Open survives an owned node-process crash and restart")]
    public async Task CrashAndRestartOpen()
    {
        await OpenAtOffset(-1);
        var capture = await Read(db => db.ElectionEntitlementCaptures.AsNoTracking().SingleAsync(c => c.ElectionId == _id));
        _restartCaptureDigest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(capture)));
        await scenario.MoveOwnedStorageToNodeProcessAsync();
        (await scenario.NodeProcess!.CaptureAsync(_id.ToString())).Digest.Should().Be(_restartCaptureDigest);
        var previousPid = scenario.NodeProcess.ProcessId;
        await scenario.NodeProcess.CrashAsync();
        scenario.NodeProcess.HasExited.Should().BeTrue();
        await scenario.NodeProcess.RestartAsync();
        scenario.NodeProcess.ProcessId.Should().NotBe(previousPid);
    }

    [Then("the new node process reads the identical durable Open capture and frozen roster")]
    public async Task VerifyRestartedOpen()
    {
        var result = await scenario.NodeProcess!.CaptureAsync(_id.ToString());
        result.Digest.Should().Be(_restartCaptureDigest);
        result.Supported.Should().BeTrue();
        result.State.Should().Be("Open");
        result.Frozen.Should().Be(1);
        result.Boundaries.Should().Be(1);
    }

    [When("a cap-raising licence executes (.*) Open in the same block")]
    public async Task SameBlockOrder(string order)
    {
        (await Import(100, "additional")).IsSuccess.Should().BeTrue();
        scenario.HistoricalBlockClock!.AdvanceTo(_licenceExpiry.AddSeconds(1));
        var baseline = await HushVotingServerLicence.SubmitAsync(scenario, _owner, new(
            HushVotingLicenceTransitionIntent.BaselineFree, HushVotingLicencePlanId.DirectFree.Value,
            HushVotingLicenceCatalogueVersion.V1Value));
        var upgrade = await HushVotingServerLicence.SubmitAsync(scenario, _owner, new(
            HushVotingLicenceTransitionIntent.ConfirmedUpgrade, HushVotingLicencePlanId.Veritas500.Value,
            HushVotingLicenceCatalogueVersion.V1Value, baseline, HushVotingLicencePlanId.DirectFree.Value), produceBlock: false);
        var open = await SignAction(EncryptedElectionEnvelopeActionTypes.OpenElection,
            new OpenElectionActionPayload(_owner.SigningPublicKey, [ElectionWarningCode.LowAnonymitySet], null, null, null, null));
        _openTransaction = open.Id;
        _upgradeFirst = order == "before";
        using var waiter = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(20));
        var reply = await scenario.Blockchain.SubmitSignedTransactionAsync(new() { SignedTransaction = open.Wire }, deadline: DateTime.UtcNow.AddSeconds(15));
        reply.Successfull.Should().BeTrue();
        await waiter.WaitAsync();
        scenario.Faults.NextBlockTransactionOrder = _upgradeFirst ? [upgrade, open.Id] : [open.Id, upgrade];
        await scenario.Blocks.ProduceBlockAsync();
        var block = _openBlocks.Values.Single(b => b.Block.Transactions.Any(t => t.TransactionId.Value == open.Id));
        // The real producer also prefixes its block-reward transaction. Preserve full-block
        // positions while checking the relative order of the two protected user operations.
        block.Block.Transactions.Select(t => t.TransactionId.Value).Where(id => id == upgrade || id == open.Id).Should().Equal(
            _upgradeFirst ? new[] { upgrade, open.Id } : new[] { open.Id, upgrade });
        _orderedOpenPosition = Array.FindIndex(block.Block.Transactions.ToArray(), t => t.TransactionId.Value == open.Id);
    }

    [Then("only the earlier licence can authorize that Open")]
    public async Task VerifySameBlockOrder()
    {
        var election = await Read(db => db.Elections.AsNoTracking().SingleAsync(e => e.ElectionId == _id));
        var capture = await Read(db => db.ElectionEntitlementCaptures.AsNoTracking().SingleOrDefaultAsync(e => e.ElectionId == _id));
        if (_upgradeFirst)
        {
            election.LifecycleState.Should().Be(ElectionLifecycleState.Open);
            capture!.EligibleVoterCap.Should().Be(500);
            capture.FrozenEligibleVoterCount.Should().Be(101);
            capture.OpenTransactionPosition.Should().Be(_orderedOpenPosition);
        }
        else
        {
            election.LifecycleState.Should().Be(ElectionLifecycleState.Draft);
            capture.Should().BeNull();
            (await Roster()).Should().OnlyContain(r => !r.WasPresentAtOpen);
            var retained = _openBlocks.Values.Single(b => b.Block.Transactions.Any(t => t.TransactionId.Value == _openTransaction));
            await ((IndexingDispatcherService)scenario.Node.Services.GetRequiredService<IIndexingDispatcherService>()).HandleAsync(retained);
            (await Read(db => db.Elections.AsNoTracking().SingleAsync(e => e.ElectionId == _id))).LifecycleState
                .Should().Be(ElectionLifecycleState.Draft, "replaying a rejected earlier Open must not use the later upgrade retroactively");
            (await Read(db => db.ElectionEntitlementCaptures.CountAsync(c => c.ElectionId == _id))).Should().Be(0);
        }
    }

    [When("PostgreSQL refuses its Open capture and that committed block is retried")]
    public async Task CaptureWriteFault()
    {
        var completion = new OpenCompletions();
        scenario.Node.Services.GetRequiredService<IEventAggregator>().Subscribe(completion);
        await Read(db => db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION "Elections".hv018_capture_fault() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'controlled capture failure' USING ERRCODE='23514'; END $$;
            CREATE TRIGGER hv018_capture_fault BEFORE INSERT ON "Elections"."ElectionEntitlementCapture"
            FOR EACH ROW EXECUTE FUNCTION "Elections".hv018_capture_fault();
            """));
        try
        {
            Func<Task> attempt = () => OpenAtOffset(-1);
            await attempt.Should().ThrowAsync<TimeoutException>();
            completion.Count.Should().Be(0, "a storage fault must not publish successful indexing completion");
            var election = await Read(db => db.Elections.AsNoTracking().SingleAsync(e => e.ElectionId == _id));
            election.LifecycleState.Should().Be(ElectionLifecycleState.Draft);
            election.OpenArtifactId.Should().BeNull();
            (await Read(db => db.ElectionEntitlementCaptures.CountAsync(c => c.ElectionId == _id))).Should().Be(0);
            (await Read(db => db.ElectionBoundaryArtifacts.CountAsync(c => c.ElectionId == _id))).Should().Be(0);
            (await Roster()).Should().OnlyContain(r => !r.WasPresentAtOpen);
            _rollbackObserved = true;
        }
        finally
        {
            await Read(db => db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER hv018_capture_fault ON "Elections"."ElectionEntitlementCapture";
                DROP FUNCTION "Elections".hv018_capture_fault();
                """));
        }
        var block = _openBlocks.Values.Single(b => b.Block.Transactions.Any(t => t.TransactionId.Value == _openTransaction));
        await ((IndexingDispatcherService)scenario.Node.Services.GetRequiredService<IIndexingDispatcherService>()).HandleAsync(block);
        completion.Count.Should().Be(1);
    }

    [Then("the fault leaves no partial Open and retry persists one consistent capture")]
    public async Task VerifyCaptureWriteFault()
    {
        _rollbackObserved.Should().BeTrue();
        await VerifyActualOpen();
        (await Read(db => db.ElectionBoundaryArtifacts.CountAsync(c => c.ElectionId == _id))).Should().Be(1);
    }

    private sealed class OpenCompletions : IHandleAsync<BlockIndexCompletedEvent>
    {
        public int Count;
        public Task HandleAsync(BlockIndexCompletedEvent message) { Count++; return Task.CompletedTask; }
    }

    private async Task<(Guid Id, string Wire)> SignAction(string actionType, object payload, Olimpo.KeyDerivation.DerivedKeys? signer = null)
    {
        var actor = signer ?? _owner;
        var action = new EncryptedElectionActionEnvelope(actionType, JsonSerializer.SerializeToElement(payload));
        var unsigned = EncryptedElectionEnvelopePayloadHandler.CreateNewV21(_id,
            EncryptKeys.Encrypt(_electionKeys.PrivateKey, actor.EncryptPublicKey), _electionKeys.PublicKey,
            EncryptKeys.Encrypt(JsonSerializer.Serialize(action), _electionKeys.PublicKey), actionType, action.ActionPayload)
            with { TransactionTimeStamp = new Timestamp(scenario.HistoricalBlockClock!.GetUtcNow().UtcDateTime) };
        var signed = new SignedTransaction<EncryptedElectionEnvelopePayload>(unsigned,
            new SignatureInfo(actor.SigningPublicKey, DigitalSignature.SignMessage(unsigned.ToJson(), actor.SigningPrivateKey)));
        var wire = signed.ToJson();
        await HushVotingArtifactClient.RegisterAsync(wire);
        return (signed.TransactionId.Value, wire);
    }

    private async Task SubmitAction(string wire)
    {
        using var waiter = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(20));
        var reply = await scenario.Blockchain.SubmitSignedTransactionAsync(new() { SignedTransaction = wire }, deadline: DateTime.UtcNow.AddSeconds(15));
        reply.Successfull.Should().BeTrue("the actual public validator must admit the owned signed action: " + reply.ValidationCode);
        await waiter.WaitAsync();
        await scenario.Blocks.ProduceBlockAsync();
    }

    [Given("an encrypted Open-ready election with an annual owner licence")]
    public async Task EncryptedOpenReady()
    {
        await Prepare("hushvoting.veritas.500");
        // The licence is indexed by real signed blocks. A separate encrypted creation proves
        // the production v2.1 action path used by actual Open; no legacy steps are shared.
        _id = ElectionId.NewElectionId;
        _electionKeys = new EncryptKeys();
        await HushVotingArtifactClient.RegisterAsync(_electionKeys.PrivateKey, _electionKeys.PublicKey);
        _draft = _draft with { AcknowledgedWarningCodes = [ElectionWarningCode.LowAnonymitySet] };
        var creation = await SignAction(EncryptedElectionEnvelopeActionTypes.CreateDraft,
            new CreateElectionDraftActionPayload(_owner.SigningPublicKey, "Owned encrypted creation", _draft));
        await SubmitAction(creation.Wire);
        (await Import(1)).IsSuccess.Should().BeTrue();
        (await Link("v0")).IsSuccess.Should().BeTrue();
        var readiness = await Service.EvaluateOpenReadinessAsync(new(_id, [ElectionWarningCode.LowAnonymitySet]));
        readiness.IsReadyToOpen.Should().BeTrue(string.Join("; ", readiness.ValidationErrors));
        AuthenticatedIdentitySubject.TryCreate(LicencePersistenceVocabulary.SubjectTypeIdentity,
            _owner.SigningPublicKey, 0, out var subject, out _).Should().BeTrue();
        var terms = await scenario.Node.Services.GetRequiredService<ILicenceIndexedProjectionReader>().ResolveEffectiveAsync(
            subject!, scenario.HistoricalBlockClock!.GetUtcNow().UtcDateTime, default);
        _licenceExpiry = terms.Entitlement!.ExpiresAtUtc!.Value;
        scenario.Node.Services.GetRequiredService<IEventAggregator>().Subscribe(_openBlocks);
    }

    [When("its signed Open executes (.*) seconds from licence expiry")]
    public async Task OpenAtOffset(int seconds)
    {
        _openOffset = seconds;
        // Signed and admitted while still active; the execution clock alone crosses expiry.
        var open = await SignAction(EncryptedElectionEnvelopeActionTypes.OpenElection,
            new OpenElectionActionPayload(_owner.SigningPublicKey, [ElectionWarningCode.LowAnonymitySet], null, null, null, null));
        _openTransaction = open.Id;
        using var waiter = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(20));
        var reply = await scenario.Blockchain.SubmitSignedTransactionAsync(new() { SignedTransaction = open.Wire }, deadline: DateTime.UtcNow.AddSeconds(15));
        reply.Successfull.Should().BeTrue("pre-expiry admission must not reserve execution rights: " + reply.ValidationCode);
        await waiter.WaitAsync();
        scenario.HistoricalBlockClock!.AdvanceTo(_licenceExpiry.AddSeconds(seconds));
        await scenario.Blocks.ProduceBlockAsync();
    }

    [Then("its persisted Open outcome matches the upper-exclusive expiry boundary")]
    public async Task VerifyActualOpen()
    {
        var election = await Read(db => db.Elections.AsNoTracking().SingleAsync(e => e.ElectionId == _id));
        var capture = await Read(db => db.ElectionEntitlementCaptures.AsNoTracking().SingleOrDefaultAsync(e => e.ElectionId == _id));
        if (_openOffset >= 0)
        {
            election.LifecycleState.Should().Be(ElectionLifecycleState.Draft);
            capture.Should().BeNull();
            election.OpenArtifactId.Should().BeNull();
            (await Roster()).Should().OnlyContain(r => !r.WasPresentAtOpen);
            return;
        }
        election.LifecycleState.Should().Be(ElectionLifecycleState.Open);
        capture.Should().NotBeNull();
        capture!.HasSupportedSemantics().Should().BeTrue();
        capture.OpenTransactionId.Should().Be(_openTransaction);
        capture.OpenBlockTimeUtc.Should().Be(_licenceExpiry.AddSeconds(_openOffset));
        capture.FrozenEligibleVoterCount.Should().Be(1);
        capture.EligibleVoterCap.Should().Be(500);
        var block = _openBlocks.Values.Single(b => b.Block.Transactions.Any(t => t.TransactionId.Value == _openTransaction));
        capture.OpenBlockId.Should().Be(block.Block.BlockId.Value);
        capture.OpenBlockHeight.Should().Be(block.Block.BlockIndex.Value);
        capture.OpenTransactionPosition.Should().Be(Array.FindIndex(block.Block.Transactions.ToArray(), t => t.TransactionId.Value == _openTransaction));
        // Replay actual retained validated transactions through the real dispatcher, at a later wall clock.
        scenario.HistoricalBlockClock!.AdvanceTo(_licenceExpiry.AddDays(1));
        await ((IndexingDispatcherService)scenario.Node.Services.GetRequiredService<IIndexingDispatcherService>()).HandleAsync(block);
        (await Read(db => db.ElectionEntitlementCaptures.AsNoTracking().SingleAsync(e => e.ElectionId == _id))).Should().Be(capture);
        (await Roster()).Should().OnlyContain(r => r.WasPresentAtOpen);
    }

    private sealed class OpenBlocks : IHandleAsync<BlockCreatedEvent>
    {
        public List<BlockCreatedEvent> Values { get; } = [];
        public Task HandleAsync(BlockCreatedEvent message) { Values.Add(message); return Task.CompletedTask; }
    }
}
