using FluentAssertions;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionCeremonyRecordTests
{
    [Fact]
    public void CreateCeremonyProfile_CapturesRolloutShapeAndVisibility()
    {
        var profile = ElectionModelFactory.CreateCeremonyProfile(
            profileId: "dkg-dev-3of5",
            displayName: "Dev 3-of-5",
            description: "Fast deterministic local profile.",
            providerKey: "hush-dev",
            profileVersion: "v1",
            trusteeCount: 5,
            requiredApprovalCount: 3,
            devOnly: true);

        profile.ProfileId.Should().Be("dkg-dev-3of5");
        profile.DevOnly.Should().BeTrue();
        profile.TrusteeCount.Should().Be(5);
        profile.RequiredApprovalCount.Should().Be(3);
        profile.ProviderKey.Should().Be("hush-dev");
    }

    [Fact]
    public void CreateCeremonyVersion_WithDuplicateTrustees_ShouldThrow()
    {
        var act = () => ElectionModelFactory.CreateCeremonyVersion(
            electionId: ElectionId.NewElectionId,
            versionNumber: 1,
            profileId: "dkg-prod-3of5",
            requiredApprovalCount: 3,
            boundTrustees:
            [
                new ElectionTrusteeReference("trustee-a", "Alice"),
                new ElectionTrusteeReference("TRUSTEE-A", "Alice Again"),
            ],
            startedByPublicAddress: "owner-address");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Duplicate bound trustee*");
    }

    [Fact]
    public void CeremonyVersion_MarkReadyThenSupersede_ShouldPreservePublicHistoryFields()
    {
        var version = CreateCeremonyVersion();

        var ready = version.MarkReady(
            completedAt: DateTime.UtcNow,
            tallyPublicKeyFingerprint: "tally-fingerprint-1");
        var superseded = ready.Supersede(
            supersededAt: DateTime.UtcNow,
            supersededReason: "roster changed after progress");

        ready.Status.Should().Be(ElectionCeremonyVersionStatus.Ready);
        ready.TallyPublicKeyFingerprint.Should().Be("tally-fingerprint-1");
        superseded.Status.Should().Be(ElectionCeremonyVersionStatus.Superseded);
        superseded.SupersededReason.Should().Be("roster changed after progress");
        superseded.BoundTrustees.Should().HaveCount(5);
    }

    [Fact]
    public void CeremonyTrusteeState_PublishJoinSubmitFailComplete_ShouldTrackLifecycleFields()
    {
        var state = ElectionModelFactory.CreateCeremonyTrusteeState(
            electionId: ElectionId.NewElectionId,
            ceremonyVersionId: Guid.NewGuid(),
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice");

        var published = state.PublishTransportKey("transport-fingerprint", DateTime.UtcNow);
        var joined = published.MarkJoined(DateTime.UtcNow);
        var selfTested = joined.RecordSelfTestSuccess(DateTime.UtcNow);
        var submitted = selfTested.RecordMaterialSubmitted(DateTime.UtcNow);
        var failed = submitted.RecordValidationFailure("wrong recipient binding", DateTime.UtcNow);
        var reselfTested = failed.RecordSelfTestSuccess(DateTime.UtcNow);
        var resubmitted = reselfTested.RecordMaterialSubmitted(DateTime.UtcNow);
        var completed = resubmitted.MarkCompleted(DateTime.UtcNow, "share-v1");

        published.HasPublishedTransportKey.Should().BeTrue();
        joined.State.Should().Be(ElectionTrusteeCeremonyState.CeremonyJoined);
        selfTested.SelfTestSucceededAt.Should().NotBeNull();
        submitted.State.Should().Be(ElectionTrusteeCeremonyState.CeremonyMaterialSubmitted);
        failed.State.Should().Be(ElectionTrusteeCeremonyState.CeremonyValidationFailed);
        failed.ValidationFailureReason.Should().Be("wrong recipient binding");
        resubmitted.State.Should().Be(ElectionTrusteeCeremonyState.CeremonyMaterialSubmitted);
        completed.State.Should().Be(ElectionTrusteeCeremonyState.CeremonyCompleted);
        completed.ShareVersion.Should().Be("share-v1");
    }

    [Fact]
    public void CeremonyTrusteeState_JoinWithoutTransportKey_ShouldThrow()
    {
        var state = ElectionModelFactory.CreateCeremonyTrusteeState(
            electionId: ElectionId.NewElectionId,
            ceremonyVersionId: Guid.NewGuid(),
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice");

        var act = () => state.MarkJoined(DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*publish a transport key before joining*");
    }

    [Fact]
    public void CeremonyShareCustody_RecordImportFailure_PreservesExactBoundMetadata()
    {
        var electionId = ElectionId.NewElectionId;
        var versionId = Guid.NewGuid();
        var share = ElectionModelFactory.CreateCeremonyShareCustodyRecord(
            electionId: electionId,
            ceremonyVersionId: versionId,
            trusteeUserAddress: "trustee-a",
            shareVersion: "share-v1");

        var failedImport = share.RecordImportFailure("version mismatch", DateTime.UtcNow);

        failedImport.Status.Should().Be(ElectionCeremonyShareCustodyStatus.ImportFailed);
        failedImport.LastImportFailureReason.Should().Be("version mismatch");
        failedImport.MatchesImportBinding(electionId, versionId, "TRUSTEE-A", "share-v1").Should().BeTrue();
        failedImport.MatchesImportBinding(ElectionId.NewElectionId, versionId, "TRUSTEE-A", "share-v1").Should().BeFalse();
        failedImport.MatchesImportBinding(electionId, Guid.NewGuid(), "TRUSTEE-A", "share-v1").Should().BeFalse();
        failedImport.MatchesImportBinding(electionId, versionId, "TRUSTEE-A", "share-v2").Should().BeFalse();
    }

    private static ElectionCeremonyVersionRecord CreateCeremonyVersion() =>
        ElectionModelFactory.CreateCeremonyVersion(
            electionId: ElectionId.NewElectionId,
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
}
