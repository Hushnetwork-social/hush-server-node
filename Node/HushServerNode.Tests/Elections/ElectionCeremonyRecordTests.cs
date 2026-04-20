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
            tallyPublicKeyFingerprint: CeremonyTestKeyFixtures.Fingerprint,
            tallyPublicKey: CeremonyTestKeyFixtures.PublicKeyBytes);
        var superseded = ready.Supersede(
            supersededAt: DateTime.UtcNow,
            supersededReason: "roster changed after progress");

        ready.Status.Should().Be(ElectionCeremonyVersionStatus.Ready);
        ready.TallyPublicKeyFingerprint.Should().Be(CeremonyTestKeyFixtures.Fingerprint);
        ready.TallyPublicKey.Should().Equal(CeremonyTestKeyFixtures.PublicKeyBytes);
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
        var submitted = selfTested.RecordMaterialSubmitted(
            DateTime.UtcNow,
            "share-v1",
            CeremonyTestKeyFixtures.PublicKeyBytes);
        var failed = submitted.RecordValidationFailure("wrong recipient binding", DateTime.UtcNow);
        var reselfTested = failed.RecordSelfTestSuccess(DateTime.UtcNow);
        var resubmitted = reselfTested.RecordMaterialSubmitted(
            DateTime.UtcNow,
            "share-v2",
            CeremonyTestKeyFixtures.PublicKeyBytes);
        var completed = resubmitted.MarkCompleted(DateTime.UtcNow, "share-v2");

        published.HasPublishedTransportKey.Should().BeTrue();
        joined.State.Should().Be(ElectionTrusteeCeremonyState.CeremonyJoined);
        selfTested.SelfTestSucceededAt.Should().NotBeNull();
        submitted.State.Should().Be(ElectionTrusteeCeremonyState.CeremonyMaterialSubmitted);
        failed.State.Should().Be(ElectionTrusteeCeremonyState.CeremonyValidationFailed);
        failed.ValidationFailureReason.Should().Be("wrong recipient binding");
        failed.SelfTestSucceededAt.Should().BeNull();
        failed.MaterialSubmittedAt.Should().BeNull();
        failed.ShareVersion.Should().BeNull();
        reselfTested.State.Should().Be(ElectionTrusteeCeremonyState.CeremonyJoined);
        reselfTested.ValidationFailureReason.Should().BeNull();
        resubmitted.State.Should().Be(ElectionTrusteeCeremonyState.CeremonyMaterialSubmitted);
        completed.State.Should().Be(ElectionTrusteeCeremonyState.CeremonyCompleted);
        completed.ShareVersion.Should().Be("share-v2");
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
    public void CeremonyTrusteeState_PublishTransportKeyTwice_ShouldThrow()
    {
        var state = ElectionModelFactory.CreateCeremonyTrusteeState(
            electionId: ElectionId.NewElectionId,
            ceremonyVersionId: Guid.NewGuid(),
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice");
        var published = state.PublishTransportKey("transport-fingerprint", DateTime.UtcNow);

        var act = () => published.PublishTransportKey("transport-fingerprint-2", DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already published a transport key*");
    }

    [Fact]
    public void CeremonyTrusteeState_RecordSelfTestTwiceWithoutValidationFailure_ShouldThrow()
    {
        var state = ElectionModelFactory.CreateCeremonyTrusteeState(
                electionId: ElectionId.NewElectionId,
                ceremonyVersionId: Guid.NewGuid(),
                trusteeUserAddress: "trustee-a",
                trusteeDisplayName: "Alice")
            .PublishTransportKey("transport-fingerprint", DateTime.UtcNow)
            .MarkJoined(DateTime.UtcNow)
            .RecordSelfTestSuccess(DateTime.UtcNow);

        var act = () => state.RecordSelfTestSuccess(DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*self-test has already been recorded*");
    }

    [Fact]
    public void CeremonyTrusteeState_SubmitWithoutJoin_ShouldThrow()
    {
        var state = ElectionModelFactory.CreateCeremonyTrusteeState(
                electionId: ElectionId.NewElectionId,
                ceremonyVersionId: Guid.NewGuid(),
                trusteeUserAddress: "trustee-a",
                trusteeDisplayName: "Alice")
            .PublishTransportKey("transport-fingerprint", DateTime.UtcNow);

        var act = () => state.RecordMaterialSubmitted(
            DateTime.UtcNow,
            "share-v1",
            CeremonyTestKeyFixtures.PublicKeyBytes);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*join the ceremony before submitting material*");
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
