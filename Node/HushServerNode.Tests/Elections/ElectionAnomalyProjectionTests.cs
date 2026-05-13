using FluentAssertions;
using HushNode.Elections;
using HushNode.Elections.gRPC;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Moq;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionAnomalyProjectionTests
{
    [Fact]
    public async Task GetElectionAnomalyOwnThreadAsync_ReturnsOnlyActorThreadWithEncryptedBody()
    {
        var election = CreateElection();
        var ownThread = CreateThread(election, "owner-address");
        var otherThread = CreateThread(election, "other-address", submitterPersonScopeId: "sha256:other");
        var ownMessage = CreateMessage(ownThread);
        var repository = CreateRepository(election, [ownThread, otherThread]);
        SetupThreadMessages(repository, ownThread, ownMessage);
        SetupThreadMessages(repository, otherThread, CreateMessage(otherThread));
        var sut = CreateService(repository.Object);

        var projection = await sut.GetElectionAnomalyOwnThreadAsync(election.ElectionId, "owner-address");

        projection.Should().NotBeNull();
        projection!.AnomalyThreadId.Should().Be(ownThread.Id);
        projection.Messages.Should().ContainSingle();
        projection.Messages[0].EncryptedBody.Should().Be("encrypted-body");
        projection.Messages[0].RecipientWraps[0].RecipientPublicAddress.Should().Be("owner-address");
    }

    [Fact]
    public async Task GetElectionAnomalyOwnerTriageAsync_WithOwner_ReturnsAllThreadsWithIdentityMetadata()
    {
        var election = CreateElection();
        var ownerThread = CreateThread(election, "owner-address");
        var otherThread = CreateThread(election, "other-address", submitterPersonScopeId: "sha256:other");
        var repository = CreateRepository(election, [ownerThread, otherThread]);
        SetupThreadMessages(repository, ownerThread, CreateMessage(ownerThread));
        SetupThreadMessages(repository, otherThread, CreateMessage(otherThread));
        var sut = CreateService(repository.Object);

        var projections = await sut.GetElectionAnomalyOwnerTriageAsync(election.ElectionId, "owner-address");

        projections.Should().HaveCount(2);
        projections.Select(x => x.SubmitterActorPublicAddress)
            .Should()
            .Contain(["owner-address", "other-address"]);
        projections.Should().OnlyContain(x => x.Messages.Count == 1);
    }

    [Fact]
    public async Task GetElectionAnomalyOwnThreadAsync_WithPeerActor_ReturnsNullForOtherThread()
    {
        var election = CreateElection();
        var otherThread = CreateThread(election, "other-address", submitterPersonScopeId: "sha256:other");
        var repository = CreateRepository(election, [otherThread]);
        SetupThreadMessages(repository, otherThread, CreateMessage(otherThread));
        var sut = CreateService(repository.Object);

        var projection = await sut.GetElectionAnomalyOwnThreadAsync(election.ElectionId, "peer-address");

        projection.Should().BeNull();
    }

    [Fact]
    public async Task GetElectionAnomalyTrusteeCountsAsync_WithAcceptedTrustee_ReturnsBodyFreeCounts()
    {
        var election = CreateElection();
        var trusteeInvitation = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            "trustee-address",
            "Trustee",
            "owner-address",
            election.CurrentDraftRevision)
            .Accept(DateTime.UtcNow, election.CurrentDraftRevision, ElectionLifecycleState.Draft);
        var firstThread = CreateThread(election, "owner-address");
        var secondThread = CreateThread(
            election,
            "other-address",
            submitterPersonScopeId: "sha256:other",
            categoryId: ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern);
        var repository = CreateRepository(election, [firstThread, secondThread]);
        repository
            .Setup(x => x.GetAcceptedTrusteeInvitationsByActorAsync("trustee-address"))
            .ReturnsAsync([trusteeInvitation]);
        var sut = CreateService(repository.Object);

        var projection = await sut.GetElectionAnomalyTrusteeCountsAsync(election.ElectionId, "trustee-address");

        projection.Should().NotBeNull();
        projection!.TotalThreadCount.Should().Be(2);
        projection.CategoryCounts.Select(x => x.CategoryId)
            .Should()
            .Contain([ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern, ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly]);
        projection.GetType().GetProperties()
            .Should()
            .NotContain(property => property.Name.Contains("Message", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetElectionAnomalyAuditorRestrictedReviewAsync_HidesSubmitterAndRecipientIdentityFields()
    {
        var election = CreateElection();
        var auditorGrant = ElectionModelFactory.CreateReportAccessGrant(
            election.ElectionId,
            "auditor-address",
            "owner-address",
            ElectionReportAccessGrantRole.DesignatedAuditor);
        var thread = CreateThread(election, "owner-address");
        var repository = CreateRepository(election, [thread]);
        repository
            .Setup(x => x.GetReportAccessGrantAsync(election.ElectionId, "auditor-address"))
            .ReturnsAsync(auditorGrant);
        SetupThreadMessages(repository, thread, CreateMessage(thread));
        var sut = CreateService(repository.Object);

        var projection = await sut.GetElectionAnomalyAuditorRestrictedReviewAsync(election.ElectionId, "auditor-address");

        projection.Should().NotBeNull();
        projection!.Threads.Should().ContainSingle();
        projection.Threads[0].Messages[0].EncryptedBody.Should().Be("encrypted-body");
        projection.Threads[0].GetType().GetProperties()
            .Should()
            .NotContain(property =>
                property.Name.Contains("Submitter", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Actor", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("PersonScope", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("RoleContext", StringComparison.OrdinalIgnoreCase));
        projection.Threads[0].Messages[0].RecipientWraps[0].GetType().GetProperties()
            .Should()
            .NotContain(property => property.Name.Contains("PublicAddress", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetElectionAnomalyAuditorRestrictedReviewAsync_WithAcceptedTrustee_ReturnsNull()
    {
        var election = CreateElection();
        var trusteeInvitation = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            "trustee-address",
            "Trustee",
            "owner-address",
            election.CurrentDraftRevision)
            .Accept(DateTime.UtcNow, election.CurrentDraftRevision, ElectionLifecycleState.Draft);
        var thread = CreateThread(election, "owner-address");
        var repository = CreateRepository(election, [thread]);
        repository
            .Setup(x => x.GetAcceptedTrusteeInvitationsByActorAsync("trustee-address"))
            .ReturnsAsync([trusteeInvitation]);
        SetupThreadMessages(repository, thread, CreateMessage(thread));
        var sut = CreateService(repository.Object);

        var projection = await sut.GetElectionAnomalyAuditorRestrictedReviewAsync(election.ElectionId, "trustee-address");

        projection.Should().BeNull();
    }

    [Fact]
    public async Task GetElectionAnomalyReportManifestSeedAsync_WithOwner_ReturnsHashesCountsAndRestrictedRecipientStatus()
    {
        var election = CreateElection();
        var firstThread = CreateThread(election, "owner-address");
        var secondThread = CreateThread(
            election,
            "other-address",
            submitterPersonScopeId: "sha256:other",
            categoryId: ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern);
        var repository = CreateRepository(election, [firstThread, secondThread]);
        SetupThreadMessages(repository, firstThread, CreateMessage(firstThread));
        SetupThreadMessages(repository, secondThread, CreateMessage(secondThread));
        var sut = CreateService(repository.Object);

        var projection = await sut.GetElectionAnomalyReportManifestSeedAsync(election.ElectionId, "owner-address");

        projection.Should().NotBeNull();
        projection!.TotalThreadCount.Should().Be(2);
        projection.Threads.Should().OnlyContain(x => x.CurrentThreadHash == "sha256:thread");
        projection.CategoryCounts.Select(x => x.CategoryId)
            .Should()
            .Contain([ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern, ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly]);
        projection.Threads[0].RecipientWraps[0].GetType().GetProperties()
            .Should()
            .NotContain(property => property.Name.Contains("PublicAddress", StringComparison.OrdinalIgnoreCase));
    }

    private static Mock<IElectionsRepository> CreateRepository(
        ElectionRecord election,
        IReadOnlyList<ElectionAnomalyThreadRecord> threads)
    {
        var repository = new Mock<IElectionsRepository>();
        repository.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
        repository.Setup(x => x.GetAnomalyThreadsAsync(election.ElectionId)).ReturnsAsync(threads);
        repository
            .Setup(x => x.GetAcceptedTrusteeInvitationsByActorAsync(It.IsAny<string>()))
            .ReturnsAsync(Array.Empty<ElectionTrusteeInvitationRecord>());
        repository
            .Setup(x => x.GetReportAccessGrantAsync(election.ElectionId, It.IsAny<string>()))
            .ReturnsAsync((ElectionReportAccessGrantRecord?)null);
        return repository;
    }

    private static void SetupThreadMessages(
        Mock<IElectionsRepository> repository,
        ElectionAnomalyThreadRecord thread,
        ElectionAnomalyMessageEnvelopeRecord message)
    {
        repository.Setup(x => x.GetAnomalyMessageEnvelopesAsync(thread.Id)).ReturnsAsync([message]);
        repository
            .Setup(x => x.GetAnomalyRecipientWrapsAsync(thread.Id))
            .ReturnsAsync(
            [
                new ElectionAnomalyRecipientWrapRecord(
                    Guid.NewGuid(),
                    message.Id,
                    thread.Id,
                    thread.ElectionId,
                    ElectionAnomalyRecipientRoleIds.Submitter,
                    thread.SubmitterActorPublicAddress,
                    "submitter-key-fingerprint",
                    "submitter-encrypted-content-key",
                    "x25519-aes-gcm",
                    ElectionAnomalyRecipientWrapStatusIds.Available,
                    DateTime.UtcNow),
                new ElectionAnomalyRecipientWrapRecord(
                    Guid.NewGuid(),
                    message.Id,
                    thread.Id,
                    thread.ElectionId,
                    ElectionAnomalyRecipientRoleIds.ElectionOwner,
                    "owner-address",
                    "owner-key-fingerprint",
                    "encrypted-content-key",
                    "x25519-aes-gcm",
                    ElectionAnomalyRecipientWrapStatusIds.Available,
                    DateTime.UtcNow),
            ]);
        repository
            .Setup(x => x.GetAnomalyThreadEventsAsync(thread.Id))
            .ReturnsAsync(Array.Empty<ElectionAnomalyThreadEventRecord>());
    }

    private static ElectionQueryApplicationService CreateService(IElectionsRepository repository)
    {
        var unitOfWork = new Mock<IReadOnlyUnitOfWork<ElectionsDbContext>>();
        unitOfWork
            .Setup(x => x.GetRepository<IElectionsRepository>())
            .Returns(repository);
        var unitOfWorkProvider = new Mock<IUnitOfWorkProvider<ElectionsDbContext>>();
        unitOfWorkProvider
            .Setup(x => x.CreateReadOnly())
            .Returns(unitOfWork.Object);

        return new ElectionQueryApplicationService(unitOfWorkProvider.Object);
    }

    private static ElectionAnomalyMessageEnvelopeRecord CreateMessage(ElectionAnomalyThreadRecord thread) =>
        new(
            Guid.NewGuid(),
            thread.Id,
            Guid.NewGuid(),
            thread.ElectionId,
            ElectionAnomalyMessageKindIds.InitialSubmission,
            "encrypted-body",
            "sha256:body",
            PlaintextBodyHash: null,
            PlaintextCharacterCount: 24,
            EncryptionAlgorithm: "x25519-aes-gcm",
            DateTime.UtcNow);

    private static ElectionAnomalyThreadRecord CreateThread(
        ElectionRecord election,
        string actorPublicAddress,
        string? submitterPersonScopeId = null,
        string categoryId = ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly)
    {
        var roleResolution = ElectionAnomalyAuthorization.ResolveActorSubmitter(
            election,
            "owner-address",
            DateTime.UtcNow);
        roleResolution.IsResolved.Should().BeTrue();

        return new ElectionAnomalyThreadRecord(
            Guid.NewGuid(),
            election.ElectionId,
            submitterPersonScopeId ?? roleResolution.SubmitterPersonScopeId!,
            roleResolution.PersonScopeDerivationVersion,
            actorPublicAddress,
            roleResolution.RoleContextId,
            roleResolution.RoleEvidenceTypeId!,
            roleResolution.RoleEvidenceReference!,
            election.LifecycleState,
            election.AnomalySubmissionWindowClosesAt,
            categoryId,
            ElectionAnomalyCaseStateIds.Submitted,
            SeverityCandidateId: null,
            GovernedDecisionRef: null,
            HasOpenClarificationRequest: false,
            OpenClarificationRequestId: null,
            DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow.AddMinutes(-5),
            Guid.NewGuid(),
            SourceBlockHeight: null,
            SourceBlockId: null,
            "sha256:thread");
    }

    private static ElectionRecord CreateElection() =>
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
                new ElectionOptionDefinition("option-a", "Alice", "First option", 1, false),
                new ElectionOptionDefinition("option-b", "Bob", "Second option", 2, false),
            ],
            acknowledgedWarningCodes: []) with
        {
            AnomalySubmissionWindowClosesAt = DateTime.UtcNow.AddHours(1),
        };
}
