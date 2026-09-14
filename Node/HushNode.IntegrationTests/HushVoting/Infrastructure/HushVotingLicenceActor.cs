using System.Globalization;
using System.Text.Json;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.HushVoting.Licence.gRPC;
using HushNode.HushVoting.Licence.Transactions;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushVoting.IntegrationTests.Infrastructure;

/// <summary>A second device for the browser-created test identity, using public server contracts.</summary>
internal sealed class HushVotingLicenceActor(HushVotingScenario scenario, HushVotingIdentityJourney identity)
{
    public async Task<GetMyEntitlementResponse> QueryAsync()
    {
        var signedAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var actor = identity.Keys.SigningPublicKey;
        var payload = LicenceQueryRequestAuthValidator.BuildSignedPayload("GetMyEntitlement", actor, signedAt);
        var headers = new Metadata
        {
            { LicenceQueryRequestAuthValidator.SignatoryHeader, actor },
            { LicenceQueryRequestAuthValidator.SignedAtHeader, signedAt },
            { LicenceQueryRequestAuthValidator.SignatureHeader, Olimpo.DigitalSignature.SignMessageCompactBase64(payload, identity.Keys.SigningPrivateKey) }
        };
        return await scenario.Licences.GetMyEntitlementAsync(new(), headers, deadline: DateTime.UtcNow.AddSeconds(10));
    }

    public async Task<SubmitSignedTransactionReply> UpgradeAsync(LicenceActiveEntitlementView current, string planId)
    {
        var payload = new HushVotingLicenceAssignmentPayload(HushVotingLicenceTransitionIntent.ConfirmedUpgrade,
            planId, current.AssignedCatalogueVersion, Guid.Parse(current.LicenceReference), current.PlanId);
        return await SubmitAsync(payload);
    }

    public Task<SubmitSignedTransactionReply> BaselineAsync() => SubmitAsync(new(
        HushVotingLicenceTransitionIntent.BaselineFree, "hushvoting.direct.free", "hushvoting-licence-catalogue/v1.0.0"));

    private async Task<SubmitSignedTransactionReply> SubmitAsync(HushVotingLicenceAssignmentPayload payload)
    {
        var unsigned = new UnsignedTransaction<HushVotingLicenceAssignmentPayload>(new TransactionId(Guid.NewGuid()),
            HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind,
            new Timestamp(scenario.HistoricalBlockClock?.GetUtcNow().UtcDateTime ?? DateTime.UtcNow),
            payload, HushVotingLicenceCanonicalJson.PayloadJsonUtf8Length(payload));
        var canonical = new HushVotingLicenceCanonicalSerializer().SerializeCanonicalUnsignedJson(
            new SignedTransaction<HushVotingLicenceAssignmentPayload>(unsigned, new SignatureInfo(identity.Keys.SigningPublicKey, "")));
        var signature = Olimpo.DigitalSignature.SignMessageCompactBase64(canonical, identity.Keys.SigningPrivateKey);
        var transaction = new SignedTransaction<HushVotingLicenceAssignmentPayload>(unsigned, new SignatureInfo(identity.Keys.SigningPublicKey, signature));
        return await scenario.Blockchain.SubmitSignedTransactionAsync(new() { SignedTransaction = JsonSerializer.Serialize(transaction) }, deadline: DateTime.UtcNow.AddSeconds(15));
    }
}
