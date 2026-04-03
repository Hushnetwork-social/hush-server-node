using FluentAssertions;
using HushNode.Caching;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Olimpo.EntityFramework.Persistency;
using System.Security.Cryptography;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionBallotPublicationServiceTests
{
    [Fact]
    public async Task ProcessPendingPublicationAsync_OpenElectionBelowHighWater_LeavesBallotsQueued()
    {
        var store = new PublicationStore();
        var election = CreateOpenElection();
        SeedAcceptedBallots(store, election, 1);
        var crypto = new FakePublicationCryptoService();
        var service = CreateService(store, crypto, new ElectionBallotPublicationOptions(HighWaterMark: 2, LowWaterMark: 1, MaxBatchPerBlock: 5));

        await service.ProcessPendingPublicationAsync(new BlockIndex(12));

        store.BallotMemPoolEntries.Should().HaveCount(1);
        store.PublishedBallots.Should().BeEmpty();
        store.BoundaryArtifacts.Should().BeEmpty();
        store.PublicationIssues.Should().BeEmpty();
        store.Elections[election.ElectionId].TallyReadyAt.Should().BeNull();
        crypto.PrepareCallCount.Should().Be(0);
        crypto.ReplayCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ProcessPendingPublicationAsync_OpenElectionAboveHighWater_PublishesDownToLowWater()
    {
        var store = new PublicationStore();
        var election = CreateOpenElection();
        SeedAcceptedBallots(store, election, 5);
        var crypto = new FakePublicationCryptoService();
        var service = CreateService(store, crypto, new ElectionBallotPublicationOptions(HighWaterMark: 4, LowWaterMark: 2, MaxBatchPerBlock: 10));

        await service.ProcessPendingPublicationAsync(new BlockIndex(12));

        store.BallotMemPoolEntries.Should().HaveCount(2);
        store.PublishedBallots.Should().HaveCount(3);
        store.PublishedBallots.Select(x => x.PublicationSequence).Should().Equal(1, 2, 3);
        store.PublishedBallots.Should().OnlyContain(x => x.EncryptedBallotPackage.EndsWith("|published", StringComparison.Ordinal));
        store.PublishedBallots.Should().OnlyContain(x => x.ProofBundle.EndsWith("|proof-published", StringComparison.Ordinal));
        store.BoundaryArtifacts.Should().BeEmpty();
        store.PublicationIssues.Should().BeEmpty();
        store.Elections[election.ElectionId].TallyReadyAt.Should().BeNull();
        crypto.ReplayCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ProcessPendingPublicationAsync_ClosedElectionDrainsPoolAndMarksTallyReady()
    {
        var store = new PublicationStore();
        var election = CreateClosedElection();
        SeedAcceptedBallots(store, election, 3);
        var expectedTallyHash = new byte[] { 9, 8, 7, 6 };
        var crypto = new FakePublicationCryptoService
        {
            ReplayBehavior = packages => ElectionBallotReplayResult.Success(expectedTallyHash),
        };
        var service = CreateService(store, crypto, new ElectionBallotPublicationOptions(HighWaterMark: 4, LowWaterMark: 2, MaxBatchPerBlock: 10));

        await service.ProcessPendingPublicationAsync(new BlockIndex(25));

        store.BallotMemPoolEntries.Should().BeEmpty();
        store.PublishedBallots.Should().HaveCount(3);
        store.PublishedBallots.Select(x => x.PublicationSequence).Should().Equal(1, 2, 3);
        store.PublishedBallots.Should().OnlyContain(x => x.EncryptedBallotPackage.EndsWith("|published", StringComparison.Ordinal));
        store.PublicationIssues.Should().BeEmpty();

        store.BoundaryArtifacts.Should().ContainSingle();
        var artifact = store.BoundaryArtifacts[0];
        artifact.ArtifactType.Should().Be(ElectionBoundaryArtifactType.TallyReady);
        artifact.AcceptedBallotCount.Should().Be(3);
        artifact.PublishedBallotCount.Should().Be(3);
        artifact.AcceptedBallotSetHash.Should().NotBeNull().And.NotBeEmpty();
        artifact.PublishedBallotStreamHash.Should().NotBeNull().And.NotBeEmpty();
        artifact.FinalEncryptedTallyHash.Should().Equal(expectedTallyHash);
        artifact.SourceBlockHeight.Should().Be(25);
        artifact.SourceBlockId.Should().Be(store.CurrentBlockId.Value);

        var updatedElection = store.Elections[election.ElectionId];
        updatedElection.TallyReadyAt.Should().NotBeNull();
        updatedElection.TallyReadyArtifactId.Should().Be(artifact.Id);

        crypto.ReplayCallCount.Should().Be(1);
        crypto.LastReplayPackages.Should().NotBeNull();
        crypto.LastReplayPackages.Should().HaveCount(3);
        crypto.LastReplayPackages.Should().OnlyContain(x => x.EndsWith("|published", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessPendingPublicationAsync_ClosedLegacyAdminOnlyElection_ProjectsSyntheticProtectedTallyBinding()
    {
        var store = new PublicationStore();
        var election = CreateClosedElection();
        var openArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            election with
            {
                LifecycleState = ElectionLifecycleState.Open,
                CloseArtifactId = null,
            },
            recordedByPublicAddress: "owner-address",
            recordedAt: election.OpenedAt ?? DateTime.UtcNow.AddMinutes(-15),
            frozenEligibleVoterSetHash: [1, 2, 3, 4]) with
        {
            Id = election.OpenArtifactId!.Value,
        };
        store.BoundaryArtifacts.Add(openArtifact);
        SeedAcceptedBallots(store, election, 2);
        var expectedTallyHash = new byte[] { 4, 3, 2, 1 };
        var expectedBinding = ElectionProtectedTallyBinding.BuildAdminOnlyProtectedTallyBindingSnapshot(election);
        var crypto = new FakePublicationCryptoService
        {
            ReplayBehavior = _ => ElectionBallotReplayResult.Success(expectedTallyHash),
        };
        var service = CreateService(store, crypto, new ElectionBallotPublicationOptions(HighWaterMark: 4, LowWaterMark: 2, MaxBatchPerBlock: 10));

        await service.ProcessPendingPublicationAsync(new BlockIndex(26));

        store.BoundaryArtifacts.Should().Contain(x => x.ArtifactType == ElectionBoundaryArtifactType.TallyReady);
        var tallyReadyArtifact = store.BoundaryArtifacts.Single(x => x.ArtifactType == ElectionBoundaryArtifactType.TallyReady);
        tallyReadyArtifact.CeremonySnapshot.Should().NotBeNull();
        tallyReadyArtifact.CeremonySnapshot!.ProfileId.Should().Be(expectedBinding.ProfileId);
        tallyReadyArtifact.CeremonySnapshot.CeremonyVersionId.Should().Be(expectedBinding.CeremonyVersionId);
        tallyReadyArtifact.CeremonySnapshot.TallyPublicKeyFingerprint.Should().Be(expectedBinding.TallyPublicKeyFingerprint);
    }

    [Fact]
    public async Task ProcessPendingPublicationAsync_ClosedElectionWithoutQueuedBallots_MarksTallyReadyAndPublishesUnofficialResult()
    {
        var store = new PublicationStore();
        var election = CreateClosedElection();
        SeedZeroBallotResultContext(store, election, activeDenominatorCount: 3);
        store.Elections[election.ElectionId] = election;
        store.ClosedAwaitingTallyReadyElectionIds.Add(election.ElectionId);
        var crypto = new FakePublicationCryptoService();
        var resultCrypto = new FakeElectionResultCryptoService();
        var service = CreateService(
            store,
            crypto,
            new ElectionBallotPublicationOptions(HighWaterMark: 4, LowWaterMark: 2, MaxBatchPerBlock: 10),
            resultCrypto);

        await service.ProcessPendingPublicationAsync(new BlockIndex(26));

        store.BallotMemPoolEntries.Should().BeEmpty();
        store.PublishedBallots.Should().BeEmpty();
        store.PublicationIssues.Should().BeEmpty();
        store.BoundaryArtifacts.Should().ContainSingle();
        store.BoundaryArtifacts[0].ArtifactType.Should().Be(ElectionBoundaryArtifactType.TallyReady);
        store.BoundaryArtifacts[0].AcceptedBallotCount.Should().Be(0);
        store.BoundaryArtifacts[0].PublishedBallotCount.Should().Be(0);
        store.BoundaryArtifacts[0].FinalEncryptedTallyHash.Should().NotBeNull().And.NotBeEmpty();
        store.Elections[election.ElectionId].TallyReadyAt.Should().NotBeNull();
        store.Elections[election.ElectionId].UnofficialResultArtifactId.Should().NotBeNull();
        store.ResultArtifacts.Should().ContainSingle();
        store.ResultArtifacts[0].ArtifactKind.Should().Be(ElectionResultArtifactKind.Unofficial);
        store.ResultArtifacts[0].Visibility.Should().Be(ElectionResultArtifactVisibility.ParticipantEncrypted);
        store.ResultArtifacts[0].TallyReadyArtifactId.Should().Be(store.BoundaryArtifacts[0].Id);
        store.ResultArtifacts[0].EligibleToVoteCount.Should().Be(3);
        store.ResultArtifacts[0].DidNotVoteCount.Should().Be(3);
        store.ResultArtifacts[0].TotalVotedCount.Should().Be(0);
        store.ResultArtifacts[0].EncryptedPayload.Should().StartWith("enc::");
        resultCrypto.EncryptCallCount.Should().Be(1);
        crypto.ReplayCallCount.Should().Be(1);
        crypto.LastReplayPackages.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task ProcessPendingPublicationAsync_ClosedTrusteeElectionDrainsPoolAndCreatesCloseCountingSession()
    {
        var store = new PublicationStore();
        var election = CreateClosedTrusteeElection(store);
        SeedAcceptedBallots(store, election, 3);
        var expectedTallyHash = new byte[] { 9, 8, 7, 6 };
        var crypto = new FakePublicationCryptoService
        {
            ReplayBehavior = packages => ElectionBallotReplayResult.Success(expectedTallyHash),
        };
        var service = CreateService(store, crypto, new ElectionBallotPublicationOptions(HighWaterMark: 4, LowWaterMark: 2, MaxBatchPerBlock: 10));

        await service.ProcessPendingPublicationAsync(new BlockIndex(27));

        store.BallotMemPoolEntries.Should().BeEmpty();
        store.PublishedBallots.Should().HaveCount(3);
        store.FinalizationSessions.Should().ContainSingle();
        store.FinalizationSessions[0].SessionPurpose.Should().Be(ElectionFinalizationSessionPurpose.CloseCounting);
        store.FinalizationSessions[0].RequiredShareCount.Should().Be(1);
        store.FinalizationSessions[0].FinalEncryptedTallyHash.Should().Equal(expectedTallyHash);
        store.BoundaryArtifacts.Should().HaveCount(2);
        store.BoundaryArtifacts.Should().OnlyContain(x =>
            x.ArtifactType == ElectionBoundaryArtifactType.Open ||
            x.ArtifactType == ElectionBoundaryArtifactType.Close);
        store.Elections[election.ElectionId].TallyReadyAt.Should().BeNull();
        store.Elections[election.ElectionId].ClosedProgressStatus.Should().Be(ElectionClosedProgressStatus.WaitingForTrusteeShares);
    }

    [Fact]
    public async Task ProcessPendingPublicationAsync_WhenRerandomizationFailsTwice_PublishesOriginalBallotAndRecordsIssue()
    {
        var store = new PublicationStore();
        var election = CreateOpenElection();
        var acceptedBallot = SeedAcceptedBallots(store, election, 1).Single();
        var crypto = new FakePublicationCryptoService
        {
            PrepareBehavior = (ballot, proof, _) =>
                ballot == acceptedBallot.EncryptedBallotPackage
                    ? ElectionBallotPublicationPreparationResult.Failure("RERAND_FAILED", "rerandomization failed")
                    : ElectionBallotPublicationPreparationResult.Success($"{ballot}|published", $"{proof}|proof-published"),
        };
        var service = CreateService(store, crypto, new ElectionBallotPublicationOptions(HighWaterMark: 1, LowWaterMark: 0, MaxBatchPerBlock: 1));

        await service.ProcessPendingPublicationAsync(new BlockIndex(31));

        store.BallotMemPoolEntries.Should().BeEmpty();
        store.PublishedBallots.Should().ContainSingle();
        store.PublishedBallots[0].EncryptedBallotPackage.Should().Be(acceptedBallot.EncryptedBallotPackage);
        store.PublishedBallots[0].ProofBundle.Should().Be(acceptedBallot.ProofBundle);
        store.PublicationIssues.Should().ContainSingle();
        store.PublicationIssues[0].IssueCode.Should().Be(ElectionPublicationIssueCode.RerandomizationFallback);
        crypto.GetPrepareAttempts(acceptedBallot.EncryptedBallotPackage).Should().Be(2);
        crypto.ReplayCallCount.Should().Be(0);
    }

    private static ElectionBallotPublicationService CreateService(
        PublicationStore store,
        FakePublicationCryptoService crypto,
        ElectionBallotPublicationOptions options,
        FakeElectionResultCryptoService? resultCrypto = null)
    {
        var repository = CreateRepository(store);
        var provider = new FakeUnitOfWorkProvider(repository.Object);

        return new ElectionBallotPublicationService(
            provider,
            crypto,
            new FakeBlockchainCache(store.CurrentBlockId),
            options,
            NullLogger<ElectionBallotPublicationService>.Instance,
            resultCrypto);
    }

    private static Mock<IElectionsRepository> CreateRepository(PublicationStore store)
    {
        var repository = new Mock<IElectionsRepository>(MockBehavior.Strict);

        repository
            .Setup(x => x.GetElectionIdsWithBallotMemPoolEntriesAsync())
            .ReturnsAsync(() =>
                (IReadOnlyList<ElectionId>)store.BallotMemPoolEntries
                    .Select(x => x.ElectionId)
                    .Distinct()
                    .ToArray());

        repository
            .Setup(x => x.GetClosedElectionIdsAwaitingTallyReadyAsync())
            .ReturnsAsync(() =>
                (IReadOnlyList<ElectionId>)store.ClosedAwaitingTallyReadyElectionIds
                    .Distinct()
                    .ToArray());

        repository
            .Setup(x => x.GetElectionForUpdateAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync((ElectionId electionId) => store.Elections.GetValueOrDefault(electionId));

        repository
            .Setup(x => x.GetBallotMemPoolEntriesAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync((ElectionId electionId) =>
                (IReadOnlyList<ElectionBallotMemPoolRecord>)store.BallotMemPoolEntries
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.QueuedAt)
                    .ThenBy(x => x.Id)
                    .ToArray());

        repository
            .Setup(x => x.GetAcceptedBallotAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid acceptedBallotId) =>
                store.AcceptedBallots.FirstOrDefault(x => x.Id == acceptedBallotId));

        repository
            .Setup(x => x.SavePublishedBallotAsync(It.IsAny<ElectionPublishedBallotRecord>()))
            .Returns((ElectionPublishedBallotRecord publishedBallot) =>
            {
                store.PublishedBallots.Add(publishedBallot);
                return Task.CompletedTask;
            });

        repository
            .Setup(x => x.DeleteBallotMemPoolEntryAsync(It.IsAny<Guid>()))
            .Returns((Guid ballotMemPoolEntryId) =>
            {
                store.BallotMemPoolEntries.RemoveAll(x => x.Id == ballotMemPoolEntryId);
                return Task.CompletedTask;
            });

        repository
            .Setup(x => x.SaveElectionAsync(It.IsAny<ElectionRecord>()))
            .Returns((ElectionRecord election) =>
            {
                store.Elections[election.ElectionId] = election;
                return Task.CompletedTask;
            });

        repository
            .Setup(x => x.GetAcceptedBallotsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync((ElectionId electionId) =>
                (IReadOnlyList<ElectionAcceptedBallotRecord>)store.AcceptedBallots
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.AcceptedAt)
                    .ThenBy(x => x.Id)
                    .ToArray());

        repository
            .Setup(x => x.GetPublishedBallotsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync((ElectionId electionId) =>
                (IReadOnlyList<ElectionPublishedBallotRecord>)store.PublishedBallots
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.PublicationSequence)
                    .ToArray());

        repository
            .Setup(x => x.GetBoundaryArtifactsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync((ElectionId electionId) =>
                (IReadOnlyList<ElectionBoundaryArtifactRecord>)store.BoundaryArtifacts
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.RecordedAt)
                    .ToArray());

        repository
            .Setup(x => x.GetEligibilitySnapshotAsync(It.IsAny<ElectionId>(), It.IsAny<ElectionEligibilitySnapshotType>()))
            .ReturnsAsync((ElectionId electionId, ElectionEligibilitySnapshotType snapshotType) =>
                store.EligibilitySnapshots.FirstOrDefault(x => x.ElectionId == electionId && x.SnapshotType == snapshotType));

        repository
            .Setup(x => x.GetElectionEnvelopeAccessAsync(It.IsAny<ElectionId>(), It.IsAny<string>()))
            .ReturnsAsync((ElectionId electionId, string actorPublicAddress) =>
                store.EnvelopeAccessRecords.FirstOrDefault(x =>
                    x.ElectionId == electionId &&
                    string.Equals(x.ActorPublicAddress, actorPublicAddress, StringComparison.Ordinal)));

        repository
            .Setup(x => x.GetActiveFinalizationSessionAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync((ElectionId electionId) =>
                store.FinalizationSessions
                    .Where(x => x.ElectionId == electionId && x.Status != ElectionFinalizationSessionStatus.Completed)
                    .OrderBy(x => x.CreatedAt)
                    .FirstOrDefault());

        repository
            .Setup(x => x.SaveFinalizationSessionAsync(It.IsAny<ElectionFinalizationSessionRecord>()))
            .Returns((ElectionFinalizationSessionRecord session) =>
            {
                store.FinalizationSessions.Add(session);
                return Task.CompletedTask;
            });

        repository
            .Setup(x => x.GetNextPublishedBallotSequenceAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync((ElectionId electionId) =>
                store.PublishedBallots
                    .Where(x => x.ElectionId == electionId)
                    .Select(x => x.PublicationSequence)
                    .DefaultIfEmpty(0)
                    .Max() + 1);

        repository
            .Setup(x => x.GetPublicationIssueAsync(It.IsAny<ElectionId>(), It.IsAny<ElectionPublicationIssueCode>()))
            .ReturnsAsync((ElectionId electionId, ElectionPublicationIssueCode issueCode) =>
                store.PublicationIssues.FirstOrDefault(x => x.ElectionId == electionId && x.IssueCode == issueCode));

        repository
            .Setup(x => x.SavePublicationIssueAsync(It.IsAny<ElectionPublicationIssueRecord>()))
            .Returns((ElectionPublicationIssueRecord issue) =>
            {
                store.PublicationIssues.Add(issue);
                return Task.CompletedTask;
            });

        repository
            .Setup(x => x.UpdatePublicationIssueAsync(It.IsAny<ElectionPublicationIssueRecord>()))
            .Returns((ElectionPublicationIssueRecord issue) =>
            {
                var index = store.PublicationIssues.FindIndex(x => x.ElectionId == issue.ElectionId && x.IssueCode == issue.IssueCode);
                if (index >= 0)
                {
                    store.PublicationIssues[index] = issue;
                }
                else
                {
                    store.PublicationIssues.Add(issue);
                }

                return Task.CompletedTask;
            });

        repository
            .Setup(x => x.SaveBoundaryArtifactAsync(It.IsAny<ElectionBoundaryArtifactRecord>()))
            .Returns((ElectionBoundaryArtifactRecord artifact) =>
            {
                store.BoundaryArtifacts.Add(artifact);
                return Task.CompletedTask;
            });

        repository
            .Setup(x => x.SaveResultArtifactAsync(It.IsAny<ElectionResultArtifactRecord>()))
            .Returns((ElectionResultArtifactRecord resultArtifact) =>
            {
                store.ResultArtifacts.Add(resultArtifact);
                return Task.CompletedTask;
            });

        return repository;
    }

    private static ElectionRecord CreateOpenElection()
    {
        var draft = CreateDraftElection();
        var openedAt = DateTime.UtcNow.AddMinutes(-15);
        return draft with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = openedAt,
            LastUpdatedAt = openedAt,
        };
    }

    private static ElectionRecord CreateClosedElection()
    {
        var openedAt = DateTime.UtcNow.AddMinutes(-15);
        var closedAt = DateTime.UtcNow.AddMinutes(-5);
        var draft = CreateDraftElection();
        return draft with
        {
            LifecycleState = ElectionLifecycleState.Closed,
            OpenedAt = openedAt,
            VoteAcceptanceLockedAt = closedAt,
            ClosedAt = closedAt,
            LastUpdatedAt = closedAt,
            OpenArtifactId = Guid.NewGuid(),
            CloseArtifactId = Guid.NewGuid(),
        };
    }

    private static ElectionRecord CreateClosedTrusteeElection(PublicationStore store)
    {
        var openedAt = DateTime.UtcNow.AddMinutes(-15);
        var closedAt = DateTime.UtcNow.AddMinutes(-5);
        var draft = CreateDraftElection() with
        {
            GovernanceMode = ElectionGovernanceMode.TrusteeThreshold,
            ReviewWindowPolicy = ReviewWindowPolicy.GovernedReviewWindowReserved,
            RequiredApprovalCount = 1,
        };
        var openElection = draft with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = openedAt,
            LastUpdatedAt = openedAt,
        };
        var ceremonySnapshot = ElectionModelFactory.CreateCeremonyBindingSnapshot(
            Guid.NewGuid(),
            ceremonyVersionNumber: 1,
            profileId: "prod-1of1-v1",
            boundTrusteeCount: 1,
            requiredApprovalCount: 1,
            activeTrustees:
            [
                new ElectionTrusteeReference("trustee-a", "Alice"),
            ],
            tallyPublicKeyFingerprint: "tally-fingerprint");
        var openArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            openElection,
            recordedByPublicAddress: "owner-address",
            recordedAt: openedAt,
            ceremonySnapshot: ceremonySnapshot);
        var closedElection = draft with
        {
            LifecycleState = ElectionLifecycleState.Closed,
            OpenedAt = openedAt,
            VoteAcceptanceLockedAt = closedAt,
            ClosedAt = closedAt,
            LastUpdatedAt = closedAt,
            OpenArtifactId = openArtifact.Id,
        };
        var closeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Close,
            closedElection,
            recordedByPublicAddress: "owner-address",
            recordedAt: closedAt);
        closedElection = closedElection with
        {
            CloseArtifactId = closeArtifact.Id,
        };
        store.BoundaryArtifacts.Add(openArtifact);
        store.BoundaryArtifacts.Add(closeArtifact);
        return closedElection;
    }

    private static ElectionRecord CreateDraftElection() =>
        ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: "Referendum",
            shortDescription: "Policy vote",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "REF-2026-100",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            governanceMode: ElectionGovernanceMode.AdminOnly,
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
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", null, 1, IsBlankOption: false),
                new ElectionOptionDefinition("no", "No", null, 2, IsBlankOption: false),
            ],
            acknowledgedWarningCodes:
            [
                ElectionWarningCode.LowAnonymitySet,
            ]);

    private static IReadOnlyList<ElectionAcceptedBallotRecord> SeedAcceptedBallots(
        PublicationStore store,
        ElectionRecord election,
        int count)
    {
        store.Elections[election.ElectionId] = election;
        var acceptedBallots = new List<ElectionAcceptedBallotRecord>(count);

        for (var index = 0; index < count; index++)
        {
            var acceptedBallot = new ElectionAcceptedBallotRecord(
                Guid.NewGuid(),
                election.ElectionId,
                EncryptedBallotPackage: $"ballot-{index + 1}",
                ProofBundle: $"proof-{index + 1}",
                BallotNullifier: $"nullifier-{index + 1}",
                AcceptedAt: DateTime.UtcNow.AddSeconds(index));
            acceptedBallots.Add(acceptedBallot);
            store.AcceptedBallots.Add(acceptedBallot);
            store.BallotMemPoolEntries.Add(ElectionModelFactory.CreateBallotMemPoolEntry(
                election.ElectionId,
                acceptedBallot.Id,
                queuedAt: acceptedBallot.AcceptedAt.AddMilliseconds(50)));
        }

        return acceptedBallots;
    }

    private static ElectionEligibilitySnapshotRecord SeedZeroBallotResultContext(
        PublicationStore store,
        ElectionRecord election,
        int activeDenominatorCount)
    {
        var snapshot = ElectionModelFactory.CreateEligibilitySnapshot(
            election.ElectionId,
            ElectionEligibilitySnapshotType.Close,
            election.EligibilityMutationPolicy,
            rosteredCount: activeDenominatorCount,
            linkedCount: activeDenominatorCount,
            activeDenominatorCount: activeDenominatorCount,
            countedParticipationCount: 0,
            blankCount: 0,
            didNotVoteCount: activeDenominatorCount,
            rosteredVoterSetHash: [1, 2, 3],
            activeDenominatorSetHash: [4, 5, 6],
            countedParticipationSetHash: [7, 8, 9],
            recordedByPublicAddress: election.OwnerPublicAddress,
            boundaryArtifactId: election.CloseArtifactId);
        store.EligibilitySnapshots.Add(snapshot);
        store.EnvelopeAccessRecords.Add(new ElectionEnvelopeAccessRecord(
            election.ElectionId,
            election.OwnerPublicAddress,
            NodeEncryptedElectionPrivateKey: "node-enc-election-private-key",
            ActorEncryptedElectionPrivateKey: "actor-enc-election-private-key",
            GrantedAt: DateTime.UtcNow,
            SourceTransactionId: null,
            SourceBlockHeight: null,
            SourceBlockId: null));
        return snapshot;
    }

    private sealed class PublicationStore
    {
        public Dictionary<ElectionId, ElectionRecord> Elections { get; } = [];
        public List<ElectionAcceptedBallotRecord> AcceptedBallots { get; } = [];
        public List<ElectionBallotMemPoolRecord> BallotMemPoolEntries { get; } = [];
        public List<ElectionPublishedBallotRecord> PublishedBallots { get; } = [];
        public List<ElectionEnvelopeAccessRecord> EnvelopeAccessRecords { get; } = [];
        public List<ElectionEligibilitySnapshotRecord> EligibilitySnapshots { get; } = [];
        public List<ElectionResultArtifactRecord> ResultArtifacts { get; } = [];
        public List<ElectionFinalizationSessionRecord> FinalizationSessions { get; } = [];
        public List<ElectionPublicationIssueRecord> PublicationIssues { get; } = [];
        public List<ElectionBoundaryArtifactRecord> BoundaryArtifacts { get; } = [];
        public List<ElectionId> ClosedAwaitingTallyReadyElectionIds { get; } = [];
        public BlockId CurrentBlockId { get; } = BlockId.NewBlockId;
    }

    private sealed class FakeUnitOfWorkProvider(IElectionsRepository repository) : IUnitOfWorkProvider<ElectionsDbContext>
    {
        public IReadOnlyUnitOfWork<ElectionsDbContext> CreateReadOnly() =>
            new FakeReadOnlyUnitOfWork(repository);

        public IWritableUnitOfWork<ElectionsDbContext> CreateWritable() =>
            new FakeWritableUnitOfWork(repository);

        public IWritableUnitOfWork<ElectionsDbContext> CreateWritable(System.Data.IsolationLevel isolationLevel) =>
            new FakeWritableUnitOfWork(repository);
    }

    private sealed class FakeReadOnlyUnitOfWork(IElectionsRepository repository) : IReadOnlyUnitOfWork<ElectionsDbContext>
    {
        public ElectionsDbContext Context => null!;

        public TRepository GetRepository<TRepository>()
            where TRepository : IRepository =>
            typeof(TRepository) == typeof(IElectionsRepository)
                ? (TRepository)repository
                : throw new InvalidOperationException($"Repository {typeof(TRepository).Name} is not supported by this test harness.");

        public void Dispose()
        {
        }
    }

    private sealed class FakeWritableUnitOfWork(IElectionsRepository repository) : IWritableUnitOfWork<ElectionsDbContext>
    {
        public ElectionsDbContext Context => null!;

        public Task CommitAsync() => Task.CompletedTask;

        public TRepository GetRepository<TRepository>()
            where TRepository : IRepository =>
            typeof(TRepository) == typeof(IElectionsRepository)
                ? (TRepository)repository
                : throw new InvalidOperationException($"Repository {typeof(TRepository).Name} is not supported by this test harness.");

        public Task RollbackAsync() => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class FakeBlockchainCache(BlockId currentBlockId) : IBlockchainCache
    {
        public BlockId PreviousBlockId => BlockId.Empty;

        public BlockId CurrentBlockId { get; } = currentBlockId;

        public BlockId NextBlockId => BlockId.Empty;

        public BlockIndex LastBlockIndex => BlockIndex.Empty;

        public bool BlockchainStateInDatabase => true;

        public IBlockchainCache SetBlockIndex(BlockIndex index) => this;

        public IBlockchainCache SetPreviousBlockId(BlockId id) => this;

        public IBlockchainCache SetCurrentBlockId(BlockId id) => this;

        public IBlockchainCache SetNextBlockId(BlockId id) => this;

        public IBlockchainCache IsBlockchainStateInDatabase() => this;
    }

    private sealed class FakePublicationCryptoService : IElectionBallotPublicationCryptoService
    {
        private readonly Dictionary<string, int> _prepareAttempts = [];

        public int PrepareCallCount { get; private set; }

        public int ReplayCallCount { get; private set; }

        public IReadOnlyList<string>? LastReplayPackages { get; private set; }

        public Func<string, string, int, ElectionBallotPublicationPreparationResult>? PrepareBehavior { get; init; }

        public Func<IReadOnlyList<string>, ElectionBallotReplayResult>? ReplayBehavior { get; init; }

        public ElectionBallotPublicationPreparationResult PrepareForPublication(
            string encryptedBallotPackage,
            string proofBundle)
        {
            PrepareCallCount++;
            var attempts = _prepareAttempts.TryGetValue(encryptedBallotPackage, out var currentAttempts)
                ? currentAttempts + 1
                : 1;
            _prepareAttempts[encryptedBallotPackage] = attempts;

            if (PrepareBehavior is not null)
            {
                return PrepareBehavior(encryptedBallotPackage, proofBundle, attempts);
            }

            return ElectionBallotPublicationPreparationResult.Success(
                $"{encryptedBallotPackage}|published",
                $"{proofBundle}|proof-published");
        }

        public ElectionBallotReplayResult ReplayPublishedBallots(IReadOnlyList<string> encryptedBallotPackages)
        {
            ReplayCallCount++;
            LastReplayPackages = encryptedBallotPackages.ToArray();

            if (ReplayBehavior is not null)
            {
                return ReplayBehavior(encryptedBallotPackages);
            }

            return ElectionBallotReplayResult.Success([1, 2, 3, 4]);
        }

        public int GetPrepareAttempts(string encryptedBallotPackage) =>
            _prepareAttempts.TryGetValue(encryptedBallotPackage, out var attempts)
                ? attempts
                : 0;
    }

    private sealed class FakeElectionResultCryptoService : IElectionResultCryptoService
    {
        public int EncryptCallCount { get; private set; }

        public ElectionAggregateReleaseResult TryReleaseAggregateTally(
            IReadOnlyList<string> encryptedBallotPackages,
            IReadOnlyList<ElectionFinalizationShareRecord> acceptedShares,
            int maxSupportedCount) =>
            ElectionAggregateReleaseResult.Success(SHA256.HashData(Array.Empty<byte>()), Array.Empty<int>());

        public string EncryptForElectionParticipants(string plaintextPayload, string nodeEncryptedElectionPrivateKey)
        {
            EncryptCallCount++;
            return $"enc::{plaintextPayload}";
        }
    }
}
