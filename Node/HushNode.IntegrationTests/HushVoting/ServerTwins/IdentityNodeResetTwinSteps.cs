// EPIC-001 -> FEAT-007 AC-007-071 / FEAT-008 AC-008-076 / FEAT-009 AC-009-081.
// FEAT-011 Phase 3 Tasks 3.3–3.8; backend reset, not browser/vault reconciliation policy.
using System.Text.Json.Nodes;
using FluentAssertions;
using Grpc.Core;
using HushNetwork.proto;
using HushVoting.IntegrationTests.Infrastructure;
using Olimpo.KeyDerivation;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.ServerTwins;

[Binding]
[Scope(Tag = "HV-SERVER-TWIN")]
internal sealed class IdentityNodeResetTwinSteps(HushVotingScenario scenario)
{
    private const string Alias = "Reset identity";
    private HushVotingNodeProcess Node => scenario.NodeProcess ?? throw new InvalidOperationException("No owned node process.");
    private DerivedKeys _retainedKeys = null!;
    private string _original = "";
    private string _replacement = "";
    private bool _public;
    private int _originalPid;

    [Given("a real node process has indexed and cached a (P01 private|P02 public) identity")]
    public async Task IndexedAsync(string kind)
    {
        (scenario.Page is null && scenario.Context is null && scenario.Node is null).Should().BeTrue();
        scenario.BaseUrl.Should().BeEmpty();
        _public = kind == "P02 public";
        _originalPid = Node.ProcessId;
        await Node.ProduceBlockAsync();
        var words = MnemonicGenerator.GenerateMnemonic();
        _retainedKeys = _public ? DeterministicKeyGenerator.DeriveKeys(words) : HushVotingTestIdentity.DeriveP01(words);
        await HushVotingArtifactClient.RegisterAsync(words, _retainedKeys.SigningPrivateKey, _retainedKeys.EncryptPrivateKey,
            _retainedKeys.SigningPublicKey, _retainedKeys.EncryptPublicKey, Alias);
        _original = HushVotingServerIdentity.Sign(_retainedKeys, Alias, _public, signCompact: !_public);
        await HushVotingArtifactClient.RegisterAsync(_original);
        await AbsentAsync();
        // The original backend acceptance criteria also require pre-admission
        // authenticity failures. Each real rejected RPC must leave no reservation.
        foreach (var defect in new[] { "forged", "altered", "mismatch" })
        {
            var invalid = JsonNode.Parse(_original)!;
            if (defect == "forged") invalid["UserSignature"]!["Signature"] = Convert.ToBase64String(new byte[64]);
            else if (defect == "altered") invalid["Payload"]!["IdentityAlias"] = "Reset identitx"; // Same UTF-8 length.
            else invalid["UserSignature"]!["Signatory"] = _retainedKeys.EncryptPublicKey;
            var bytes = invalid.ToJsonString();
            await HushVotingArtifactClient.RegisterAsync(bytes);
            var rejection = await SubmitAsync(bytes);
            rejection.Successfull.Should().BeFalse();
            rejection.Status.Should().Be(TransactionStatus.Rejected);
            rejection.ValidationCode.Should().Be(defect == "mismatch" ? "FULL_IDENTITY_SIGNATORY_MISMATCH" : "FULL_IDENTITY_INVALID_SIGNATURE");
            (await Node.StatsAsync(_retainedKeys.SigningPublicKey)).Should().Be((0, 0, 0));
            await Node.ProduceBlockAsync();
            await AbsentAsync();
        }
        AssertReply(await SubmitAsync(_original), TransactionStatus.Accepted);
        await Node.WaitForPendingAsync(_retainedKeys.SigningPublicKey, 1);
        await Node.ProduceBlockAsync();
        await ExactAsync(); // Populate the real identity cache.
        await ExactAsync();
        (await Node.HasCachedIdentityAsync(_retainedKeys.SigningPublicKey)).Should().BeTrue();
        AssertReply(await SubmitAsync(_original), TransactionStatus.AlreadyExists);
        (await Node.StatsAsync(_retainedKeys.SigningPublicKey)).Should().Be((1, 0, 0));
    }

    [When("that node is stopped and its owned chain database and Redis are reset before a new node starts")]
    public async Task ResetAsync()
    {
        await Node.CrashAsync();
        Node.HasExited.Should().BeTrue();
        var dead = await FluentActions.Awaiting(async () => await LookupAsync(3)).Should().ThrowAsync<RpcException>();
        dead.Which.StatusCode.Should().BeOneOf(StatusCode.Unavailable, StatusCode.DeadlineExceeded);
        await scenario.ResetStoppedNodeChainAsync();
        Node.ProcessId.Should().NotBe(_originalPid);
        await Node.ProduceBlockAsync(); // Index only the new node's normal bootstrap.
        (await Node.StatsAsync(_retainedKeys.SigningPublicKey)).Should().Be((0, 0, 0));
        (await Node.HasCachedIdentityAsync(_retainedKeys.SigningPublicKey)).Should().BeFalse();
        await AbsentAsync(); // A former positive Redis result cannot survive the reset.
        await AbsentAsync();
    }

    [Then("forged recreation is rejected and one fresh same-key transaction restores the exact profile")]
    public async Task RecreateAsync()
    {
        // Retained fixture keys and last verified metadata are reused. No derivation after reset.
        _replacement = HushVotingServerIdentity.Sign(_retainedKeys, Alias, _public, signCompact: !_public);
        await HushVotingArtifactClient.RegisterAsync(_replacement);
        var original = JsonNode.Parse(_original)!;
        var replacement = JsonNode.Parse(_replacement)!;
        (original["TransactionId"]!.ToJsonString()
            != replacement["TransactionId"]!.ToJsonString()).Should().BeTrue();
        (_replacement != _original).Should().BeTrue();
        var forged = replacement.DeepClone();
        forged["UserSignature"]!["Signature"] = Convert.ToBase64String(new byte[64]);
        var rejectedBytes = forged.ToJsonString();
        await HushVotingArtifactClient.RegisterAsync(rejectedBytes);
        var rejected = await SubmitAsync(rejectedBytes);
        rejected.Successfull.Should().BeFalse();
        rejected.Status.Should().Be(TransactionStatus.Rejected);
        rejected.ValidationCode.Should().Be("FULL_IDENTITY_INVALID_SIGNATURE");
        await Node.ProduceBlockAsync();
        await AbsentAsync();
        (await Node.StatsAsync(_retainedKeys.SigningPublicKey)).Should().Be((0, 0, 0));

        AssertReply(await SubmitAsync(_replacement), TransactionStatus.Accepted);
        await Node.WaitForPendingAsync(_retainedKeys.SigningPublicKey, 1);
        AssertReply(await SubmitAsync(_replacement), TransactionStatus.Pending);
        await AbsentAsync();
        (await Node.StatsAsync(_retainedKeys.SigningPublicKey)).Should().Be((0, 1, 1));
        await Node.ProduceBlockAsync();
        await ExactAsync();
        AssertReply(await SubmitAsync(_replacement), TransactionStatus.AlreadyExists);
        AssertReply(await SubmitAsync(_original), TransactionStatus.AlreadyExists);
        (await Node.StatsAsync(_retainedKeys.SigningPublicKey)).Should().Be((1, 0, 0));
    }

    private async Task AbsentAsync()
    {
        var reply = await LookupAsync();
        (!reply.Successfull && string.IsNullOrEmpty(reply.PublicSigningAddress)
            && string.IsNullOrEmpty(reply.PublicEncryptAddress)).Should().BeTrue();
    }

    private async Task ExactAsync()
    {
        var reply = await LookupAsync();
        (reply.Successfull && reply.ProfileName == Alias && reply.IsPublic == _public
            && reply.PublicSigningAddress == _retainedKeys.SigningPublicKey
            && reply.PublicEncryptAddress == _retainedKeys.EncryptPublicKey).Should().BeTrue();
    }

    private Task<GetIdentityReply> LookupAsync(int seconds = 10) => Node.Identities.GetIdentityAsync(
        new() { PublicSigningAddress = _retainedKeys.SigningPublicKey }, deadline: DateTime.UtcNow.AddSeconds(seconds)).ResponseAsync;
    private Task<SubmitSignedTransactionReply> SubmitAsync(string signed) => Node.Blockchain.SubmitSignedTransactionAsync(
        new() { SignedTransaction = signed }, deadline: DateTime.UtcNow.AddSeconds(15)).ResponseAsync;
    private static void AssertReply(SubmitSignedTransactionReply reply, TransactionStatus status)
    {
        reply.Successfull.Should().BeTrue();
        reply.Status.Should().Be(status);
        reply.ValidationCode.Should().BeEmpty();
    }
}
