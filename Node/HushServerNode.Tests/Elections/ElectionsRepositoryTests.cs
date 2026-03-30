using FluentAssertions;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionsRepositoryTests
{
    [Fact]
    public async Task SaveElectionAndDraftSnapshot_ShouldRoundTrip()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var election = CreateAdminElection();
        var snapshot = ElectionModelFactory.CreateDraftSnapshot(
            election,
            snapshotReason: "initial draft",
            recordedByPublicAddress: "owner-address");

        await repository.SaveElectionAsync(election);
        await repository.SaveDraftSnapshotAsync(snapshot);
        await context.SaveChangesAsync();

        var retrievedElection = await repository.GetElectionAsync(election.ElectionId);
        var latestSnapshot = await repository.GetLatestDraftSnapshotAsync(election.ElectionId);

        retrievedElection.Should().NotBeNull();
        retrievedElection!.Options.Last().OptionId.Should().Be(ElectionOptionDefinition.ReservedBlankOptionId);
        latestSnapshot.Should().NotBeNull();
        latestSnapshot!.SnapshotReason.Should().Be("initial draft");
        latestSnapshot.Policy.ProtocolOmegaVersion.Should().Be("omega-v1.0.0");
        retrievedElection.OfficialResultVisibilityPolicy.Should().Be(OfficialResultVisibilityPolicy.ParticipantEncryptedOnly);
    }

    [Fact]
    public async Task SaveEnvelopeAccessAndResultArtifacts_ShouldRoundTrip()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var election = CreateAdminElection() with
        {
            LifecycleState = ElectionLifecycleState.Closed,
            ClosedAt = DateTime.UtcNow.AddMinutes(-5),
            TallyReadyAt = DateTime.UtcNow.AddMinutes(-1),
            CloseArtifactId = Guid.NewGuid(),
            TallyReadyArtifactId = Guid.NewGuid(),
            ClosedProgressStatus = ElectionClosedProgressStatus.WaitingForTrusteeShares,
        };
        var accessRecord = new ElectionEnvelopeAccessRecord(
            election.ElectionId,
            "voter-address",
            "node-encrypted-election-key",
            "actor-encrypted-election-key",
            DateTime.UtcNow,
            Guid.NewGuid(),
            42,
            Guid.NewGuid());
        var denominatorEvidence = new ElectionResultDenominatorEvidence(
            ElectionEligibilitySnapshotType.Close,
            Guid.NewGuid(),
            election.CloseArtifactId,
            [9, 8, 7, 6]);
        var unofficialResult = ElectionModelFactory.CreateResultArtifact(
            election.ElectionId,
            ElectionResultArtifactKind.Unofficial,
            ElectionResultArtifactVisibility.ParticipantEncrypted,
            title: election.Title,
            namedOptionResults:
            [
                new ElectionResultOptionCount("alice", "Alice", null, 1, 1, 10),
                new ElectionResultOptionCount("bob", "Bob", null, 2, 2, 7),
            ],
            blankCount: 2,
            totalVotedCount: 19,
            eligibleToVoteCount: 25,
            didNotVoteCount: 6,
            denominatorEvidence: denominatorEvidence,
            recordedByPublicAddress: election.OwnerPublicAddress,
            tallyReadyArtifactId: election.TallyReadyArtifactId,
            encryptedPayload: "encrypted-unofficial-result");
        var officialResult = ElectionModelFactory.CreateResultArtifact(
            election.ElectionId,
            ElectionResultArtifactKind.Official,
            ElectionResultArtifactVisibility.ParticipantEncrypted,
            title: election.Title,
            namedOptionResults: unofficialResult.NamedOptionResults,
            blankCount: unofficialResult.BlankCount,
            totalVotedCount: unofficialResult.TotalVotedCount,
            eligibleToVoteCount: unofficialResult.EligibleToVoteCount,
            didNotVoteCount: unofficialResult.DidNotVoteCount,
            denominatorEvidence: unofficialResult.DenominatorEvidence,
            recordedByPublicAddress: election.OwnerPublicAddress,
            sourceResultArtifactId: unofficialResult.Id,
            encryptedPayload: "encrypted-official-result");

        await repository.SaveElectionAsync(election);
        await repository.SaveElectionEnvelopeAccessAsync(accessRecord);
        await repository.SaveResultArtifactAsync(unofficialResult);
        await repository.SaveResultArtifactAsync(officialResult);
        await context.SaveChangesAsync();

        var storedAccess = await repository.GetElectionEnvelopeAccessAsync(election.ElectionId, "voter-address");
        var artifacts = await repository.GetResultArtifactsAsync(election.ElectionId);
        var storedUnofficial = await repository.GetResultArtifactAsync(
            election.ElectionId,
            ElectionResultArtifactKind.Unofficial);
        var storedOfficial = await repository.GetResultArtifactAsync(officialResult.Id);

        storedAccess.Should().NotBeNull();
        storedAccess!.NodeEncryptedElectionPrivateKey.Should().Be("node-encrypted-election-key");
        storedAccess.ActorEncryptedElectionPrivateKey.Should().Be("actor-encrypted-election-key");

        artifacts.Should().HaveCount(2);
        artifacts.Select(x => x.ArtifactKind).Should().Equal(
            ElectionResultArtifactKind.Unofficial,
            ElectionResultArtifactKind.Official);

        storedUnofficial.Should().NotBeNull();
        storedUnofficial!.Visibility.Should().Be(ElectionResultArtifactVisibility.ParticipantEncrypted);
        storedUnofficial.TallyReadyArtifactId.Should().Be(election.TallyReadyArtifactId);
        storedUnofficial.NamedOptionResults.Should().HaveCount(2);
        storedUnofficial.NamedOptionResults[0].VoteCount.Should().Be(10);

        storedOfficial.Should().NotBeNull();
        storedOfficial!.SourceResultArtifactId.Should().Be(unofficialResult.Id);
        storedOfficial.DidNotVoteCount.Should().Be(6);
        storedOfficial.DenominatorEvidence.ActiveDenominatorSetHash.Should().Equal([9, 8, 7, 6]);
    }

    [Fact]
    public async Task SaveReportPackageArtifactsAndAccessGrant_ShouldRoundTrip()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var election = CreateAdminElection() with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            ClosedAt = DateTime.UtcNow.AddMinutes(-10),
            TallyReadyAt = DateTime.UtcNow.AddMinutes(-7),
            FinalizedAt = DateTime.UtcNow.AddMinutes(-5),
            TallyReadyArtifactId = Guid.NewGuid(),
            UnofficialResultArtifactId = Guid.NewGuid(),
            OfficialResultArtifactId = Guid.NewGuid(),
            FinalizeArtifactId = Guid.NewGuid(),
        };
        var reportPackage = ElectionModelFactory.CreateSealedReportPackage(
            electionId: election.ElectionId,
            attemptNumber: 1,
            tallyReadyArtifactId: election.TallyReadyArtifactId!.Value,
            unofficialResultArtifactId: election.UnofficialResultArtifactId!.Value,
            officialResultArtifactId: election.OfficialResultArtifactId!.Value,
            finalizeArtifactId: election.FinalizeArtifactId!.Value,
            frozenEvidenceHash: [1, 2, 3, 4],
            frozenEvidenceFingerprint: "freeze:abc123",
            packageHash: [5, 6, 7, 8],
            artifactCount: 2,
            attemptedByPublicAddress: "owner-address");
        var humanManifest = ElectionModelFactory.CreateReportArtifact(
            reportPackageId: reportPackage.Id,
            electionId: election.ElectionId,
            artifactKind: ElectionReportArtifactKind.HumanManifest,
            format: ElectionReportArtifactFormat.Markdown,
            accessScope: ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
            sortOrder: 1,
            title: "Final manifest",
            fileName: "final-manifest.md",
            mediaType: "text/markdown",
            contentHash: [9, 9, 9, 9],
            content: "# Final Manifest");
        var machineManifest = ElectionModelFactory.CreateReportArtifact(
            reportPackageId: reportPackage.Id,
            electionId: election.ElectionId,
            artifactKind: ElectionReportArtifactKind.MachineManifest,
            format: ElectionReportArtifactFormat.Json,
            accessScope: ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
            sortOrder: 2,
            title: "Machine manifest",
            fileName: "machine-manifest.json",
            mediaType: "application/json",
            contentHash: [8, 8, 8, 8],
            content: "{\"packageId\":\"pkg-1\"}",
            pairedArtifactId: humanManifest.Id);
        var auditorGrant = ElectionModelFactory.CreateReportAccessGrant(
            election.ElectionId,
            actorPublicAddress: "auditor-address",
            grantedByPublicAddress: "owner-address");

        await repository.SaveElectionAsync(election);
        await repository.SaveReportPackageAsync(reportPackage);
        await repository.SaveReportArtifactAsync(humanManifest);
        await repository.SaveReportArtifactAsync(machineManifest);
        await repository.SaveReportAccessGrantAsync(auditorGrant);
        await context.SaveChangesAsync();

        var latestPackage = await repository.GetLatestReportPackageAsync(election.ElectionId);
        var sealedPackage = await repository.GetSealedReportPackageAsync(election.ElectionId);
        var storedPackage = await repository.GetReportPackageAsync(reportPackage.Id);
        var artifacts = await repository.GetReportArtifactsAsync(reportPackage.Id);
        var grants = await repository.GetReportAccessGrantsAsync(election.ElectionId);
        var grant = await repository.GetReportAccessGrantAsync(election.ElectionId, "auditor-address");

        latestPackage.Should().NotBeNull();
        latestPackage!.Id.Should().Be(reportPackage.Id);
        latestPackage.Status.Should().Be(ElectionReportPackageStatus.Sealed);
        latestPackage.PackageHash.Should().Equal([5, 6, 7, 8]);
        sealedPackage.Should().NotBeNull();
        sealedPackage!.ArtifactCount.Should().Be(2);
        storedPackage.Should().NotBeNull();
        storedPackage!.FrozenEvidenceFingerprint.Should().Be("freeze:abc123");
        artifacts.Should().HaveCount(2);
        artifacts.Select(x => x.ArtifactKind).Should().Equal(
            ElectionReportArtifactKind.HumanManifest,
            ElectionReportArtifactKind.MachineManifest);
        artifacts[1].PairedArtifactId.Should().Be(humanManifest.Id);
        grants.Should().ContainSingle();
        grants[0].GrantRole.Should().Be(ElectionReportAccessGrantRole.DesignatedAuditor);
        grant.Should().NotBeNull();
        grant!.GrantedByPublicAddress.Should().Be("owner-address");
    }

    [Fact]
    public async Task ActorScopedElectionQueries_ShouldRoundTripOwnedLinkedTrusteeAndAuditorLookups()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var now = DateTime.UtcNow;
        var ownedElection = CreateAdminElection() with
        {
            Title = "Owned Election",
            OwnerPublicAddress = "actor-address",
            LastUpdatedAt = now.AddMinutes(-4),
        };
        var linkedElection = CreateAdminElection() with
        {
            Title = "Linked Election",
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = now.AddMinutes(-10),
            LastUpdatedAt = now.AddMinutes(-3),
        };
        var trusteeElection = CreateTrusteeElection() with
        {
            Title = "Trustee Election",
            LastUpdatedAt = now.AddMinutes(-2),
        };
        var auditorElection = CreateAdminElection() with
        {
            Title = "Auditor Election",
            LifecycleState = ElectionLifecycleState.Finalized,
            FinalizedAt = now.AddMinutes(-1),
            LastUpdatedAt = now.AddMinutes(-1),
        };
        var linkedRosterEntry = ElectionModelFactory.CreateRosterEntry(
                linkedElection.ElectionId,
                "1001",
                ElectionRosterContactType.Email,
                "linked@example.org")
            .FreezeAtOpen(linkedElection.OpenedAt!.Value)
            .LinkToActor("actor-address", now.AddMinutes(-9));
        var acceptedInvitation = ElectionModelFactory.CreateTrusteeInvitation(
                trusteeElection.ElectionId,
                trusteeUserAddress: "actor-address",
                trusteeDisplayName: "Actor Trustee",
                invitedByPublicAddress: "owner-address",
                sentAtDraftRevision: trusteeElection.CurrentDraftRevision)
            .Accept(
                respondedAt: now.AddMinutes(-8),
                resolvedAtDraftRevision: trusteeElection.CurrentDraftRevision,
                lifecycleState: ElectionLifecycleState.Draft);
        var auditorGrant = ElectionModelFactory.CreateReportAccessGrant(
            auditorElection.ElectionId,
            actorPublicAddress: "actor-address",
            grantedByPublicAddress: "owner-address",
            grantedAt: now.AddMinutes(-7));

        await repository.SaveElectionAsync(ownedElection);
        await repository.SaveElectionAsync(linkedElection);
        await repository.SaveElectionAsync(trusteeElection);
        await repository.SaveElectionAsync(auditorElection);
        await repository.SaveRosterEntryAsync(linkedRosterEntry);
        await repository.SaveTrusteeInvitationAsync(acceptedInvitation);
        await repository.SaveReportAccessGrantAsync(auditorGrant);
        await context.SaveChangesAsync();

        var selectedElections = await repository.GetElectionsByIdsAsync([ownedElection.ElectionId, linkedElection.ElectionId]);
        var linkedEntries = await repository.GetRosterEntriesByLinkedActorAsync("actor-address");
        var acceptedInvitations = await repository.GetAcceptedTrusteeInvitationsByActorAsync("actor-address");
        var actorGrants = await repository.GetReportAccessGrantsByActorAsync("actor-address");

        selectedElections.Select(x => x.ElectionId).Should().BeEquivalentTo([ownedElection.ElectionId, linkedElection.ElectionId]);
        linkedEntries.Should().ContainSingle();
        linkedEntries[0].ElectionId.Should().Be(linkedElection.ElectionId);
        acceptedInvitations.Should().ContainSingle();
        acceptedInvitations[0].ElectionId.Should().Be(trusteeElection.ElectionId);
        actorGrants.Should().ContainSingle();
        actorGrants[0].ElectionId.Should().Be(auditorElection.ElectionId);
        actorGrants[0].GrantRole.Should().Be(ElectionReportAccessGrantRole.DesignatedAuditor);
    }

    [Fact]
    public async Task SearchElectionsAsync_WithOwnerAddressFilter_ReturnsNewestMatchesWithinLimit()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var now = DateTime.UtcNow;
        var olderOwnedElection = CreateAdminElection() with
        {
            Title = "Older Owned Election",
            OwnerPublicAddress = "actor-address",
            LastUpdatedAt = now.AddMinutes(-3),
        };
        var newerOwnedElection = CreateAdminElection() with
        {
            Title = "Newest Owned Election",
            OwnerPublicAddress = "actor-address",
            LastUpdatedAt = now.AddMinutes(-1),
        };
        var unrelatedElection = CreateAdminElection() with
        {
            Title = "Unrelated Election",
            OwnerPublicAddress = "other-address",
            LastUpdatedAt = now,
        };

        await repository.SaveElectionAsync(olderOwnedElection);
        await repository.SaveElectionAsync(newerOwnedElection);
        await repository.SaveElectionAsync(unrelatedElection);
        await context.SaveChangesAsync();

        var results = await repository.SearchElectionsAsync(
            searchTerm: string.Empty,
            ownerPublicAddresses: [" actor-address ", "ACTOR-ADDRESS", " "],
            limit: 1);

        results.Should().ContainSingle();
        results[0].ElectionId.Should().Be(newerOwnedElection.ElectionId);
        results[0].Title.Should().Be("Newest Owned Election");
    }

    [Fact]
    public async Task SaveArtifactsWarningsAndInvitations_ShouldRoundTrip()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var election = CreateTrusteeElection();
        var warning = ElectionModelFactory.CreateWarningAcknowledgement(
            electionId: election.ElectionId,
            warningCode: ElectionWarningCode.LowAnonymitySet,
            draftRevision: election.CurrentDraftRevision,
            acknowledgedByPublicAddress: "owner-address");
        var invitation = ElectionModelFactory.CreateTrusteeInvitation(
            electionId: election.ElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision);
        var artifact = ElectionModelFactory.CreateBoundaryArtifact(
            artifactType: ElectionBoundaryArtifactType.Open,
            election: election,
            recordedByPublicAddress: "owner-address",
            trusteeSnapshot: ElectionModelFactory.CreateTrusteeBoundarySnapshot(
                requiredApprovalCount: 2,
                acceptedTrustees:
                [
                    new ElectionTrusteeReference("trustee-a", "Alice"),
                    new ElectionTrusteeReference("trustee-b", "Bob"),
                ]),
            ceremonySnapshot: ElectionModelFactory.CreateCeremonyBindingSnapshot(
                ceremonyVersionId: Guid.NewGuid(),
                ceremonyVersionNumber: 1,
                profileId: "dkg-prod-2of2",
                boundTrusteeCount: 2,
                requiredApprovalCount: 2,
                activeTrustees:
                [
                    new ElectionTrusteeReference("trustee-a", "Alice"),
                    new ElectionTrusteeReference("trustee-b", "Bob"),
                ],
                tallyPublicKeyFingerprint: "tally-fingerprint-1"));

        await repository.SaveElectionAsync(election);
        await repository.SaveWarningAcknowledgementAsync(warning);
        await repository.SaveTrusteeInvitationAsync(invitation);
        await repository.SaveBoundaryArtifactAsync(artifact);
        await context.SaveChangesAsync();

        var acceptedInvitation = invitation.Accept(
            respondedAt: DateTime.UtcNow,
            resolvedAtDraftRevision: election.CurrentDraftRevision,
            lifecycleState: ElectionLifecycleState.Draft);
        await repository.UpdateTrusteeInvitationAsync(acceptedInvitation);
        await context.SaveChangesAsync();

        var warnings = await repository.GetWarningAcknowledgementsAsync(election.ElectionId);
        var invitations = await repository.GetTrusteeInvitationsAsync(election.ElectionId);
        var artifacts = await repository.GetBoundaryArtifactsAsync(election.ElectionId);

        warnings.Should().ContainSingle();
        warnings[0].WarningCode.Should().Be(ElectionWarningCode.LowAnonymitySet);
        invitations.Should().ContainSingle();
        invitations[0].Status.Should().Be(ElectionTrusteeInvitationStatus.Accepted);
        artifacts.Should().ContainSingle();
        artifacts[0].ArtifactType.Should().Be(ElectionBoundaryArtifactType.Open);
        artifacts[0].CeremonySnapshot.Should().NotBeNull();
        artifacts[0].CeremonySnapshot!.ProfileId.Should().Be("dkg-prod-2of2");
    }

    [Fact]
    public async Task SaveGovernedProposalAndApprovals_ShouldRoundTripDurableExecutionState()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var election = CreateTrusteeElection();
        var proposal = ElectionModelFactory.CreateGovernedProposal(
            election,
            ElectionGovernedActionType.Open,
            proposedByPublicAddress: "owner-address");
        var approval = ElectionModelFactory.CreateGovernedProposalApproval(
            proposal,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            approvalNote: "Safe to proceed.");

        await repository.SaveElectionAsync(election);
        await repository.SaveGovernedProposalAsync(proposal);
        await repository.SaveGovernedProposalApprovalAsync(approval);
        await context.SaveChangesAsync();

        var failedProposal = proposal.RecordExecutionFailure(
            failureReason: "open transition failed",
            attemptedAt: DateTime.UtcNow,
            executionTriggeredByPublicAddress: "owner-address");
        await repository.UpdateGovernedProposalAsync(failedProposal);
        await context.SaveChangesAsync();

        var storedProposal = await repository.GetGovernedProposalAsync(proposal.Id);
        var pendingProposal = await repository.GetPendingGovernedProposalAsync(election.ElectionId);
        var approvals = await repository.GetGovernedProposalApprovalsAsync(proposal.Id);

        storedProposal.Should().NotBeNull();
        storedProposal!.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatus.ExecutionFailed);
        storedProposal.ExecutionFailureReason.Should().Be("open transition failed");
        storedProposal.LastExecutionTriggeredByPublicAddress.Should().Be("owner-address");
        pendingProposal.Should().NotBeNull();
        pendingProposal!.Id.Should().Be(proposal.Id);
        approvals.Should().ContainSingle();
        approvals[0].ApprovalNote.Should().Be("Safe to proceed.");
        approvals[0].ActionType.Should().Be(ElectionGovernedActionType.Open);
    }

    [Fact]
    public async Task GetPendingGovernedProposalAsync_WithMultiplePendingProposals_ShouldThrow()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var election = CreateTrusteeElection();
        var waitingProposal = ElectionModelFactory.CreateGovernedProposal(
            election,
            ElectionGovernedActionType.Open,
            proposedByPublicAddress: "owner-address");
        var failedProposal = ElectionModelFactory.CreateGovernedProposal(
            election,
            ElectionGovernedActionType.Open,
            proposedByPublicAddress: "owner-address").RecordExecutionFailure(
                failureReason: "transient execution failure",
                attemptedAt: DateTime.UtcNow);

        await repository.SaveElectionAsync(election);
        await repository.SaveGovernedProposalAsync(waitingProposal);
        await repository.SaveGovernedProposalAsync(failedProposal);
        await context.SaveChangesAsync();

        var act = async () => await repository.GetPendingGovernedProposalAsync(election.ElectionId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*multiple pending governed proposals*");
    }

    [Fact]
    public async Task SaveEligibilityData_ShouldRoundTripRosterParticipationAndSnapshots()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var election = CreateAdminElection();
        var importedAt = DateTime.UtcNow.AddMinutes(-15);
        var openedAt = importedAt.AddMinutes(5);
        var activatedAt = openedAt.AddMinutes(2);
        var blankUpdatedAt = activatedAt.AddMinutes(1);

        var inactiveRosterEntry = ElectionModelFactory.CreateRosterEntry(
            election.ElectionId,
            organizationVoterId: "VOTER-1001",
            contactType: ElectionRosterContactType.Email,
            contactValue: "voter@example.com",
            votingRightStatus: ElectionVotingRightStatus.Inactive,
            importedAt: importedAt);
        var activeRosterEntry = ElectionModelFactory.CreateRosterEntry(
            election.ElectionId,
            organizationVoterId: "VOTER-1002",
            contactType: ElectionRosterContactType.Phone,
            contactValue: "+15555550123",
            votingRightStatus: ElectionVotingRightStatus.Active,
            importedAt: importedAt.AddSeconds(1));

        await repository.SaveElectionAsync(election);
        await repository.SaveRosterEntryAsync(inactiveRosterEntry);
        await repository.SaveRosterEntryAsync(activeRosterEntry);
        await context.SaveChangesAsync();

        var linkedAndActivatedEntry = inactiveRosterEntry
            .LinkToActor("voter-address", importedAt.AddMinutes(1))
            .FreezeAtOpen(openedAt)
            .MarkVotingRightActive("owner-address", activatedAt);
        var frozenActiveEntry = activeRosterEntry.FreezeAtOpen(openedAt);
        var blockedActivation = ElectionModelFactory.CreateEligibilityActivationEvent(
            election.ElectionId,
            organizationVoterId: "VOTER-9999",
            attemptedByPublicAddress: "owner-address",
            outcome: ElectionEligibilityActivationOutcome.Blocked,
            blockReason: ElectionEligibilityActivationBlockReason.NotRosteredAtOpen,
            occurredAt: openedAt.AddMinutes(1));
        var successfulActivation = ElectionModelFactory.CreateEligibilityActivationEvent(
            election.ElectionId,
            organizationVoterId: "VOTER-1001",
            attemptedByPublicAddress: "owner-address",
            outcome: ElectionEligibilityActivationOutcome.Activated,
            occurredAt: activatedAt);
        var participation = ElectionModelFactory.CreateParticipationRecord(
            election.ElectionId,
            organizationVoterId: "VOTER-1001",
            participationStatus: ElectionParticipationStatus.CountedAsVoted,
            recordedAt: activatedAt);
        var updatedParticipation = participation.UpdateStatus(
            ElectionParticipationStatus.Blank,
            blankUpdatedAt);
        var openSnapshot = ElectionModelFactory.CreateEligibilitySnapshot(
            election.ElectionId,
            snapshotType: ElectionEligibilitySnapshotType.Open,
            eligibilityMutationPolicy: EligibilityMutationPolicy.LateActivationForRosteredVotersOnly,
            rosteredCount: 2,
            linkedCount: 1,
            activeDenominatorCount: 1,
            countedParticipationCount: 0,
            blankCount: 0,
            didNotVoteCount: 1,
            rosteredVoterSetHash: [1, 2, 3],
            activeDenominatorSetHash: [4, 5, 6],
            countedParticipationSetHash: [7, 8, 9],
            recordedByPublicAddress: "owner-address",
            recordedAt: openedAt);
        var closeSnapshot = ElectionModelFactory.CreateEligibilitySnapshot(
            election.ElectionId,
            snapshotType: ElectionEligibilitySnapshotType.Close,
            eligibilityMutationPolicy: EligibilityMutationPolicy.LateActivationForRosteredVotersOnly,
            rosteredCount: 2,
            linkedCount: 1,
            activeDenominatorCount: 2,
            countedParticipationCount: 1,
            blankCount: 1,
            didNotVoteCount: 1,
            rosteredVoterSetHash: [10, 11, 12],
            activeDenominatorSetHash: [13, 14, 15],
            countedParticipationSetHash: [16, 17, 18],
            recordedByPublicAddress: "owner-address",
            recordedAt: blankUpdatedAt.AddMinutes(1));

        await repository.UpdateRosterEntryAsync(linkedAndActivatedEntry);
        await repository.UpdateRosterEntryAsync(frozenActiveEntry);
        await repository.SaveEligibilityActivationEventAsync(blockedActivation);
        await repository.SaveEligibilityActivationEventAsync(successfulActivation);
        await repository.SaveParticipationRecordAsync(participation);
        await repository.SaveEligibilitySnapshotAsync(openSnapshot);
        await repository.SaveEligibilitySnapshotAsync(closeSnapshot);
        await context.SaveChangesAsync();

        await repository.UpdateParticipationRecordAsync(updatedParticipation);
        await context.SaveChangesAsync();

        var rosterEntries = await repository.GetRosterEntriesAsync(election.ElectionId);
        var linkedEntry = await repository.GetRosterEntryByLinkedActorAsync(election.ElectionId, "voter-address");
        var activationEvents = await repository.GetEligibilityActivationEventsAsync(election.ElectionId);
        var participationRecords = await repository.GetParticipationRecordsAsync(election.ElectionId);
        var closeSnapshotRecord = await repository.GetEligibilitySnapshotAsync(
            election.ElectionId,
            ElectionEligibilitySnapshotType.Close);
        var snapshotRecords = await repository.GetEligibilitySnapshotsAsync(election.ElectionId);

        rosterEntries.Should().HaveCount(2);
        linkedEntry.Should().NotBeNull();
        linkedEntry!.OrganizationVoterId.Should().Be("VOTER-1001");
        linkedEntry.WasPresentAtOpen.Should().BeTrue();
        linkedEntry.WasActiveAtOpen.Should().BeFalse();
        linkedEntry.VotingRightStatus.Should().Be(ElectionVotingRightStatus.Active);
        activationEvents.Should().HaveCount(2);
        activationEvents[0].Outcome.Should().Be(ElectionEligibilityActivationOutcome.Blocked);
        activationEvents[0].BlockReason.Should().Be(ElectionEligibilityActivationBlockReason.NotRosteredAtOpen);
        activationEvents[1].Outcome.Should().Be(ElectionEligibilityActivationOutcome.Activated);
        activationEvents[1].BlockReason.Should().Be(ElectionEligibilityActivationBlockReason.None);
        participationRecords.Should().ContainSingle();
        participationRecords[0].ParticipationStatus.Should().Be(ElectionParticipationStatus.Blank);
        participationRecords[0].CountsAsParticipation.Should().BeTrue();
        closeSnapshotRecord.Should().NotBeNull();
        closeSnapshotRecord!.ActiveDenominatorCount.Should().Be(2);
        closeSnapshotRecord.BlankCount.Should().Be(1);
        closeSnapshotRecord.DidNotVoteCount.Should().Be(1);
        snapshotRecords.Select(x => x.SnapshotType).Should().Equal(
            ElectionEligibilitySnapshotType.Open,
            ElectionEligibilitySnapshotType.Close);
    }

    [Fact]
    public async Task SaveAcceptanceArtifacts_ShouldRoundTripCommitmentCheckoffBallotAndIdempotencyRecords()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var election = CreateAdminElection();
        var commitment = ElectionModelFactory.CreateCommitmentRegistrationRecord(
            election.ElectionId,
            organizationVoterId: "VOTER-1001",
            linkedActorPublicAddress: "voter-address",
            commitmentHash: "commitment-hash-1");
        var checkoff = ElectionModelFactory.CreateCheckoffConsumptionRecord(
            election.ElectionId,
            organizationVoterId: "VOTER-1001");
        var ballot = ElectionModelFactory.CreateAcceptedBallotRecord(
            election.ElectionId,
            encryptedBallotPackage: "ciphertext-payload",
            proofBundle: "proof-bundle",
            ballotNullifier: "nullifier-1");
        var idempotency = ElectionModelFactory.CreateCastIdempotencyRecord(
            election.ElectionId,
            idempotencyKeyHash: "idem-hash-1");

        await repository.SaveElectionAsync(election);
        await repository.SaveCommitmentRegistrationAsync(commitment);
        await repository.SaveCheckoffConsumptionAsync(checkoff);
        await repository.SaveAcceptedBallotAsync(ballot);
        await repository.SaveCastIdempotencyRecordAsync(idempotency);
        await context.SaveChangesAsync();

        var commitments = await repository.GetCommitmentRegistrationsAsync(election.ElectionId);
        var storedCommitment = await repository.GetCommitmentRegistrationByLinkedActorAsync(
            election.ElectionId,
            "voter-address");
        var storedCheckoff = await repository.GetCheckoffConsumptionAsync(
            election.ElectionId,
            "VOTER-1001");
        var acceptedBallots = await repository.GetAcceptedBallotsAsync(election.ElectionId);
        var storedBallot = await repository.GetAcceptedBallotByNullifierAsync(
            election.ElectionId,
            "nullifier-1");
        var idempotencyRecords = await repository.GetCastIdempotencyRecordsAsync(election.ElectionId);
        var storedIdempotency = await repository.GetCastIdempotencyRecordAsync(
            election.ElectionId,
            "idem-hash-1");

        commitments.Should().ContainSingle();
        commitments[0].CommitmentHash.Should().Be("commitment-hash-1");
        storedCommitment.Should().NotBeNull();
        storedCommitment!.OrganizationVoterId.Should().Be("VOTER-1001");

        storedCheckoff.Should().NotBeNull();
        storedCheckoff!.ParticipationStatus.Should().Be(ElectionParticipationStatus.CountedAsVoted);

        acceptedBallots.Should().ContainSingle();
        acceptedBallots[0].BallotNullifier.Should().Be("nullifier-1");
        storedBallot.Should().NotBeNull();
        storedBallot!.ProofBundle.Should().Be("proof-bundle");

        idempotencyRecords.Should().ContainSingle();
        idempotencyRecords[0].IdempotencyKeyHash.Should().Be("idem-hash-1");
        storedIdempotency.Should().NotBeNull();
        storedIdempotency!.ElectionId.Should().Be(election.ElectionId);
    }

    [Fact]
    public async Task DeleteRosterEntriesAsync_ShouldRemovePriorImportRows()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var election = CreateAdminElection();

        await repository.SaveElectionAsync(election);
        await repository.SaveRosterEntryAsync(ElectionModelFactory.CreateRosterEntry(
            election.ElectionId,
            organizationVoterId: "VOTER-1001",
            contactType: ElectionRosterContactType.Email,
            contactValue: "voter1@example.com"));
        await repository.SaveRosterEntryAsync(ElectionModelFactory.CreateRosterEntry(
            election.ElectionId,
            organizationVoterId: "VOTER-1002",
            contactType: ElectionRosterContactType.Phone,
            contactValue: "+15555550124"));
        await context.SaveChangesAsync();

        await repository.DeleteRosterEntriesAsync(election.ElectionId);
        await context.SaveChangesAsync();

        var rosterEntries = await repository.GetRosterEntriesAsync(election.ElectionId);

        rosterEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveFinalizationSessionSharesAndReleaseEvidence_ShouldRoundTrip()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var election = CreateTrusteeElection() with
        {
            LifecycleState = ElectionLifecycleState.Closed,
            OpenArtifactId = Guid.NewGuid(),
            CloseArtifactId = Guid.NewGuid(),
            ClosedAt = DateTime.UtcNow.AddMinutes(-5),
            TallyReadyAt = DateTime.UtcNow.AddMinutes(-4),
            VoteAcceptanceLockedAt = DateTime.UtcNow.AddMinutes(-5),
            LastUpdatedAt = DateTime.UtcNow.AddMinutes(-4),
        };
        var ceremonySnapshot = ElectionModelFactory.CreateCeremonyBindingSnapshot(
            ceremonyVersionId: Guid.NewGuid(),
            ceremonyVersionNumber: 1,
            profileId: "dkg-prod-2of2",
            boundTrusteeCount: 2,
            requiredApprovalCount: 1,
            activeTrustees:
            [
                new ElectionTrusteeReference("trustee-a", "Alice"),
                new ElectionTrusteeReference("trustee-b", "Bob"),
            ],
            tallyPublicKeyFingerprint: "tally-fingerprint-1");
        var session = ElectionModelFactory.CreateFinalizationSession(
            election,
            closeArtifactId: election.CloseArtifactId!.Value,
            acceptedBallotSetHash: [1, 2, 3],
            finalEncryptedTallyHash: [4, 5, 6],
            sessionPurpose: ElectionFinalizationSessionPurpose.Finalization,
            ceremonySnapshot: ceremonySnapshot,
            requiredShareCount: 1,
            eligibleTrustees:
            [
                new ElectionTrusteeReference("trustee-a", "Alice"),
                new ElectionTrusteeReference("trustee-b", "Bob"),
            ],
            createdByPublicAddress: "owner-address");
        var acceptedShare = ElectionModelFactory.CreateAcceptedFinalizationShare(
            finalizationSessionId: session.Id,
            electionId: election.ElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            submittedByPublicAddress: "trustee-a",
            shareIndex: 1,
            shareVersion: "share-v1",
            targetType: ElectionFinalizationTargetType.AggregateTally,
            claimedCloseArtifactId: session.CloseArtifactId,
            claimedAcceptedBallotSetHash: session.AcceptedBallotSetHash,
            claimedFinalEncryptedTallyHash: session.FinalEncryptedTallyHash,
            claimedTargetTallyId: session.TargetTallyId,
            claimedCeremonyVersionId: ceremonySnapshot.CeremonyVersionId,
            claimedTallyPublicKeyFingerprint: ceremonySnapshot.TallyPublicKeyFingerprint,
            shareMaterial: "ciphertext-share");
        var releaseEvidence = ElectionModelFactory.CreateFinalizationReleaseEvidence(
            session,
            acceptedTrustees:
            [
                new ElectionTrusteeReference("trustee-a", "Alice"),
            ],
            completedByPublicAddress: "owner-address");
        var completedSession = session.MarkCompleted(releaseEvidence.Id, releaseEvidence.CompletedAt);

        await repository.SaveElectionAsync(election);
        await repository.SaveFinalizationSessionAsync(session);
        await repository.SaveFinalizationShareAsync(acceptedShare);
        await repository.SaveFinalizationReleaseEvidenceRecordAsync(releaseEvidence);
        await context.SaveChangesAsync();

        await repository.UpdateFinalizationSessionAsync(completedSession);
        await context.SaveChangesAsync();

        var sessions = await repository.GetFinalizationSessionsAsync(election.ElectionId);
        var storedSession = await repository.GetFinalizationSessionAsync(session.Id);
        var activeSession = await repository.GetActiveFinalizationSessionAsync(election.ElectionId);
        var shares = await repository.GetFinalizationSharesAsync(session.Id);
        var accepted = await repository.GetAcceptedFinalizationShareAsync(session.Id, "trustee-a");
        var releaseEvidenceRecords = await repository.GetFinalizationReleaseEvidenceRecordsAsync(election.ElectionId);
        var storedReleaseEvidence = await repository.GetFinalizationReleaseEvidenceRecordAsync(session.Id);

        sessions.Should().ContainSingle();
        sessions[0].Status.Should().Be(ElectionFinalizationSessionStatus.Completed);
        sessions[0].CeremonySnapshot.Should().NotBeNull();
        sessions[0].CeremonySnapshot!.ProfileId.Should().Be("dkg-prod-2of2");
        storedSession.Should().NotBeNull();
        storedSession!.ReleaseEvidenceId.Should().Be(releaseEvidence.Id);
        activeSession.Should().BeNull();
        shares.Should().ContainSingle();
        shares[0].Status.Should().Be(ElectionFinalizationShareStatus.Accepted);
        shares[0].ClaimedTargetTallyId.Should().Be(session.TargetTallyId);
        accepted.Should().NotBeNull();
        accepted!.ShareVersion.Should().Be("share-v1");
        releaseEvidenceRecords.Should().ContainSingle();
        releaseEvidenceRecords[0].AcceptedShareCount.Should().Be(1);
        storedReleaseEvidence.Should().NotBeNull();
        storedReleaseEvidence!.ReleaseMode.Should().Be(ElectionFinalizationReleaseMode.AggregateTallyOnly);
    }

    private static ElectionsRepository CreateRepository(ElectionsDbContext context)
    {
        var repository = new ElectionsRepository();
        repository.SetContext(context);
        return repository;
    }

    private static ElectionsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ElectionsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ElectionsDbContext(new ElectionsDbContextConfigurator(), options);
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
            externalReferenceCode: "REF-1",
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
}
