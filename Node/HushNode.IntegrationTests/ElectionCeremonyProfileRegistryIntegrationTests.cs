using FluentAssertions;
using Google.Protobuf;
using HushNetwork.proto;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode;
using HushServerNode.Testing;
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

        var startResponse = await client.StartElectionCeremonyAsync(new StartElectionCeremonyRequest
        {
            ElectionId = createResponse.Election.ElectionId,
            ActorPublicAddress = TestIdentities.Alice.PublicSigningAddress,
            ProfileId = "dkg-dev-3of5",
        });

        startResponse.Success.Should().BeFalse();
        startResponse.ErrorCode.Should().Be(ElectionCommandErrorCodeProto.NotSupported);
        startResponse.ErrorMessage.Should().Contain("disabled");
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

        var proposalResponse = await client.StartElectionGovernedProposalAsync(new StartElectionGovernedProposalRequest
        {
            ElectionId = electionId,
            ActionType = ElectionGovernedActionTypeProto.GovernedActionOpen,
            ActorPublicAddress = TestIdentities.Alice.PublicSigningAddress,
        });

        proposalResponse.Success.Should().BeTrue();
        proposalResponse.GovernedProposal.Should().NotBeNull();
        proposalResponse.GovernedProposal!.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatusProto.WaitingForApprovals);
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

        var proposalResponse = await client.StartElectionGovernedProposalAsync(new StartElectionGovernedProposalRequest
        {
            ElectionId = electionId,
            ActionType = ElectionGovernedActionTypeProto.GovernedActionOpen,
            ActorPublicAddress = TestIdentities.Alice.PublicSigningAddress,
        });

        proposalResponse.Success.Should().BeFalse();
        proposalResponse.ErrorCode.Should().Be(ElectionCommandErrorCodeProto.ValidationFailed);
        proposalResponse.ValidationErrors.Should().Contain(x => x.Contains("ready key-ceremony version", StringComparison.OrdinalIgnoreCase));
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

    private static async Task<ElectionCommandResponse> CreateTrusteeThresholdDraftAsync(
        HushElections.HushElectionsClient client,
        string title)
    {
        var response = await client.CreateElectionDraftAsync(new CreateElectionDraftRequest
        {
            OwnerPublicAddress = TestIdentities.Alice.PublicSigningAddress,
            ActorPublicAddress = TestIdentities.Alice.PublicSigningAddress,
            SnapshotReason = "feat-097 integration draft",
            Draft = BuildTrusteeThresholdDraft(title),
        });

        response.Success.Should().BeTrue(response.ErrorMessage);
        return response;
    }

    private static async Task InviteAndAcceptRolloutTrusteesAsync(HushElections.HushElectionsClient client, string electionId)
    {
        foreach (var trustee in RolloutTrustees)
        {
            var inviteResponse = await client.InviteElectionTrusteeAsync(new InviteElectionTrusteeRequest
            {
                ElectionId = electionId,
                ActorPublicAddress = TestIdentities.Alice.PublicSigningAddress,
                TrusteeUserAddress = trustee.PublicSigningAddress,
                TrusteeDisplayName = trustee.DisplayName,
            });

            inviteResponse.Success.Should().BeTrue(inviteResponse.ErrorMessage);

            var acceptResponse = await client.AcceptElectionTrusteeInvitationAsync(new ResolveElectionTrusteeInvitationRequest
            {
                ElectionId = electionId,
                InvitationId = inviteResponse.TrusteeInvitation!.Id,
                ActorPublicAddress = trustee.PublicSigningAddress,
            });

            acceptResponse.Success.Should().BeTrue(acceptResponse.ErrorMessage);
        }
    }

    private static async Task<string> StartCeremonyAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        string profileId)
    {
        var response = await client.StartElectionCeremonyAsync(new StartElectionCeremonyRequest
        {
            ElectionId = electionId,
            ActorPublicAddress = TestIdentities.Alice.PublicSigningAddress,
            ProfileId = profileId,
        });

        response.Success.Should().BeTrue(response.ErrorMessage);
        response.CeremonyVersion.Should().NotBeNull();
        return response.CeremonyVersion!.Id;
    }

    private static async Task CompleteReadyThresholdAsync(
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

            var submitResponse = await client.SubmitElectionCeremonyMaterialAsync(new SubmitElectionCeremonyMaterialRequest
            {
                ElectionId = electionId,
                CeremonyVersionId = ceremonyVersionId,
                ActorPublicAddress = trustee.PublicSigningAddress,
                MessageType = "dkg-share-package",
                PayloadVersion = "omega-v1.0.0",
                EncryptedPayload = ByteString.CopyFromUtf8($"payload-{index}"),
                PayloadFingerprint = $"payload-fingerprint-{index}",
            });

            submitResponse.Success.Should().BeTrue(submitResponse.ErrorMessage);

            var completeResponse = await client.CompleteElectionCeremonyTrusteeAsync(new CompleteElectionCeremonyTrusteeRequest
            {
                ElectionId = electionId,
                CeremonyVersionId = ceremonyVersionId,
                ActorPublicAddress = TestIdentities.Alice.PublicSigningAddress,
                TrusteeUserAddress = trustee.PublicSigningAddress,
                ShareVersion = $"share-v1-{index}",
                TallyPublicKeyFingerprint = tallyFingerprint,
            });

            completeResponse.Success.Should().BeTrue(completeResponse.ErrorMessage);
        }
    }

    private static async Task PublishJoinAndSelfTestAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        string ceremonyVersionId,
        TestIdentity trustee,
        int index)
    {
        var publishResponse = await client.PublishElectionCeremonyTransportKeyAsync(new PublishElectionCeremonyTransportKeyRequest
        {
            ElectionId = electionId,
            CeremonyVersionId = ceremonyVersionId,
            ActorPublicAddress = trustee.PublicSigningAddress,
            TransportPublicKeyFingerprint = $"transport-fingerprint-{index}",
        });

        publishResponse.Success.Should().BeTrue(publishResponse.ErrorMessage);

        var joinResponse = await client.JoinElectionCeremonyAsync(new JoinElectionCeremonyRequest
        {
            ElectionId = electionId,
            CeremonyVersionId = ceremonyVersionId,
            ActorPublicAddress = trustee.PublicSigningAddress,
        });

        joinResponse.Success.Should().BeTrue(joinResponse.ErrorMessage);

        var selfTestResponse = await client.RecordElectionCeremonySelfTestSuccessAsync(new RecordElectionCeremonySelfTestRequest
        {
            ElectionId = electionId,
            CeremonyVersionId = ceremonyVersionId,
            ActorPublicAddress = trustee.PublicSigningAddress,
        });

        selfTestResponse.Success.Should().BeTrue(selfTestResponse.ErrorMessage);
    }

    private static ElectionDraftInput BuildTrusteeThresholdDraft(string title)
    {
        var draft = new ElectionDraftInput
        {
            Title = title,
            ShortDescription = "Governed policy vote",
            ExternalReferenceCode = "REF-2026-097",
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
}
