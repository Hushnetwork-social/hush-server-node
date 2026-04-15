using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.Caching;
using HushNode.Elections;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushNode.MemPool;
using HushNode.Reactions.Crypto;
using HushServerNode;
using HushServerNode.Testing;
using HushServerNode.Testing.Elections;
using HushShared.Elections.Model;
using ReactionECPoint = HushShared.Reactions.Model.ECPoint;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Olimpo;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

[Binding]
[Scope(Feature = "FEAT-094 election lifecycle integration")]
public sealed class ElectionLifecycleIntegrationSteps
{
    private static readonly TestIdentity Delta = TestIdentities.GenerateFromSeed("TEST_DELTA_V1", "Delta");
    private static readonly TestIdentity Echo = TestIdentities.GenerateFromSeed("TEST_ECHO_V1", "Echo");
    private static readonly TestIdentity Foxtrot = TestIdentities.GenerateFromSeed("TEST_FOXTROT_V1", "Foxtrot");
    private static readonly TestIdentity Guest = TestIdentities.GenerateFromSeed("TEST_GUEST_V1", "Guest");
    private static readonly IReadOnlyList<TestIdentity> RolloutTrustees =
    [
        TestIdentities.Bob,
        TestIdentities.Charlie,
        Delta,
        Echo,
        Foxtrot,
    ];

    private readonly ScenarioContext _scenarioContext;
    private readonly List<ElectionLifecycleStateProto> _observedStates = [];
    private readonly Dictionary<string, string> _trusteeInvitationIds = new(StringComparer.OrdinalIgnoreCase);

    private HushElections.HushElectionsClient? _client;
    private TestIdentity? _owner;
    private string? _electionId;
    private string? _lastGovernedProposalId;
    private ElectionCommandResponse? _lastCommandResponse;
    private SubmitSignedTransactionReply? _lastSubmitTransactionResponse;
    private GetElectionOpenReadinessResponse? _lastReadinessResponse;
    private GetElectionResponse? _lastElectionResponse;
    private GetElectionEligibilityViewResponse? _lastEligibilityViewResponse;
    private GetElectionVotingViewResponse? _lastVotingViewResponse;
    private GetElectionResultViewResponse? _lastResultViewResponse;

    public ElectionLifecycleIntegrationSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"FEAT-094 election integration services are available")]
    public void GivenFeatElectionIntegrationServicesAreAvailable()
    {
        _client = GetGrpcFactory().CreateClient<HushElections.HushElectionsClient>();
        _owner = TestIdentities.Alice;
        _observedStates.Clear();
        _electionId = null;
        _lastGovernedProposalId = null;
        _lastCommandResponse = null;
        _lastSubmitTransactionResponse = null;
        _lastReadinessResponse = null;
        _lastElectionResponse = null;
        _lastEligibilityViewResponse = null;
        _lastVotingViewResponse = null;
        _lastResultViewResponse = null;
        _trusteeInvitationIds.Clear();
    }

    [When(@"the owner creates an admin-only election draft through blockchain submission")]
    public async Task WhenTheOwnerCreatesAnAdminOnlyElectionDraftThroughGrpc()
    {
        var response = await CreateElectionDraftViaBlockchainAsync(
            snapshotReason: "initial draft",
            draft: BuildAdminDraftSpecification("Board Election"));

        _lastCommandResponse = response;
        _electionId = response.Election.ElectionId;
        RecordState(response.Election.LifecycleState);
    }

    [When(@"the owner creates a trustee-threshold election draft through blockchain submission")]
    public async Task WhenTheOwnerCreatesATrusteeThresholdElectionDraftThroughGrpc()
    {
        var response = await CreateElectionDraftViaBlockchainAsync(
            snapshotReason: "trustee-threshold draft",
            draft: BuildTrusteeThresholdDraftSpecification("Governed Referendum"));

        _lastCommandResponse = response;
        _electionId = response.Election.ElectionId;
        RecordState(response.Election.LifecycleState);
    }

    [When(@"the owner creates a late-activation admin-only election draft through blockchain submission")]
    public async Task WhenTheOwnerCreatesALateActivationAdminOnlyElectionDraftThroughBlockchainSubmission()
    {
        var response = await CreateElectionDraftViaBlockchainAsync(
            snapshotReason: "late-activation draft",
            draft: BuildAdminDraftSpecification(
                "Late Activation Board Election",
                eligibilityMutationPolicy: EligibilityMutationPolicy.LateActivationForRosteredVotersOnly));

        _lastCommandResponse = response;
        _electionId = response.Election.ElectionId;
        RecordState(response.Election.LifecycleState);
    }

    [When(@"the owner imports the default election roster through blockchain submission")]
    public async Task WhenTheOwnerImportsTheDefaultElectionRosterThroughBlockchainSubmission()
    {
        var response = await ImportRosterViaBlockchainAsync(BuildDefaultRosterImportItems());

        _lastCommandResponse = new ElectionCommandResponse
        {
            Success = response.Success,
            ErrorMessage = response.ErrorMessage,
            Election = response.Election,
        };
        _lastElectionResponse = response;
    }

    [When(@"voter ""(.*)"" claims roster entry ""(.*)"" with temporary verification code through blockchain submission")]
    public async Task WhenVoterClaimsRosterEntryWithTemporaryVerificationCodeThroughBlockchainSubmission(
        string voterAlias,
        string organizationVoterId)
    {
        _lastEligibilityViewResponse = await ClaimRosterEntryViaBlockchainAsync(
            ResolveIdentity(voterAlias),
            organizationVoterId,
            "1111");
    }

    [When(@"the owner updates the election draft title to ""(.*)""")]
    public async Task WhenTheOwnerUpdatesTheElectionDraftTitleTo(string title)
    {
        var response = await UpdateElectionDraftViaBlockchainAsync(
            snapshotReason: "owner draft update",
            draft: BuildAdminDraftSpecification(title));

        _lastCommandResponse = new ElectionCommandResponse
        {
            Success = true,
            Election = response.Election,
            DraftSnapshot = response.LatestDraftSnapshot,
        };
        RecordState(response.Election.LifecycleState);
    }

    [When(@"the owner checks open readiness for the election")]
    public async Task WhenTheOwnerChecksOpenReadinessForTheElection()
    {
        var request = new GetElectionOpenReadinessRequest
        {
            ElectionId = GetElectionId(),
        };
        request.RequiredWarningCodes.Add(ElectionWarningCodeProto.LowAnonymitySet);

        var response = await GetClient().GetElectionOpenReadinessAsync(request);
        response.IsReadyToOpen.Should().BeTrue(
            $"open readiness should pass when required warnings are acknowledged. Errors: {string.Join("; ", response.ValidationErrors)}");

        _lastReadinessResponse = response;
    }

    [When(@"the owner checks open readiness for the trustee-threshold election")]
    public async Task WhenTheOwnerChecksOpenReadinessForTheTrusteeThresholdElection()
    {
        var response = await GetClient().GetElectionOpenReadinessAsync(new GetElectionOpenReadinessRequest
        {
            ElectionId = GetElectionId(),
        });

        response.IsReadyToOpen.Should().BeFalse(
            "trustee-threshold FEAT-094 elections must remain blocked until FEAT-096 is implemented.");

        _lastReadinessResponse = response;
    }

    [When(@"the owner opens the election through blockchain submission")]
    public async Task WhenTheOwnerOpensTheElectionThroughBlockchainSubmission()
    {
        var frozenEligibleVoterSetHash = await BuildExpectedFrozenEligibleVoterSetHashAsync();
        var response = await OpenElectionViaBlockchainAsync(
            [ElectionWarningCode.LowAnonymitySet],
            frozenEligibleVoterSetHash,
            trusteePolicyExecutionReference: string.Empty,
            reportingPolicyExecutionReference: "phase-one-reporting-package",
            reviewWindowExecutionReference: string.Empty);

        response.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Open);

        _lastCommandResponse = new ElectionCommandResponse
        {
            Success = response.Success,
            ErrorMessage = response.ErrorMessage,
            Election = response.Election,
        };
        RecordState(response.Election.LifecycleState);
    }

    [When(@"the owner invites trustee ""(.*)"" through blockchain submission")]
    public async Task WhenTheOwnerInvitesTrusteeThroughGrpc(string trusteeAlias)
    {
        var trustee = ResolveIdentity(trusteeAlias);
        var invitationId = await InviteTrusteeViaBlockchainAsync(trustee);
        var response = await ReloadElectionAsync();
        response.Success.Should().BeTrue($"trustee invite readback should succeed: {response.ErrorMessage}");
        _trusteeInvitationIds[trustee.PublicSigningAddress] = invitationId.ToString();
        _lastCommandResponse = new ElectionCommandResponse
        {
            Success = response.Success,
            ErrorMessage = response.ErrorMessage,
            Election = response.Election,
        };
    }

    [When(@"trustee ""(.*)"" accepts the invitation through blockchain submission")]
    public async Task WhenTrusteeAcceptsTheInvitationThroughBlockchainSubmission(string trusteeAlias)
    {
        var trustee = ResolveIdentity(trusteeAlias);
        var response = await AcceptTrusteeInvitationViaBlockchainAsync(trustee);

        response.Success.Should().BeTrue($"trustee acceptance should succeed: {response.ErrorMessage}");
        response.TrusteeInvitation.Should().NotBeNull();
        response.TrusteeInvitation.Status.Should().Be(ElectionTrusteeInvitationStatusProto.Accepted);
        _lastCommandResponse = response;
    }

    [When(@"the owner attempts to open the trustee-threshold election through blockchain submission")]
    public async Task WhenTheOwnerAttemptsToOpenTheTrusteeThresholdElectionThroughBlockchainSubmission()
    {
        var frozenEligibleVoterSetHash = await BuildExpectedFrozenEligibleVoterSetHashAsync();
        _lastSubmitTransactionResponse = await SubmitOpenElectionViaBlockchainAsync(
            Array.Empty<ElectionWarningCode>(),
            frozenEligibleVoterSetHash,
            trusteePolicyExecutionReference: "reserved-feat-096-governance",
            reportingPolicyExecutionReference: "phase-one-reporting-package",
            reviewWindowExecutionReference: "governed-review-window-reserved");
    }

    [When(@"the owner submits a legacy plaintext open election transaction")]
    public async Task WhenTheOwnerSubmitsALegacyPlaintextOpenElectionTransaction()
    {
        var frozenEligibleVoterSetHash = await BuildExpectedFrozenEligibleVoterSetHashAsync();
        _lastSubmitTransactionResponse = await SubmitLegacyPlaintextOpenElectionViaBlockchainAsync(
            [ElectionWarningCode.LowAnonymitySet],
            frozenEligibleVoterSetHash,
            trusteePolicyExecutionReference: string.Empty,
            reportingPolicyExecutionReference: "phase-one-reporting-package",
            reviewWindowExecutionReference: string.Empty);
    }

    [When(@"the owner reloads the election through gRPC")]
    public async Task WhenTheOwnerReloadsTheElectionThroughGrpc()
    {
        _lastElectionResponse = await ReloadElectionAsync();
        RecordState(_lastElectionResponse.Election.LifecycleState);
    }

    [When(@"the actor ""(.*)"" requests the election eligibility view through gRPC")]
    public async Task WhenTheActorRequestsTheElectionEligibilityViewThroughGrpc(string actorAlias)
    {
        _lastEligibilityViewResponse = await GetElectionEligibilityViewAsync(ResolveIdentity(actorAlias));
    }

    [When(@"voter ""(.*)"" registers voting commitment ""(.*)"" through blockchain submission")]
    public async Task WhenVoterRegistersVotingCommitmentThroughBlockchainSubmission(
        string voterAlias,
        string commitmentHash)
    {
        _lastVotingViewResponse = await RegisterVotingCommitmentViaBlockchainAsync(
            ResolveIdentity(voterAlias),
            commitmentHash);
    }

    [When(@"the actor ""(.*)"" requests the election voting view through gRPC")]
    public async Task WhenTheActorRequestsTheElectionVotingViewThroughGrpc(string actorAlias)
    {
        _lastVotingViewResponse = await GetElectionVotingViewAsync(ResolveIdentity(actorAlias));
    }

    [When(@"the actor ""(.*)"" requests the election voting view with submission idempotency key ""(.*)"" through gRPC")]
    public async Task WhenTheActorRequestsTheElectionVotingViewWithSubmissionIdempotencyKeyThroughGrpc(
        string actorAlias,
        string submissionIdempotencyKey)
    {
        _lastVotingViewResponse = await GetElectionVotingViewAsync(
            ResolveIdentity(actorAlias),
            submissionIdempotencyKey);
    }

    [When(@"voter ""(.*)"" submits ballot cast with idempotency key ""(.*)"" without block production")]
    public async Task WhenVoterSubmitsBallotCastWithIdempotencyKeyWithoutBlockProduction(
        string voterAlias,
        string submissionIdempotencyKey)
    {
        _lastSubmitTransactionResponse = await SubmitAcceptedBallotCastWithoutBlockAsync(
            ResolveIdentity(voterAlias),
            submissionIdempotencyKey);
    }

    [When(@"voter ""(.*)"" retries ballot cast with idempotency key ""(.*)"" before block production")]
    public async Task WhenVoterRetriesBallotCastWithIdempotencyKeyBeforeBlockProduction(
        string voterAlias,
        string submissionIdempotencyKey)
    {
        _lastSubmitTransactionResponse = await SubmitAcceptedBallotCastAttemptAsync(
            ResolveIdentity(voterAlias),
            submissionIdempotencyKey);
    }

    [When(@"voter ""(.*)"" retries ballot cast with idempotency key ""(.*)"" after block production")]
    public async Task WhenVoterRetriesBallotCastWithIdempotencyKeyAfterBlockProduction(
        string voterAlias,
        string submissionIdempotencyKey)
    {
        _lastSubmitTransactionResponse = await SubmitAcceptedBallotCastAttemptAsync(
            ResolveIdentity(voterAlias),
            submissionIdempotencyKey);
    }

    [When(@"voter ""(.*)"" submits ballot cast with idempotency key ""(.*)"" through blockchain submission")]
    public async Task WhenVoterSubmitsBallotCastWithIdempotencyKeyThroughBlockchainSubmission(
        string voterAlias,
        string submissionIdempotencyKey)
    {
        _lastSubmitTransactionResponse = await SubmitAcceptedBallotCastViaBlockchainAsync(
            ResolveIdentity(voterAlias),
            submissionIdempotencyKey);
    }

    [When(@"voter ""(.*)"" submits FEAT-103 dev ballot cast with idempotency key ""(.*)"" through blockchain submission")]
    public async Task WhenVoterSubmitsFeat103DevBallotCastWithIdempotencyKeyThroughBlockchainSubmission(
        string voterAlias,
        string submissionIdempotencyKey)
    {
        _lastSubmitTransactionResponse = await SubmitAcceptedDevModeBallotCastViaBlockchainAsync(
            ResolveIdentity(voterAlias),
            submissionIdempotencyKey);
        _lastSubmitTransactionResponse.Successfull.Should().BeTrue(_lastSubmitTransactionResponse.Message);
    }

    [When(@"the pending cast block is produced")]
    public async Task WhenThePendingCastBlockIsProduced()
    {
        await GetBlockControl().ProduceBlockAsync();
        _lastElectionResponse = await ReloadElectionAsync();
    }

    [When(@"the owner closes the election through blockchain submission")]
    public async Task WhenTheOwnerClosesTheElectionThroughBlockchainSubmission()
    {
        var response = await CloseElectionViaBlockchainAsync(
            ByteString.CopyFromUtf8("accepted-ballot-set").ToByteArray(),
            ByteString.CopyFromUtf8("final-encrypted-tally").ToByteArray());

        _lastCommandResponse = new ElectionCommandResponse
        {
            Success = response.Success,
            ErrorMessage = response.ErrorMessage,
            Election = response.Election,
        };
        RecordState(response.Election.LifecycleState);
    }

    [When(@"the owner finalizes the election through blockchain submission")]
    public async Task WhenTheOwnerFinalizesTheElectionThroughBlockchainSubmission()
    {
        var response = await FinalizeElectionViaBlockchainAsync(
            ByteString.CopyFromUtf8("accepted-ballot-set").ToByteArray(),
            ByteString.CopyFromUtf8("final-encrypted-tally").ToByteArray());

        _lastCommandResponse = new ElectionCommandResponse
        {
            Success = response.Success,
            ErrorMessage = response.ErrorMessage,
            Election = response.Election,
        };
        RecordState(response.Election.LifecycleState);
        _lastElectionResponse = await ReloadElectionAsync();
    }

    [When(@"the owner activates roster entry ""(.*)"" through blockchain submission")]
    public async Task WhenTheOwnerActivatesRosterEntryThroughBlockchainSubmission(string organizationVoterId)
    {
        _lastEligibilityViewResponse = await ActivateRosterEntryViaBlockchainAsync(organizationVoterId);
    }

    [When(@"the owner grants designated auditor access to actor ""(.*)"" through blockchain submission")]
    public async Task WhenTheOwnerGrantsDesignatedAuditorAccessToActorThroughBlockchainSubmission(string actorAlias)
    {
        await CreateReportAccessGrantViaBlockchainAsync(ResolveIdentity(actorAlias));
        _lastElectionResponse = await ReloadElectionAsync();
    }

    [Given(@"the owner has an open admin-only election through blockchain submission")]
    public async Task GivenTheOwnerHasAnOpenAdminOnlyElectionThroughBlockchainSubmission()
    {
        await WhenTheOwnerCreatesAnAdminOnlyElectionDraftThroughGrpc();
        await WhenTheOwnerImportsTheDefaultElectionRosterThroughBlockchainSubmission();
        await WhenTheOwnerChecksOpenReadinessForTheElection();
        await WhenTheOwnerOpensTheElectionThroughBlockchainSubmission();
        await WhenTheOwnerReloadsTheElectionThroughGrpc();
    }

    [Given(@"the owner has an open trustee-threshold election through governed approval blockchain submission")]
    public async Task GivenTheOwnerHasAnOpenTrusteeThresholdElectionThroughGovernedApprovalBlockchainSubmission()
    {
        await WhenTheOwnerCreatesATrusteeThresholdElectionDraftThroughGrpc();
        await WhenTheOwnerImportsTheDefaultElectionRosterThroughBlockchainSubmission();
        await WhenTheOwnerPreparesAReadyTrusteeCeremonyThroughBlockchainSubmission();
        await WhenTheOwnerStartsAGovernedProposalThroughBlockchainSubmission("open");
        await WhenTrusteeApprovesTheGovernedProposalThroughBlockchainSubmission("Bob");
        await WhenTrusteeApprovesTheGovernedProposalThroughBlockchainSubmission("Charlie");
        await WhenTrusteeApprovesTheGovernedProposalThroughBlockchainSubmission("Delta");
        await WhenTheOwnerReloadsTheElectionThroughGrpc();
    }

    [Given(@"the owner has a closed trustee-threshold election through governed approval blockchain submission")]
    public async Task GivenTheOwnerHasAClosedTrusteeThresholdElectionThroughGovernedApprovalBlockchainSubmission()
    {
        await GivenTheOwnerHasAnOpenTrusteeThresholdElectionThroughGovernedApprovalBlockchainSubmission();
        await WhenTheOwnerStartsAGovernedProposalThroughBlockchainSubmission("close");
        await WhenTrusteeApprovesTheGovernedProposalThroughBlockchainSubmission("Bob");
        await WhenTrusteeApprovesTheGovernedProposalThroughBlockchainSubmission("Charlie");
        await WhenTrusteeApprovesTheGovernedProposalThroughBlockchainSubmission("Delta");
        await WhenTheOwnerReloadsTheElectionThroughGrpc();
    }

    [When(@"the owner prepares a ready trustee ceremony through blockchain submission")]
    public async Task WhenTheOwnerPreparesAReadyTrusteeCeremonyThroughBlockchainSubmission()
    {
        const string tallyFingerprint = "feat094-ready-tally-fingerprint";

        foreach (var trustee in RolloutTrustees)
        {
            if (!_trusteeInvitationIds.ContainsKey(trustee.PublicSigningAddress))
            {
                await WhenTheOwnerInvitesTrusteeThroughGrpc(trustee.DisplayName);
            }

            await EnsureTrusteeAcceptedAsync(trustee);
        }

        var startResponse = await StartElectionCeremonyViaBlockchainAsync("dkg-prod-3of5");

        startResponse.Success.Should().BeTrue($"ceremony start should succeed: {startResponse.ErrorMessage}");
        startResponse.CeremonyVersion.Should().NotBeNull();
        var ceremonyVersionId = startResponse.CeremonyVersion!.Id;
        var ceremonyVersionGuid = Guid.Parse(ceremonyVersionId);

        for (var index = 0; index < 3; index++)
        {
            var trustee = RolloutTrustees[index];

            var publishResponse = await PublishElectionCeremonyTransportKeyViaBlockchainAsync(
                trustee,
                ceremonyVersionGuid,
                $"feat094-transport-{index}");
            publishResponse.Success.Should().BeTrue($"transport publish should succeed: {publishResponse.ErrorMessage}");

            var joinResponse = await JoinElectionCeremonyViaBlockchainAsync(
                trustee,
                ceremonyVersionGuid);
            joinResponse.Success.Should().BeTrue($"ceremony join should succeed: {joinResponse.ErrorMessage}");

            var selfTestResponse = await RecordElectionCeremonySelfTestSuccessViaBlockchainAsync(
                trustee,
                ceremonyVersionGuid);
            selfTestResponse.Success.Should().BeTrue($"self-test should succeed: {selfTestResponse.ErrorMessage}");

            var submitResponse = await SubmitElectionCeremonyMaterialViaBlockchainAsync(
                trustee,
                ceremonyVersionGuid,
                recipientTrusteeUserAddress: null,
                messageType: "dkg-share-package",
                payloadVersion: "omega-v1.0.0",
                encryptedPayload: $"feat094-payload-{index}",
                payloadFingerprint: $"feat094-payload-{index}");
            submitResponse.Success.Should().BeTrue($"material submit should succeed: {submitResponse.ErrorMessage}");

            var completeResponse = await CompleteElectionCeremonyTrusteeViaBlockchainAsync(
                trustee.PublicSigningAddress,
                ceremonyVersionGuid,
                $"feat094-share-v1-{index}",
                tallyFingerprint);
            completeResponse.Success.Should().BeTrue($"ceremony completion should succeed: {completeResponse.ErrorMessage}");
        }
    }

    [When(@"the owner attempts to change the binding status after open")]
    public async Task WhenTheOwnerAttemptsToChangeTheBindingStatusAfterOpen()
    {
        _lastSubmitTransactionResponse = await SubmitDraftUpdateViaBlockchainAsync(
            snapshotReason: "post-open immutable mutation",
            draft: BuildAdminDraftSpecification(
                "Board Election",
                bindingStatus: ElectionBindingStatus.NonBinding));
    }

    [When(@"the owner starts an? ""(.*)"" governed proposal through blockchain submission")]
    public async Task WhenTheOwnerStartsAGovernedProposalThroughBlockchainSubmission(string actionType)
    {
        var response = await StartGovernedProposalViaBlockchainAsync(actionType);
        var proposal = response.GovernedProposals.Single(x => x.Id == GetLastGovernedProposalId());

        proposal.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatusProto.WaitingForApprovals);
        _lastElectionResponse = response;
        _lastCommandResponse = new ElectionCommandResponse
        {
            Success = true,
            ErrorMessage = string.Empty,
            Election = response.Election,
            GovernedProposal = proposal,
        };
    }

    [When(@"trustee ""(.*)"" approves the governed proposal through blockchain submission")]
    public async Task WhenTrusteeApprovesTheGovernedProposalThroughBlockchainSubmission(string trusteeAlias)
    {
        var trustee = ResolveIdentity(trusteeAlias);
        var response = await ApproveGovernedProposalViaBlockchainAsync(
            trustee,
            $"Approved by {trustee.DisplayName} in FEAT-096 integration coverage.");
        var proposal = response.GovernedProposals.Single(x => x.Id == GetLastGovernedProposalId());
        var approval = response.GovernedProposalApprovals
            .Last(x => x.ProposalId == GetLastGovernedProposalId() && x.TrusteeUserAddress == trustee.PublicSigningAddress);

        _lastElectionResponse = response;
        _lastCommandResponse = new ElectionCommandResponse
        {
            Success = true,
            ErrorMessage = string.Empty,
            Election = response.Election,
            GovernedProposal = proposal,
            GovernedProposalApproval = approval,
        };
    }

    [When(@"trustee ""(.*)"" submits a bound finalization share through blockchain submission")]
    public async Task WhenTrusteeSubmitsABoundFinalizationShareThroughBlockchainSubmission(string trusteeAlias)
    {
        var trustee = ResolveIdentity(trusteeAlias);
        var response = await SubmitFinalizationShareViaBlockchainAsync(
            trustee,
            ElectionFinalizationTargetType.AggregateTally);

        _lastElectionResponse = response;
        _lastCommandResponse = new ElectionCommandResponse
        {
            Success = true,
            ErrorMessage = string.Empty,
            Election = response.Election,
            FinalizationSession = response.FinalizationSessions.LastOrDefault(),
            FinalizationShare = response.FinalizationShares
                .LastOrDefault(x => x.TrusteeUserAddress == trustee.PublicSigningAddress),
            FinalizationReleaseEvidence = response.FinalizationReleaseEvidenceRecords.LastOrDefault(),
        };
        RecordState(response.Election.LifecycleState);
    }

    [When(@"trustee ""(.*)"" submits a single-ballot finalization share through blockchain submission")]
    public async Task WhenTrusteeSubmitsASingleBallotFinalizationShareThroughBlockchainSubmission(string trusteeAlias)
    {
        var trustee = ResolveIdentity(trusteeAlias);
        var response = await SubmitFinalizationShareViaBlockchainAsync(
            trustee,
            ElectionFinalizationTargetType.SingleBallot);

        _lastElectionResponse = response;
        _lastCommandResponse = new ElectionCommandResponse
        {
            Success = true,
            ErrorMessage = string.Empty,
            Election = response.Election,
            FinalizationSession = response.FinalizationSessions.LastOrDefault(),
            FinalizationShare = response.FinalizationShares
                .LastOrDefault(x => x.TrusteeUserAddress == trustee.PublicSigningAddress),
        };
        RecordState(response.Election.LifecycleState);
    }

    [When(@"the owner retries the governed proposal execution through blockchain submission")]
    public async Task WhenTheOwnerRetriesTheGovernedProposalExecutionThroughBlockchainSubmission()
    {
        var response = await RetryGovernedProposalExecutionViaBlockchainAsync();
        var proposal = response.GovernedProposals.Single(x => x.Id == GetLastGovernedProposalId());

        _lastElectionResponse = response;
        _lastCommandResponse = new ElectionCommandResponse
        {
            Success = true,
            ErrorMessage = string.Empty,
            Election = response.Election,
            GovernedProposal = proposal,
        };
    }

    [When(@"the owner attempts to update the trustee-threshold draft title to ""(.*)"" while a governed open proposal is pending")]
    public async Task WhenTheOwnerAttemptsToUpdateTheTrusteeThresholdDraftTitleWhileAGovernedOpenProposalIsPending(string title)
    {
        _lastSubmitTransactionResponse = await SubmitDraftUpdateViaBlockchainAsync(
            snapshotReason: "governed-open-pending-draft-edit",
            draft: BuildTrusteeThresholdDraftSpecification(title));
    }

    [When(@"the integration test forces the election into a stale ""(.*)"" state before the governed proposal executes")]
    public async Task WhenTheIntegrationTestForcesTheElectionIntoAStaleStateBeforeTheGovernedProposalExecutes(string lifecycleState)
    {
        var now = DateTime.UtcNow;
        await using var scope = GetNode().Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();

        switch (lifecycleState.Trim().ToLowerInvariant())
        {
            case "closed":
                await dbContext.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE "Elections"."ElectionRecord"
                    SET "LifecycleState" = {0},
                        "LastUpdatedAt" = {1},
                        "OpenedAt" = COALESCE("OpenedAt", {1}),
                        "ClosedAt" = {1},
                        "FinalizedAt" = NULL,
                        "TallyReadyAt" = NULL,
                        "VoteAcceptanceLockedAt" = COALESCE("VoteAcceptanceLockedAt", {1})
                    WHERE "ElectionId" = {2}
                    """,
                    "Closed",
                    now,
                    GetElectionId());
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(lifecycleState), lifecycleState, "Unsupported forced lifecycle state.");
        }
    }

    [When(@"the integration test restores the election to the ""(.*)"" state for governed retry")]
    public async Task WhenTheIntegrationTestRestoresTheElectionToTheStateForGovernedRetry(string lifecycleState)
    {
        var now = DateTime.UtcNow;
        await using var scope = GetNode().Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();

        switch (lifecycleState.Trim().ToLowerInvariant())
        {
            case "draft":
                await dbContext.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE "Elections"."ElectionRecord"
                    SET "LifecycleState" = {0},
                        "LastUpdatedAt" = {1},
                        "OpenedAt" = NULL,
                        "ClosedAt" = NULL,
                        "FinalizedAt" = NULL,
                        "TallyReadyAt" = NULL,
                        "OpenArtifactId" = NULL,
                        "CloseArtifactId" = NULL,
                        "FinalizeArtifactId" = NULL,
                        "VoteAcceptanceLockedAt" = NULL
                    WHERE "ElectionId" = {2}
                    """,
                    "Draft",
                    now,
                    GetElectionId());
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(lifecycleState), lifecycleState, "Unsupported restored lifecycle state.");
        }
    }

    [When(@"the integration test deletes accepted ballot records while leaving (\d+) queued publication entries")]
    public async Task WhenTheIntegrationTestDeletesAcceptedBallotRecordsWhileLeavingQueuedPublicationEntries(int expectedQueuedEntries)
    {
        await using var scope = GetNode().Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var electionId = GetCurrentElectionId();

        var queuedEntries = await dbContext.Set<ElectionBallotMemPoolRecord>()
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.QueuedAt)
            .ToListAsync();
        queuedEntries.Should().HaveCount(expectedQueuedEntries);

        var acceptedBallotIds = queuedEntries
            .Select(x => x.AcceptedBallotId)
            .ToHashSet();
        var acceptedBallots = await dbContext.Set<ElectionAcceptedBallotRecord>()
            .Where(x => acceptedBallotIds.Contains(x.Id))
            .ToListAsync();
        acceptedBallots.Should().HaveCount(expectedQueuedEntries);

        dbContext.RemoveRange(acceptedBallots);
        await dbContext.SaveChangesAsync();
    }

    [When(@"the integration test removes tally-ready and unofficial result artifacts for the closed election")]
    public async Task WhenTheIntegrationTestRemovesTallyReadyAndUnofficialResultArtifactsForTheClosedElection()
    {
        await using var scope = GetNode().Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var electionId = GetCurrentElectionId();

        var tallyReadyArtifacts = await dbContext.Set<ElectionBoundaryArtifactRecord>()
            .Where(x =>
                x.ElectionId == electionId &&
                x.ArtifactType == ElectionBoundaryArtifactType.TallyReady)
            .ToListAsync();
        tallyReadyArtifacts.Should().NotBeEmpty();

        var unofficialResults = await dbContext.Set<ElectionResultArtifactRecord>()
            .Where(x =>
                x.ElectionId == electionId &&
                x.ArtifactKind == ElectionResultArtifactKind.Unofficial)
            .ToListAsync();
        unofficialResults.Should().NotBeEmpty();

        dbContext.RemoveRange(unofficialResults);
        dbContext.RemoveRange(tallyReadyArtifacts);

        var election = await dbContext.Set<ElectionRecord>()
            .SingleAsync(x => x.ElectionId == electionId);
        var updatedElection = election with
        {
            LastUpdatedAt = DateTime.UtcNow,
            TallyReadyAt = null,
            TallyReadyArtifactId = null,
            UnofficialResultArtifactId = null,
            ClosedProgressStatus = ElectionClosedProgressStatus.None,
        };
        dbContext.Entry(election).CurrentValues.SetValues(updatedElection);

        await dbContext.SaveChangesAsync();
    }

    [Then(@"the election lifecycle should progress through ""(.*)""")]
    public void ThenTheElectionLifecycleShouldProgressThrough(string expectedStates)
    {
        var actualStates = _observedStates.Distinct().Select(ToFriendlyState).ToArray();
        var expected = expectedStates
            .Replace("\", and \"", "\", \"", StringComparison.Ordinal)
            .Split("\", \"", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(state => state.Trim('"').Trim())
            .ToArray();

        actualStates.Should().Equal(expected);
    }

    [Then(@"the owner dashboard should include the election")]
    public async Task ThenTheOwnerDashboardShouldIncludeTheElection()
    {
        var request = new GetElectionsByOwnerRequest
        {
            OwnerPublicAddress = GetOwner().PublicSigningAddress,
        };
        var response = await GetClient().GetElectionsByOwnerAsync(
            request,
            headers: CreateSignedElectionQueryHeaders(
                nameof(HushElections.HushElectionsClient.GetElectionsByOwner),
                GetOwner(),
                new Dictionary<string, object?>
                {
                    ["OwnerPublicAddress"] = request.OwnerPublicAddress,
                }));

        response.Elections.Should().Contain(summary =>
            summary.ElectionId == GetElectionId() &&
            summary.LifecycleState == ElectionLifecycleStateProto.Finalized);
    }

    [Then(@"the frozen policy and warning acknowledgement should remain visible after reload")]
    public async Task ThenTheFrozenPolicyAndWarningAcknowledgementShouldRemainVisibleAfterReload()
    {
        var response = _lastElectionResponse ?? await ReloadElectionAsync();

        response.Success.Should().BeTrue();
        response.Election.ElectionId.Should().Be(GetElectionId());
        response.Election.DisclosureMode.Should().Be(ElectionDisclosureModeProto.FinalResultsOnly);
        response.Election.BindingStatus.Should().Be(ElectionBindingStatusProto.Binding);
        response.WarningAcknowledgements.Should().Contain(ack =>
            ack.WarningCode == ElectionWarningCodeProto.LowAnonymitySet);
        response.BoundaryArtifacts.Should().Contain(artifact =>
            artifact.ArtifactType == ElectionBoundaryArtifactTypeProto.OpenArtifact &&
            artifact.Policy.DisclosureMode == ElectionDisclosureModeProto.FinalResultsOnly &&
            artifact.Policy.BindingStatus == ElectionBindingStatusProto.Binding);

        _lastElectionResponse = response;
    }

    [Then(@"the boundary artifacts should include open, close, and finalize records")]
    public async Task ThenTheBoundaryArtifactsShouldIncludeOpenCloseAndFinalizeRecords()
    {
        var response = _lastElectionResponse ?? await ReloadElectionAsync();

        response.BoundaryArtifacts.Select(artifact => artifact.ArtifactType).Should().Contain(
        [
            ElectionBoundaryArtifactTypeProto.OpenArtifact,
            ElectionBoundaryArtifactTypeProto.CloseArtifact,
            ElectionBoundaryArtifactTypeProto.FinalizeArtifact,
        ]);
    }

    [Then(@"the election should expose a tally-ready boundary after close drain")]
    public async Task ThenTheElectionShouldExposeATallyReadyBoundaryAfterCloseDrain()
    {
        var response = await WaitForTallyReadyElectionAsync();

        response.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        response.Election.TallyReadyAt.Should().NotBeNull();
        response.Election.TallyReadyArtifactId.Should().NotBeNullOrWhiteSpace();
        response.BoundaryArtifacts.Select(artifact => artifact.ArtifactType).Should().Contain(
        [
            ElectionBoundaryArtifactTypeProto.OpenArtifact,
            ElectionBoundaryArtifactTypeProto.CloseArtifact,
            ElectionBoundaryArtifactTypeProto.TallyReadyArtifact,
        ]);

        _lastElectionResponse = response;
    }

    [Then(@"the tally-ready boundary should reconcile (\d+) accepted ballots and (\d+) published ballots")]
    public async Task ThenTheTallyReadyBoundaryShouldReconcileAcceptedAndPublishedBallots(
        int expectedAcceptedBallots,
        int expectedPublishedBallots)
    {
        var response = await WaitForTallyReadyElectionAsync();
        var tallyReadyArtifact = response.BoundaryArtifacts.Single(x =>
            x.ArtifactType == ElectionBoundaryArtifactTypeProto.TallyReadyArtifact);

        tallyReadyArtifact.AcceptedBallotCount.Should().Be(expectedAcceptedBallots);
        tallyReadyArtifact.PublishedBallotCount.Should().Be(expectedPublishedBallots);
        tallyReadyArtifact.AcceptedBallotSetHash.ToByteArray().Should().NotBeEmpty();
        tallyReadyArtifact.PublishedBallotStreamHash.ToByteArray().Should().NotBeEmpty();
        tallyReadyArtifact.FinalEncryptedTallyHash.ToByteArray().Should().NotBeEmpty();
        response.Election.TallyReadyArtifactId.Should().Be(tallyReadyArtifact.Id);

        _lastElectionResponse = response;
    }

    [Then(@"the immutable update transaction should be rejected before indexing")]
    public void ThenTheImmutableUpdateTransactionShouldBeRejectedBeforeIndexing()
    {
        _lastSubmitTransactionResponse.Should().NotBeNull();
        _lastSubmitTransactionResponse!.Successfull.Should().BeFalse();
    }

    [Then(@"the open-time binding status should remain ""(.*)""")]
    public async Task ThenTheOpenTimeBindingStatusShouldRemain(string expectedBindingStatus)
    {
        var expected = expectedBindingStatus switch
        {
            "Binding" => ElectionBindingStatusProto.Binding,
            "NonBinding" => ElectionBindingStatusProto.NonBinding,
            _ => throw new ArgumentOutOfRangeException(nameof(expectedBindingStatus), expectedBindingStatus, "Unsupported binding status."),
        };

        var response = await ReloadElectionAsync();

        response.Election.BindingStatus.Should().Be(expected);
        response.BoundaryArtifacts.Should().Contain(artifact =>
            artifact.ArtifactType == ElectionBoundaryArtifactTypeProto.OpenArtifact &&
            artifact.Policy.BindingStatus == expected);

        _lastElectionResponse = response;
    }

    [Then(@"the readiness response should include the missing ready-ceremony blocker")]
    public void ThenTheReadinessResponseShouldIncludeTheMissingReadyCeremonyBlocker()
    {
        _lastReadinessResponse.Should().NotBeNull();
        _lastReadinessResponse!.ValidationErrors.Should().Contain(error =>
            error.Contains("key-ceremony version", StringComparison.OrdinalIgnoreCase));
    }

    [Then(@"the trustee-threshold open transaction should be rejected before the MemPool")]
    public void ThenTheTrusteeThresholdOpenTransactionShouldBeRejectedBeforeTheMemPool()
    {
        _lastSubmitTransactionResponse.Should().NotBeNull();
        _lastSubmitTransactionResponse!.Successfull.Should().BeFalse();
    }

    [Then(@"the legacy plaintext election transaction should be rejected before the MemPool")]
    public void ThenTheLegacyPlaintextElectionTransactionShouldBeRejectedBeforeTheMemPool()
    {
        _lastSubmitTransactionResponse.Should().NotBeNull();
        _lastSubmitTransactionResponse!.Successfull.Should().BeFalse();
        _lastSubmitTransactionResponse.Status.Should().Be(TransactionStatus.Rejected);
        _lastSubmitTransactionResponse.Message.Should().Contain("not accepted for direct submission");
    }

    [Then(@"the election should remain in ""(.*)""")]
    public async Task ThenTheElectionShouldRemainIn(string lifecycleState)
    {
        var response = await ReloadElectionAsync();
        response.Election.LifecycleState.Should().Be(ParseProtoLifecycleState(lifecycleState));
        _lastElectionResponse = response;
    }

    [Then(@"the publication issue log should contain (\d+) ""(.*)"" issue with occurrence count (\d+)")]
    public async Task ThenThePublicationIssueLogShouldContainIssueWithOccurrenceCount(
        int expectedRecordCount,
        string issueCode,
        int expectedOccurrenceCount)
    {
        await using var scope = GetNode().Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var parsedIssueCode = ParsePublicationIssueCode(issueCode);
        var issues = await dbContext.Set<ElectionPublicationIssueRecord>()
            .Where(x => x.ElectionId == GetCurrentElectionId() && x.IssueCode == parsedIssueCode)
            .ToListAsync();

        issues.Should().HaveCount(expectedRecordCount);
        issues.Should().OnlyContain(x => x.OccurrenceCount == expectedOccurrenceCount);
    }

    [Then(@"the publication issue log should contain (\d+) ""(.*)"" issue with occurrence count at least (\d+)")]
    public async Task ThenThePublicationIssueLogShouldContainIssueWithOccurrenceCountAtLeast(
        int expectedRecordCount,
        string issueCode,
        int minimumOccurrenceCount)
    {
        await using var scope = GetNode().Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var parsedIssueCode = ParsePublicationIssueCode(issueCode);
        var issues = await dbContext.Set<ElectionPublicationIssueRecord>()
            .Where(x => x.ElectionId == GetCurrentElectionId() && x.IssueCode == parsedIssueCode)
            .ToListAsync();

        issues.Should().HaveCount(expectedRecordCount);
        issues.Should().OnlyContain(x => x.OccurrenceCount >= minimumOccurrenceCount);
    }

    [Then(@"(\d+) ballot mempool entries should remain queued for the election")]
    public async Task ThenBallotMempoolEntriesShouldRemainQueuedForTheElection(int expectedCount)
    {
        await using var scope = GetNode().Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var entries = await dbContext.Set<ElectionBallotMemPoolRecord>()
            .Where(x => x.ElectionId == GetCurrentElectionId())
            .ToListAsync();

        entries.Should().HaveCount(expectedCount);
    }

    [Then(@"the pending governed open should block further draft changes")]
    public void ThenThePendingGovernedOpenShouldBlockFurtherDraftChanges()
    {
        _lastSubmitTransactionResponse.Should().NotBeNull();
        _lastSubmitTransactionResponse!.Successfull.Should().BeFalse();
    }

    [Then(@"the governed proposal should remain pending for ""(.*)"" while the election stays ""(.*)""")]
    public async Task ThenTheGovernedProposalShouldRemainPendingForWhileTheElectionStays(string actionType, string lifecycleState)
    {
        var response = await ReloadElectionAsync();
        var proposal = response.GovernedProposals.Single(x => x.Id == GetLastGovernedProposalId());

        proposal.ActionType.Should().Be(ParseGovernedActionType(actionType));
        proposal.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatusProto.WaitingForApprovals);
        response.Election.LifecycleState.Should().Be(ParseProtoLifecycleState(lifecycleState));

        _lastElectionResponse = response;
    }

    [Then(@"vote acceptance should be locked immediately on the election")]
    public async Task ThenVoteAcceptanceShouldBeLockedImmediatelyOnTheElection()
    {
        var response = _lastElectionResponse ?? await ReloadElectionAsync();

        response.Election.VoteAcceptanceLockedAt.Should().NotBeNull();
        response.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Open);

        _lastElectionResponse = response;
    }

    [Then(@"the governed proposal should execute and transition the election to ""(.*)""")]
    public async Task ThenTheGovernedProposalShouldExecuteAndTransitionTheElectionTo(string lifecycleState)
    {
        var response = await ReloadElectionAsync();
        var proposal = response.GovernedProposals.Single(x => x.Id == GetLastGovernedProposalId());

        proposal.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatusProto.ExecutionSucceeded);
        response.Election.LifecycleState.Should().Be(ParseProtoLifecycleState(lifecycleState));
        response.GovernedProposalApprovals.Should().Contain(x => x.ProposalId == proposal.Id);

        _lastElectionResponse = response;
    }

    [Then(@"the governed finalize should open a bound finalization session while the election stays ""(.*)""")]
    public async Task ThenTheGovernedFinalizeShouldOpenABoundFinalizationSessionWhileTheElectionStays(string lifecycleState)
    {
        var response = await ReloadElectionAsync();
        var proposal = response.GovernedProposals.Single(x => x.Id == GetLastGovernedProposalId());
        var session = GetAwaitingFinalizationSession(response);

        proposal.ActionType.Should().Be(ElectionGovernedActionTypeProto.GovernedActionFinalize);
        proposal.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatusProto.ExecutionSucceeded);
        response.Election.LifecycleState.Should().Be(ParseProtoLifecycleState(lifecycleState));
        response.Election.FinalizeArtifactId.Should().BeEmpty();
        response.FinalizationSessions.Should().ContainSingle();
        session.RequiredShareCount.Should().Be(3);
        session.EligibleTrustees.Should().HaveCount(3);
        session.CeremonySnapshot.Should().NotBeNull();
        response.FinalizationReleaseEvidenceRecords.Should().BeEmpty();

        _lastElectionResponse = response;
    }

    [Then(@"the close workflow should open a bound close-counting session while the election stays ""(.*)""")]
    public async Task ThenTheCloseWorkflowShouldOpenABoundCloseCountingSessionWhileTheElectionStays(string lifecycleState)
    {
        GetElectionResponse response = _lastElectionResponse ?? await ReloadElectionAsync();

        for (var attempt = 0;
             attempt < 20 &&
             response.FinalizationSessions.All(x =>
                 x.Status != ElectionFinalizationSessionStatusProto.FinalizationSessionAwaitingShares ||
                 x.SessionPurpose != ElectionFinalizationSessionPurposeProto.FinalizationSessionPurposeCloseCounting);
             attempt++)
        {
            await Task.Delay(100);
            response = await ReloadElectionAsync();
        }

        var proposal = response.GovernedProposals.Single(x => x.Id == GetLastGovernedProposalId());
        var session = response.FinalizationSessions.Single(x =>
            x.Status == ElectionFinalizationSessionStatusProto.FinalizationSessionAwaitingShares &&
            x.SessionPurpose == ElectionFinalizationSessionPurposeProto.FinalizationSessionPurposeCloseCounting);

        proposal.ActionType.Should().Be(ElectionGovernedActionTypeProto.GovernedActionClose);
        proposal.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatusProto.ExecutionSucceeded);
        response.Election.LifecycleState.Should().Be(ParseProtoLifecycleState(lifecycleState));
        response.Election.TallyReadyArtifactId.Should().BeNullOrEmpty();
        response.Election.UnofficialResultArtifactId.Should().BeNullOrEmpty();
        response.FinalizationSessions.Should().ContainSingle();
        session.SessionPurpose.Should().Be(ElectionFinalizationSessionPurposeProto.FinalizationSessionPurposeCloseCounting);
        session.RequiredShareCount.Should().Be(3);
        session.EligibleTrustees.Should().HaveCount(3);
        session.CeremonySnapshot.Should().NotBeNull();
        response.FinalizationReleaseEvidenceRecords.Should().BeEmpty();

        _lastElectionResponse = response;
    }

    [Then(@"the finalization share log should record rejection code ""(.*)"" for trustee ""(.*)""")]
    public async Task ThenTheFinalizationShareLogShouldRecordRejectionCodeForTrustee(
        string failureCode,
        string trusteeAlias)
    {
        var trustee = ResolveIdentity(trusteeAlias);
        var response = _lastElectionResponse ?? await ReloadElectionAsync();

        response.FinalizationShares.Should().Contain(x =>
            x.TrusteeUserAddress == trustee.PublicSigningAddress &&
            x.Status == ElectionFinalizationShareStatusProto.FinalizationShareRejected &&
            x.FailureCode == failureCode);
        response.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);

        _lastElectionResponse = response;
    }

    [Then(@"the finalization session should remain waiting for (.*) accepted shares")]
    public async Task ThenTheFinalizationSessionShouldRemainWaitingForAcceptedShares(int acceptedShareCount)
    {
        var response = _lastElectionResponse ?? await ReloadElectionAsync();
        var session = GetAwaitingFinalizationSession(response);

        response.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        response.FinalizationShares.Count(x =>
            x.Status == ElectionFinalizationShareStatusProto.FinalizationShareAccepted)
            .Should()
            .Be(acceptedShareCount);
        response.FinalizationReleaseEvidenceRecords.Should().BeEmpty();
        session.RequiredShareCount.Should().BeGreaterThan(acceptedShareCount);

        _lastElectionResponse = response;
    }

    [Then(@"the finalization release evidence should record (.*) accepted trustee shares")]
    public async Task ThenTheFinalizationReleaseEvidenceShouldRecordAcceptedTrusteeShares(int acceptedShareCount)
    {
        var response = _lastElectionResponse ?? await ReloadElectionAsync();
        var releaseEvidence = response.FinalizationReleaseEvidenceRecords.Should().ContainSingle().Subject;
        var completedSession = response.FinalizationSessions.Should().ContainSingle().Subject;

        response.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Finalized);
        completedSession.Status.Should().Be(ElectionFinalizationSessionStatusProto.FinalizationSessionCompleted);
        releaseEvidence.AcceptedShareCount.Should().Be(acceptedShareCount);
        releaseEvidence.AcceptedTrustees.Should().HaveCount(acceptedShareCount);
        response.FinalizationShares.Count(x =>
            x.Status == ElectionFinalizationShareStatusProto.FinalizationShareAccepted)
            .Should()
            .Be(acceptedShareCount);

        _lastElectionResponse = response;
    }

    [Then(@"the close-counting release evidence should record (.*) accepted trustee shares")]
    public async Task ThenTheCloseCountingReleaseEvidenceShouldRecordAcceptedTrusteeShares(int acceptedShareCount)
    {
        var response = await WaitForTallyReadyElectionAsync();
        var releaseEvidence = response.FinalizationReleaseEvidenceRecords.Should().ContainSingle().Subject;
        var completedSession = response.FinalizationSessions.Should().ContainSingle().Subject;

        response.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        response.Election.TallyReadyAt.Should().NotBeNull();
        completedSession.Status.Should().Be(ElectionFinalizationSessionStatusProto.FinalizationSessionCompleted);
        completedSession.SessionPurpose.Should().Be(ElectionFinalizationSessionPurposeProto.FinalizationSessionPurposeCloseCounting);
        releaseEvidence.AcceptedShareCount.Should().Be(acceptedShareCount);
        releaseEvidence.SessionPurpose.Should().Be(ElectionFinalizationSessionPurposeProto.FinalizationSessionPurposeCloseCounting);
        releaseEvidence.AcceptedTrustees.Should().HaveCount(acceptedShareCount);
        response.FinalizationShares.Count(x =>
            x.Status == ElectionFinalizationShareStatusProto.FinalizationShareAccepted)
            .Should()
            .Be(acceptedShareCount);

        _lastElectionResponse = response;
    }

    [Then(@"the election closed progress status should be ""(.*)""")]
    public async Task ThenTheElectionClosedProgressStatusShouldBe(string expectedProgressStatus)
    {
        var expected = ParseClosedProgressStatus(expectedProgressStatus);
        GetElectionResponse response = _lastElectionResponse ?? await ReloadElectionAsync();

        for (var attempt = 0; attempt < 20 && response.Election.ClosedProgressStatus != expected; attempt++)
        {
            await Task.Delay(100);
            response = await ReloadElectionAsync();
        }

        response.Election.ClosedProgressStatus.Should().Be(expected);
        _lastElectionResponse = response;
    }

    [Then(@"the election should not expose a tally-ready boundary yet")]
    public async Task ThenTheElectionShouldNotExposeATallyReadyBoundaryYet()
    {
        var response = _lastElectionResponse ?? await ReloadElectionAsync();

        response.Election.TallyReadyAt.Should().BeNull();
        response.Election.TallyReadyArtifactId.Should().BeNullOrEmpty();
        response.BoundaryArtifacts.Should().NotContain(x =>
            x.ArtifactType == ElectionBoundaryArtifactTypeProto.TallyReadyArtifact);

        _lastElectionResponse = response;
    }

    [Then(@"the eligibility view should show actor role ""(.*)""")]
    public void ThenTheEligibilityViewShouldShowActorRole(string expectedActorRole)
    {
        GetLastEligibilityView()
            .ActorRole
            .Should()
            .Be(ParseEligibilityActorRole(expectedActorRole));
    }

    [Then(@"the eligibility view should expose temporary verification code ""(.*)""")]
    public void ThenTheEligibilityViewShouldExposeTemporaryVerificationCode(string expectedVerificationCode)
    {
        var response = GetLastEligibilityView();
        response.UsesTemporaryVerificationCode.Should().BeTrue();
        response.TemporaryVerificationCode.Should().Be(expectedVerificationCode);
    }

    [Then(@"the eligibility self row should show organization voter ""(.*)"" as ""(.*)"" with participation ""(.*)""")]
    public void ThenTheEligibilitySelfRowShouldShowOrganizationVoterAsWithParticipation(
        string organizationVoterId,
        string expectedVotingRightStatus,
        string expectedParticipationStatus)
    {
        var selfRosterEntry = GetLastEligibilityView().SelfRosterEntry;
        selfRosterEntry.Should().NotBeNull("linked voters should receive a self roster entry view");
        selfRosterEntry.OrganizationVoterId.Should().Be(organizationVoterId);
        selfRosterEntry.VotingRightStatus.Should().Be(ParseVotingRightStatus(expectedVotingRightStatus));
        selfRosterEntry.ParticipationStatus.Should().Be(ParseParticipationStatus(expectedParticipationStatus));
    }

    [Then(@"the owner eligibility summary should report (\d+) rostered voters, (\d+) linked voters, and (\d+) activation events")]
    public void ThenTheOwnerEligibilitySummaryShouldReportCounts(
        int expectedRosteredCount,
        int expectedLinkedCount,
        int expectedActivationEventCount)
    {
        var response = GetLastEligibilityView();
        response.CanReviewRestrictedRoster.Should().BeTrue();
        response.Summary.RosteredCount.Should().Be(expectedRosteredCount);
        response.Summary.LinkedCount.Should().Be(expectedLinkedCount);
        response.Summary.ActivationEventCount.Should().Be(expectedActivationEventCount);
    }

    [Then(@"the restricted eligibility roster should include (\d+) entries")]
    public void ThenTheRestrictedEligibilityRosterShouldIncludeEntries(int expectedEntryCount)
    {
        GetLastEligibilityView()
            .RestrictedRosterEntries
            .Should()
            .HaveCount(expectedEntryCount);
    }

    [Then(@"the voting view should show commitment as registered")]
    public void ThenTheVotingViewShouldShowCommitmentAsRegistered()
    {
        var response = GetLastVotingView();
        response.CommitmentRegistered.Should().BeTrue();
        response.HasCommitmentRegisteredAt.Should().BeTrue();
        response.SelfRosterEntry.Should().NotBeNull();
    }

    [Then(@"the voting view should show submission status ""(.*)""")]
    public void ThenTheVotingViewShouldShowSubmissionStatus(string expectedSubmissionStatus)
    {
        GetLastVotingView()
            .SubmissionStatus
            .Should()
            .Be(ParseVotingSubmissionStatus(expectedSubmissionStatus));
    }

    [Then(@"the voting view should show personal participation ""(.*)""")]
    public void ThenTheVotingViewShouldShowPersonalParticipation(string expectedParticipationStatus)
    {
        GetLastVotingView()
            .PersonalParticipationStatus
            .Should()
            .Be(ParseParticipationStatus(expectedParticipationStatus));
    }

    [Then(@"the voting view should expose acceptance receipt metadata")]
    public void ThenTheVotingViewShouldExposeAcceptanceReceiptMetadata()
    {
        var response = GetLastVotingView();
        response.HasAcceptedAt.Should().BeTrue();
        response.AcceptedAt.Should().NotBeNull();
        response.AcceptanceId.Should().NotBeNullOrWhiteSpace();
        response.ReceiptId.Should().NotBeNullOrWhiteSpace();
        response.ServerProof.Should().NotBeNullOrWhiteSpace();
    }

    [Then(@"the voting view should not expose acceptance receipt metadata")]
    public void ThenTheVotingViewShouldNotExposeAcceptanceReceiptMetadata()
    {
        var response = GetLastVotingView();
        response.HasAcceptedAt.Should().BeFalse();
        response.AcceptanceId.Should().BeNullOrEmpty();
        response.ReceiptId.Should().BeNullOrEmpty();
        response.ServerProof.Should().BeNullOrEmpty();
    }

    [Then(@"the election result view for actor ""(.*)"" should expose participant-encrypted unofficial results")]
    public async Task ThenTheElectionResultViewForActorShouldExposeParticipantEncryptedUnofficialResults(string actorAlias)
    {
        var response = await GetElectionResultViewAsync(ResolveIdentity(actorAlias), waitForUnofficialResult: true);
        var unofficialResult = response.UnofficialResult;

        response.CanViewParticipantEncryptedResults.Should().BeTrue();
        unofficialResult.Should().NotBeNull();
        unofficialResult!.Id.Should().NotBeNullOrWhiteSpace();
        unofficialResult.Visibility.Should().Be(ElectionResultArtifactVisibilityProto.ElectionResultArtifactParticipantEncrypted);
        unofficialResult.TallyReadyArtifactId.Should().NotBeNullOrWhiteSpace();
        string.IsNullOrWhiteSpace(response.OfficialResult?.Id).Should().BeTrue();

        _lastResultViewResponse = response;
    }

    [Then(@"the unofficial result should report (\d+) total voted, (\d+) eligible to vote, (\d+) did not vote, and (\d+) blank")]
    public void ThenTheUnofficialResultShouldReportVoteTotals(
        int expectedTotalVotedCount,
        int expectedEligibleToVoteCount,
        int expectedDidNotVoteCount,
        int expectedBlankCount)
    {
        var unofficialResult = GetLastResultView().UnofficialResult;

        unofficialResult.TotalVotedCount.Should().Be(expectedTotalVotedCount);
        unofficialResult.EligibleToVoteCount.Should().Be(expectedEligibleToVoteCount);
        unofficialResult.DidNotVoteCount.Should().Be(expectedDidNotVoteCount);
        unofficialResult.BlankCount.Should().Be(expectedBlankCount);
        unofficialResult.NamedOptionResults.Sum(x => x.VoteCount).Should().Be(
            expectedTotalVotedCount - expectedBlankCount);
    }

    [Then(@"the unofficial result should include all named options")]
    public async Task ThenTheUnofficialResultShouldIncludeAllNamedOptions()
    {
        var response = _lastElectionResponse ?? await ReloadElectionAsync();
        var unofficialResult = GetLastResultView().UnofficialResult;

        unofficialResult.NamedOptionResults.Should().HaveCount(response.Election.Options.Count(x => !x.IsBlankOption));
        unofficialResult.NamedOptionResults.Select(x => x.OptionId).Should().OnlyHaveUniqueItems();
    }

    [Then(@"the official result should copy the unofficial result for actor ""(.*)""")]
    public async Task ThenTheOfficialResultShouldCopyTheUnofficialResultForActor(string actorAlias)
    {
        var response = await GetElectionResultViewAsync(ResolveIdentity(actorAlias), waitForOfficialResult: true);
        var unofficialResult = response.UnofficialResult;
        var officialResult = response.OfficialResult;

        unofficialResult.Should().NotBeNull();
        officialResult.Should().NotBeNull();
        officialResult!.Id.Should().NotBeNullOrWhiteSpace();
        officialResult.SourceResultArtifactId.Should().Be(unofficialResult!.Id);
        officialResult.Title.Should().Be(unofficialResult.Title);
        officialResult.BlankCount.Should().Be(unofficialResult.BlankCount);
        officialResult.TotalVotedCount.Should().Be(unofficialResult.TotalVotedCount);
        officialResult.EligibleToVoteCount.Should().Be(unofficialResult.EligibleToVoteCount);
        officialResult.DidNotVoteCount.Should().Be(unofficialResult.DidNotVoteCount);
        officialResult.NamedOptionResults.Should().BeEquivalentTo(
            unofficialResult.NamedOptionResults,
            options => options.WithStrictOrdering());

        _lastResultViewResponse = response;
    }

    [Then(@"the election result view for actor ""(.*)"" should expose a sealed report package with (\d+) downloadable artifacts")]
    public async Task ThenTheElectionResultViewForActorShouldExposeASealedReportPackageWithDownloadableArtifacts(
        string actorAlias,
        int expectedArtifactCount)
    {
        var response = await GetElectionResultViewAsync(ResolveIdentity(actorAlias), waitForOfficialResult: true);

        response.CanViewReportPackage.Should().BeTrue();
        response.LatestReportPackage.Should().NotBeNull();
        response.LatestReportPackage!.Status.Should().Be(ElectionReportPackageStatusProto.ReportPackageSealed);
        response.VisibleReportArtifacts.Should().HaveCount(expectedArtifactCount);
        response.VisibleReportArtifacts.Should().OnlyContain(x =>
            !string.IsNullOrWhiteSpace(x.Id) &&
            !string.IsNullOrWhiteSpace(x.Title) &&
            !string.IsNullOrWhiteSpace(x.FileName) &&
            x.ContentHash.Length > 0 &&
            !string.IsNullOrWhiteSpace(x.Content));

        _lastResultViewResponse = response;
    }

    [Then(@"the visible report artifacts should include the named participation roster artifacts")]
    public void ThenTheVisibleReportArtifactsShouldIncludeTheNamedParticipationRosterArtifacts()
    {
        var artifacts = GetLastResultView().VisibleReportArtifacts;
        artifacts.Should().Contain(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactHumanNamedParticipationRoster);
        artifacts.Should().Contain(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactMachineNamedParticipationRosterProjection);
    }

    [Then(@"the visible report artifacts should include the audit provenance artifact")]
    public void ThenTheVisibleReportArtifactsShouldIncludeTheAuditProvenanceArtifact()
    {
        GetLastResultView().VisibleReportArtifacts.Should().Contain(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactHumanAuditProvenanceReport);
    }

    [Then(@"the visible report artifacts should all include downloadable content")]
    public void ThenTheVisibleReportArtifactsShouldAllIncludeDownloadableContent()
    {
        GetLastResultView().VisibleReportArtifacts.Should().OnlyContain(x =>
            !string.IsNullOrWhiteSpace(x.FileName) &&
            !string.IsNullOrWhiteSpace(x.MediaType) &&
            x.ContentHash.Length > 0 &&
            !string.IsNullOrWhiteSpace(x.Content));
    }

    [Then(@"the last blockchain submission should be rejected with validation code ""(.*)""")]
    public void ThenTheLastBlockchainSubmissionShouldBeRejectedWithValidationCode(string expectedValidationCode)
    {
        _lastSubmitTransactionResponse.Should().NotBeNull();
        _lastSubmitTransactionResponse!.Successfull.Should().BeFalse();
        _lastSubmitTransactionResponse.Status.Should().Be(TransactionStatus.Rejected);
        _lastSubmitTransactionResponse.ValidationCode.Should().Be(expectedValidationCode);
    }

    [Then(@"only the committed FEAT-099 acceptance artifacts should remain for actor ""(.*)"" and idempotency key ""(.*)""")]
    public async Task ThenOnlyTheCommittedFeat099AcceptanceArtifactsShouldRemainForActorAndIdempotencyKey(
        string actorAlias,
        string submissionIdempotencyKey)
    {
        var actor = ResolveIdentity(actorAlias);
        var electionId = GetCurrentElectionId();
        var expectedNullifier = BuildBallotNullifier(actor, submissionIdempotencyKey);
        CountPendingBallotCastSubmissions(actor, submissionIdempotencyKey)
            .Should()
            .Be(0, "the FEAT-099 cast should no longer remain in the mempool after block inclusion");

        var votingView = await GetElectionVotingViewAsync(actor);
        votingView.SelfRosterEntry.Should().NotBeNull();
        var organizationVoterId = votingView.SelfRosterEntry!.OrganizationVoterId;
        var expectedIdempotencyHash = ComputeElectionScopedIdempotencyHash(submissionIdempotencyKey);

        await using var scope = GetNode().Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();

        var checkoffConsumptions = await dbContext.Set<ElectionCheckoffConsumptionRecord>()
            .Where(x => x.ElectionId == electionId && x.OrganizationVoterId == organizationVoterId)
            .ToListAsync();
        checkoffConsumptions.Should().ContainSingle();
        checkoffConsumptions.Single().ParticipationStatus.Should().Be(ElectionParticipationStatus.CountedAsVoted);

        var acceptedBallots = await dbContext.Set<ElectionAcceptedBallotRecord>()
            .Where(x => x.ElectionId == electionId && x.BallotNullifier == expectedNullifier)
            .ToListAsync();
        acceptedBallots.Should().ContainSingle();
        acceptedBallots.Single().ProofBundle.Should().NotBeNullOrWhiteSpace();

        var idempotencyRecords = await dbContext.Set<ElectionCastIdempotencyRecord>()
            .Where(x => x.ElectionId == electionId && x.IdempotencyKeyHash == expectedIdempotencyHash)
            .ToListAsync();
        idempotencyRecords.Should().ContainSingle();

        var cacheService = scope.ServiceProvider.GetRequiredService<IElectionCastIdempotencyCacheService>();
        var cachedMarker = await cacheService.ExistsAsync(
            electionId.ToString(),
            expectedIdempotencyHash);
        cachedMarker.Should().BeTrue(
            "the FEAT-099 committed idempotency marker should remain available in Redis for short-term same-election reuse detection");
    }

    [Then(@"the governed proposal should record an execution failure for ""(.*)""")]
    public async Task ThenTheGovernedProposalShouldRecordAnExecutionFailureFor(string actionType)
    {
        var response = await ReloadElectionAsync();
        var proposal = response.GovernedProposals.Single(x => x.Id == GetLastGovernedProposalId());

        proposal.ActionType.Should().Be(ParseGovernedActionType(actionType));
        proposal.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatusProto.ExecutionFailed);
        proposal.ExecutionFailureReason.Should().NotBeNullOrWhiteSpace();
        response.GovernedProposalApprovals.Should().Contain(x => x.ProposalId == proposal.Id);

        _lastElectionResponse = response;
    }

    private GrpcClientFactory GetGrpcFactory() =>
        _scenarioContext.Get<GrpcClientFactory>(ScenarioHooks.GrpcFactoryKey);

    private HushServerNodeCore GetNode() =>
        _scenarioContext.Get<HushServerNodeCore>(ScenarioHooks.NodeKey);

    private BlockProductionControl GetBlockControl() =>
        _scenarioContext.Get<BlockProductionControl>(ScenarioHooks.BlockControlKey);

    private HushElections.HushElectionsClient GetClient() =>
        _client ?? throw new InvalidOperationException("Election gRPC client not initialized. Call the availability step first.");

    private HushBlockchain.HushBlockchainClient GetBlockchainClient() =>
        GetGrpcFactory().CreateClient<HushBlockchain.HushBlockchainClient>();

    private TestIdentity GetOwner() =>
        _owner ?? throw new InvalidOperationException("Owner identity not initialized. Call the availability step first.");

    private string GetElectionId() =>
        _electionId ?? throw new InvalidOperationException("Election ID not initialized.");

    private string GetLastGovernedProposalId() =>
        _lastGovernedProposalId ?? throw new InvalidOperationException("No governed proposal has been recorded for this scenario.");

    private string GetTrusteeInvitationId(string trusteePublicAddress) =>
        _trusteeInvitationIds.TryGetValue(trusteePublicAddress, out var invitationId)
            ? invitationId
            : throw new InvalidOperationException($"No trustee invitation recorded for {trusteePublicAddress}.");

    private async Task<GetElectionResponse> ReloadElectionAsync()
    {
        var owner = GetOwner();
        var request = new GetElectionRequest
        {
            ElectionId = GetElectionId(),
        };
        var response = await GetClient().GetElectionAsync(
            request,
            headers: CreateSignedElectionQueryHeaders(
                nameof(HushElections.HushElectionsClient.GetElection),
                owner,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = request.ElectionId,
                }));

        response.Success.Should().BeTrue($"GetElection should succeed for {GetElectionId()}: {response.ErrorMessage}");
        return response;
    }

    private async Task<GetElectionResponse> WaitForTallyReadyElectionAsync()
    {
        GetElectionResponse response = _lastElectionResponse ?? await ReloadElectionAsync();

        for (var attempt = 0;
             attempt < 20 &&
             (response.Election.TallyReadyAt is null ||
              response.BoundaryArtifacts.All(x => x.ArtifactType != ElectionBoundaryArtifactTypeProto.TallyReadyArtifact));
             attempt++)
        {
            await Task.Delay(100);
            response = await ReloadElectionAsync();
        }

        _lastElectionResponse = response;
        return response;
    }

    private async Task<GetElectionEligibilityViewResponse> GetElectionEligibilityViewAsync(TestIdentity actor)
    {
        var request = new GetElectionEligibilityViewRequest
        {
            ElectionId = GetElectionId(),
            ActorPublicAddress = actor.PublicSigningAddress,
        };
        var response = await GetClient().GetElectionEligibilityViewAsync(
            request,
            headers: CreateSignedElectionQueryHeaders(
                nameof(HushElections.HushElectionsClient.GetElectionEligibilityView),
                actor,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = request.ElectionId,
                    ["ActorPublicAddress"] = request.ActorPublicAddress,
                }));

        response.Success.Should().BeTrue(
            $"GetElectionEligibilityView should succeed for {GetElectionId()} and actor {actor.PublicSigningAddress}: {response.ErrorMessage}");
        return response;
    }

    private async Task<GetElectionVotingViewResponse> GetElectionVotingViewAsync(
        TestIdentity actor,
        string? submissionIdempotencyKey = null)
    {
        async Task<GetElectionVotingViewResponse> QueryAsync()
        {
            var request = new GetElectionVotingViewRequest
            {
                ElectionId = GetElectionId(),
                ActorPublicAddress = actor.PublicSigningAddress,
                SubmissionIdempotencyKey = submissionIdempotencyKey ?? string.Empty,
            };

            return await GetClient().GetElectionVotingViewAsync(
                request,
                headers: CreateSignedElectionQueryHeaders(
                    nameof(HushElections.HushElectionsClient.GetElectionVotingView),
                    actor,
                    new Dictionary<string, object?>
                    {
                        ["ElectionId"] = request.ElectionId,
                        ["ActorPublicAddress"] = request.ActorPublicAddress,
                        ["SubmissionIdempotencyKey"] = request.SubmissionIdempotencyKey,
                    }));
        }

        var response = await QueryAsync();

        if (!string.IsNullOrWhiteSpace(submissionIdempotencyKey))
        {
            for (var attempt = 0;
                 attempt < 10 && response.SubmissionStatus == ElectionVotingSubmissionStatusProto.VotingSubmissionStatusNone;
                 attempt++)
            {
                await Task.Delay(100);
                response = await QueryAsync();
            }
        }

        response.Success.Should().BeTrue(
            $"GetElectionVotingView should succeed for {GetElectionId()} and actor {actor.PublicSigningAddress}: {response.ErrorMessage}");
        return response;
    }

    private async Task<GetElectionResultViewResponse> GetElectionResultViewAsync(
        TestIdentity actor,
        bool waitForUnofficialResult = false,
        bool waitForOfficialResult = false)
    {
        async Task<GetElectionResultViewResponse> QueryAsync()
        {
            var request = new GetElectionResultViewRequest
            {
                ElectionId = GetElectionId(),
                ActorPublicAddress = actor.PublicSigningAddress,
            };

            return await GetClient().GetElectionResultViewAsync(
                request,
                headers: CreateSignedElectionQueryHeaders(
                    nameof(HushElections.HushElectionsClient.GetElectionResultView),
                    actor,
                    new Dictionary<string, object?>
                    {
                        ["ElectionId"] = request.ElectionId,
                        ["ActorPublicAddress"] = request.ActorPublicAddress,
                    }));
        }

        var response = await QueryAsync();

        for (var attempt = 0;
             attempt < 20 &&
             ((waitForUnofficialResult && string.IsNullOrWhiteSpace(response.UnofficialResult?.Id)) ||
              (waitForOfficialResult && string.IsNullOrWhiteSpace(response.OfficialResult?.Id)));
             attempt++)
        {
            await Task.Delay(100);
            response = await QueryAsync();
        }

        response.Success.Should().BeTrue(
            $"GetElectionResultView should succeed for {GetElectionId()} and actor {actor.PublicSigningAddress}: {response.ErrorMessage}");
        return response;
    }

    private static Metadata CreateSignedElectionQueryHeaders(
        string method,
        TestIdentity actor,
        IReadOnlyDictionary<string, object?> request)
    {
        var signedAt = DateTimeOffset.UtcNow.ToString("O");
        var payload = BuildSignedElectionQueryPayload(
            method,
            actor.PublicSigningAddress,
            signedAt,
            request);

        return new Metadata
        {
            { "x-hush-election-query-signatory", actor.PublicSigningAddress },
            { "x-hush-election-query-signed-at", signedAt },
            { "x-hush-election-query-signature", DigitalSignature.SignMessageCompactBase64(payload, actor.PrivateSigningKey) },
        };
    }

    private static string BuildSignedElectionQueryPayload(
        string method,
        string actorAddress,
        string signedAt,
        IReadOnlyDictionary<string, object?> request)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["actorAddress"] = actorAddress,
            ["method"] = method,
            ["request"] = DeepSortElectionQueryValue(request),
            ["signedAt"] = signedAt,
        };

        return JsonSerializer.Serialize(payload);
    }

    private static object? DeepSortElectionQueryValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            var sortedDictionary = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var entry in readOnlyDictionary)
            {
                sortedDictionary[entry.Key] = DeepSortElectionQueryValue(entry.Value);
            }

            return sortedDictionary;
        }

        if (value is IDictionary<string, object?> dictionary)
        {
            var sortedDictionary = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var entry in dictionary)
            {
                sortedDictionary[entry.Key] = DeepSortElectionQueryValue(entry.Value);
            }

            return sortedDictionary;
        }

        if (value is IEnumerable<object?> sequence && value is not string)
        {
            return sequence.Select(DeepSortElectionQueryValue).ToArray();
        }

        return value;
    }

    private async Task CreateReportAccessGrantViaBlockchainAsync(TestIdentity designatedAuditor)
    {
        var signedTransaction = TestTransactionFactory.CreateElectionReportAccessGrant(
            GetOwner(),
            new ElectionId(Guid.Parse(GetElectionId())),
            designatedAuditor.PublicSigningAddress);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue(
            $"designated auditor grant transaction should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();
    }

    private async Task<byte[]> BuildExpectedFrozenEligibleVoterSetHashAsync()
    {
        var electionResponse = await ReloadElectionAsync();
        var eligibilityView = await GetElectionEligibilityViewAsync(GetOwner());

        var eligibleOrganizationVoterIds = electionResponse.Election.EligibilityMutationPolicy switch
        {
            EligibilityMutationPolicyProto.FrozenAtOpen => eligibilityView.RestrictedRosterEntries
                .Where(x => x.VotingRightStatus == ElectionVotingRightStatusProto.VotingRightActive)
                .Select(x => x.OrganizationVoterId),
            EligibilityMutationPolicyProto.LateActivationForRosteredVotersOnly => eligibilityView.RestrictedRosterEntries
                .Select(x => x.OrganizationVoterId),
            _ => throw new ArgumentOutOfRangeException(
                nameof(electionResponse.Election.EligibilityMutationPolicy),
                electionResponse.Election.EligibilityMutationPolicy,
                "Unsupported eligibility mutation policy."),
        };

        return HashOrganizationVoterIds(eligibleOrganizationVoterIds);
    }

    private async Task<ElectionCommandResponse> CreateElectionDraftViaBlockchainAsync(
        string snapshotReason,
        ElectionDraftSpecification draft)
    {
        var (signedTransaction, electionId) = TestTransactionFactory.CreateElectionDraft(
            GetOwner(),
            snapshotReason,
            draft);
        _electionId = electionId.ToString();
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"draft transaction should be accepted: {submitResponse.Message}");
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await ReloadElectionAsync();
        response.Success.Should().BeTrue($"draft query should succeed for {electionId}: {response.ErrorMessage}");
        return new ElectionCommandResponse
        {
            Success = true,
            Election = response.Election,
            DraftSnapshot = response.LatestDraftSnapshot,
        };
    }

    private async Task<GetElectionResponse> UpdateElectionDraftViaBlockchainAsync(
        string snapshotReason,
        ElectionDraftSpecification draft)
    {
        var baseline = await ReloadElectionAsync();
        var expectedRevision = baseline.Election.CurrentDraftRevision + 1;
        var signedTransaction = TestTransactionFactory.UpdateElectionDraft(
            GetOwner(),
            new ElectionId(Guid.Parse(GetElectionId())),
            snapshotReason,
            draft);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"draft update transaction should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await ReloadElectionAsync();
        response.Election.CurrentDraftRevision.Should().Be(expectedRevision);
        response.LatestDraftSnapshot.Should().NotBeNull();
        response.LatestDraftSnapshot.DraftRevision.Should().Be(expectedRevision);
        response.LatestDraftSnapshot.SnapshotReason.Should().Be(snapshotReason);
        return response;
    }

    private async Task<GetElectionResponse> ImportRosterViaBlockchainAsync(
        IReadOnlyList<ElectionRosterImportItem> rosterEntries)
    {
        var signedTransaction = TestTransactionFactory.ImportElectionRoster(
            GetOwner(),
            new ElectionId(Guid.Parse(GetElectionId())),
            rosterEntries);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"roster import transaction should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        return await ReloadElectionAsync();
    }

    private async Task<SubmitSignedTransactionReply> SubmitDraftUpdateViaBlockchainAsync(
        string snapshotReason,
        ElectionDraftSpecification draft)
    {
        var signedTransaction = TestTransactionFactory.UpdateElectionDraft(
            GetOwner(),
            new ElectionId(Guid.Parse(GetElectionId())),
            snapshotReason,
            draft);

        return await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });
    }

    private async Task<GetElectionEligibilityViewResponse> ClaimRosterEntryViaBlockchainAsync(
        TestIdentity actor,
        string organizationVoterId,
        string verificationCode)
    {
        var signedTransaction = TestTransactionFactory.ClaimElectionRosterEntry(
            actor,
            new ElectionId(Guid.Parse(GetElectionId())),
            organizationVoterId,
            verificationCode);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"roster claim transaction should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await GetElectionEligibilityViewAsync(actor);
        response.ActorRole.Should().BeOneOf(
            ElectionEligibilityActorRoleProto.EligibilityActorLinkedVoter,
            ElectionEligibilityActorRoleProto.EligibilityActorOwner);
        response.SelfRosterEntry.Should().NotBeNull();
        response.SelfRosterEntry.OrganizationVoterId.Should().Be(organizationVoterId);
        return response;
    }

    private async Task<GetElectionVotingViewResponse> RegisterVotingCommitmentViaBlockchainAsync(
        TestIdentity actor,
        string commitmentHash)
    {
        var signedTransaction = TestTransactionFactory.RegisterElectionVotingCommitment(
            actor,
            GetCurrentElectionId(),
            commitmentHash);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"voting commitment transaction should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await GetElectionVotingViewAsync(actor);
        response.CommitmentRegistered.Should().BeTrue();
        return response;
    }

    private async Task<Guid> InviteTrusteeViaBlockchainAsync(TestIdentity trustee)
    {
        var (signedTransaction, invitationId) = TestTransactionFactory.CreateElectionTrusteeInvitation(
            GetOwner(),
            new ElectionId(Guid.Parse(GetElectionId())),
            trustee);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"invite transaction should be accepted: {submitResponse.Message}");
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await ReloadElectionAsync();
        response.Success.Should().BeTrue($"invite query should succeed for {GetElectionId()}: {response.ErrorMessage}");
        response.TrusteeInvitations.Should().Contain(x => x.Id == invitationId.ToString());
        return invitationId;
    }

    private async Task<GetElectionEligibilityViewResponse> ActivateRosterEntryViaBlockchainAsync(string organizationVoterId)
    {
        var signedTransaction = TestTransactionFactory.ActivateElectionRosterEntry(
            GetOwner(),
            new ElectionId(Guid.Parse(GetElectionId())),
            organizationVoterId);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"roster activation transaction should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await GetElectionEligibilityViewAsync(GetOwner());
        response.ActivationEvents.Should().Contain(x =>
            x.OrganizationVoterId == organizationVoterId &&
            x.Outcome == ElectionEligibilityActivationOutcomeProto.EligibilityActivationSucceeded);
        return response;
    }

    private async Task<GetElectionResponse> OpenElectionViaBlockchainAsync(
        ElectionWarningCode[] requiredWarningCodes,
        byte[]? frozenEligibleVoterSetHash,
        string? trusteePolicyExecutionReference,
        string? reportingPolicyExecutionReference,
        string? reviewWindowExecutionReference)
    {
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));
        var submitResponse = await SubmitOpenElectionViaBlockchainAsync(
            requiredWarningCodes,
            frozenEligibleVoterSetHash,
            trusteePolicyExecutionReference,
            reportingPolicyExecutionReference,
            reviewWindowExecutionReference);

        submitResponse.Successfull.Should().BeTrue($"open transaction should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await ReloadElectionAsync();
        response.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Open);
        return response;
    }

    private async Task<SubmitSignedTransactionReply> SubmitOpenElectionViaBlockchainAsync(
        ElectionWarningCode[] requiredWarningCodes,
        byte[]? frozenEligibleVoterSetHash,
        string? trusteePolicyExecutionReference,
        string? reportingPolicyExecutionReference,
        string? reviewWindowExecutionReference)
    {
        var signedTransaction = TestTransactionFactory.OpenElection(
            GetOwner(),
            new ElectionId(Guid.Parse(GetElectionId())),
            requiredWarningCodes,
            frozenEligibleVoterSetHash,
            trusteePolicyExecutionReference,
            reportingPolicyExecutionReference,
            reviewWindowExecutionReference);

        return await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });
    }

    private async Task<SubmitSignedTransactionReply> SubmitLegacyPlaintextOpenElectionViaBlockchainAsync(
        ElectionWarningCode[] requiredWarningCodes,
        byte[]? frozenEligibleVoterSetHash,
        string? trusteePolicyExecutionReference,
        string? reportingPolicyExecutionReference,
        string? reviewWindowExecutionReference)
    {
        var signedTransaction = TestTransactionFactory.LegacyPlaintextOpenElection(
            GetOwner(),
            new ElectionId(Guid.Parse(GetElectionId())),
            requiredWarningCodes,
            frozenEligibleVoterSetHash,
            trusteePolicyExecutionReference,
            reportingPolicyExecutionReference,
            reviewWindowExecutionReference);

        return await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });
    }

    private async Task<GetElectionResponse> CloseElectionViaBlockchainAsync(
        byte[]? acceptedBallotSetHash,
        byte[]? finalEncryptedTallyHash)
    {
        var signedTransaction = TestTransactionFactory.CloseElection(
            GetOwner(),
            new ElectionId(Guid.Parse(GetElectionId())),
            acceptedBallotSetHash,
            finalEncryptedTallyHash);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"close transaction should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await ReloadElectionAsync();
        response.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        return response;
    }

    private async Task<GetElectionResponse> FinalizeElectionViaBlockchainAsync(
        byte[]? acceptedBallotSetHash,
        byte[]? finalEncryptedTallyHash)
    {
        var signedTransaction = TestTransactionFactory.FinalizeElection(
            GetOwner(),
            new ElectionId(Guid.Parse(GetElectionId())),
            acceptedBallotSetHash,
            finalEncryptedTallyHash);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"finalize transaction should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await ReloadElectionAsync();
        response.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Finalized);
        return response;
    }

    private async Task<GetElectionResponse> StartGovernedProposalViaBlockchainAsync(string actionType)
    {
        var action = ParseGovernedActionType(actionType);
        var (signedTransaction, proposalId) = TestTransactionFactory.StartElectionGovernedProposal(
            GetOwner(),
            new ElectionId(Guid.Parse(GetElectionId())),
            ParseSharedGovernedActionType(actionType));
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"governed proposal start should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        _lastGovernedProposalId = proposalId.ToString();
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await ReloadElectionAsync();
        var proposal = response.GovernedProposals.Single(x => x.Id == proposalId.ToString());
        proposal.ActionType.Should().Be(action);
        if (action == ElectionGovernedActionTypeProto.GovernedActionClose)
        {
            response.Election.VoteAcceptanceLockedAt.Should().NotBeNull();
        }

        return response;
    }

    private async Task<GetElectionResponse> ApproveGovernedProposalViaBlockchainAsync(
        TestIdentity trustee,
        string? approvalNote)
    {
        var signedTransaction = TestTransactionFactory.ApproveElectionGovernedProposal(
            trustee,
            new ElectionId(Guid.Parse(GetElectionId())),
            Guid.Parse(GetLastGovernedProposalId()),
            approvalNote);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"governed approval should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await ReloadElectionAsync();
        response.GovernedProposalApprovals.Should().Contain(x =>
            x.ProposalId == GetLastGovernedProposalId() &&
            x.TrusteeUserAddress == trustee.PublicSigningAddress);
        return response;
    }

    private async Task<GetElectionResponse> RetryGovernedProposalExecutionViaBlockchainAsync()
    {
        var signedTransaction = TestTransactionFactory.RetryElectionGovernedProposalExecution(
            GetOwner(),
            new ElectionId(Guid.Parse(GetElectionId())),
            Guid.Parse(GetLastGovernedProposalId()));
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"governed retry should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        return await ReloadElectionAsync();
    }

    private async Task<GetElectionResponse> SubmitFinalizationShareViaBlockchainAsync(
        TestIdentity trustee,
        ElectionFinalizationTargetType targetType)
    {
        var currentResponse = _lastElectionResponse ?? await ReloadElectionAsync();
        var session = GetAwaitingFinalizationSession(currentResponse);
        var shareIndex = session.EligibleTrustees
            .Select((trusteeReference, index) => new { trusteeReference.TrusteeUserAddress, ShareIndex = index + 1 })
            .Single(x => x.TrusteeUserAddress == trustee.PublicSigningAddress)
            .ShareIndex;
        var ceremonyVersionId = string.IsNullOrWhiteSpace(session.CeremonySnapshot?.CeremonyVersionId)
            ? (Guid?)null
            : Guid.Parse(session.CeremonySnapshot.CeremonyVersionId);
        var shareMaterial = BuildFinalizationShareMaterial(trustee, session);
        var signedTransaction = TestTransactionFactory.SubmitElectionFinalizationShare(
            trustee,
            new ElectionId(Guid.Parse(GetElectionId())),
            Guid.Parse(session.Id),
            shareIndex,
            $"feat098-share-v1-{trustee.DisplayName.ToLowerInvariant()}",
            targetType,
            Guid.Parse(session.CloseArtifactId),
            session.AcceptedBallotSetHash.ToByteArray(),
            session.FinalEncryptedTallyHash.ToByteArray(),
            session.TargetTallyId,
            ceremonyVersionId,
            session.CeremonySnapshot?.TallyPublicKeyFingerprint,
            shareMaterial,
            string.IsNullOrWhiteSpace(session.CloseCountingJobId)
                ? null
                : Guid.Parse(session.CloseCountingJobId),
            string.IsNullOrWhiteSpace(session.ExecutorSessionPublicKey)
                ? null
                : session.ExecutorSessionPublicKey,
            string.IsNullOrWhiteSpace(session.ExecutorKeyAlgorithm)
                ? null
                : session.ExecutorKeyAlgorithm);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"finalization share transaction should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        return await ReloadElectionAsync();
    }

    private async Task<SubmitSignedTransactionReply> SubmitAcceptedBallotCastWithoutBlockAsync(
        TestIdentity actor,
        string submissionIdempotencyKey)
    {
        var signedTransaction = await BuildAcceptedBallotCastTransactionAsync(actor, submissionIdempotencyKey);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"ballot cast transaction should be accepted into the mempool: {submitResponse.Message}");
        await waiter.WaitAsync();
        return submitResponse;
    }

    private async Task<SubmitSignedTransactionReply> SubmitAcceptedBallotCastAttemptAsync(
        TestIdentity actor,
        string submissionIdempotencyKey)
    {
        var signedTransaction = await BuildAcceptedBallotCastTransactionAsync(actor, submissionIdempotencyKey);

        return await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });
    }

    private async Task<SubmitSignedTransactionReply> SubmitAcceptedBallotCastViaBlockchainAsync(
        TestIdentity actor,
        string submissionIdempotencyKey)
    {
        var signedTransaction = await BuildAcceptedBallotCastTransactionAsync(actor, submissionIdempotencyKey);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        if (!submitResponse.Successfull)
        {
            return submitResponse;
        }

        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();
        _lastElectionResponse = await ReloadElectionAsync();
        return submitResponse;
    }

    private async Task<SubmitSignedTransactionReply> SubmitAcceptedDevModeBallotCastViaBlockchainAsync(
        TestIdentity actor,
        string submissionIdempotencyKey)
    {
        var signedTransaction = await BuildAcceptedBallotCastTransactionAsync(
            actor,
            submissionIdempotencyKey,
            useDevModePayload: true);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        if (!submitResponse.Successfull)
        {
            return submitResponse;
        }

        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();
        _lastElectionResponse = await ReloadElectionAsync();
        return submitResponse;
    }

    private async Task<string> BuildAcceptedBallotCastTransactionAsync(
        TestIdentity actor,
        string submissionIdempotencyKey,
        bool useDevModePayload = false)
    {
        var votingView = await GetElectionVotingViewAsync(actor);

        votingView.CommitmentRegistered.Should().BeTrue("the FEAT-099 cast path requires a registered commitment before final cast");
        votingView.OpenArtifactId.Should().NotBeNullOrWhiteSpace();
        votingView.EligibleSetHash.Should().NotBeNullOrWhiteSpace();
        votingView.CeremonyVersionId.Should().NotBeNullOrWhiteSpace();
        votingView.DkgProfileId.Should().NotBeNullOrWhiteSpace();
        votingView.TallyPublicKeyFingerprint.Should().NotBeNullOrWhiteSpace();
        var selectionCount = votingView.Election.Options.Count;
        selectionCount.Should().BeGreaterThan(0, "the election must expose at least one ballot option before ballot casting");
        var nonBlankChoiceIndexes = votingView.Election.Options
            .Select((option, index) => new { option, index })
            .Where(x => !x.option.IsBlankOption)
            .Select(x => x.index)
            .ToArray();
        nonBlankChoiceIndexes.Should().NotBeEmpty("the integration cast path should target a named option, not a blank vote");
        var choiceIndex = ResolveChoiceIndex(actor, submissionIdempotencyKey, nonBlankChoiceIndexes);
        var selectedOption = votingView.Election.Options[choiceIndex];
        var devArtifactSeed = Guid.NewGuid().ToString();
        var devModeBallotPackage = useDevModePayload
            ? BuildDevModeEncryptedBallotPackage(
                votingView.Election.ElectionId,
                selectedOption.OptionId,
                selectedOption.DisplayLabel,
                selectedOption.ShortDescription,
                selectedOption.BallotOrder,
                selectedOption.IsBlankOption)
            : null;

        var encryptedBallotPackage = useDevModePayload
            ? devModeBallotPackage!
            : BuildEncryptedBallotPackage(actor, submissionIdempotencyKey, selectionCount, choiceIndex);
        var proofBundle = useDevModePayload
            ? BuildDevModeProofBundle(
                votingView,
                selectedOption.OptionId,
                devModeBallotPackage!)
            : BuildProofBundle(actor, submissionIdempotencyKey);
        var ballotNullifier = useDevModePayload
            ? BuildDevModeBallotNullifier(votingView.Election.ElectionId, devArtifactSeed)
            : BuildBallotNullifier(actor, submissionIdempotencyKey);

        return TestTransactionFactory.AcceptElectionBallotCast(
            actor,
            GetCurrentElectionId(),
            submissionIdempotencyKey,
            encryptedBallotPackage,
            proofBundle,
            ballotNullifier,
            Guid.Parse(votingView.OpenArtifactId),
            Convert.FromBase64String(votingView.EligibleSetHash),
            Guid.Parse(votingView.CeremonyVersionId),
            votingView.DkgProfileId,
            votingView.TallyPublicKeyFingerprint);
    }

    private int CountPendingBallotCastSubmissions(TestIdentity actor, string submissionIdempotencyKey)
    {
        var memPoolService = GetNode().Services.GetRequiredService<IMemPoolService>();
        var envelopeCryptoService = GetNode().Services.GetRequiredService<IElectionEnvelopeCryptoService>();
        var electionId = GetCurrentElectionId();
        var normalizedKey = submissionIdempotencyKey.Trim();

        return memPoolService.PeekPendingValidatedTransactions()
            .Select(envelopeCryptoService.TryDecryptValidated)
            .Where(x => x is not null)
            .Where(x =>
                x!.ActionType == EncryptedElectionEnvelopeActionTypes.AcceptBallotCast &&
                x.Transaction.Payload.ElectionId == electionId)
            .Select(x => x!.DeserializeAction<AcceptElectionBallotCastActionPayload>())
            .Count(x =>
                x is not null &&
                string.Equals(x.ActorPublicAddress, actor.PublicSigningAddress, StringComparison.Ordinal) &&
                string.Equals(x.IdempotencyKey?.Trim(), normalizedKey, StringComparison.Ordinal));
    }

    private static ElectionFinalizationSession GetAwaitingFinalizationSession(GetElectionResponse response) =>
        response.FinalizationSessions
            .Single(x => x.Status == ElectionFinalizationSessionStatusProto.FinalizationSessionAwaitingShares);

    private void RecordState(ElectionLifecycleStateProto lifecycleState)
    {
        _observedStates.Add(lifecycleState);
    }

    private static string ToFriendlyState(ElectionLifecycleStateProto lifecycleState) =>
        lifecycleState switch
        {
            ElectionLifecycleStateProto.Draft => "Draft",
            ElectionLifecycleStateProto.Open => "Open",
            ElectionLifecycleStateProto.Closed => "Closed",
            ElectionLifecycleStateProto.Finalized => "Finalized",
            _ => lifecycleState.ToString(),
        };

    private static TestIdentity ResolveIdentity(string alias) =>
        alias.Trim().ToLowerInvariant() switch
        {
            "alice" => TestIdentities.Alice,
            "bob" => TestIdentities.Bob,
            "charlie" => TestIdentities.Charlie,
            "delta" => Delta,
            "echo" => Echo,
            "foxtrot" => Foxtrot,
            "guest" => Guest,
            _ => throw new ArgumentOutOfRangeException(nameof(alias), alias, "Unsupported test identity alias."),
        };

    private static ElectionWarningCodeProto ParseWarningCode(string warningCode) =>
        Enum.TryParse<ElectionWarningCodeProto>(warningCode, ignoreCase: false, out var parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(warningCode), warningCode, "Unsupported warning code.");

    private static ElectionEligibilityActorRoleProto ParseEligibilityActorRole(string actorRole) =>
        Enum.TryParse<ElectionEligibilityActorRoleProto>(actorRole, ignoreCase: false, out var parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(actorRole), actorRole, "Unsupported eligibility actor role.");

    private static ElectionVotingRightStatusProto ParseVotingRightStatus(string votingRightStatus) =>
        Enum.TryParse<ElectionVotingRightStatusProto>(votingRightStatus, ignoreCase: false, out var parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(votingRightStatus), votingRightStatus, "Unsupported voting right status.");

    private static ElectionParticipationStatusProto ParseParticipationStatus(string participationStatus) =>
        Enum.TryParse<ElectionParticipationStatusProto>(participationStatus, ignoreCase: false, out var parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(participationStatus), participationStatus, "Unsupported participation status.");

    private static ElectionVotingSubmissionStatusProto ParseVotingSubmissionStatus(string submissionStatus) =>
        Enum.TryParse<ElectionVotingSubmissionStatusProto>(submissionStatus, ignoreCase: false, out var parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(submissionStatus), submissionStatus, "Unsupported voting submission status.");

    private static ElectionClosedProgressStatusProto ParseClosedProgressStatus(string progressStatus) =>
        Enum.TryParse<ElectionClosedProgressStatusProto>(progressStatus, ignoreCase: false, out var parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(progressStatus), progressStatus, "Unsupported closed progress status.");

    private static IReadOnlyList<ElectionRosterImportItem> BuildDefaultRosterImportItems() =>
    [
        new ElectionRosterImportItem("voter-alice", ElectionRosterContactType.Email, "alice.eligibility@hush.test"),
        new ElectionRosterImportItem("voter-bob", ElectionRosterContactType.Phone, "+15550001002", IsInitiallyActive: false),
        new ElectionRosterImportItem("voter-charlie", ElectionRosterContactType.Email, "charlie.eligibility@hush.test"),
    ];

    private static byte[] HashOrganizationVoterIds(IEnumerable<string> organizationVoterIds)
    {
        var normalizedIds = organizationVoterIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Length}:{x}");

        return SHA256.HashData(Encoding.UTF8.GetBytes(
            $"feat095:organization-voter-set:{string.Join("\n", normalizedIds)}"));
    }

    private GetElectionVotingViewResponse GetLastVotingView() =>
        _lastVotingViewResponse ?? throw new InvalidOperationException("No election voting view has been loaded for this scenario.");

    private GetElectionResultViewResponse GetLastResultView() =>
        _lastResultViewResponse ?? throw new InvalidOperationException("No election result view has been loaded for this scenario.");

    private GetElectionEligibilityViewResponse GetLastEligibilityView() =>
        _lastEligibilityViewResponse ?? throw new InvalidOperationException("No election eligibility view has been loaded for this scenario.");

    private ElectionId GetCurrentElectionId() =>
        new(Guid.Parse(GetElectionId()));

    private string BuildEncryptedBallotPackage(
        TestIdentity actor,
        string submissionIdempotencyKey,
        int selectionCount,
        int choiceIndex)
    {
        var curve = new BabyJubJubCurve();
        var publicKeySeed = ParseSeedToScalar($"feat100:public-key:{GetElectionId()}", curve.Order);
        var nonceSeed = ParseSeedToScalar(
            $"feat100:nonces:{GetElectionId()}:{actor.PublicSigningAddress}:{submissionIdempotencyKey.Trim()}",
            curve.Order);
        var keyPair = ControlledElectionHarness.CreateDeterministicKeyPair(publicKeySeed, curve);
        var ballot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: $"feat100-ballot:{GetElectionId()}:{actor.PublicSigningAddress}:{submissionIdempotencyKey.Trim()}",
            choiceIndex: choiceIndex,
            publicKey: keyPair.PublicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(
                nonceSeed,
                selectionCount,
                curve),
            selectionCount: selectionCount,
            curve: curve);

        var payload = new PublishedElectionBallotPackage(
            Version: "election-ballot.v1",
            PublicKey: ToPublishedPoint(keyPair.PublicKey),
            SelectionCount: selectionCount,
            Ciphertext: new PublishedElectionCiphertext(
                ballot.Slots.Select(slot => ToPublishedPoint(slot.C1)).ToArray(),
                ballot.Slots.Select(slot => ToPublishedPoint(slot.C2)).ToArray()));

        return JsonSerializer.Serialize(payload);
    }

    private string BuildProofBundle(TestIdentity actor, string submissionIdempotencyKey) =>
        JsonSerializer.Serialize(new PublishedElectionProofBundle(
            Version: "integration-proof-bundle.v1",
            Actor: actor.PublicSigningAddress,
            ElectionId: GetElectionId(),
            SubmissionIdempotencyKey: submissionIdempotencyKey.Trim()));

    private static string BuildDevModeEncryptedBallotPackage(
        string electionId,
        string optionId,
        string optionLabel,
        string? optionDescription,
        int ballotOrder,
        bool isBlankOption)
    {
        var selectionFingerprint = ComputeLowerHexSha256(
            $"election-dev-selection:v1:{electionId}:{optionId}:{optionLabel}");

        return JsonSerializer.Serialize(new
        {
            mode = "election-dev-mode-v1",
            packageType = "dev-protected-ballot",
            electionId,
            optionId,
            optionLabel,
            optionDescription = optionDescription ?? string.Empty,
            ballotOrder,
            isBlankOption,
            selectionFingerprint,
        });
    }

    private static string BuildDevModeProofBundle(
        GetElectionVotingViewResponse votingView,
        string optionId,
        string ballotPackage)
    {
        var electionId = votingView.Election.ElectionId;
        var ballotPackageHash = ComputeLowerHexSha256(ballotPackage);

        return JsonSerializer.Serialize(new
        {
            mode = "election-dev-mode-v1",
            proofType = "dev-election-proof",
            electionId,
            optionId,
            ballotPackageHash,
            openArtifactId = votingView.OpenArtifactId,
            eligibleSetHash = votingView.EligibleSetHash,
            ceremonyVersionId = votingView.CeremonyVersionId,
            dkgProfileId = votingView.DkgProfileId,
            tallyPublicKeyFingerprint = votingView.TallyPublicKeyFingerprint,
        });
    }

    private static string BuildDevModeBallotNullifier(string electionId, string devArtifactSeed) =>
        ComputeLowerHexSha256($"election-dev-nullifier:v2:{electionId}:{devArtifactSeed}:nullifier");

    private string BuildBallotNullifier(TestIdentity actor, string submissionIdempotencyKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"feat099:ballot-nullifier:{GetElectionId()}:{actor.PublicSigningAddress}:{submissionIdempotencyKey.Trim()}")));

    private static string ComputeLowerHexSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

    private string BuildEncodedCastArtifact(string artifactKind, TestIdentity actor, string submissionIdempotencyKey) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{artifactKind}:{GetElectionId()}:{actor.PublicSigningAddress}:{submissionIdempotencyKey.Trim()}"));

    private static int ResolveChoiceIndex(
        TestIdentity actor,
        string submissionIdempotencyKey,
        IReadOnlyList<int> availableChoiceIndexes)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"feat100:choice-index:{actor.PublicSigningAddress}:{submissionIdempotencyKey.Trim()}"));
        var scalar = new BigInteger(digest, isUnsigned: true, isBigEndian: true);
        return availableChoiceIndexes[(int)(scalar % availableChoiceIndexes.Count)];
    }

    private string BuildFinalizationShareMaterial(
        TestIdentity trustee,
        ElectionFinalizationSession session)
    {
        var curve = new BabyJubJubCurve();
        var publicKeySeed = ParseSeedToScalar($"feat100:public-key:{GetElectionId()}", curve.Order);
        var keyPair = ControlledElectionHarness.CreateDeterministicKeyPair(publicKeySeed, curve);
        // ControlledElectionThresholdSetup derives coefficient[0] from seed + 7919 before normalization.
        var thresholdSeed = keyPair.PrivateKey - 7920;
        var trusteeIds = System.Collections.Immutable.ImmutableArray.CreateRange(
            session.EligibleTrustees.Select(x => x.TrusteeUserAddress));
        var thresholdDefinition = new ControlledElectionThresholdDefinition(
            GetElectionId(),
            trusteeIds,
            session.RequiredShareCount);
        var thresholdSetup = ControlledElectionHarness.CreateControlledThresholdSetup(
            thresholdDefinition,
            session.Id,
            session.TargetTallyId,
            thresholdSeed,
            curve);

        return thresholdSetup.Shares
            .Single(x => string.Equals(x.TrusteeId, trustee.PublicSigningAddress, StringComparison.Ordinal))
            .ShareMaterial;
    }

    private static BigInteger ParseSeedToScalar(string seed, BigInteger order)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var scalar = new BigInteger(digest, isUnsigned: true, isBigEndian: true) % order;
        return scalar == BigInteger.Zero ? BigInteger.One : scalar;
    }

    private static PublishedElectionPointPayload ToPublishedPoint(ReactionECPoint point) =>
        new(
            point.X.ToString(CultureInfo.InvariantCulture),
            point.Y.ToString(CultureInfo.InvariantCulture));

    private static string ComputeElectionScopedIdempotencyHash(string submissionIdempotencyKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(submissionIdempotencyKey.Trim())));

    private static ElectionDraftSpecification BuildAdminDraftSpecification(
        string title,
        ElectionBindingStatus bindingStatus = ElectionBindingStatus.Binding,
        EligibilityMutationPolicy eligibilityMutationPolicy = EligibilityMutationPolicy.FrozenAtOpen) =>
        new(
            Title: title,
            ShortDescription: "Annual board vote",
            ExternalReferenceCode: "ORG-2026-01",
            ElectionClass: ElectionClass.OrganizationalRemoteVoting,
            BindingStatus: bindingStatus,
            GovernanceMode: ElectionGovernanceMode.AdminOnly,
            DisclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            ParticipationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            VoteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            EligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            EligibilityMutationPolicy: eligibilityMutationPolicy,
            OutcomeRule: new OutcomeRuleDefinition(
                OutcomeRuleKind.SingleWinner,
                "single_winner",
                SeatCount: 1,
                BlankVoteCountsForTurnout: true,
                BlankVoteExcludedFromWinnerSelection: true,
                BlankVoteExcludedFromThresholdDenominator: false,
                TieResolutionRule: "tie_unresolved",
                CalculationBasis: "highest_non_blank_votes"),
            ApprovedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            ProtocolOmegaVersion: "omega-v1.0.0",
            ReportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            ReviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            OwnerOptions:
            [
                new ElectionOptionDefinition("option-a", "Alice", "First option", 1, false),
                new ElectionOptionDefinition("option-b", "Bob", "Second option", 2, false),
            ],
            AcknowledgedWarningCodes:
            [
                ElectionWarningCode.LowAnonymitySet,
            ]);

    private sealed record PublishedElectionBallotPackage(
        string Version,
        PublishedElectionPointPayload PublicKey,
        int SelectionCount,
        PublishedElectionCiphertext Ciphertext);

    private sealed record PublishedElectionCiphertext(
        PublishedElectionPointPayload[] C1,
        PublishedElectionPointPayload[] C2);

    private sealed record PublishedElectionPointPayload(
        string X,
        string Y);

    private sealed record PublishedElectionProofBundle(
        string Version,
        string Actor,
        string ElectionId,
        string SubmissionIdempotencyKey);

    private static ElectionDraftSpecification BuildTrusteeThresholdDraftSpecification(string title) =>
        new(
            Title: title,
            ShortDescription: "Governed policy vote",
            ExternalReferenceCode: "REF-2026-096",
            ElectionClass: ElectionClass.OrganizationalRemoteVoting,
            BindingStatus: ElectionBindingStatus.Binding,
            GovernanceMode: ElectionGovernanceMode.TrusteeThreshold,
            DisclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            ParticipationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            VoteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            EligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            EligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            OutcomeRule: new OutcomeRuleDefinition(
                OutcomeRuleKind.PassFail,
                "pass_fail_yes_no",
                SeatCount: 1,
                BlankVoteCountsForTurnout: true,
                BlankVoteExcludedFromWinnerSelection: true,
                BlankVoteExcludedFromThresholdDenominator: true,
                TieResolutionRule: "tie_unresolved",
                CalculationBasis: "simple_majority_of_non_blank_votes"),
            ApprovedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            ProtocolOmegaVersion: "omega-v1.0.0",
            ReportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            ReviewWindowPolicy: ReviewWindowPolicy.GovernedReviewWindowReserved,
            OwnerOptions:
            [
                new ElectionOptionDefinition("option-yes", "Yes", "Approve the motion", 1, false),
                new ElectionOptionDefinition("option-no", "No", "Reject the motion", 2, false),
            ],
            AcknowledgedWarningCodes:
            [
                ElectionWarningCode.AllTrusteesRequiredFragility,
            ],
            RequiredApprovalCount: 3);

    private static ElectionGovernedActionTypeProto ParseGovernedActionType(string actionType) =>
        actionType.Trim().ToLowerInvariant() switch
        {
            "open" => ElectionGovernedActionTypeProto.GovernedActionOpen,
            "close" => ElectionGovernedActionTypeProto.GovernedActionClose,
            "finalize" => ElectionGovernedActionTypeProto.GovernedActionFinalize,
            _ => throw new ArgumentOutOfRangeException(nameof(actionType), actionType, "Unsupported governed action type."),
        };

    private static ElectionGovernedActionType ParseSharedGovernedActionType(string actionType) =>
        actionType.Trim().ToLowerInvariant() switch
        {
            "open" => ElectionGovernedActionType.Open,
            "close" => ElectionGovernedActionType.Close,
            "finalize" => ElectionGovernedActionType.Finalize,
            _ => throw new ArgumentOutOfRangeException(nameof(actionType), actionType, "Unsupported governed action type."),
        };

    private static ElectionLifecycleStateProto ParseProtoLifecycleState(string lifecycleState) =>
        lifecycleState.Trim().ToLowerInvariant() switch
        {
            "draft" => ElectionLifecycleStateProto.Draft,
            "open" => ElectionLifecycleStateProto.Open,
            "closed" => ElectionLifecycleStateProto.Closed,
            "finalized" => ElectionLifecycleStateProto.Finalized,
            _ => throw new ArgumentOutOfRangeException(nameof(lifecycleState), lifecycleState, "Unsupported lifecycle state."),
        };

    private static ElectionPublicationIssueCode ParsePublicationIssueCode(string issueCode) =>
        Enum.TryParse<ElectionPublicationIssueCode>(issueCode.Trim(), ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(issueCode), issueCode, "Unsupported publication issue code.");

    private static ElectionDraftInput BuildDraftInput(
        string title,
        ElectionBindingStatusProto bindingStatus = ElectionBindingStatusProto.Binding)
    {
        var draft = new ElectionDraftInput
        {
            Title = title,
            ShortDescription = "Annual board vote",
            ExternalReferenceCode = "ORG-2026-01",
            ElectionClass = ElectionClassProto.OrganizationalRemoteVoting,
            BindingStatus = bindingStatus,
            GovernanceMode = ElectionGovernanceModeProto.AdminOnly,
            DisclosureMode = ElectionDisclosureModeProto.FinalResultsOnly,
            ParticipationPrivacyMode = ParticipationPrivacyModeProto.PublicCheckoffAnonymousBallotPrivateChoice,
            VoteUpdatePolicy = VoteUpdatePolicyProto.SingleSubmissionOnly,
            EligibilitySourceType = EligibilitySourceTypeProto.OrganizationImportedRoster,
            EligibilityMutationPolicy = EligibilityMutationPolicyProto.FrozenAtOpen,
            OutcomeRule = new OutcomeRule
            {
                Kind = OutcomeRuleKindProto.SingleWinner,
                TemplateKey = "single_winner",
                SeatCount = 1,
                BlankVoteCountsForTurnout = true,
                BlankVoteExcludedFromWinnerSelection = true,
                BlankVoteExcludedFromThresholdDenominator = false,
                TieResolutionRule = "tie_unresolved",
                CalculationBasis = "highest_non_blank_votes",
            },
            ProtocolOmegaVersion = "omega-v1.0.0",
            ReportingPolicy = ReportingPolicyProto.DefaultPhaseOnePackage,
            ReviewWindowPolicy = ReviewWindowPolicyProto.NoReviewWindow,
        };

        draft.ApprovedClientApplications.Add(new ApprovedClientApplication
        {
            ApplicationId = "hushsocial",
            Version = "1.0.0",
        });
        draft.OwnerOptions.Add(new ElectionOption
        {
            OptionId = "option-a",
            DisplayLabel = "Alice",
            ShortDescription = "Candidate Alice",
            BallotOrder = 1,
            IsBlankOption = false,
        });
        draft.OwnerOptions.Add(new ElectionOption
        {
            OptionId = "option-b",
            DisplayLabel = "Bob",
            ShortDescription = "Candidate Bob",
            BallotOrder = 2,
            IsBlankOption = false,
        });
        draft.AcknowledgedWarningCodes.Add(ElectionWarningCodeProto.LowAnonymitySet);

        return draft;
    }

    private static ElectionDraftInput BuildTrusteeThresholdDraftInput(string title)
    {
        var draft = new ElectionDraftInput
        {
            Title = title,
            ShortDescription = "Governed policy vote",
            ExternalReferenceCode = "REF-2026-01",
            ElectionClass = ElectionClassProto.OrganizationalRemoteVoting,
            BindingStatus = ElectionBindingStatusProto.Binding,
            GovernanceMode = ElectionGovernanceModeProto.TrusteeThreshold,
            DisclosureMode = ElectionDisclosureModeProto.FinalResultsOnly,
            ParticipationPrivacyMode = ParticipationPrivacyModeProto.PublicCheckoffAnonymousBallotPrivateChoice,
            VoteUpdatePolicy = VoteUpdatePolicyProto.SingleSubmissionOnly,
            EligibilitySourceType = EligibilitySourceTypeProto.OrganizationImportedRoster,
            EligibilityMutationPolicy = EligibilityMutationPolicyProto.FrozenAtOpen,
            OutcomeRule = new OutcomeRule
            {
                Kind = OutcomeRuleKindProto.PassFail,
                TemplateKey = "pass_fail_yes_no",
                SeatCount = 1,
                BlankVoteCountsForTurnout = true,
                BlankVoteExcludedFromWinnerSelection = true,
                BlankVoteExcludedFromThresholdDenominator = true,
                TieResolutionRule = "tie_unresolved",
                CalculationBasis = "simple_majority_of_non_blank_votes",
            },
            ProtocolOmegaVersion = "omega-v1.0.0",
            ReportingPolicy = ReportingPolicyProto.DefaultPhaseOnePackage,
            ReviewWindowPolicy = ReviewWindowPolicyProto.GovernedReviewWindowReserved,
            RequiredApprovalCount = 3,
        };

        draft.ApprovedClientApplications.Add(new ApprovedClientApplication
        {
            ApplicationId = "hushsocial",
            Version = "1.0.0",
        });
        draft.OwnerOptions.Add(new ElectionOption
        {
            OptionId = "yes",
            DisplayLabel = "Yes",
            ShortDescription = "Approve the proposal",
            BallotOrder = 1,
            IsBlankOption = false,
        });
        draft.OwnerOptions.Add(new ElectionOption
        {
            OptionId = "no",
            DisplayLabel = "No",
            ShortDescription = "Reject the proposal",
            BallotOrder = 2,
            IsBlankOption = false,
        });
        draft.AcknowledgedWarningCodes.Add(ElectionWarningCodeProto.AllTrusteesRequiredFragility);

        return draft;
    }

    private async Task EnsureTrusteeAcceptedAsync(TestIdentity trustee)
    {
        var response = await AcceptTrusteeInvitationViaBlockchainAsync(trustee);
        response.Success.Should().BeTrue($"trustee acceptance should succeed: {response.ErrorMessage}");
        response.TrusteeInvitation.Should().NotBeNull();
        response.TrusteeInvitation!.Status.Should().Be(ElectionTrusteeInvitationStatusProto.Accepted);
        _lastCommandResponse = response;
    }

    private async Task<ElectionCommandResponse> AcceptTrusteeInvitationViaBlockchainAsync(TestIdentity trustee)
    {
        var signedTransaction = TestTransactionFactory.AcceptElectionTrusteeInvitation(
            trustee,
            new ElectionId(Guid.Parse(GetElectionId())),
            Guid.Parse(GetTrusteeInvitationId(trustee.PublicSigningAddress)));
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"trustee acceptance transaction should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await ReloadElectionAsync();
        var invitation = response.TrusteeInvitations.Single(x => x.Id == GetTrusteeInvitationId(trustee.PublicSigningAddress));
        invitation.Status.Should().Be(ElectionTrusteeInvitationStatusProto.Accepted);

        return new ElectionCommandResponse
        {
            Success = true,
            Election = response.Election,
            TrusteeInvitation = invitation,
        };
    }

    private async Task<ElectionCommandResponse> StartElectionCeremonyViaBlockchainAsync(string profileId)
    {
        var signedTransaction = TestTransactionFactory.StartElectionCeremony(
            GetOwner(),
            new ElectionId(Guid.Parse(GetElectionId())),
            profileId);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"start ceremony transaction should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await ReloadElectionAsync();
        var version = response.CeremonyVersions
            .Where(x => x.ProfileId == profileId)
            .OrderByDescending(x => x.VersionNumber)
            .First();

        return new ElectionCommandResponse
        {
            Success = true,
            Election = response.Election,
            CeremonyVersion = version,
            CeremonyProfile = response.CeremonyProfiles.SingleOrDefault(x => x.ProfileId == profileId),
        };
    }

    private async Task<ElectionCommandResponse> PublishElectionCeremonyTransportKeyViaBlockchainAsync(
        TestIdentity trustee,
        Guid ceremonyVersionId,
        string transportPublicKeyFingerprint)
    {
        var signedTransaction = TestTransactionFactory.PublishElectionCeremonyTransportKey(
            trustee,
            new ElectionId(Guid.Parse(GetElectionId())),
            ceremonyVersionId,
            transportPublicKeyFingerprint);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"publish transport key transaction should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await ReloadElectionAsync();
        var trusteeState = response.ActiveCeremonyTrusteeStates.Single(x =>
            x.TrusteeUserAddress == trustee.PublicSigningAddress &&
            x.CeremonyVersionId == ceremonyVersionId.ToString());
        trusteeState.TransportPublicKeyFingerprint.Should().Be(transportPublicKeyFingerprint);

        return new ElectionCommandResponse
        {
            Success = true,
            Election = response.Election,
            CeremonyTrusteeState = trusteeState,
        };
    }

    private async Task<ElectionCommandResponse> JoinElectionCeremonyViaBlockchainAsync(
        TestIdentity trustee,
        Guid ceremonyVersionId)
    {
        var signedTransaction = TestTransactionFactory.JoinElectionCeremony(
            trustee,
            new ElectionId(Guid.Parse(GetElectionId())),
            ceremonyVersionId);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"join ceremony transaction should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await ReloadElectionAsync();
        var trusteeState = response.ActiveCeremonyTrusteeStates.Single(x =>
            x.TrusteeUserAddress == trustee.PublicSigningAddress &&
            x.CeremonyVersionId == ceremonyVersionId.ToString());
        trusteeState.JoinedAt.Should().NotBeNull();

        return new ElectionCommandResponse
        {
            Success = true,
            Election = response.Election,
            CeremonyTrusteeState = trusteeState,
        };
    }

    private async Task<ElectionCommandResponse> RecordElectionCeremonySelfTestSuccessViaBlockchainAsync(
        TestIdentity trustee,
        Guid ceremonyVersionId)
    {
        var signedTransaction = TestTransactionFactory.RecordElectionCeremonySelfTestSuccess(
            trustee,
            new ElectionId(Guid.Parse(GetElectionId())),
            ceremonyVersionId);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"self-test transaction should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await ReloadElectionAsync();
        var trusteeState = response.ActiveCeremonyTrusteeStates.Single(x =>
            x.TrusteeUserAddress == trustee.PublicSigningAddress &&
            x.CeremonyVersionId == ceremonyVersionId.ToString());
        trusteeState.SelfTestSucceededAt.Should().NotBeNull();

        return new ElectionCommandResponse
        {
            Success = true,
            Election = response.Election,
            CeremonyTrusteeState = trusteeState,
        };
    }

    private async Task<ElectionCommandResponse> SubmitElectionCeremonyMaterialViaBlockchainAsync(
        TestIdentity trustee,
        Guid ceremonyVersionId,
        string? recipientTrusteeUserAddress,
        string messageType,
        string payloadVersion,
        string encryptedPayload,
        string payloadFingerprint)
    {
        var signedTransaction = TestTransactionFactory.SubmitElectionCeremonyMaterial(
            trustee,
            new ElectionId(Guid.Parse(GetElectionId())),
            ceremonyVersionId,
            recipientTrusteeUserAddress,
            messageType,
            payloadVersion,
            encryptedPayload,
            payloadFingerprint);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"ceremony material transaction should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await ReloadElectionAsync();
        var trusteeState = response.ActiveCeremonyTrusteeStates.Single(x =>
            x.TrusteeUserAddress == trustee.PublicSigningAddress &&
            x.CeremonyVersionId == ceremonyVersionId.ToString());
        trusteeState.MaterialSubmittedAt.Should().NotBeNull();

        return new ElectionCommandResponse
        {
            Success = true,
            Election = response.Election,
            CeremonyTrusteeState = trusteeState,
        };
    }

    private async Task<ElectionCommandResponse> CompleteElectionCeremonyTrusteeViaBlockchainAsync(
        string trusteeUserAddress,
        Guid ceremonyVersionId,
        string shareVersion,
        string? tallyPublicKeyFingerprint)
    {
        var signedTransaction = TestTransactionFactory.CompleteElectionCeremonyTrustee(
            GetOwner(),
            new ElectionId(Guid.Parse(GetElectionId())),
            ceremonyVersionId,
            trusteeUserAddress,
            shareVersion,
            tallyPublicKeyFingerprint);
        using var waiter = GetNode().StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await GetBlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        submitResponse.Successfull.Should().BeTrue($"ceremony completion transaction should be accepted: {submitResponse.Message}");
        _lastSubmitTransactionResponse = submitResponse;
        await waiter.WaitAsync();
        await GetBlockControl().ProduceBlockAsync();

        var response = await ReloadElectionAsync();
        var trusteeState = response.ActiveCeremonyTrusteeStates.Single(x =>
            x.TrusteeUserAddress == trusteeUserAddress &&
            x.CeremonyVersionId == ceremonyVersionId.ToString());
        trusteeState.CompletedAt.Should().NotBeNull();
        trusteeState.ShareVersion.Should().Be(shareVersion);

        return new ElectionCommandResponse
        {
            Success = true,
            Election = response.Election,
            CeremonyTrusteeState = trusteeState,
        };
    }
}
