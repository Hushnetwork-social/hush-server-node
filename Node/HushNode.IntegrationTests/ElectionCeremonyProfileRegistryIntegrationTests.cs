using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode;
using HushServerNode.Testing;
using HushShared.Elections.Model;
using Xunit;

namespace HushNode.IntegrationTests;

[Collection("Integration Tests")]
[Trait("Category", "FEAT-097")]
public sealed class ElectionCeremonyProfileRegistryIntegrationTests : IAsyncLifetime
{
    private static readonly TestIdentity Delta = TestIdentities.GenerateFromSeed("TEST_DELTA_V1", "Delta");
    private static readonly TestIdentity Echo = TestIdentities.GenerateFromSeed("TEST_ECHO_V1", "Echo");
    private static readonly TestIdentity Foxtrot = TestIdentities.GenerateFromSeed("TEST_FOXTROT_V1", "Foxtrot");
    private static readonly IReadOnlyList<TestIdentity> RolloutTrustees =
    [
        TestIdentities.Bob,
        TestIdentities.Charlie,
        Delta,
        Echo,
        Foxtrot,
    ];

    private HushTestFixture? _fixture;
    private HushServerNodeCore? _node;
    private BlockProductionControl? _blockControl;
    private GrpcClientFactory? _grpcFactory;

    public async Task InitializeAsync()
    {
        _fixture = new HushTestFixture();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await DisposeNodeAsync();

        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetElection_WithDevProfilesEnabled_ExposesTheShippedProfilePair()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Dev Visibility");

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = createResponse.Election.ElectionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.CeremonyProfiles.Select(x => x.ProfileId).Should().Contain(["dkg-dev-3of5", "dkg-prod-3of5"]);
    }

    [Fact]
    public async Task StartElectionCeremony_WithDevProfilesDisabled_HidesAndRejectsDevOnlyProfiles()
    {
        var client = await StartClientAsync(enableDevProfiles: false);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Production Gating");

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = createResponse.Election.ElectionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.CeremonyProfiles.Select(x => x.ProfileId).Should().Equal("dkg-prod-3of5");

        var startSubmitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.StartElectionCeremony(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(createResponse.Election.ElectionId)),
                "dkg-dev-3of5"));

        startSubmitResponse.Successfull.Should().BeFalse();
        startSubmitResponse.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task StartGovernedProposal_WithReadyCeremony_AllowsGovernedOpenPath()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Ready Ceremony");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        await CompleteReadyThresholdAsync(client, electionId, ceremonyVersionId, requiredCompletionCount: 3);

        var readiness = await client.GetElectionOpenReadinessAsync(new GetElectionOpenReadinessRequest
        {
            ElectionId = electionId,
        });

        readiness.IsReadyToOpen.Should().BeTrue(string.Join(" | ", readiness.ValidationErrors));
        readiness.CeremonySnapshot.Should().NotBeNull();
        readiness.CeremonySnapshot!.ProfileId.Should().Be("dkg-prod-3of5");
        readiness.CeremonySnapshot.CompletedTrustees.Should().HaveCount(3);

        var proposalResponse = await StartGovernedProposalViaBlockchainAsync(
            client,
            electionId,
            ElectionGovernedActionType.Open);

        proposalResponse.Success.Should().BeTrue();
        proposalResponse.GovernedProposals.Should().ContainSingle(x =>
            x.ActionType == ElectionGovernedActionTypeProto.GovernedActionOpen &&
            x.ExecutionStatus == ElectionGovernedProposalExecutionStatusProto.WaitingForApprovals);
    }

    [Fact]
    public async Task StartGovernedProposal_WithIncompleteCeremony_BlocksGovernedOpenPath()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Incomplete Ceremony");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        await PublishJoinAndSelfTestAsync(client, electionId, ceremonyVersionId, RolloutTrustees[0], 0);

        var readiness = await client.GetElectionOpenReadinessAsync(new GetElectionOpenReadinessRequest
        {
            ElectionId = electionId,
        });

        readiness.IsReadyToOpen.Should().BeFalse();
        readiness.ValidationErrors.Should().Contain(x => x.Contains("ready key-ceremony version", StringComparison.OrdinalIgnoreCase));

        var (submitResponse, proposalId) = await SubmitStartGovernedProposalAsync(
            electionId,
            ElectionGovernedActionType.Open);

        submitResponse.Successfull.Should().BeFalse();
        submitResponse.Message.Should().NotBeNullOrWhiteSpace();

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.GovernedProposals.Should().NotContain(x => x.Id == proposalId.ToString());
    }

    [Fact]
    public async Task RestartElectionCeremony_AfterProgress_SupersedesPreviousVersionAndKeepsOpenBlocked()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Superseded Ceremony");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var firstVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        await PublishAndJoinAsync(client, electionId, firstVersionId, RolloutTrustees[0], 0);

        var restartResponse = await RestartCeremonyAsync(
            electionId,
            "dkg-prod-3of5",
            "Replace the partially completed version.");

        restartResponse.Success.Should().BeTrue(restartResponse.ErrorMessage);
        restartResponse.CeremonyVersion.Should().NotBeNull();
        restartResponse.CeremonyVersion!.VersionNumber.Should().Be(2);

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.CeremonyVersions.Should().ContainSingle(x =>
            string.Equals(x.Id, firstVersionId, StringComparison.Ordinal) &&
            x.Status == ElectionCeremonyVersionStatusProto.CeremonyVersionSuperseded);
        electionResponse.CeremonyVersions.Should().ContainSingle(x =>
            string.Equals(x.Id, restartResponse.CeremonyVersion.Id, StringComparison.Ordinal) &&
            x.Status == ElectionCeremonyVersionStatusProto.CeremonyVersionInProgress);

        var readiness = await client.GetElectionOpenReadinessAsync(new GetElectionOpenReadinessRequest
        {
            ElectionId = electionId,
        });

        readiness.IsReadyToOpen.Should().BeFalse();
        readiness.ValidationErrors.Should().Contain(x => x.Contains("ready key-ceremony version", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SubmitElectionCeremonyMaterial_WithoutSelfTest_ReturnsValidationFailedAndKeepsTrusteeIncomplete()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Self-Test Required");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        var trustee = RolloutTrustees[0];
        await PublishAndJoinAsync(client, electionId, ceremonyVersionId, trustee, 0);

        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.SubmitElectionCeremonyMaterial(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                recipientTrusteeUserAddress: null,
                messageType: "dkg-share-package",
                payloadVersion: "omega-v1.0.0",
                encryptedPayload: "payload-without-self-test",
                payloadFingerprint: "payload-without-self-test"));

        submitResponse.Successfull.Should().BeFalse();
        submitResponse.Message.Should().NotBeNullOrWhiteSpace();

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.ActiveCeremonyTrusteeStates.Should().ContainSingle(x =>
            string.Equals(x.TrusteeUserAddress, trustee.PublicSigningAddress, StringComparison.Ordinal) &&
            x.State == ElectionTrusteeCeremonyStateProto.CeremonyStateJoined);
    }

    private async Task<HushElections.HushElectionsClient> StartClientAsync(bool enableDevProfiles)
    {
        await DisposeNodeAsync();
        await _fixture!.ResetAllAsync();

        var configurationOverrides = new Dictionary<string, string?>
        {
            ["Elections:Ceremony:EnableDevCeremonyProfiles"] = enableDevProfiles ? "true" : "false",
        };

        (_node, _blockControl, _grpcFactory) = await _fixture.StartNodeAsync(configurationOverrides: configurationOverrides);
        return _grpcFactory.CreateClient<HushElections.HushElectionsClient>();
    }

    private async Task DisposeNodeAsync()
    {
        _grpcFactory?.Dispose();
        _grpcFactory = null;
        _blockControl = null;

        if (_node is not null)
        {
            await _node.DisposeAsync();
            _node = null;
        }
    }

    private async Task<ElectionCommandResponse> CreateTrusteeThresholdDraftAsync(
        HushElections.HushElectionsClient client,
        string title)
    {
        var (signedTransaction, electionId) = TestTransactionFactory.CreateElectionDraft(
            TestIdentities.Alice,
            "feat-097 integration draft",
            BuildTrusteeThresholdDraftSpecification(title));
        var submitResponse = await SubmitBlockchainTransactionAsync(signedTransaction);
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var response = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId.ToString(),
        });

        response.Success.Should().BeTrue(response.ErrorMessage);
        response.LatestDraftSnapshot.Should().NotBeNull();

        var importRosterResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.ImportElectionRoster(
                TestIdentities.Alice,
                electionId,
                BuildOpenReadyRosterEntries()));
        importRosterResponse.Successfull.Should().BeTrue(importRosterResponse.Message);

        return new ElectionCommandResponse
        {
            Success = true,
            Election = response.Election,
            DraftSnapshot = response.LatestDraftSnapshot,
        };
    }

    private static IReadOnlyList<ElectionRosterImportItem> BuildOpenReadyRosterEntries() =>
    [
        new ElectionRosterImportItem("feat097-owner-ready-001", ElectionRosterContactType.Email, "ready-owner-001@hush.test"),
        new ElectionRosterImportItem("feat097-owner-ready-002", ElectionRosterContactType.Phone, "+15550002002", IsInitiallyActive: false),
        new ElectionRosterImportItem("feat097-owner-ready-003", ElectionRosterContactType.Email, "ready-owner-003@hush.test"),
    ];

    private async Task InviteAndAcceptRolloutTrusteesAsync(HushElections.HushElectionsClient client, string electionId)
    {
        foreach (var trustee in RolloutTrustees)
        {
            var (signedTransaction, invitationId) = TestTransactionFactory.CreateElectionTrusteeInvitation(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                trustee);
            var inviteSubmitResponse = await SubmitBlockchainTransactionAsync(signedTransaction);
            inviteSubmitResponse.Successfull.Should().BeTrue(inviteSubmitResponse.Message);

            var acceptSubmitResponse = await SubmitBlockchainTransactionAsync(
                TestTransactionFactory.AcceptElectionTrusteeInvitation(
                    trustee,
                    new ElectionId(Guid.Parse(electionId)),
                    invitationId));
            acceptSubmitResponse.Successfull.Should().BeTrue(acceptSubmitResponse.Message);
        }
    }

    private async Task<string> StartCeremonyAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        string profileId)
    {
        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.StartElectionCeremony(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                profileId));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var response = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        response.Success.Should().BeTrue(response.ErrorMessage);
        return response.CeremonyVersions
            .Where(x => x.ProfileId == profileId)
            .OrderByDescending(x => x.VersionNumber)
            .First()
            .Id;
    }

    private async Task<ElectionCommandResponse> RestartCeremonyAsync(
        string electionId,
        string profileId,
        string restartReason)
    {
        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RestartElectionCeremony(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                profileId,
                restartReason));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var response = await _grpcFactory!.CreateClient<HushElections.HushElectionsClient>().GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        response.Success.Should().BeTrue(response.ErrorMessage);
        var version = response.CeremonyVersions
            .Where(x => x.ProfileId == profileId)
            .OrderByDescending(x => x.VersionNumber)
            .First();

        return new ElectionCommandResponse
        {
            Success = true,
            Election = response.Election,
            CeremonyVersion = version,
        };
    }

    private async Task<GetElectionResponse> StartGovernedProposalViaBlockchainAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        ElectionGovernedActionType actionType)
    {
        var (submitResponse, proposalId) = await SubmitStartGovernedProposalAsync(electionId, actionType);
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);
        await _blockControl!.ProduceBlockAsync();

        var response = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        response.Success.Should().BeTrue(response.ErrorMessage);
        response.GovernedProposals.Should().ContainSingle(x => x.Id == proposalId.ToString());
        return response;
    }

    private async Task<(SubmitSignedTransactionReply SubmitResponse, Guid ProposalId)> SubmitStartGovernedProposalAsync(
        string electionId,
        ElectionGovernedActionType actionType)
    {
        var blockchainClient = _grpcFactory!.CreateClient<HushBlockchain.HushBlockchainClient>();
        var (signedTransaction, proposalId) = TestTransactionFactory.StartElectionGovernedProposal(
            TestIdentities.Alice,
            new ElectionId(Guid.Parse(electionId)),
            actionType);
        using var waiter = _node!.StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        if (submitResponse.Successfull)
        {
            await waiter.WaitAsync();
        }

        return (submitResponse, proposalId);
    }

    private async Task CompleteReadyThresholdAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        string ceremonyVersionId,
        int requiredCompletionCount)
    {
        const string tallyFingerprint = "feat097-ready-tally-fingerprint";

        for (var index = 0; index < requiredCompletionCount; index++)
        {
            var trustee = RolloutTrustees[index];
            await PublishJoinAndSelfTestAsync(client, electionId, ceremonyVersionId, trustee, index);

            var submitResponse = await SubmitBlockchainTransactionAsync(
                TestTransactionFactory.SubmitElectionCeremonyMaterial(
                    trustee,
                    new ElectionId(Guid.Parse(electionId)),
                    Guid.Parse(ceremonyVersionId),
                    recipientTrusteeUserAddress: null,
                    messageType: "dkg-share-package",
                    payloadVersion: "omega-v1.0.0",
                    encryptedPayload: $"payload-{index}",
                    payloadFingerprint: $"payload-fingerprint-{index}"));
            submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

            var completeResponse = await SubmitBlockchainTransactionAsync(
                TestTransactionFactory.CompleteElectionCeremonyTrustee(
                    TestIdentities.Alice,
                    new ElectionId(Guid.Parse(electionId)),
                    Guid.Parse(ceremonyVersionId),
                    trustee.PublicSigningAddress,
                    $"share-v1-{index}",
                    tallyFingerprint));
            completeResponse.Successfull.Should().BeTrue(completeResponse.Message);
        }
    }

    private async Task PublishJoinAndSelfTestAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        string ceremonyVersionId,
        TestIdentity trustee,
        int index)
    {
        await PublishAndJoinAsync(client, electionId, ceremonyVersionId, trustee, index);

        var selfTestResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RecordElectionCeremonySelfTestSuccess(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId)));
        selfTestResponse.Successfull.Should().BeTrue(selfTestResponse.Message);
    }

    private async Task PublishAndJoinAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        string ceremonyVersionId,
        TestIdentity trustee,
        int index)
    {
        var publishResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.PublishElectionCeremonyTransportKey(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                $"transport-fingerprint-{index}"));
        publishResponse.Successfull.Should().BeTrue(publishResponse.Message);

        var joinResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.JoinElectionCeremony(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId)));
        joinResponse.Successfull.Should().BeTrue(joinResponse.Message);
    }

    private async Task<SubmitSignedTransactionReply> SubmitBlockchainTransactionAsync(string signedTransaction)
    {
        var blockchainClient = _grpcFactory!.CreateClient<HushBlockchain.HushBlockchainClient>();
        using var waiter = _node!.StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        if (submitResponse.Successfull)
        {
            await waiter.WaitAsync();
            await _blockControl!.ProduceBlockAsync();
        }

        return submitResponse;
    }

    private static ElectionDraftSpecification BuildTrusteeThresholdDraftSpecification(string title) =>
        new(
            Title: title,
            ShortDescription: "Governed policy vote",
            ExternalReferenceCode: "REF-2026-097",
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
                new ElectionOptionDefinition("yes", "Yes", "Approve the proposal", 1, false),
                new ElectionOptionDefinition("no", "No", "Reject the proposal", 2, false),
            ],
            AcknowledgedWarningCodes:
            [
                ElectionWarningCode.AllTrusteesRequiredFragility,
            ],
            RequiredApprovalCount: 3);
}
