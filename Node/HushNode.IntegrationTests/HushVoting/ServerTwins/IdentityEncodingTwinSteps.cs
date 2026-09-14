// EPIC-001 -> FEAT-008 AC-008-076 / FEAT-009 AC-009-081.
// FEAT-001 exact encoded candidate contract; FEAT-011 Phase 3 Tasks 3.1/3.2,
// 3.7/3.8. Backend FEAT evidence, not browser/EPIC acceptance.
using System.Security.Cryptography;
using System.Text.Json;
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
internal sealed class IdentityEncodingTwinSteps(HushVotingScenario scenario)
{
    private const string Alias = "Encoding Alice";
    private DerivedKeys _keys = null!;
    private string _signed = "";

    [Given("an unregistered (P01|P02) identity uses an approved (compact|DER) signature")]
    public async Task PrepareAsync(string producer, string signature)
    {
        (scenario.Page is null && scenario.Context is null).Should().BeTrue();
        scenario.BaseUrl.Should().BeEmpty();
        await scenario.Blocks.ProduceBlockAsync();
        var words = MnemonicGenerator.GenerateMnemonic();
        _keys = producer == "P01" ? HushVotingTestIdentity.DeriveP01(words) : DeterministicKeyGenerator.DeriveKeys(words);
        await RegisterSecretsAsync(words, _keys);
        _keys.SigningPublicKey.Length.Should().Be(producer == "P01" ? 66 : 130);
        _keys.EncryptPublicKey.Length.Should().Be(producer == "P01" ? 66 : 130);
        _signed = HushVotingServerIdentity.Sign(_keys, Alias, false, signCompact: signature == "compact");
        await HushVotingArtifactClient.RegisterAsync(_signed);
        using var json = JsonDocument.Parse(_signed);
        var encoded = json.RootElement.GetProperty("UserSignature").GetProperty("Signature").GetString()!;
        if (signature == "compact") Convert.FromBase64String(encoded).Length.Should().Be(64);
        else Convert.FromHexString(encoded)[0].Should().Be(0x30);
        await AbsentAsync(_keys.SigningPublicKey);
        await EffectsAsync(_keys.SigningPublicKey, 0, 0);
    }

    [When("the encoded identity is admitted and its exact bytes are retried before indexing")]
    public async Task AdmitAsync()
    {
        using var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(20));
        AssertReply(await SubmitAsync(_signed), TransactionStatus.Accepted);
        await received.WaitAsync();
        AssertReply(await SubmitAsync(_signed), TransactionStatus.Pending);
        await EffectsAsync(_keys.SigningPublicKey, 0, 1);
        await AbsentAsync(_keys.SigningPublicKey);
    }

    [Then("real indexing preserves the exact encoded pair and a duplicate adds no profile or transaction")]
    public async Task IndexedAsync()
    {
        await scenario.Blocks.ProduceBlockAsync();
        await ExactAsync(_keys, Alias);
        AssertReply(await SubmitAsync(_signed), TransactionStatus.AlreadyExists);
        await EffectsAsync(_keys.SigningPublicKey, 1, 0);
    }

    [Then("the uncompressed encoding of the same P01 pair remains a separately registered exact identity")]
    public async Task SamePointAsync()
    {
        await IndexedAsync();
        var expanded = new DerivedKeys(Expand(_keys.SigningPrivateKey), _keys.SigningPrivateKey,
            Expand(_keys.EncryptPrivateKey), _keys.EncryptPrivateKey);
        await RegisterSecretsAsync(Alias, expanded);
        (expanded.SigningPublicKey != _keys.SigningPublicKey && expanded.EncryptPublicKey != _keys.EncryptPublicKey).Should().BeTrue();
        // Real public points derived from the same private scalars, not a different P02 derivation.
        await AbsentAsync(expanded.SigningPublicKey);
        await EffectsAsync(expanded.SigningPublicKey, 0, 0);
        var signed = HushVotingServerIdentity.Sign(expanded, "Encoding Other", false, signCompact: false);
        await HushVotingArtifactClient.RegisterAsync(signed, "Encoding Other");
        using (var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(20)))
        {
            AssertReply(await SubmitAsync(signed), TransactionStatus.Accepted);
            await received.WaitAsync();
        }
        AssertReply(await SubmitAsync(signed), TransactionStatus.Pending);
        await EffectsAsync(expanded.SigningPublicKey, 0, 1);
        await scenario.Blocks.ProduceBlockAsync();
        await ExactAsync(_keys, Alias);
        await ExactAsync(expanded, "Encoding Other");
        AssertReply(await SubmitAsync(_signed), TransactionStatus.AlreadyExists);
        AssertReply(await SubmitAsync(signed), TransactionStatus.AlreadyExists);
        await EffectsAsync(_keys.SigningPublicKey, 1, 0);
        await EffectsAsync(expanded.SigningPublicKey, 1, 0);
    }

    [When("invalid lookup bounds are rejected over RPC while a valid absent identity stays absent")]
    public async Task BoundsAsync()
    {
        var cases = new[] { "", "   ", "not-hex-at-all", "12345", "03", new string('g', 66),
            new string('a', 65), new string('a', 67), new string('a', 129), new string('a', 131) };
        foreach (var address in cases)
        {
            Func<Task> lookup = async () => await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = address },
                deadline: DateTime.UtcNow.AddSeconds(5));
            (await lookup.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
            await AbsentAsync(_keys.SigningPublicKey);
            await EffectsAsync(_keys.SigningPublicKey, 0, 0);
        }
        // A null protobuf setter is client-side rejection, not a server RPC case.
        await AdmitAsync();
    }

    private static string Expand(string privateKey)
    {
        var scalar = Convert.FromHexString(privateKey);
        try
        {
            using var key = ECDsa.Create(new ECParameters { Curve = ECCurve.CreateFromFriendlyName("secp256k1"), D = scalar });
            var point = key.ExportParameters(false).Q;
            return "04" + Convert.ToHexString(point.X!).ToLowerInvariant() + Convert.ToHexString(point.Y!).ToLowerInvariant();
        }
        finally { CryptographicOperations.ZeroMemory(scalar); }
    }

    private static Task RegisterSecretsAsync(string words, DerivedKeys keys) => HushVotingArtifactClient.RegisterAsync(
        words, keys.SigningPublicKey, keys.EncryptPublicKey, keys.SigningPrivateKey, keys.EncryptPrivateKey, Alias);

    private Task<SubmitSignedTransactionReply> SubmitAsync(string signed) => scenario.Blockchain.SubmitSignedTransactionAsync(
        new() { SignedTransaction = signed }, deadline: DateTime.UtcNow.AddSeconds(10)).ResponseAsync;

    private static void AssertReply(SubmitSignedTransactionReply reply, TransactionStatus status)
    {
        reply.Status.Should().Be(status); reply.Successfull.Should().BeTrue(); reply.ValidationCode.Should().BeEmpty();
    }

    private async Task AbsentAsync(string address)
    {
        var reply = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = address }, deadline: DateTime.UtcNow.AddSeconds(5));
        (!reply.Successfull && string.IsNullOrEmpty(reply.PublicSigningAddress) && string.IsNullOrEmpty(reply.PublicEncryptAddress)).Should().BeTrue();
    }

    private async Task ExactAsync(DerivedKeys keys, string alias)
    {
        var reply = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = keys.SigningPublicKey }, deadline: DateTime.UtcNow.AddSeconds(5));
        (reply.Successfull && reply.PublicSigningAddress == keys.SigningPublicKey && reply.PublicEncryptAddress == keys.EncryptPublicKey
            && reply.ProfileName == alias && !reply.IsPublic).Should().BeTrue();
    }

    private async Task EffectsAsync(string address, int profiles, int pending)
    {
        using var scope = scenario.Node.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Profiles.CountAsync(p => p.PublicSigningAddress == address)).Should().Be(profiles);
        scenario.Node.Services.GetRequiredService<IMemPoolService>().PeekPendingValidatedTransactions().Count().Should().Be(pending);
    }
}
