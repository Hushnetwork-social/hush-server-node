using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Google.Protobuf;
using HushNode.Elections;
using HushNode.Elections.gRPC;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.Reactions.Crypto;
using HushServerNode.Testing.Elections;
using HushShared.Elections.Model;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechTalk.SpecFlow;
using Olimpo;
using Olimpo.KeyDerivation;
using ReactionPoint = HushShared.Reactions.Model.ECPoint;

namespace HushVoting.IntegrationTests.ServerTwins;

// EPIC-002 AT-LIC-014 -> AC-018-007/009 -> P018-3-04 -> T018-3-04.
internal sealed partial class EntitlementEnforcementTwinSteps
{
    private DerivedKeys _secondVoter = null!;
    private ElectionEntitlementCapture _originalCompletionCapture = null!;
    private AcceptElectionBallotCastActionPayload _completionCast = null!;

    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private async Task<HushNetwork.proto.GetElectionResponse> SignedElectionAccess(DerivedKeys actor)
    {
        var signedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var payload = JsonSerializer.Serialize(new { actorAddress = actor.SigningPublicKey,
            method = "GetElection", request = new { ElectionId = _id.ToString() }, signedAt });
        var headers = new Grpc.Core.Metadata
        {
            { "x-hush-election-query-signatory", actor.SigningPublicKey },
            { "x-hush-election-query-signed-at", signedAt },
            { "x-hush-election-query-signature", DigitalSignature.SignMessageCompactBase64(payload, actor.SigningPrivateKey) },
        };
        return await scenario.Elections.GetElectionAsync(new HushNetwork.proto.GetElectionRequest { ElectionId = _id.ToString() },
            headers, DateTime.UtcNow.AddSeconds(10));
    }
    private async Task<HushNetwork.proto.GetElectionVotingViewResponse> VotingView(DerivedKeys? voter = null)
    {
        var response = await scenario.Node.Services.GetRequiredService<IElectionQueryApplicationService>()
            .GetElectionVotingViewAsync(_id, (voter ?? _owner).SigningPublicKey, null);
        // Integration tests generate their own client protobuf types; preserve the actual
        // server projection through the shared wire contract rather than copying fields.
        return HushNetwork.proto.GetElectionVotingViewResponse.Parser.ParseFrom(response.ToByteArray());
    }

    private async Task SendOwnedAction(string type, object payload, DerivedKeys? signer = null)
    {
        var signed = await SignAction(type, payload, signer);
        await SubmitAction(signed.Wire);
    }

    [Given("a second independently registered voter is eligible before Open")]
    public async Task AddSecondCompletionVoter()
    {
        var words = MnemonicGenerator.GenerateMnemonic();
        _secondVoter = HushVotingTestIdentity.DeriveP01(words);
        await HushVotingArtifactClient.RegisterAsync(words, _secondVoter.SigningPrivateKey,
            _secondVoter.EncryptPrivateKey, _secondVoter.SigningPublicKey, _secondVoter.EncryptPublicKey);
        await HushVotingServerIdentity.RegisterAsync(scenario, _secondVoter, "Second completion voter");
        (await Import(1, "peer")).IsSuccess.Should().BeTrue();
        (await Execute(tx => Service.ClaimRosterEntryAsync(new(_id, _secondVoter.SigningPublicKey,
            "peer0", ElectionEligibilityContracts.TemporaryVerificationCode, SourceTransactionId: tx)))).IsSuccess.Should().BeTrue();
    }

    [When("its owner expires and a valid prepared ballot is cast before actual Close")]
    public async Task VoteAfterExpiry()
    {
        await OpenAtOffset(-1);
        _originalCompletionCapture = await Read(db => db.ElectionEntitlementCaptures.AsNoTracking().SingleAsync(c => c.ElectionId == _id));
        scenario.HistoricalBlockClock!.AdvanceTo(_licenceExpiry.AddSeconds(1));
        AuthenticatedIdentitySubject.TryCreate(LicencePersistenceVocabulary.SubjectTypeIdentity,
            _owner.SigningPublicKey, 0, out var subject, out _).Should().BeTrue();
        var current = await scenario.Node.Services.GetRequiredService<ILicenceIndexedProjectionReader>()
            .ResolveEffectiveAsync(subject!, scenario.HistoricalBlockClock.GetUtcNow().UtcDateTime, default);
        current.Outcome.Should().Be(IndexedEntitlementReadOutcome.NoActive);

        var ownerAccess = await SignedElectionAccess(_owner);
        ownerAccess.ScopedAccess.AllowedOperations.Should().Contain("close").And.Contain("vote");
        var voterAccess = await SignedElectionAccess(_secondVoter);
        voterAccess.ScopedAccess.AllowedOperations.Should().Contain("vote").And.NotContain("close");
        (await scenario.Elections.GetElectionAsync(new HushNetwork.proto.GetElectionRequest { ElectionId = _id.ToString() })).ScopedAccess.Should().BeNull();
        _completionCast = await CastAfterExpiry(_owner, "v0");
        await CastAfterExpiry(_secondVoter, "peer0");
        (await Read(db => db.ElectionAcceptedBallots.CountAsync(b => b.ElectionId == _id))).Should().Be(2);
    }

    private async Task<AcceptElectionBallotCastActionPayload> CastAfterExpiry(DerivedKeys voter, string voterId)
    {
        Task Send(string type, object payload) => SendOwnedAction(type, payload, voter);
        await Send(EncryptedElectionEnvelopeActionTypes.RegisterVotingCommitment,
            new RegisterElectionVotingCommitmentActionPayload(voter.SigningPublicKey, Digest(Guid.NewGuid().ToString()), voterId));
        var view = await VotingView(voter);
        view.Success.Should().BeTrue();
        var challengeId = Guid.NewGuid();
        var challengeHash = Digest(challengeId.ToString());
        await PrepareBallot(challengeId, challengeHash, view, voter, voterId);
        await Send(EncryptedElectionEnvelopeActionTypes.SpoilPreparedBallot,
            new SpoilPreparedBallotActionPayload(voter.SigningPublicKey, challengeId, challengeHash,
                Digest("owned spoiled transcript:" + challengeId), Digest("owned spoil record:" + challengeId), "hushvoting-local-sp04-verifier-v1",
                OrganizationVoterId: voterId));
        (await VotingView(voter)).ChallengeSatisfied.Should().BeTrue();

        // Real ElGamal ciphertext under this election's actual tally key. This exercises
        // the existing binding-envelope contract; it is not external proof qualification.
        var key = ReactionPoint.FromCoordinates(view.TallyPublicKey.X.ToByteArray(), view.TallyPublicKey.Y.ToByteArray());
        var curve = new BabyJubJubCurve();
        var count = view.Election.Options.Count;
        var nonce = new BigInteger(RandomNumberGenerator.GetBytes(32), isUnsigned: true) % curve.Order;
        var encrypted = ControlledElectionHarness.EncryptOneHotBallot("owned-captured-vote", 0, key,
            ControlledElectionHarness.CreateDeterministicNonceSequence(nonce, count, curve), count, curve);
        static object Point(ReactionPoint point) => new { x = point.X.ToString(CultureInfo.InvariantCulture), y = point.Y.ToString(CultureInfo.InvariantCulture) };
        var package = JsonSerializer.Serialize(new { version = "omega-binding-ballot-v1", publicKey = Point(key),
            selectionCount = count, ciphertext = new { c1 = encrypted.Slots.Select(s => Point(s.C1)), c2 = encrypted.Slots.Select(s => Point(s.C2)) } });
        var proof = JsonSerializer.Serialize(new { version = "omega-binding-proof-v1", proofType = "binding-circuit-envelope",
            proofProfile = "PRODUCTION_LIKE_PROFILE", circuitVersion = "omega-v1.0.0", artifactShape = "opaque-one-hot-elgamal",
            ballotPackageHash = Digest(package), openArtifactId = view.OpenArtifactId, eligibleSetHash = view.EligibleSetHash,
            ceremonyVersionId = view.CeremonyVersionId, dkgProfileId = view.DkgProfileId, tallyPublicKeyFingerprint = view.TallyPublicKeyFingerprint });
        var preparedId = Guid.NewGuid();
        var preparedHash = Digest(package + proof);
        await PrepareBallot(preparedId, preparedHash, view, voter, voterId);
        var cast = new AcceptElectionBallotCastActionPayload(voter.SigningPublicKey, Guid.NewGuid().ToString(), package, proof,
            Digest(Guid.NewGuid().ToString()), Guid.Parse(view.OpenArtifactId), Convert.FromBase64String(view.EligibleSetHash),
            Guid.Parse(view.CeremonyVersionId), view.DkgProfileId, view.TallyPublicKeyFingerprint,
            preparedId, preparedHash, Digest(Guid.NewGuid().ToString()), "hushvoting-sp04-receipt-commitment-sha256-v1",
            view.BallotDefinitionVersion, view.BallotDefinitionHash.ToByteArray(), voterId);
        await HushVotingArtifactClient.RegisterAsync(package, proof, JsonSerializer.Serialize(cast));
        await Send(EncryptedElectionEnvelopeActionTypes.AcceptBallotCast, cast);
        return cast;
    }

    private Task PrepareBallot(Guid id, string hash, HushNetwork.proto.GetElectionVotingViewResponse view, DerivedKeys voter, string voterId) =>
        SendOwnedAction(EncryptedElectionEnvelopeActionTypes.RegisterPreparedBallotCommitment,
            new RegisterPreparedBallotCommitmentActionPayload(voter.SigningPublicKey, id, hash,
                view.BallotDefinitionVersion, view.BallotDefinitionHash.ToByteArray(), ElectionSp04ProfileIds.ChallengeSpoilV1,
                "hushvoting-sp04-prepared-ballot-proof-v1", OrganizationVoterId: voterId), voter);

    [Then("captured rights permit Close counting and finalization but reject another ballot after Close")]
    public async Task FinishAfterExpiry()
    {
        await SendOwnedAction(EncryptedElectionEnvelopeActionTypes.CloseElection,
            new CloseElectionActionPayload(_owner.SigningPublicKey, null, null));
        var closed = await Read(db => db.Elections.AsNoTracking().SingleAsync(e => e.ElectionId == _id));
        closed.LifecycleState.Should().Be(ElectionLifecycleState.Closed);
        for (var attempt = 0; attempt < 20 && !closed.TallyReadyAt.HasValue; attempt++)
        {
            // Exercise the existing query-time repair route used by the application; it
            // runs real publication/counting and may race the block-completion subscriber.
            await scenario.Node.Services.GetRequiredService<IElectionQueryApplicationService>().GetElectionAsync(_id, _owner.SigningPublicKey);
            await Task.Delay(100);
            closed = await Read(db => db.Elections.AsNoTracking().SingleAsync(e => e.ElectionId == _id));
        }
        var pending = await Read(db => db.ElectionBallotMemPoolEntries.CountAsync(b => b.ElectionId == _id));
        var published = await Read(db => db.ElectionPublishedBallots.CountAsync(b => b.ElectionId == _id));
        var issues = await Read(db => db.ElectionPublicationIssues.Where(b => b.ElectionId == _id).Select(b => b.IssueCode).ToArrayAsync());
        var proofStates = await Read(db => db.ElectionPublicationProofSessions.Where(b => b.ElectionId == _id)
            .Select(b => b.Status).ToArrayAsync());
        closed.TallyReadyAt.Should().NotBeNull($"real counting must complete; progress={closed.ClosedProgressStatus}, pending={pending}, published={published}, issues={string.Join(',', issues)}, proof states={string.Join(',', proofStates)}");
        var closedAccess = await SignedElectionAccess(_owner);
        closedAccess.ScopedAccess.AllowedOperations.Should().Contain("finalize").And.NotContain("vote");
        var afterClose = _completionCast with { IdempotencyKey = Guid.NewGuid().ToString(), BallotNullifier = Digest(Guid.NewGuid().ToString()) };
        var rejectedCast = await SignAction(EncryptedElectionEnvelopeActionTypes.AcceptBallotCast, afterClose);
        var admission = await scenario.Blockchain.SubmitSignedTransactionAsync(
            new() { SignedTransaction = rejectedCast.Wire }, deadline: DateTime.UtcNow.AddSeconds(15));
        admission.Successfull.Should().BeFalse();
        admission.ValidationCode.Should().Be("election_cast_close_persisted");
        (await Read(db => db.ElectionAcceptedBallots.CountAsync(b => b.ElectionId == _id))).Should().Be(2);
        await SendOwnedAction(EncryptedElectionEnvelopeActionTypes.FinalizeElection,
            new FinalizeElectionActionPayload(_owner.SigningPublicKey, null, null));
        (await Read(db => db.Elections.AsNoTracking().SingleAsync(e => e.ElectionId == _id))).LifecycleState.Should().Be(ElectionLifecycleState.Finalized);
        (await Read(db => db.ElectionEntitlementCaptures.AsNoTracking().SingleAsync(c => c.ElectionId == _id))).Should().Be(_originalCompletionCapture);
    }
}
