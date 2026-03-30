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

namespace HushServerNode.Tests.Elections;

public class ElectionsGrpcServiceTests
{
    [Fact]
    public async Task CreateElectionDraft_RejectsDirectCommandPath()
    {
        // Arrange
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = CreateDraftRequest();

        // Act
        var act = async () => await sut.CreateElectionDraft(request, CreateMockServerCallContext());

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Which.Status.Detail.Should().Contain("SubmitSignedTransaction");
    }

    [Fact]
    public async Task InviteElectionTrustee_RejectsDirectCommandPath()
    {
        // Arrange
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new InviteElectionTrusteeRequest
        {
            ElectionId = ElectionId.NewElectionId.ToString(),
            ActorPublicAddress = "owner-address",
            TrusteeUserAddress = "trustee-address",
            TrusteeDisplayName = "Trustee",
        };

        // Act
        var act = async () => await sut.InviteElectionTrustee(request, CreateMockServerCallContext());

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Which.Status.Detail.Should().Contain("SubmitSignedTransaction");
    }

    [Fact]
    public async Task CreateElectionReportAccessGrant_RejectsDirectCommandPath()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new CreateElectionReportAccessGrantRequest
        {
            ElectionId = ElectionId.NewElectionId.ToString(),
            ActorPublicAddress = "owner-address",
            DesignatedAuditorPublicAddress = "auditor-address",
        };

        var act = async () => await sut.CreateElectionReportAccessGrant(request, CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Which.Status.Detail.Should().Contain("SubmitSignedTransaction");
    }

    [Fact]
    public async Task UpdateElectionDraft_RejectsDirectCommandPath()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new UpdateElectionDraftRequest
        {
            ElectionId = ElectionId.NewElectionId.ToString(),
            ActorPublicAddress = "owner-address",
            SnapshotReason = "owner update",
            Draft = CreateDraftRequest().Draft,
        };

        var act = async () => await sut.UpdateElectionDraft(request, CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Which.Status.Detail.Should().Contain("SubmitSignedTransaction");
    }

    [Fact]
    public async Task RevokeElectionTrusteeInvitation_RejectsDirectCommandPath()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new ResolveElectionTrusteeInvitationRequest
        {
            ElectionId = ElectionId.NewElectionId.ToString(),
            InvitationId = Guid.NewGuid().ToString(),
            ActorPublicAddress = "owner-address",
        };

        var act = async () => await sut.RevokeElectionTrusteeInvitation(request, CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Which.Status.Detail.Should().Contain("SubmitSignedTransaction");
    }

    [Fact]
    public async Task OpenElection_RejectsDirectCommandPath()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new OpenElectionRequest
        {
            ElectionId = ElectionId.NewElectionId.ToString(),
            ActorPublicAddress = "owner-address",
        };

        var act = async () => await sut.OpenElection(request, CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Which.Status.Detail.Should().Contain("SubmitSignedTransaction");
    }

    [Fact]
    public async Task CloseElection_RejectsDirectCommandPath()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new CloseElectionRequest
        {
            ElectionId = ElectionId.NewElectionId.ToString(),
            ActorPublicAddress = "owner-address",
        };

        var act = async () => await sut.CloseElection(request, CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Which.Status.Detail.Should().Contain("SubmitSignedTransaction");
    }

    [Fact]
    public async Task FinalizeElection_RejectsDirectCommandPath()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new FinalizeElectionRequest
        {
            ElectionId = ElectionId.NewElectionId.ToString(),
            ActorPublicAddress = "owner-address",
        };

        var act = async () => await sut.FinalizeElection(request, CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Which.Status.Detail.Should().Contain("SubmitSignedTransaction");
    }

    [Fact]
    public async Task RegisterElectionVotingCommitment_RejectsDirectCommandPath()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new Proto.RegisterElectionVotingCommitmentRequest
        {
            ElectionId = ElectionId.NewElectionId.ToString(),
            ActorPublicAddress = "voter-address",
            CommitmentHash = "commitment-hash-1",
        };

        var act = async () => await sut.RegisterElectionVotingCommitment(request, CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Which.Status.Detail.Should().Contain("SubmitSignedTransaction");
    }

    [Fact]
    public async Task AcceptElectionBallotCast_RejectsDirectCommandPath()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new Proto.AcceptElectionBallotCastRequest
        {
            ElectionId = ElectionId.NewElectionId.ToString(),
            ActorPublicAddress = "voter-address",
            IdempotencyKey = "cast-key-1",
            EncryptedBallotPackage = "ciphertext",
            ProofBundle = "proof-bundle",
            BallotNullifier = "nullifier-1",
            OpenArtifactId = Guid.NewGuid().ToString(),
            EligibleSetHash = Google.Protobuf.ByteString.CopyFrom(new byte[] { 1, 2, 3, 4 }),
            CeremonyVersionId = Guid.NewGuid().ToString(),
            DkgProfileId = "dkg-prod-1of1",
            TallyPublicKeyFingerprint = "tally-fingerprint",
        };

        var act = async () => await sut.AcceptElectionBallotCast(request, CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Which.Status.Detail.Should().Contain("SubmitSignedTransaction");
    }

    [Fact]
    public async Task StartElectionCeremony_RejectsDirectCommandPath()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new StartElectionCeremonyRequest
        {
            ElectionId = ElectionId.NewElectionId.ToString(),
            ActorPublicAddress = "owner-address",
            ProfileId = "prod-1of1-v1",
        };

        var act = async () => await sut.StartElectionCeremony(request, CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Which.Status.Detail.Should().Contain("SubmitSignedTransaction");
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
    public async Task GetElectionEligibilityView_WithValidRequest_ReturnsEligibilityPayload()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionEligibilityViewAsync(
                electionId,
                "voter-address"))
            .ReturnsAsync(new GetElectionEligibilityViewResponse
            {
                Success = true,
                ActorRole = ElectionEligibilityActorRoleProto.EligibilityActorLinkedVoter,
                ActorPublicAddress = "voter-address",
                UsesTemporaryVerificationCode = true,
                TemporaryVerificationCode = "1111",
                Summary = new ElectionEligibilitySummaryView
                {
                    RosteredCount = 12,
                    CurrentDenominatorCount = 9,
                },
                SelfRosterEntry = new ElectionRosterEntryView
                {
                    ElectionId = electionId.ToString(),
                    OrganizationVoterId = "1001",
                    ParticipationStatus = ElectionParticipationStatusProto.ParticipationDidNotVote,
                },
            });

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionEligibilityView(new GetElectionEligibilityViewRequest
        {
            ElectionId = electionId.ToString(),
            ActorPublicAddress = "voter-address",
        }, CreateMockServerCallContext());

        response.Success.Should().BeTrue();
        response.ActorRole.Should().Be(ElectionEligibilityActorRoleProto.EligibilityActorLinkedVoter);
        response.TemporaryVerificationCode.Should().Be("1111");
        response.SelfRosterEntry.Should().NotBeNull();
        response.SelfRosterEntry.OrganizationVoterId.Should().Be("1001");
        response.Summary.RosteredCount.Should().Be(12);
    }

    [Fact]
    public async Task GetElectionHubView_WithValidRequest_ReturnsActorScopedHubPayload()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionHubViewAsync("actor-address"))
            .ReturnsAsync(new GetElectionHubViewResponse
            {
                Success = true,
                ActorPublicAddress = "actor-address",
                HasAnyElectionRoles = true,
                Elections =
                {
                    new ElectionHubEntryView
                    {
                        Election = new ElectionSummary
                        {
                            ElectionId = electionId.ToString(),
                            Title = "Board Election",
                        },
                        ActorRoles = new ElectionApplicationRoleFlagsView
                        {
                            IsOwnerAdmin = true,
                        },
                        SuggestedAction = ElectionHubNextActionHintProto.ElectionHubActionOwnerManageDraft,
                    },
                },
            });

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionHubView(new GetElectionHubViewRequest
        {
            ActorPublicAddress = "actor-address",
        }, CreateMockServerCallContext());

        response.Success.Should().BeTrue();
        response.ActorPublicAddress.Should().Be("actor-address");
        response.HasAnyElectionRoles.Should().BeTrue();
        response.Elections.Should().ContainSingle();
        response.Elections[0].Election.Title.Should().Be("Board Election");
        response.Elections[0].SuggestedAction.Should().Be(ElectionHubNextActionHintProto.ElectionHubActionOwnerManageDraft);
    }

    [Fact]
    public async Task GetElectionVotingView_WithValidRequest_ReturnsVotingPayload()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionVotingViewAsync(
                electionId,
                "voter-address",
                "cast-key-1"))
            .ReturnsAsync(new GetElectionVotingViewResponse
            {
                Success = true,
                ActorPublicAddress = "voter-address",
                CommitmentRegistered = true,
                PersonalParticipationStatus = ElectionParticipationStatusProto.ParticipationCountedAsVoted,
                SubmissionStatus = ElectionVotingSubmissionStatusProto.VotingSubmissionStatusAlreadyUsed,
                OpenArtifactId = Guid.NewGuid().ToString(),
                DkgProfileId = "dkg-prod-1of1",
                TallyPublicKeyFingerprint = "tally-fingerprint",
            });

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionVotingView(new GetElectionVotingViewRequest
        {
            ElectionId = electionId.ToString(),
            ActorPublicAddress = "voter-address",
            SubmissionIdempotencyKey = "cast-key-1",
        }, CreateMockServerCallContext());

        response.Success.Should().BeTrue();
        response.ActorPublicAddress.Should().Be("voter-address");
        response.CommitmentRegistered.Should().BeTrue();
        response.PersonalParticipationStatus.Should().Be(ElectionParticipationStatusProto.ParticipationCountedAsVoted);
        response.SubmissionStatus.Should().Be(ElectionVotingSubmissionStatusProto.VotingSubmissionStatusAlreadyUsed);
        response.DkgProfileId.Should().Be("dkg-prod-1of1");
        response.TallyPublicKeyFingerprint.Should().Be("tally-fingerprint");
    }

    [Fact]
    public async Task GetElectionReportAccessGrants_WithValidRequest_ReturnsGrantPayload()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionReportAccessGrantsAsync(electionId, "owner-address"))
            .ReturnsAsync(new GetElectionReportAccessGrantsResponse
            {
                Success = true,
                ActorPublicAddress = "owner-address",
                CanManageGrants = true,
                Grants =
                {
                    new ElectionReportAccessGrantView
                    {
                        Id = Guid.NewGuid().ToString(),
                        ElectionId = electionId.ToString(),
                        ActorPublicAddress = "auditor-address",
                        GrantRole = ElectionReportAccessGrantRoleProto.ReportAccessGrantDesignatedAuditor,
                    },
                },
            });

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionReportAccessGrants(new GetElectionReportAccessGrantsRequest
        {
            ElectionId = electionId.ToString(),
            ActorPublicAddress = "owner-address",
        }, CreateMockServerCallContext());

        response.Success.Should().BeTrue();
        response.CanManageGrants.Should().BeTrue();
        response.Grants.Should().ContainSingle();
        response.Grants[0].ActorPublicAddress.Should().Be("auditor-address");
    }

    [Fact]
    public async Task StartElectionGovernedProposal_RejectsDirectCommandPath()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new Proto.StartElectionGovernedProposalRequest
        {
            ElectionId = ElectionId.NewElectionId.ToString(),
            ActionType = ElectionGovernedActionTypeProto.GovernedActionOpen,
            ActorPublicAddress = "owner-address",
        };

        var act = async () => await sut.StartElectionGovernedProposal(request, CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Which.Status.Detail.Should().Contain("SubmitSignedTransaction");
    }

    [Fact]
    public async Task ApproveElectionGovernedProposal_RejectsDirectCommandPath()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new Proto.ApproveElectionGovernedProposalRequest
        {
            ElectionId = ElectionId.NewElectionId.ToString(),
            ProposalId = Guid.NewGuid().ToString(),
            ActorPublicAddress = "trustee-address",
            ApprovalNote = "Approved",
        };

        var act = async () => await sut.ApproveElectionGovernedProposal(request, CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Which.Status.Detail.Should().Contain("SubmitSignedTransaction");
    }

    [Fact]
    public async Task RetryElectionGovernedProposalExecution_RejectsDirectCommandPath()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new Proto.RetryElectionGovernedProposalExecutionRequest
        {
            ElectionId = ElectionId.NewElectionId.ToString(),
            ProposalId = Guid.NewGuid().ToString(),
            ActorPublicAddress = "owner-address",
        };

        var act = async () => await sut.RetryElectionGovernedProposalExecution(request, CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Which.Status.Detail.Should().Contain("SubmitSignedTransaction");
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
