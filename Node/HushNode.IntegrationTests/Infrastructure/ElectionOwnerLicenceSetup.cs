using System.Text.Json;
using FluentAssertions;
using HushNetwork.proto;
using HushNode.HushVoting.Licence.Transactions;
using HushNode.HushVoting.Licensing.Storage;
using HushServerNode;
using HushServerNode.Testing;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.HushVoting.Licensing.Model;
using Microsoft.Extensions.DependencyInjection;

namespace HushNode.IntegrationTests.Infrastructure;

/// <summary>
/// Prerequisite for existing election lifecycle fixtures under FEAT-018 enforcement.
/// Registers the owner and indexes real signed baseline/upgrade transactions. It does
/// not seed licence rows, replace an authorizer or couple HushVoting scenario bindings.
/// </summary>
internal static class ElectionOwnerLicenceSetup
{
    public static async Task RegisterAsync(HushServerNodeCore node, BlockProductionControl blocks,
        GrpcClientFactory grpc, TestIdentity owner)
    {
        var client = grpc.CreateClient<HushBlockchain.HushBlockchainClient>();
        async Task Submit(string wire)
        {
            using var received = node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(20));
            var reply = await client.SubmitSignedTransactionAsync(new() { SignedTransaction = wire },
                deadline: DateTime.UtcNow.AddSeconds(15));
            reply.Successfull.Should().BeTrue("the election owner's signed prerequisite must be admitted: {0}", reply.ValidationCode);
            await received.WaitAsync();
            await blocks.ProduceBlockAsync();
        }

        await Submit(TestTransactionFactory.CreateIdentityRegistration(owner));
        async Task<Guid> Assign(HushVotingLicenceAssignmentPayload payload)
        {
            var id = Guid.NewGuid();
            var unsigned = new UnsignedTransaction<HushVotingLicenceAssignmentPayload>(new TransactionId(id),
                HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind, Timestamp.Current,
                payload, HushVotingLicenceCanonicalJson.PayloadJsonUtf8Length(payload));
            var canonical = new HushVotingLicenceCanonicalSerializer().SerializeCanonicalUnsignedJson(
                new SignedTransaction<HushVotingLicenceAssignmentPayload>(unsigned, new SignatureInfo(owner.PublicSigningAddress, "")));
            var signed = new SignedTransaction<HushVotingLicenceAssignmentPayload>(unsigned,
                new SignatureInfo(owner.PublicSigningAddress,
                    Olimpo.DigitalSignature.SignMessageCompactBase64(canonical, owner.PrivateSigningKey)));
            await Submit(JsonSerializer.Serialize(signed));
            return id;
        }

        var baseline = await Assign(new(HushVotingLicenceTransitionIntent.BaselineFree,
            HushVotingLicencePlanId.DirectFree.Value, HushVotingLicenceCatalogueVersion.V1Value));
        await Assign(new(HushVotingLicenceTransitionIntent.ConfirmedUpgrade,
            HushVotingLicencePlanId.Veritas500.Value, HushVotingLicenceCatalogueVersion.V1Value,
            baseline, HushVotingLicencePlanId.DirectFree.Value));

        var identity = await node.Services.GetRequiredService<IHushVotingLicenceValidationContextSource>()
            .ResolveIdentityAsync(AuthenticatedIdentitySubject.NormalizeCanonicalAddress(owner.PublicSigningAddress)!,
                CancellationToken.None);
        identity.Should().NotBeNull("the fixture identity must come from its indexed registration");
        AuthenticatedIdentitySubject.TryCreate(LicencePersistenceVocabulary.SubjectTypeIdentity,
            owner.PublicSigningAddress, identity!.IdentityCreationBlockIndex, out var subject, out _).Should().BeTrue();
        var indexed = await node.Services.GetRequiredService<ILicenceIndexedProjectionReader>()
            .ResolveEffectiveAsync(subject!, DateTime.UtcNow, CancellationToken.None);
        indexed.Outcome.Should().Be(IndexedEntitlementReadOutcome.Active);
        indexed.Entitlement!.PlanId.Should().Be(HushVotingLicencePlanId.Veritas500.Value);
    }
}
