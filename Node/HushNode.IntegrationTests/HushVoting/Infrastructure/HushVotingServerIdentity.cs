using System.Text.Json;
using HushNode.Identity;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;
using Olimpo.KeyDerivation;

namespace HushVoting.IntegrationTests.Infrastructure;

/// <summary>Registers fixture identities through public node transactions, without legacy test helpers or database seeding.</summary>
internal static class HushVotingServerIdentity
{
    public static async Task RegisterAsync(HushVotingScenario scenario, DerivedKeys keys, string alias, bool isPublic = false, string? encryptionAddress = null)
    {
        var encryption = encryptionAddress ?? keys.EncryptPublicKey;
        var signed = Sign(keys, alias, isPublic, encryption, scenario.HistoricalBlockClock?.GetUtcNow().UtcDateTime);
        using (var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30)))
        {
            var reply = await scenario.Blockchain.SubmitSignedTransactionAsync(new() { SignedTransaction = signed }, deadline: DateTime.UtcNow.AddSeconds(15));
            if (!reply.Successfull) throw new InvalidOperationException("Fixture identity transaction rejected: " + reply.ValidationCode);
            await received.WaitAsync();
        }
        await scenario.Blocks.ProduceBlockAsync();
        var profile = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = keys.SigningPublicKey }, deadline: DateTime.UtcNow.AddSeconds(10));
        if (!profile.Successfull || profile.PublicSigningAddress != keys.SigningPublicKey || profile.PublicEncryptAddress != encryption || profile.ProfileName != alias || profile.IsPublic != isPublic)
            throw new InvalidOperationException("Fixture identity was not indexed with its exact submitted profile.");
    }

    public static string Sign(DerivedKeys keys, string alias, bool isPublic, string? encryptionAddress = null, DateTime? transactionTimeUtc = null, bool signCompact = true)
    {
        var encryption = encryptionAddress ?? keys.EncryptPublicKey;
        var payload = new FullIdentityPayload(alias, keys.SigningPublicKey, encryption, isPublic);
        var serializer = new FullIdentityCanonicalSerializer();
        var unsigned = new UnsignedTransaction<FullIdentityPayload>(new TransactionId(Guid.NewGuid()), FullIdentityPayloadHandler.FullIdentityPayloadKind,
            new Timestamp(transactionTimeUtc ?? DateTime.UtcNow), payload, serializer.PayloadJsonUtf8Length(alias, keys.SigningPublicKey, encryption, isPublic));
        var canonical = serializer.SerializeCanonicalUnsignedJson(new SignedTransaction<FullIdentityPayload>(unsigned, new SignatureInfo(keys.SigningPublicKey, "")));
        var signed = new SignedTransaction<FullIdentityPayload>(unsigned, new SignatureInfo(keys.SigningPublicKey,
            signCompact ? Olimpo.DigitalSignature.SignMessageCompactBase64(canonical, keys.SigningPrivateKey)
                : Olimpo.DigitalSignature.SignMessage(canonical, keys.SigningPrivateKey)));
        return JsonSerializer.Serialize(signed);
    }
}
