// EPIC-001 -> FEAT-011 Phase 3 Tasks 3.3/3.4, 3.7/3.8;
// supports FEAT-007 AC-007-071, FEAT-008 AC-008-076, FEAT-009 AC-009-081.
using FluentAssertions;
using Grpc.Core;
using HushNetwork.proto;
using HushVoting.IntegrationTests.Infrastructure;
using Olimpo.KeyDerivation;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.ServerTwins;

[Binding]
[Scope(Tag = "HV-SERVER-TWIN")]
internal sealed class IdentityNodeRestartTwinSteps(HushVotingScenario scenario)
{
    private const string Alias = "Restart identity";
    private HushVotingNodeProcess Node => scenario.NodeProcess ?? throw new InvalidOperationException("No owned process host.");
    private DerivedKeys _keys = null!;
    private string _transaction = "";
    private bool _indexed;
    private int _originalPid;
    private int _restartPending;

    [Given("a real node process has accepted an (unindexed|indexed) identity transaction")]
    public async Task ArrangeAsync(string state)
    {
        (scenario.Node is null && scenario.Page is null && scenario.Context is null).Should().BeTrue();
        scenario.BaseUrl.Should().BeEmpty();
        _indexed = state == "indexed";
        _originalPid = Node.ProcessId;
        _originalPid.Should().NotBe(Environment.ProcessId);
        await Node.ProduceBlockAsync(); // Index normal bootstrap transactions first.
        var words = MnemonicGenerator.GenerateMnemonic();
        _keys = HushVotingTestIdentity.DeriveP01(words);
        await HushVotingArtifactClient.RegisterAsync(words, _keys.SigningPrivateKey, _keys.EncryptPrivateKey,
            _keys.SigningPublicKey, _keys.EncryptPublicKey, Alias);
        _transaction = HushVotingServerIdentity.Sign(_keys, Alias, false);
        await HushVotingArtifactClient.RegisterAsync(_transaction);
        (await Node.StatsAsync(_keys.SigningPublicKey)).Should().Be((0, 0, 0));
        AssertReply(await SubmitAsync(), TransactionStatus.Accepted);
        await Node.WaitForPendingAsync(_keys.SigningPublicKey, 1);
        AssertReply(await SubmitAsync(), TransactionStatus.Pending);
        (await Node.StatsAsync(_keys.SigningPublicKey)).Should().Be((0, 1, 1));
        if (_indexed)
        {
            await Node.ProduceBlockAsync();
            (await Node.StatsAsync(_keys.SigningPublicKey)).Should().Be((1, 0, 0));
        }
    }

    [When("that node process is killed and a new process opens the same database and cache")]
    public async Task RestartAsync()
    {
        await Node.CrashAsync();
        Node.HasExited.Should().BeTrue();
        var dead = await FluentActions.Awaiting(async () => await Node.Identities.GetIdentityAsync(
            new() { PublicSigningAddress = _keys.SigningPublicKey }, deadline: DateTime.UtcNow.AddSeconds(3)))
            .Should().ThrowAsync<RpcException>();
        dead.Which.StatusCode.Should().BeOneOf(StatusCode.Unavailable, StatusCode.DeadlineExceeded);
        await Node.RestartAsync();
        Node.ProcessId.Should().NotBe(_originalPid);
        Node.HasExited.Should().BeFalse();
        // No schema reset, cache flush, direct profile insertion or block occurs here.
        var restarted = await Node.StatsAsync(_keys.SigningPublicKey);
        restarted.Profiles.Should().Be(_indexed ? 1 : 0);
        restarted.Pending.Should().Be(0);
        // Startup can enqueue the node's own bootstrap transactions. Measure
        // exact identity admission separately, and retain the total baseline.
        _restartPending = restarted.Total;
    }

    [Then("exact retry follows persisted identity truth and indexes only one unchanged profile")]
    public async Task RetryAsync()
    {
        AssertReply(await SubmitAsync(), _indexed ? TransactionStatus.AlreadyExists : TransactionStatus.Accepted);
        if (!_indexed) await Node.WaitForPendingAsync(_keys.SigningPublicKey, 1);
        AssertReply(await SubmitAsync(), _indexed ? TransactionStatus.AlreadyExists : TransactionStatus.Pending);
        (await Node.StatsAsync(_keys.SigningPublicKey)).Should().Be((_indexed ? 1 : 0, _indexed ? 0 : 1, _restartPending + (_indexed ? 0 : 1)));
        await Node.ProduceBlockAsync();
        var profile = await Node.Identities.GetIdentityAsync(new() { PublicSigningAddress = _keys.SigningPublicKey },
            deadline: DateTime.UtcNow.AddSeconds(10));
        (profile.Successfull && profile.PublicSigningAddress == _keys.SigningPublicKey
            && profile.PublicEncryptAddress == _keys.EncryptPublicKey && profile.ProfileName == Alias && !profile.IsPublic).Should().BeTrue();
        AssertReply(await SubmitAsync(), TransactionStatus.AlreadyExists);
        (await Node.StatsAsync(_keys.SigningPublicKey)).Should().Be((1, 0, 0));
    }

    private Task<SubmitSignedTransactionReply> SubmitAsync() => Node.Blockchain.SubmitSignedTransactionAsync(
        new() { SignedTransaction = _transaction }, deadline: DateTime.UtcNow.AddSeconds(15)).ResponseAsync;

    private static void AssertReply(SubmitSignedTransactionReply reply, TransactionStatus status)
    {
        reply.Status.Should().Be(status);
        reply.Successfull.Should().BeTrue();
        reply.ValidationCode.Should().BeEmpty();
    }
}
