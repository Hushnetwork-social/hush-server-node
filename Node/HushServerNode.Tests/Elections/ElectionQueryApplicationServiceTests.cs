using FluentAssertions;
using HushNetwork.proto;
using HushNode.Elections.gRPC;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Moq;
using Moq.AutoMock;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionQueryApplicationServiceTests
{
    [Fact]
    public async Task GetElectionAsync_WithExistingElection_ReturnsElectionDetailReadModel()
    {
        // Arrange
        var mocker = new AutoMocker();
        var election = CreateAdminElection(acknowledgedWarningCodes: [ElectionWarningCode.LowAnonymitySet]);
        var snapshot = ElectionModelFactory.CreateDraftSnapshot(
            election,
            snapshotReason: "initial draft",
            recordedByPublicAddress: "owner-address");
        var warning = ElectionModelFactory.CreateWarningAcknowledgement(
            election.ElectionId,
            ElectionWarningCode.LowAnonymitySet,
            election.CurrentDraftRevision,
            acknowledgedByPublicAddress: "owner-address");
        var invitation = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision);
        var boundaryArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            election,
            recordedByPublicAddress: "owner-address",
            frozenEligibleVoterSetHash: [1, 2, 3]);

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetLatestDraftSnapshotAsync(election.ElectionId)).ReturnsAsync(snapshot);
            repo.Setup(x => x.GetWarningAcknowledgementsAsync(election.ElectionId)).ReturnsAsync([warning]);
            repo.Setup(x => x.GetTrusteeInvitationsAsync(election.ElectionId)).ReturnsAsync([invitation]);
            repo.Setup(x => x.GetBoundaryArtifactsAsync(election.ElectionId)).ReturnsAsync([boundaryArtifact]);
        });

        var sut = mocker.CreateInstance<ElectionQueryApplicationService>();

        // Act
        var response = await sut.GetElectionAsync(election.ElectionId);

        // Assert
        response.Success.Should().BeTrue();
        response.Election.Should().NotBeNull();
        response.Election.Title.Should().Be("Board Election");
        response.LatestDraftSnapshot.Should().NotBeNull();
        response.LatestDraftSnapshot.DraftRevision.Should().Be(1);
        response.WarningAcknowledgements.Should().ContainSingle();
        response.WarningAcknowledgements[0].WarningCode.Should().Be(ElectionWarningCodeProto.LowAnonymitySet);
        response.TrusteeInvitations.Should().ContainSingle();
        response.BoundaryArtifacts.Should().ContainSingle();
    }

    [Fact]
    public async Task GetElectionAsync_WithMissingElection_ReturnsNotFoundResponse()
    {
        // Arrange
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(electionId)).ReturnsAsync((ElectionRecord?)null);
        });

        var sut = mocker.CreateInstance<ElectionQueryApplicationService>();

        // Act
        var response = await sut.GetElectionAsync(electionId);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Contain(electionId.ToString());
    }

    [Fact]
    public async Task GetElectionsByOwnerAsync_WithOwnerAddress_ReturnsSummaries()
    {
        // Arrange
        var mocker = new AutoMocker();
        var elections = new[]
        {
            CreateAdminElection(title: "Board Election"),
            CreateAdminElection(title: "Treasurer Vote"),
        };

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionsByOwnerAsync("owner-address")).ReturnsAsync(elections);
        });

        var sut = mocker.CreateInstance<ElectionQueryApplicationService>();

        // Act
        var response = await sut.GetElectionsByOwnerAsync("owner-address");

        // Assert
        response.Elections.Should().HaveCount(2);
        response.Elections.Select(x => x.Title).Should().Equal("Board Election", "Treasurer Vote");
    }

    private static void ConfigureReadOnlyRepository(AutoMocker mocker, Action<Mock<IElectionsRepository>> configureRepository)
    {
        var unitOfWork = mocker.GetMock<IReadOnlyUnitOfWork<ElectionsDbContext>>();
        var repository = mocker.GetMock<IElectionsRepository>();

        mocker.GetMock<IUnitOfWorkProvider<ElectionsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(unitOfWork.Object);
        unitOfWork
            .Setup(x => x.GetRepository<IElectionsRepository>())
            .Returns(repository.Object);

        configureRepository(repository);
    }

    private static ElectionRecord CreateAdminElection(
        string title = "Board Election",
        IReadOnlyList<ElectionWarningCode>? acknowledgedWarningCodes = null) =>
        ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: title,
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
            ],
            acknowledgedWarningCodes: acknowledgedWarningCodes);
}
