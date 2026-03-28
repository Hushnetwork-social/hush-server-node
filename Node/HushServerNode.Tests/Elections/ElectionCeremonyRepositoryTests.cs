using FluentAssertions;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionCeremonyRepositoryTests
{
    [Fact]
    public async Task SaveCeremonyProfiles_ShouldRoundTripRegistryMetadata()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var devProfile = ElectionModelFactory.CreateCeremonyProfile(
            profileId: "dkg-dev-3of5",
            displayName: "Dev 3-of-5",
            description: "Fast local profile",
            providerKey: "hush-dev",
            profileVersion: "v1",
            trusteeCount: 5,
            requiredApprovalCount: 3,
            devOnly: true);
        var prodProfile = ElectionModelFactory.CreateCeremonyProfile(
            profileId: "dkg-prod-3of5",
            displayName: "Prod 3-of-5",
            description: "Production-like profile",
            providerKey: "hush-prod",
            profileVersion: "v1",
            trusteeCount: 5,
            requiredApprovalCount: 3,
            devOnly: false);

        await repository.SaveCeremonyProfileAsync(devProfile);
        await repository.SaveCeremonyProfileAsync(prodProfile);
        await context.SaveChangesAsync();

        var profiles = await repository.GetCeremonyProfilesAsync();
        var storedProd = await repository.GetCeremonyProfileAsync("dkg-prod-3of5");

        profiles.Should().HaveCount(2);
        profiles.Select(x => x.ProfileId).Should().ContainInOrder("dkg-dev-3of5", "dkg-prod-3of5");
        storedProd.Should().NotBeNull();
        storedProd!.DevOnly.Should().BeFalse();
        storedProd.TrusteeCount.Should().Be(5);
        storedProd.RequiredApprovalCount.Should().Be(3);
    }

    [Fact]
    public async Task SaveCeremonyVersionTranscriptTrusteeStateAndShareCustody_ShouldRoundTrip()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var election = CreateTrusteeElection();
        var version = ElectionModelFactory.CreateCeremonyVersion(
            electionId: election.ElectionId,
            versionNumber: 1,
            profileId: "dkg-prod-3of5",
            requiredApprovalCount: 3,
            boundTrustees:
            [
                new ElectionTrusteeReference("trustee-a", "Alice"),
                new ElectionTrusteeReference("trustee-b", "Bob"),
                new ElectionTrusteeReference("trustee-c", "Carla"),
                new ElectionTrusteeReference("trustee-d", "Dmitri"),
                new ElectionTrusteeReference("trustee-e", "Erin"),
            ],
            startedByPublicAddress: "owner-address");
        var readyVersion = version.MarkReady(DateTime.UtcNow, "tally-fingerprint-1");
        var transcriptEvent = ElectionModelFactory.CreateCeremonyTranscriptEvent(
            electionId: election.ElectionId,
            ceremonyVersionId: version.Id,
            versionNumber: version.VersionNumber,
            eventType: ElectionCeremonyTranscriptEventType.VersionReady,
            eventSummary: "Threshold reached for the active version.",
            tallyPublicKeyFingerprint: readyVersion.TallyPublicKeyFingerprint);
        var envelope = ElectionModelFactory.CreateCeremonyMessageEnvelope(
            electionId: election.ElectionId,
            ceremonyVersionId: version.Id,
            versionNumber: version.VersionNumber,
            profileId: version.ProfileId,
            senderTrusteeUserAddress: "trustee-a",
            recipientTrusteeUserAddress: "trustee-b",
            messageType: "contribution_package",
            payloadVersion: "payload-v1",
            encryptedPayload: [1, 2, 3, 4],
            payloadFingerprint: "payload-fingerprint-1");
        var trusteeState = ElectionModelFactory.CreateCeremonyTrusteeState(
            electionId: election.ElectionId,
            ceremonyVersionId: version.Id,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice")
            .PublishTransportKey("transport-fingerprint", DateTime.UtcNow)
            .MarkJoined(DateTime.UtcNow)
            .RecordSelfTestSuccess(DateTime.UtcNow)
            .RecordMaterialSubmitted(DateTime.UtcNow)
            .MarkCompleted(DateTime.UtcNow, "share-v1");
        var shareCustody = ElectionModelFactory.CreateCeremonyShareCustodyRecord(
            electionId: election.ElectionId,
            ceremonyVersionId: version.Id,
            trusteeUserAddress: "trustee-a",
            shareVersion: "share-v1")
            .RecordExport(DateTime.UtcNow);

        await repository.SaveElectionAsync(election);
        await repository.SaveCeremonyVersionAsync(version);
        await repository.SaveCeremonyTranscriptEventAsync(transcriptEvent);
        await repository.SaveCeremonyMessageEnvelopeAsync(envelope);
        await repository.SaveCeremonyTrusteeStateAsync(trusteeState);
        await repository.SaveCeremonyShareCustodyRecordAsync(shareCustody);
        await context.SaveChangesAsync();

        await repository.UpdateCeremonyVersionAsync(readyVersion);
        await context.SaveChangesAsync();

        var activeVersion = await repository.GetActiveCeremonyVersionAsync(election.ElectionId);
        var versions = await repository.GetCeremonyVersionsAsync(election.ElectionId);
        var transcript = await repository.GetCeremonyTranscriptEventsAsync(version.Id);
        var envelopes = await repository.GetCeremonyMessageEnvelopesForRecipientAsync(version.Id, "trustee-b");
        var storedTrusteeState = await repository.GetCeremonyTrusteeStateAsync(version.Id, "trustee-a");
        var storedShareCustody = await repository.GetCeremonyShareCustodyRecordAsync(version.Id, "trustee-a");

        activeVersion.Should().NotBeNull();
        activeVersion!.Status.Should().Be(ElectionCeremonyVersionStatus.Ready);
        activeVersion.TallyPublicKeyFingerprint.Should().Be("tally-fingerprint-1");
        versions.Should().ContainSingle();
        transcript.Should().ContainSingle();
        transcript[0].EventType.Should().Be(ElectionCeremonyTranscriptEventType.VersionReady);
        transcript[0].TallyPublicKeyFingerprint.Should().Be("tally-fingerprint-1");
        envelopes.Should().ContainSingle();
        envelopes[0].EncryptedPayload.Should().Equal([1, 2, 3, 4]);
        envelopes[0].PayloadFingerprint.Should().Be("payload-fingerprint-1");
        storedTrusteeState.Should().NotBeNull();
        storedTrusteeState!.State.Should().Be(ElectionTrusteeCeremonyState.CeremonyCompleted);
        storedTrusteeState.TransportPublicKeyFingerprint.Should().Be("transport-fingerprint");
        storedShareCustody.Should().NotBeNull();
        storedShareCustody!.Status.Should().Be(ElectionCeremonyShareCustodyStatus.Exported);
        storedShareCustody.PasswordProtected.Should().BeTrue();
    }

    [Fact]
    public async Task GetActiveCeremonyVersionAsync_WithMultipleActiveVersions_ShouldThrow()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var election = CreateTrusteeElection();
        var versionOne = ElectionModelFactory.CreateCeremonyVersion(
            electionId: election.ElectionId,
            versionNumber: 1,
            profileId: "dkg-dev-3of5",
            requiredApprovalCount: 3,
            boundTrustees:
            [
                new ElectionTrusteeReference("trustee-a", "Alice"),
                new ElectionTrusteeReference("trustee-b", "Bob"),
                new ElectionTrusteeReference("trustee-c", "Carla"),
            ],
            startedByPublicAddress: "owner-address");
        var versionTwo = ElectionModelFactory.CreateCeremonyVersion(
            electionId: election.ElectionId,
            versionNumber: 2,
            profileId: "dkg-prod-3of5",
            requiredApprovalCount: 3,
            boundTrustees:
            [
                new ElectionTrusteeReference("trustee-a", "Alice"),
                new ElectionTrusteeReference("trustee-b", "Bob"),
                new ElectionTrusteeReference("trustee-c", "Carla"),
            ],
            startedByPublicAddress: "owner-address");

        await repository.SaveElectionAsync(election);
        await repository.SaveCeremonyVersionAsync(versionOne);
        await repository.SaveCeremonyVersionAsync(versionTwo);
        await context.SaveChangesAsync();

        var act = async () => await repository.GetActiveCeremonyVersionAsync(election.ElectionId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*multiple active ceremony versions*");
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
            requiredApprovalCount: 3);
}
