using System.Security.Cryptography;
using FluentAssertions;
using HushNode.Elections;
using HushNode.Reactions.Crypto;
using HushShared.Elections.Model;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Olimpo.KeyDerivation;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.ServerTwins;

internal sealed partial class EntitlementEnforcementTwinSteps
{
    private readonly List<DerivedKeys> _governedTrustees = [];
    private Guid _governedProposal;
    private int _governedOffset;

    [Given("an encrypted governed Open with two earlier trustee approvals")]
    public async Task GovernedOpenReady()
    {
        await EncryptedOpenReady();
        _draft = _draft with { SelectedProfileId = "dkg-prod-3of5", GovernanceMode = ElectionGovernanceMode.TrusteeThreshold,
            RequiredApprovalCount = 3 };
        (await Update(_draft)).IsSuccess.Should().BeTrue();
        var refresh = await Execute(tx => Service.RefreshProtocolPackageBindingAsync(new(_id, _owner.SigningPublicKey, SourceTransactionId: tx)));
        refresh.IsSuccess.Should().BeTrue(refresh.ErrorMessage);
        var invitations = new List<ElectionTrusteeInvitationRecord>();
        for (var i = 0; i < 5; i++)
        {
            var words = MnemonicGenerator.GenerateMnemonic();
            var keys = HushVotingTestIdentity.DeriveP01(words);
            await HushVotingArtifactClient.RegisterAsync(words, keys.SigningPrivateKey, keys.EncryptPrivateKey, keys.SigningPublicKey, keys.EncryptPublicKey);
            await HushVotingServerIdentity.RegisterAsync(scenario, keys, "Entitlement trustee " + i);
            _governedTrustees.Add(keys);
            var invited = await Service.InviteTrusteeAsync(new(_id, _owner.SigningPublicKey, keys.SigningPublicKey, "Trustee " + i));
            invited.IsSuccess.Should().BeTrue(invited.ErrorMessage);
            var accepted = await Service.AcceptTrusteeInvitationAsync(new(_id, invited.TrusteeInvitation!.Id, keys.SigningPublicKey));
            accepted.IsSuccess.Should().BeTrue(accepted.ErrorMessage);
            invitations.Add(accepted.TrusteeInvitation!);
        }
        // Given-state fixture for completed ceremony prerequisites. This suite qualifies licence
        // enforcement, real trustee signatures/roles and actual governed dispatch, not DKG itself.
        // No entitlement/capture or successful Open outcome is fabricated here.
        var at = scenario.HistoricalBlockClock!.GetUtcNow().UtcDateTime;
        var publicKey = new BabyJubJubCurve().Generator.ToBytes();
        var version = ElectionModelFactory.CreateCeremonyVersion(_id, 1, _draft.SelectedProfileId, 3,
            invitations.Select(i => new ElectionTrusteeReference(i.TrusteeUserAddress, i.TrusteeDisplayName)).ToArray(),
            _owner.SigningPublicKey).MarkReady(at, Convert.ToHexString(SHA256.HashData(publicKey)), publicKey);
        await Read(async db =>
        {
            db.ElectionCeremonyVersions.Add(version);
            foreach (var invitation in invitations)
            {
                var state = ElectionModelFactory.CreateCeremonyTrusteeState(_id, version.Id, invitation.TrusteeUserAddress,
                    invitation.TrusteeDisplayName, state: ElectionTrusteeCeremonyState.AcceptedTrustee)
                    .PublishTransportKey("owned-ceremony-public-transport", at).MarkJoined(at).RecordSelfTestSuccess(at)
                    .RecordMaterialSubmitted(at, "owned-share-v1", publicKey).MarkCompleted(at, "owned-share-v1");
                db.ElectionCeremonyTrusteeStates.Add(state);
                db.ElectionCeremonyShareCustodyRecords.Add(ElectionModelFactory.CreateCeremonyShareCustodyRecord(
                    _id, version.Id, invitation.TrusteeUserAddress, "owned-share-v1"));
            }
            return await db.SaveChangesAsync();
        });
        var readiness = await Service.EvaluateOpenReadinessAsync(new(_id, [ElectionWarningCode.LowAnonymitySet]));
        readiness.IsReadyToOpen.Should().BeTrue(string.Join("; ", readiness.ValidationErrors));
        var proposal = await Service.StartGovernedProposalAsync(new(_id, ElectionGovernedActionType.Open, _owner.SigningPublicKey));
        proposal.IsSuccess.Should().BeTrue(proposal.ErrorMessage);
        _governedProposal = proposal.GovernedProposal!.Id;
        foreach (var keys in _governedTrustees.Take(2))
        {
            var signed = await SignAction(EncryptedElectionEnvelopeActionTypes.ApproveGovernedProposal,
                new ApproveElectionGovernedProposalActionPayload(_governedProposal, keys.SigningPublicKey, "Earlier approval"), keys);
            await SubmitAction(signed.Wire);
        }
        (await Read(db => db.ElectionGovernedProposalApprovals.CountAsync(a => a.ProposalId == _governedProposal))).Should().Be(2);
        (await Read(db => db.ElectionEntitlementCaptures.CountAsync(c => c.ElectionId == _id))).Should().Be(0);
    }

    [When("the final signed trustee approval executes (.*) seconds from owner expiry")]
    public async Task FinalApprovalAtExpiry(int seconds)
    {
        _governedOffset = seconds;
        var keys = _governedTrustees[2];
        var signed = await SignAction(EncryptedElectionEnvelopeActionTypes.ApproveGovernedProposal,
            new ApproveElectionGovernedProposalActionPayload(_governedProposal, keys.SigningPublicKey, "Final approval"), keys);
        using var waiter = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(20));
        var reply = await scenario.Blockchain.SubmitSignedTransactionAsync(new() { SignedTransaction = signed.Wire }, deadline: DateTime.UtcNow.AddSeconds(15));
        reply.Successfull.Should().BeTrue();
        await waiter.WaitAsync();
        scenario.HistoricalBlockClock!.AdvanceTo(_licenceExpiry.AddSeconds(seconds));
        _openTransaction = signed.Id;
        await scenario.Blocks.ProduceBlockAsync();
    }

    [Then("governed Open uses execution-time owner rights and preserves its proposal reference")]
    public async Task GovernedExpiryOutcome()
    {
        var election = await Read(db => db.Elections.AsNoTracking().SingleAsync(e => e.ElectionId == _id));
        var proposal = await Read(db => db.ElectionGovernedProposals.AsNoTracking().SingleAsync(p => p.Id == _governedProposal));
        var capture = await Read(db => db.ElectionEntitlementCaptures.AsNoTracking().SingleOrDefaultAsync(c => c.ElectionId == _id));
        (await Read(db => db.ElectionGovernedProposalApprovals.CountAsync(a => a.ProposalId == _governedProposal))).Should().Be(3);
        if (_governedOffset >= 0)
        {
            election.LifecycleState.Should().Be(ElectionLifecycleState.Draft);
            proposal.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatus.ExecutionFailed);
            capture.Should().BeNull();
            (await Roster()).Should().OnlyContain(r => !r.WasPresentAtOpen);
        }
        else
        {
            election.LifecycleState.Should().Be(ElectionLifecycleState.Open);
            proposal.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatus.ExecutionSucceeded);
            capture!.GovernedProposalId.Should().Be(_governedProposal);
            capture.OpenTransactionId.Should().Be(_openTransaction);
            capture.OpenBlockTimeUtc.Should().Be(_licenceExpiry.AddSeconds(_governedOffset));
            capture.SelectedProfileId.Should().Be("dkg-prod-3of5");
            capture.HasSupportedSemantics().Should().BeTrue();
        }
    }
}
