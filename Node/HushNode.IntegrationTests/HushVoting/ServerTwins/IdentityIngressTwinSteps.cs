// EPIC-001 -> FEAT-011 Phase 3 Tasks 3.1/3.2, 3.7/3.8.
// Supports FEAT-007 AC-007-071/076, FEAT-008 AC-008-076/083,
// FEAT-009 AC-009-081/087. Backend FEAT evidence, never EPIC E2E.
using System.Text.Json.Nodes;
using FluentAssertions;
using HushNetwork.proto;
using HushNode.MemPool;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Olimpo.KeyDerivation;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.ServerTwins;

[Binding]
[Scope(Tag = "HV-SERVER-TWIN")]
internal sealed class IdentityIngressTwinSteps(HushVotingScenario scenario)
{
    private const string Alias = "Matrix Alice";
    private DerivedKeys _keys = null!;
    private string _valid = "";
    private string _invalid = "";
    private int _pendingBefore;
    private SubmitSignedTransactionReply? _reply;

    [Given("the isolated real node has an unregistered P01 identity and a (.*) request")]
    public async Task ArrangeAsync(string defect)
    {
        (scenario.Page is null && scenario.Context is null).Should().BeTrue("backend Twins must not launch a browser");
        scenario.BaseUrl.Should().BeEmpty("backend Twins must not start a frontend service");
        var words = MnemonicGenerator.GenerateMnemonic();
        _keys = HushVotingTestIdentity.DeriveP01(words);
        await HushVotingArtifactClient.RegisterAsync(words, _keys.SigningPrivateKey, _keys.EncryptPrivateKey,
            _keys.SigningPublicKey, _keys.EncryptPublicKey, Alias);
        _valid = HushVotingServerIdentity.Sign(_keys, Alias, false);
        var changed = JsonNode.Parse(_valid)!.AsObject();
        switch (defect)
        {
            case "forged-signature":
                changed["UserSignature"]!["Signature"] = Convert.ToBase64String(new byte[64]);
                break;
            case "wrong-signing-key":
                var otherWords = MnemonicGenerator.GenerateMnemonic();
                var other = HushVotingTestIdentity.DeriveP01(otherWords);
                await HushVotingArtifactClient.RegisterAsync(otherWords, other.SigningPrivateKey, other.EncryptPrivateKey);
                changed = JsonNode.Parse(HushVotingServerIdentity.Sign(new DerivedKeys(_keys.SigningPublicKey,
                    other.SigningPrivateKey, _keys.EncryptPublicKey, _keys.EncryptPrivateKey), Alias, false))!.AsObject();
                break;
            case "altered-payload":
                changed["Payload"]!["IdentityAlias"] = "Matrix Other"; // same byte count; only signature binding fails
                break;
            case "altered-encryption-binding":
                // Both addresses are real equal-length curve points. Changing the
                // signed encryption address must fail signature binding, not shape validation.
                (_keys.SigningPublicKey != _keys.EncryptPublicKey).Should().BeTrue();
                changed["Payload"]!["PublicEncryptAddress"] = _keys.SigningPublicKey;
                break;
            case "signatory-mismatch":
                changed["UserSignature"]!["Signatory"] = _keys.EncryptPublicKey;
                break;
            case "malformed-signature":
                changed["UserSignature"]!["Signature"] = "not-a-signature";
                break;
            case "payload-size-mismatch":
                changed["PayloadSize"] = changed["PayloadSize"]!.GetValue<int>() + 1;
                break;
            case "invalid-encryption-encoding":
                changed = JsonNode.Parse(HushVotingServerIdentity.Sign(_keys, Alias, false, "00"))!.AsObject();
                break;
            case "unsupported-payload-kind":
                changed["PayloadKind"] = "ffffffff-ffff-ffff-ffff-ffffffffffff";
                break;
            case "malformed-json":
                break;
            case "missing-payload-kind":
                changed.Remove("PayloadKind");
                break;
            case "numeric-payload-kind":
                changed["PayloadKind"] = 42;
                break;
            case "object-payload-kind":
                changed["PayloadKind"] = new JsonObject();
                break;
            case "array-payload-kind":
                changed["PayloadKind"] = new JsonArray();
                break;
            case "null-payload":
                changed["Payload"] = null;
                break;
            case "missing-payload":
                changed.Remove("Payload");
                break;
            case "null-user-signature":
                changed["UserSignature"] = null;
                break;
            case "missing-user-signature":
                changed.Remove("UserSignature");
                break;
            case "null-alias":
                changed["Payload"]!["IdentityAlias"] = null;
                break;
            case "missing-alias":
                changed["Payload"]!.AsObject().Remove("IdentityAlias");
                break;
            case "null-signing-address":
                changed["Payload"]!["PublicSigningAddress"] = null;
                break;
            case "null-encryption-address":
                changed["Payload"]!["PublicEncryptAddress"] = null;
                break;
            case "null-signature-value":
                changed["UserSignature"]!["Signature"] = null;
                break;
            case "null-signatory":
                changed["UserSignature"]!["Signatory"] = null;
                break;
            case "array-payload":
                changed["Payload"] = new JsonArray();
                break;
            case "array-user-signature":
                changed["UserSignature"] = new JsonArray();
                break;
            case "numeric-alias": changed["Payload"]!["IdentityAlias"] = 42; break;
            case "object-alias": changed["Payload"]!["IdentityAlias"] = new JsonObject(); break;
            case "boolean-signing-address": changed["Payload"]!["PublicSigningAddress"] = true; break;
            case "array-encryption-address": changed["Payload"]!["PublicEncryptAddress"] = new JsonArray(); break;
            case "string-visibility": changed["Payload"]!["IsPublic"] = "false"; break;
            case "numeric-signature": changed["UserSignature"]!["Signature"] = 42; break;
            case "object-signatory": changed["UserSignature"]!["Signatory"] = new JsonObject(); break;
            case "string-payload-size": changed["PayloadSize"] = "42"; break;
            case "fractional-payload-size": changed["PayloadSize"] = 1.5; break;
            case "overflow-payload-size": changed["PayloadSize"] = JsonNode.Parse("9223372036854775808"); break;
            case "null-payload-size": changed["PayloadSize"] = null; break;
            case "negative-payload-size": changed["PayloadSize"] = -1; break;
            case "missing-payload-size": changed.Remove("PayloadSize"); break;
            case "malformed-transaction-id": changed["TransactionId"] = "not-a-uuid"; break;
            case "numeric-transaction-id": changed["TransactionId"] = 42; break;
            case "empty-transaction-id": changed["TransactionId"] = Guid.Empty.ToString(); break;
            case "null-transaction-id": changed["TransactionId"] = null; break;
            case "missing-transaction-id": changed.Remove("TransactionId"); break;
            case "malformed-timestamp": changed["TransactionTimeStamp"] = "not-a-date"; break;
            case "numeric-timestamp": changed["TransactionTimeStamp"] = 42; break;
            case "object-timestamp": changed["TransactionTimeStamp"] = new JsonObject(); break;
            case "array-root":
            case "number-root":
            case "string-root":
            case "boolean-root":
                break;
            default: throw new InvalidOperationException("Unknown closed ingress test case.");
        }
        // Isolate null field validation from the earlier canonical size check.
        if (defect is "null-alias" or "missing-alias" or "null-signing-address" or "null-encryption-address")
        {
            var payload = changed["Payload"]!.AsObject();
            changed["PayloadSize"] = new HushNode.Identity.FullIdentityCanonicalSerializer().PayloadJsonUtf8Length(
                payload["IdentityAlias"]?.GetValue<string>()!, payload["PublicSigningAddress"]?.GetValue<string>()!,
                payload["PublicEncryptAddress"]?.GetValue<string>()!, payload["IsPublic"]!.GetValue<bool>());
        }
        _invalid = defect switch
        {
            "malformed-json" => "{ this is not json",
            // Legal JSON whitespace keeps these public fixtures within the artifact
            // protocol's four-character minimum, including final hook registration.
            "array-root" => "[  ]",
            "number-root" => "\t42\t",
            "string-root" => "\"transaction\"",
            "boolean-root" => "true",
            _ => changed.ToJsonString()
        };
        await HushVotingArtifactClient.RegisterAsync(_valid, _invalid);
        _pendingBefore = PendingCount();
        await AssertAbsentAsync();
    }

    [When("the defective request reaches the unchanged SubmitSignedTransaction RPC")]
    public async Task SubmitAsync() => _reply = await scenario.Blockchain.SubmitSignedTransactionAsync(
        new() { SignedTransaction = _invalid }, deadline: DateTime.UtcNow.AddSeconds(15));

    [Then("ingress returns REJECTED with stable code (.*) and no mempool or indexed identity effect")]
    public async Task RejectAsync(string code)
    {
        _reply.Should().NotBeNull();
        _reply!.Successfull.Should().BeFalse();
        _reply.Status.Should().Be(TransactionStatus.Rejected);
        _reply.ValidationCode.Should().Be(code);
        PendingCount().Should().Be(_pendingBefore);
        await AssertAbsentAsync();
        await scenario.Blocks.ProduceBlockAsync();
        await AssertAbsentAsync();
    }

    [Then("the original valid identity is still accepted once and indexes with its exact public pair")]
    public async Task ValidControlAsync()
    {
        using (var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(20)))
        {
            var admitted = await scenario.Blockchain.SubmitSignedTransactionAsync(new() { SignedTransaction = _valid },
                deadline: DateTime.UtcNow.AddSeconds(15));
            admitted.Successfull.Should().BeTrue();
            admitted.Status.Should().Be(TransactionStatus.Accepted, "rejected requests must not poison same-key reservation");
            await received.WaitAsync();
        }
        var retry = await scenario.Blockchain.SubmitSignedTransactionAsync(new() { SignedTransaction = _valid },
            deadline: DateTime.UtcNow.AddSeconds(15));
        retry.Status.Should().Be(TransactionStatus.Pending);
        PendingCount().Should().Be(1);
        await scenario.Blocks.ProduceBlockAsync();
        var indexed = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = _keys.SigningPublicKey },
            deadline: DateTime.UtcNow.AddSeconds(10));
        (indexed.Successfull && indexed.PublicSigningAddress == _keys.SigningPublicKey
            && indexed.PublicEncryptAddress == _keys.EncryptPublicKey && indexed.ProfileName == Alias && !indexed.IsPublic).Should().BeTrue();
        PendingCount().Should().Be(0);
    }

    private int PendingCount() => scenario.Node.Services.GetRequiredService<IMemPoolService>().PeekPendingValidatedTransactions().Count();

    private async Task AssertAbsentAsync()
    {
        var reply = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = _keys.SigningPublicKey },
            deadline: DateTime.UtcNow.AddSeconds(10));
        (reply.Successfull || !string.IsNullOrEmpty(reply.PublicSigningAddress) || !string.IsNullOrEmpty(reply.PublicEncryptAddress)).Should().BeFalse();
    }
}
