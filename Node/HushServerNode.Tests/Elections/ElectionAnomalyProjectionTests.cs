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
    public async Task GetElectionAnomalyOwnThreadAsync_ReturnsCallerUsableWrapMaterialOnly()
    {
        var election = CreateElection();
        var ownThread = CreateThread(election, "owner-address");
        var ownMessage = CreateMessage(ownThread);
        var repository = CreateRepository(election, [ownThread]);
        SetupThreadMessages(repository, ownThread, ownMessage, ownerRecipientAddress: "authority-address");
        var sut = CreateService(repository.Object);

        var projection = await sut.GetElectionAnomalyOwnThreadAsync(election.ElectionId, "owner-address");

        projection.Should().NotBeNull();
        var submitterWrap = projection!.Messages[0].RecipientWraps
            .Single(x => x.RecipientRoleId == ElectionAnomalyRecipientRoleIds.Submitter);
        submitterWrap.EncryptedContentKey.Should().Be("submitter-encrypted-content-key");
        submitterWrap.WrapAlgorithm.Should().Be("x25519-aes-gcm");

        var authorityWrap = projection.Messages[0].RecipientWraps
            .Single(x => x.RecipientRoleId == ElectionAnomalyRecipientRoleIds.ElectionOwner);
        authorityWrap.RecipientPublicAddress.Should().Be("authority-address");
        authorityWrap.EncryptedContentKey.Should().BeNull();
        authorityWrap.WrapAlgorithm.Should().BeNull();
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

        var projection = await sut.GetElectionAnomalyOwnerTriageAsync(election.ElectionId, "owner-address");

        projection.Should().NotBeNull();
        projection!.TotalThreadCount.Should().Be(2);
        projection.Threads.Select(x => x.SubmitterActorPublicAddress)
            .Should()
            .Contain(["owner-address", "other-address"]);
        projection.Threads.Should().OnlyContain(x => x.Messages.Count == 1);
        projection.Threads.Should().OnlyContain(x => x.Messages[0].CallerOwnerWrap != null);
        projection.Threads.SelectMany(x => x.Messages)
            .Should()
            .OnlyContain(x => !string.IsNullOrWhiteSpace(x.CallerOwnerWrap!.EncryptedContentKey));
        projection.Threads[0].GetType().GetProperties()
            .Should()
            .NotContain(property => property.Name.Contains("PersonScope", StringComparison.OrdinalIgnoreCase));
        projection.Threads[0].Messages[0].RecipientWraps[0].GetType().GetProperties()
            .Should()
            .NotContain(property => property.Name.Contains("PublicAddress", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetElectionAnomalyOwnerTriageAsync_WithNonOwner_ReturnsNull()
    {
        var election = CreateElection();
        var thread = CreateThread(election, "owner-address");
        var repository = CreateRepository(election, [thread]);
        SetupThreadMessages(repository, thread, CreateMessage(thread));
        var sut = CreateService(repository.Object);

        var projection = await sut.GetElectionAnomalyOwnerTriageAsync(election.ElectionId, "other-address");

        projection.Should().BeNull();
    }

    [Fact]
    public async Task GetElectionAnomalyOwnerTriageAsync_ReturnsCallerOwnerWrapOnly()
    {
        var election = CreateElection();
        var thread = CreateThread(election, "other-address", submitterPersonScopeId: "sha256:other");
        var repository = CreateRepository(election, [thread]);
        SetupThreadMessages(
            repository,
            thread,
            CreateMessage(thread),
            ownerRecipientAddress: "owner-address",
            auditorRecipientAddress: "auditor-address");
        var sut = CreateService(repository.Object);

        var projection = await sut.GetElectionAnomalyOwnerTriageAsync(election.ElectionId, "owner-address");

        projection.Should().NotBeNull();
        projection!.DecryptableMessageCount.Should().Be(1);
        var message = projection.Threads[0].Messages[0];
        message.CallerOwnerWrap.Should().NotBeNull();
        message.CallerOwnerWrap!.EncryptedContentKey.Should().Be("encrypted-content-key");
        message.CallerOwnerWrap.WrapAlgorithm.Should().Be("x25519-aes-gcm");
        message.RecipientWraps.Should().Contain(x =>
            x.RecipientRoleId == ElectionAnomalyRecipientRoleIds.DesignatedAuditor &&
            x.WrapStatusId == ElectionAnomalyRecipientWrapStatusIds.Available);
        message.RecipientWraps[0].GetType().GetProperties()
            .Should()
            .NotContain(property =>
                property.Name.Contains("PublicAddress", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("EncryptedContentKey", StringComparison.OrdinalIgnoreCase));
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
        projection.ContinuitySummary.TrusteeContinuityThreadCount.Should().Be(1);
        projection.ContinuitySummary.OpenContinuityThreadCount.Should().Be(1);
        projection.ContinuitySummary.HasContinuityIssue.Should().BeTrue();
        projection.CategoryCounts.Select(x => x.CategoryId)
            .Should()
            .Contain([ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern, ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly]);
        projection.GetType().GetProperties()
            .Should()
            .NotContain(property => property.Name.Contains("Message", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetElectionAnomalyTrusteeCountsAsync_WithNonTrustee_ReturnsNull()
    {
        var election = CreateElection();
        var thread = CreateThread(election, "owner-address");
        var repository = CreateRepository(election, [thread]);
        var sut = CreateService(repository.Object);

        var projection = await sut.GetElectionAnomalyTrusteeCountsAsync(election.ElectionId, "voter-address");

        projection.Should().BeNull();
    }

    [Fact]
    public async Task GetElectionAnomalyTrusteeCountsAsync_BuildsBodyFreeContinuitySummary()
    {
        var election = CreateElection();
        var trusteeInvitation = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            "trustee-address",
            "Trustee",
            "owner-address",
            election.CurrentDraftRevision)
            .Accept(DateTime.UtcNow, election.CurrentDraftRevision, ElectionLifecycleState.Draft);
        var submittedContinuity = CreateThread(election, "owner-address");
        var awaitingContinuity = CreateThread(
            election,
            "other-address",
            submitterPersonScopeId: "sha256:awaiting",
            caseStateId: ElectionAnomalyCaseStateIds.AuthorityRequestedInformation);
        var closedContinuity = CreateThread(
            election,
            "third-address",
            submitterPersonScopeId: "sha256:closed",
            caseStateId: ElectionAnomalyCaseStateIds.ResolvedNonBlocking,
            governedDecisionRef: "governed-decision-1");
        var securityThread = CreateThread(
            election,
            "security-address",
            submitterPersonScopeId: "sha256:security",
            categoryId: ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern);
        var repository = CreateRepository(election, [submittedContinuity, awaitingContinuity, closedContinuity, securityThread]);
        repository
            .Setup(x => x.GetAcceptedTrusteeInvitationsByActorAsync("trustee-address"))
            .ReturnsAsync([trusteeInvitation]);
        var sut = CreateService(repository.Object);

        var projection = await sut.GetElectionAnomalyTrusteeCountsAsync(election.ElectionId, "trustee-address");

        projection.Should().NotBeNull();
        projection!.ContinuitySummary.Should().BeEquivalentTo(new ElectionAnomalyTrusteeContinuitySummaryProjection(
            TrusteeContinuityThreadCount: 3,
            OpenContinuityThreadCount: 2,
            AwaitingInformationContinuityThreadCount: 1,
            ClosedContinuityThreadCount: 1,
            GovernedDecisionLinkedCount: 1,
            HasContinuityIssue: true));
        projection.GetType().GetProperties()
            .Should()
            .NotContain(property =>
                property.Name.Contains("Submitter", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("PersonScope", StringComparison.OrdinalIgnoreCase));
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
        SetupThreadMessages(
            repository,
            thread,
            CreateMessage(thread),
            auditorRecipientAddress: "auditor-address");
        var sut = CreateService(repository.Object);

        var projection = await sut.GetElectionAnomalyAuditorRestrictedReviewAsync(election.ElectionId, "auditor-address");

        projection.Should().NotBeNull();
        projection!.Threads.Should().ContainSingle();
        projection.Threads[0].Messages[0].EncryptedBody.Should().Be("encrypted-body");
        projection.Threads[0].Messages[0].RecordedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        projection.Threads[0].Messages[0].CallerAuditorWrap.Should().NotBeNull();
        projection.Threads[0].Messages[0].CallerAuditorWrap!.WrapStatusId.Should().Be(ElectionAnomalyRecipientWrapStatusIds.Available);
        projection.Threads[0].Messages[0].CallerAuditorWrap!.EncryptedContentKey.Should().Be("auditor-encrypted-content-key");
        projection.Threads[0].Messages[0].CallerAuditorWrap!.WrapAlgorithm.Should().Be("x25519-aes-gcm");
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
        projection.Threads[0].Messages[0].CallerAuditorWrap!.GetType().GetProperties()
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
        ElectionAnomalyMessageEnvelopeRecord message,
        string ownerRecipientAddress = "owner-address",
        string? auditorRecipientAddress = null,
        string auditorWrapStatusId = ElectionAnomalyRecipientWrapStatusIds.Available)
    {
        var wraps = new List<ElectionAnomalyRecipientWrapRecord>
        {
            new(
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
            new(
                Guid.NewGuid(),
                message.Id,
                thread.Id,
                thread.ElectionId,
                ElectionAnomalyRecipientRoleIds.ElectionOwner,
                ownerRecipientAddress,
                "owner-key-fingerprint",
                "encrypted-content-key",
                "x25519-aes-gcm",
                ElectionAnomalyRecipientWrapStatusIds.Available,
                DateTime.UtcNow),
        };

        if (!string.IsNullOrWhiteSpace(auditorRecipientAddress))
        {
            wraps.Add(new ElectionAnomalyRecipientWrapRecord(
                Guid.NewGuid(),
                message.Id,
                thread.Id,
                thread.ElectionId,
                ElectionAnomalyRecipientRoleIds.DesignatedAuditor,
                auditorRecipientAddress,
                "auditor-key-fingerprint",
                auditorWrapStatusId == ElectionAnomalyRecipientWrapStatusIds.Available
                    ? "auditor-encrypted-content-key"
                    : string.Empty,
                auditorWrapStatusId == ElectionAnomalyRecipientWrapStatusIds.Available
                    ? "x25519-aes-gcm"
                    : string.Empty,
                auditorWrapStatusId,
                DateTime.UtcNow));
        }

        repository.Setup(x => x.GetAnomalyMessageEnvelopesAsync(thread.Id)).ReturnsAsync([message]);
        repository
            .Setup(x => x.GetAnomalyRecipientWrapsAsync(thread.Id))
            .ReturnsAsync(wraps);
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
        string categoryId = ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly,
        string caseStateId = ElectionAnomalyCaseStateIds.Submitted,
        string? governedDecisionRef = null)
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
            caseStateId,
            SeverityCandidateId: null,
            GovernedDecisionRef: governedDecisionRef,
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
