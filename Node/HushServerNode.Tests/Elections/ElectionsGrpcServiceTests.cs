using FluentAssertions;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.Elections.gRPC;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.AutoMock;
using Xunit;
using Domain = HushNode.Elections;
using Proto = HushNetwork.proto;
using SharedTrusteeReference = HushShared.Elections.Model.ElectionTrusteeReference;

namespace HushServerNode.Tests.Elections;

public class ElectionsGrpcServiceTests
{
    [Fact]
    public async Task CreateElectionDraft_WithValidRequest_MapsLifecycleResultToProtoResponse()
    {
        // Arrange
        var mocker = new AutoMocker();
        var election = CreateAdminElection();
        var snapshot = ElectionModelFactory.CreateDraftSnapshot(
            election,
            snapshotReason: "initial draft",
            recordedByPublicAddress: "owner-address");

        mocker.GetMock<Domain.IElectionLifecycleService>()
            .Setup(x => x.CreateDraftAsync(It.Is<Domain.CreateElectionDraftRequest>(request =>
                request.OwnerPublicAddress == "owner-address" &&
                request.ActorPublicAddress == "owner-address" &&
                request.SnapshotReason == "initial draft" &&
                request.Draft.Title == "Board Election" &&
                request.Draft.OwnerOptions.Count == 2)))
            .ReturnsAsync(Domain.ElectionCommandResult.Success(election, draftSnapshot: snapshot));

        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = CreateDraftRequest();

        // Act
        var response = await sut.CreateElectionDraft(request, CreateMockServerCallContext());

        // Assert
        response.Success.Should().BeTrue();
        response.ErrorCode.Should().Be(ElectionCommandErrorCodeProto.None);
        response.Election.Should().NotBeNull();
        response.Election.Title.Should().Be("Board Election");
        response.DraftSnapshot.Should().NotBeNull();
        response.DraftSnapshot.SnapshotReason.Should().Be("initial draft");
    }

    [Fact]
    public async Task OpenElection_WithDependencyBlockedFailure_MapsValidationErrors()
    {
        // Arrange
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<Domain.IElectionLifecycleService>()
            .Setup(x => x.OpenElectionAsync(It.IsAny<Domain.OpenElectionRequest>()))
            .ReturnsAsync(Domain.ElectionCommandResult.Failure(
                Domain.ElectionCommandErrorCode.DependencyBlocked,
                "Election cannot be opened.",
                ["Trustee-threshold elections cannot open until FEAT-096 provides the governed approval workflow."]));

        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new OpenElectionRequest
        {
            ElectionId = electionId.ToString(),
            ActorPublicAddress = "owner-address",
        };

        // Act
        var response = await sut.OpenElection(request, CreateMockServerCallContext());

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ElectionCommandErrorCodeProto.DependencyBlocked);
        response.ValidationErrors.Should().ContainSingle();
        response.ValidationErrors[0].Should().Contain("FEAT-096");
    }

    [Fact]
    public async Task StartElectionCeremony_WithValidRequest_MapsCeremonyPayload()
    {
        // Arrange
        var mocker = new AutoMocker();
        var election = CreateTrusteeElection();
        var profile = ElectionModelFactory.CreateCeremonyProfile(
            "prod-1of1-v1",
            "Production 1 of 1",
            "Production profile",
            "provider-a",
            "v1",
            trusteeCount: 1,
            requiredApprovalCount: 1,
            devOnly: false);
        var version = ElectionModelFactory.CreateCeremonyVersion(
            election.ElectionId,
            versionNumber: 1,
            profileId: profile.ProfileId,
            requiredApprovalCount: 1,
            boundTrustees:
            [
                new SharedTrusteeReference("trustee-a", "Alice"),
            ],
            startedByPublicAddress: "owner-address");

        mocker.GetMock<Domain.IElectionLifecycleService>()
            .Setup(x => x.StartElectionCeremonyAsync(It.Is<Domain.StartElectionCeremonyRequest>(request =>
                request.ElectionId == election.ElectionId &&
                request.ActorPublicAddress == "owner-address" &&
                request.ProfileId == profile.ProfileId)))
            .ReturnsAsync(Domain.ElectionCommandResult.Success(
                election,
                ceremonyProfile: profile,
                ceremonyVersion: version));

        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new Proto.StartElectionCeremonyRequest
        {
            ElectionId = election.ElectionId.ToString(),
            ActorPublicAddress = "owner-address",
            ProfileId = profile.ProfileId,
        };

        // Act
        var response = await sut.StartElectionCeremony(request, CreateMockServerCallContext());

        // Assert
        response.Success.Should().BeTrue();
        response.CeremonyProfile.Should().NotBeNull();
        response.CeremonyProfile.ProfileId.Should().Be(profile.ProfileId);
        response.CeremonyVersion.Should().NotBeNull();
        response.CeremonyVersion.VersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task GetElectionOpenReadiness_WithWarningGap_MapsReadinessResponse()
    {
        // Arrange
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<Domain.IElectionLifecycleService>()
            .Setup(x => x.EvaluateOpenReadinessAsync(It.IsAny<Domain.EvaluateElectionOpenReadinessRequest>()))
            .ReturnsAsync(Domain.ElectionOpenValidationResult.NotReady(
                ["Required warning acknowledgement is missing for LowAnonymitySet."],
                [ElectionWarningCode.LowAnonymitySet],
                [ElectionWarningCode.LowAnonymitySet]));

        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new GetElectionOpenReadinessRequest
        {
            ElectionId = electionId.ToString(),
        };
        request.RequiredWarningCodes.Add(ElectionWarningCodeProto.LowAnonymitySet);

        // Act
        var response = await sut.GetElectionOpenReadiness(request, CreateMockServerCallContext());

        // Assert
        response.IsReadyToOpen.Should().BeFalse();
        response.RequiredWarningCodes.Should().Contain(ElectionWarningCodeProto.LowAnonymitySet);
        response.MissingWarningAcknowledgements.Should().Contain(ElectionWarningCodeProto.LowAnonymitySet);
    }

    [Fact]
    public async Task GetElectionCeremonyActionView_WithValidRequest_ReturnsRoleScopedActionPayload()
    {
        // Arrange
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;
        var versionId = Guid.NewGuid();

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionCeremonyActionViewAsync(
                electionId,
                "trustee-a"))
            .ReturnsAsync(new GetElectionCeremonyActionViewResponse
            {
                Success = true,
                ActorRole = ElectionCeremonyActorRoleProto.CeremonyActorTrustee,
                ActorPublicAddress = "trustee-a",
                PendingIncomingMessageCount = 1,
                ActiveCeremonyVersion = new ElectionCeremonyVersion
                {
                    Id = versionId.ToString(),
                    ElectionId = electionId.ToString(),
                    VersionNumber = 1,
                    ProfileId = "prod-1of1-v1",
                    Status = ElectionCeremonyVersionStatusProto.CeremonyVersionInProgress,
                    TrusteeCount = 1,
                    RequiredApprovalCount = 1,
                    StartedByPublicAddress = "owner-address",
                    StartedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
                },
            });

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        // Act
        var response = await sut.GetElectionCeremonyActionView(new GetElectionCeremonyActionViewRequest
        {
            ElectionId = electionId.ToString(),
            ActorPublicAddress = "trustee-a",
        }, CreateMockServerCallContext());

        // Assert
        response.Success.Should().BeTrue();
        response.ActorRole.Should().Be(ElectionCeremonyActorRoleProto.CeremonyActorTrustee);
        response.PendingIncomingMessageCount.Should().Be(1);
        response.ActiveCeremonyVersion.Should().NotBeNull();
        response.ActiveCeremonyVersion.ProfileId.Should().Be("prod-1of1-v1");
    }

    [Fact]
    public async Task StartElectionGovernedProposal_WithValidRequest_MapsGovernedProposalPayload()
    {
        // Arrange
        var mocker = new AutoMocker();
        var election = CreateTrusteeElection();
        var proposal = ElectionModelFactory.CreateGovernedProposal(
            election,
            ElectionGovernedActionType.Open,
            proposedByPublicAddress: "owner-address");

        mocker.GetMock<Domain.IElectionLifecycleService>()
            .Setup(x => x.StartGovernedProposalAsync(It.Is<Domain.StartElectionGovernedProposalRequest>(request =>
                request.ElectionId == election.ElectionId &&
                request.ActionType == ElectionGovernedActionType.Open &&
                request.ActorPublicAddress == "owner-address")))
            .ReturnsAsync(Domain.ElectionCommandResult.Success(election, governedProposal: proposal));

        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new Proto.StartElectionGovernedProposalRequest
        {
            ElectionId = election.ElectionId.ToString(),
            ActionType = ElectionGovernedActionTypeProto.GovernedActionOpen,
            ActorPublicAddress = "owner-address",
        };

        // Act
        var response = await sut.StartElectionGovernedProposal(request, CreateMockServerCallContext());

        // Assert
        response.Success.Should().BeTrue();
        response.GovernedProposal.Should().NotBeNull();
        response.GovernedProposal.ActionType.Should().Be(ElectionGovernedActionTypeProto.GovernedActionOpen);
        response.GovernedProposal.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatusProto.WaitingForApprovals);
    }

    [Fact]
    public async Task GetElection_WithInvalidElectionId_ThrowsInvalidArgumentRpcException()
    {
        // Arrange
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        // Act
        var act = async () => await sut.GetElection(new GetElectionRequest { ElectionId = "not-a-guid" }, CreateMockServerCallContext());

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    private static Proto.CreateElectionDraftRequest CreateDraftRequest()
    {
        var request = new Proto.CreateElectionDraftRequest
        {
            OwnerPublicAddress = "owner-address",
            ActorPublicAddress = "owner-address",
            SnapshotReason = "initial draft",
            Draft = new ElectionDraftInput
            {
                Title = "Board Election",
                ShortDescription = "Annual board vote",
                ExternalReferenceCode = "ORG-2026-01",
                ElectionClass = ElectionClassProto.OrganizationalRemoteVoting,
                BindingStatus = ElectionBindingStatusProto.Binding,
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
            },
        };

        request.Draft.ApprovedClientApplications.Add(new ApprovedClientApplication
        {
            ApplicationId = "hushsocial",
            Version = "1.0.0",
        });
        request.Draft.OwnerOptions.Add(new ElectionOption
        {
            OptionId = "alice",
            DisplayLabel = "Alice",
            BallotOrder = 1,
            IsBlankOption = false,
        });
        request.Draft.OwnerOptions.Add(new ElectionOption
        {
            OptionId = "bob",
            DisplayLabel = "Bob",
            BallotOrder = 2,
            IsBlankOption = false,
        });

        return request;
    }

    private static ElectionRecord CreateAdminElection() =>
        ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: "Board Election",
            shortDescription: "Annual board vote",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "ORG-2026-01",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            governanceMode: ElectionGovernanceMode.AdminOnly,
            disclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            participationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            voteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            eligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            outcomeRule: new OutcomeRuleDefinition(
                OutcomeRuleKind.SingleWinner,
                "single_winner",
                SeatCount: 1,
                BlankVoteCountsForTurnout: true,
                BlankVoteExcludedFromWinnerSelection: true,
                BlankVoteExcludedFromThresholdDenominator: false,
                TieResolutionRule: "tie_unresolved",
                CalculationBasis: "highest_non_blank_votes"),
            approvedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            protocolOmegaVersion: "omega-v1.0.0",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("alice", "Alice", null, 1, IsBlankOption: false),
                new ElectionOptionDefinition("bob", "Bob", null, 2, IsBlankOption: false),
            ]);

    private static ElectionRecord CreateTrusteeElection() =>
        ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: "Referendum",
            shortDescription: "Policy vote",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "REF-2026-01",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            governanceMode: ElectionGovernanceMode.TrusteeThreshold,
            disclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            participationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            voteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            eligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            outcomeRule: new OutcomeRuleDefinition(
                OutcomeRuleKind.PassFail,
                "pass_fail_yes_no",
                SeatCount: 1,
                BlankVoteCountsForTurnout: true,
                BlankVoteExcludedFromWinnerSelection: true,
                BlankVoteExcludedFromThresholdDenominator: true,
                TieResolutionRule: "tie_unresolved",
                CalculationBasis: "simple_majority_of_non_blank_votes"),
            approvedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            protocolOmegaVersion: "omega-v1.0.0",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.GovernedReviewWindowReserved,
            ownerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", null, 1, IsBlankOption: false),
                new ElectionOptionDefinition("no", "No", null, 2, IsBlankOption: false),
            ],
            requiredApprovalCount: 2);

    private static ServerCallContext CreateMockServerCallContext() => new TestServerCallContext();
}

public class TestServerCallContext : ServerCallContext
{
    protected override string MethodCore => "TestMethod";
    protected override string HostCore => "TestHost";
    protected override string PeerCore => "TestPeer";
    protected override DateTime DeadlineCore => DateTime.MaxValue;
    protected override Metadata RequestHeadersCore => new();
    protected override CancellationToken CancellationTokenCore => CancellationToken.None;
    protected override Metadata ResponseTrailersCore => new();
    protected override Status StatusCore { get; set; }
    protected override WriteOptions? WriteOptionsCore { get; set; }
    protected override AuthContext AuthContextCore => new(null, new Dictionary<string, List<AuthProperty>>());

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
        throw new NotImplementedException();

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
        Task.CompletedTask;
}
