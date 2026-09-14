// EPIC-001 -> FEAT-011 Phase 2 Tasks 2.7/2.8, Phase 3 Tasks 3.3/3.4,
// 3.5/3.6, 3.7/3.8. Supports the FEAT-007/008/009 backend matrices.
using FluentAssertions;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.Identity.Storage;
using HushNode.MemPool;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Olimpo.KeyDerivation;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.ServerTwins;

[Binding]
[Scope(Tag = "HV-SERVER-TWIN")]
internal sealed class IdentityAdmissionTwinSteps(HushVotingScenario scenario)
{
    private const string FirstAlias = "Admission Alice";
    private const string SecondAlias = "Admission Other";
    private DerivedKeys _keys = null!;
    private string[] _requests = [];
    private SubmitSignedTransactionReply[] _replies = [];
    private string _winner = "";
    private string _winnerAlias = FirstAlias;
    private bool _conflicting;
    private bool _alreadyIndexed;

    [Given("one unregistered identity has (exact retransmissions|two competing signed profiles) for real admission")]
    public async Task ArrangeAsync(string arrangement)
    {
        (scenario.Page is null && scenario.Context is null).Should().BeTrue();
        scenario.BaseUrl.Should().BeEmpty();
        // Index the node's own bootstrap transactions before measuring the
        // controlled identity's admission effects; never clear the mempool.
        await scenario.Blocks.ProduceBlockAsync();
        var words = MnemonicGenerator.GenerateMnemonic();
        _keys = HushVotingTestIdentity.DeriveP01(words);
        await HushVotingArtifactClient.RegisterAsync(words, _keys.SigningPrivateKey, _keys.EncryptPrivateKey,
            _keys.SigningPublicKey, _keys.EncryptPublicKey, FirstAlias, SecondAlias);
        _winner = HushVotingServerIdentity.Sign(_keys, FirstAlias, false);
        _conflicting = arrangement == "two competing signed profiles";
        _requests = _conflicting
            ? [_winner, HushVotingServerIdentity.Sign(_keys, SecondAlias, false)]
            : Enumerable.Repeat(_winner, 8).ToArray();
        foreach (var request in _requests) await HushVotingArtifactClient.RegisterAsync(request);
        PendingCount().Should().Be(0);
        (await ProfileCountAsync()).Should().Be(0);
    }

    [When("every competing RPC reaches a barrier before real admission is released")]
    public async Task ConcurrentAsync()
    {
        scenario.Faults.HoldSubmissionBatch(_requests.Length);
        Task<SubmitSignedTransactionReply>[] pending = [];
        using var admitted = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        try
        {
            pending = _requests.Select(SubmitAsync).ToArray();
            await scenario.Faults.SubmissionBatchArrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
            scenario.Faults.SubmittedTransactions.Count.Should().Be(_requests.Length);
            scenario.Faults.Submissions.Should().BeEmpty();
            PendingCount().Should().Be(0);
        }
        finally { scenario.Faults.ReleaseSubmissionBatch(); }
        _replies = await Task.WhenAll(pending);
        await admitted.WaitAsync();
    }

    [When("the real mempool retains that exact identity through three minutes without block production")]
    public async Task WaitWithoutConfirmationAsync()
    {
        using (var admitted = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30)))
        {
            var accepted = await SubmitAsync(_winner);
            (accepted.Successfull && accepted.Status == TransactionStatus.Accepted && accepted.ValidationCode.Length == 0).Should().BeTrue();
            await admitted.WaitAsync();
        }
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        for (var interval = 0; interval <= 6; interval++)
        {
            PendingCount().Should().Be(1);
            (await ProfileCountAsync()).Should().Be(0);
            var lookup = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = _keys.SigningPublicKey },
                deadline: DateTime.UtcNow.AddSeconds(10));
            lookup.Successfull.Should().BeFalse("mempool waiting is not indexed identity confirmation");
            scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
            if (interval < 6) await Task.Delay(TimeSpan.FromSeconds(30));
        }
        (elapsed.Elapsed >= TimeSpan.FromMinutes(3)).Should().BeTrue();
        var retry = await SubmitAsync(_winner);
        (retry.Successfull && retry.Status == TransactionStatus.Pending && retry.ValidationCode.Length == 0).Should().BeTrue();
        PendingCount().Should().Be(1);
        (await ProfileCountAsync()).Should().Be(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        scenario.Faults.SubmittedTransactions.All(request => request == _winner).Should().BeTrue();
    }

    [Then("the node admits exactly one transaction and preserves typed duplicate or conflict outcomes")]
    public void Outcomes()
    {
        _replies.Count(reply => reply.Status == TransactionStatus.Accepted).Should().Be(1);
        var winner = Array.FindIndex(_replies, reply => reply.Status == TransactionStatus.Accepted);
        _winner = _requests[winner];
        _winnerAlias = _conflicting && winner == 1 ? SecondAlias : FirstAlias;
        _replies[winner].Successfull.Should().BeTrue();
        _replies[winner].ValidationCode.Should().BeEmpty();
        if (_conflicting)
        {
            var rejected = _replies[1 - winner];
            rejected.Status.Should().Be(TransactionStatus.Rejected);
            rejected.Successfull.Should().BeFalse();
            rejected.ValidationCode.Should().Be("FULL_IDENTITY_CONFLICT");
        }
        else
        {
            _replies.Count(reply => reply.Status == TransactionStatus.Pending).Should().Be(7);
            _replies.All(reply => reply.Successfull && reply.ValidationCode.Length == 0).Should().BeTrue();
            scenario.Faults.SubmittedTransactions.All(request => request == _winner).Should().BeTrue();
        }
        PendingCount().Should().Be(1);
    }

    [When("an accepted submission response is lost and the exact retry arrives (before|after) indexing")]
    public async Task LostResponseAsync(string boundary)
    {
        scenario.Faults.DropNextSubmissionResponse = true;
        using (var admitted = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30)))
        {
            try
            {
                var error = await FluentActions.Awaiting(() => SubmitAsync(_winner)).Should().ThrowAsync<RpcException>();
                error.Which.StatusCode.Should().Be(StatusCode.Unavailable);
                await admitted.WaitAsync();
            }
            finally { scenario.Faults.DropNextSubmissionResponse = false; }
        }
        scenario.Faults.DroppedSubmissionResponses.Should().Be(1);
        scenario.Faults.Submissions.Single().Status.Should().Be(TransactionStatus.Accepted);
        PendingCount().Should().Be(1);
        (await ProfileCountAsync()).Should().Be(0);
        _alreadyIndexed = boundary == "after";
        if (_alreadyIndexed) await scenario.Blocks.ProduceBlockAsync();
        var retry = await SubmitAsync(_winner);
        retry.Successfull.Should().BeTrue();
        retry.Status.Should().Be(_alreadyIndexed ? TransactionStatus.AlreadyExists : TransactionStatus.Pending);
        retry.ValidationCode.Should().BeEmpty();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        scenario.Faults.SubmittedTransactions.All(request => request == _winner).Should().BeTrue();
        PendingCount().Should().Be(_alreadyIndexed ? 0 : 1);
    }

    [Then("one indexed profile retains the winning exact pair and later retries add no mempool entries")]
    public async Task IndexedAsync()
    {
        if (!_alreadyIndexed)
        {
            (await ProfileCountAsync()).Should().Be(0);
            await scenario.Blocks.ProduceBlockAsync();
        }
        (await ProfileCountAsync()).Should().Be(1);
        var lookup = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = _keys.SigningPublicKey },
            deadline: DateTime.UtcNow.AddSeconds(10));
        (lookup.Successfull && lookup.PublicSigningAddress == _keys.SigningPublicKey
            && lookup.PublicEncryptAddress == _keys.EncryptPublicKey && lookup.ProfileName == _winnerAlias && !lookup.IsPublic).Should().BeTrue();
        foreach (var request in _requests.Distinct())
        {
            var retry = await SubmitAsync(request);
            retry.Successfull.Should().BeTrue();
            retry.Status.Should().Be(TransactionStatus.AlreadyExists);
            retry.ValidationCode.Should().BeEmpty();
        }
        PendingCount().Should().Be(0);
        (await ProfileCountAsync()).Should().Be(1);
        using var scope = scenario.Node.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Profiles
            .AnyAsync(profile => profile.PublicSigningAddress == _keys.SigningPublicKey && profile.Alias == _winnerAlias)).Should().BeTrue();
    }

    private Task<SubmitSignedTransactionReply> SubmitAsync(string request) => scenario.Blockchain.SubmitSignedTransactionAsync(
        new() { SignedTransaction = request }, deadline: DateTime.UtcNow.AddSeconds(25)).ResponseAsync;

    private int PendingCount() => scenario.Node.Services.GetRequiredService<IMemPoolService>().PeekPendingValidatedTransactions().Count();

    private async Task<int> ProfileCountAsync()
    {
        using var scope = scenario.Node.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Profiles
            .CountAsync(profile => profile.PublicSigningAddress == _keys.SigningPublicKey);
    }
}
