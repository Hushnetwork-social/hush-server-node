using System.Text.Json;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushNode.HushVoting.Licence.Transactions;
using Olimpo.KeyDerivation;

namespace HushVoting.IntegrationTests.Infrastructure;

/// <summary>Browser-independent fixture actor; assignments always use public signed admission and block indexing.</summary>
internal static class HushVotingServerLicence
{
    public static async Task<Guid> SubmitAsync(HushVotingScenario scenario, DerivedKeys keys, HushVotingLicenceAssignmentPayload payload)
    {
        var id = Guid.NewGuid();
        var unsigned = new UnsignedTransaction<HushVotingLicenceAssignmentPayload>(new TransactionId(id),
            HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind,
            new Timestamp(scenario.HistoricalBlockClock?.GetUtcNow().UtcDateTime ?? DateTime.UtcNow), payload,
            HushVotingLicenceCanonicalJson.PayloadJsonUtf8Length(payload));
        var canonical = new HushVotingLicenceCanonicalSerializer().SerializeCanonicalUnsignedJson(
            new SignedTransaction<HushVotingLicenceAssignmentPayload>(unsigned, new SignatureInfo(keys.SigningPublicKey, "")));
        var signed = new SignedTransaction<HushVotingLicenceAssignmentPayload>(unsigned,
            new SignatureInfo(keys.SigningPublicKey, Olimpo.DigitalSignature.SignMessageCompactBase64(canonical, keys.SigningPrivateKey)));
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
}
