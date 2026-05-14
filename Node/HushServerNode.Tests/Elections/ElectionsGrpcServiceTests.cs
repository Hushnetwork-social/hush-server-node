using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.Elections.gRPC;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.AutoMock;
using Olimpo;
using System.Text.Json;
using Xunit;
using Domain = HushNode.Elections;
using Proto = HushNetwork.proto;

namespace HushServerNode.Tests.Elections;

public class ElectionsGrpcServiceTests
{
    private const string TestSigningPrivateKey =
        "1111111111111111111111111111111111111111111111111111111111111111";
    private static readonly string TestActorPublicAddress =
        DigitalSignature.GetCompressedPublicAddress(TestSigningPrivateKey);

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
    public async Task RegisterPreparedBallotCommitment_RejectsDirectCommandPath()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new Proto.RegisterPreparedBallotCommitmentRequest
        {
            ElectionId = ElectionId.NewElectionId.ToString(),
            ActorPublicAddress = "voter-address",
            PreparedBallotId = Guid.NewGuid().ToString(),
            PreparedBallotHash = "prepared-hash-1",
            BallotDefinitionVersion = 1,
            BallotDefinitionHash = Google.Protobuf.ByteString.CopyFrom(new byte[] { 1, 2, 3, 4 }),
            CeremonyProfileId = ElectionSp04ProfileIds.ChallengeSpoilV1,
            ProofStatementId = "sp04-proof-v1",
        };

        var act = async () => await sut.RegisterPreparedBallotCommitment(request, CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Which.Status.Detail.Should().Contain("SubmitSignedTransaction");
    }

    [Fact]
    public async Task SpoilPreparedBallot_RejectsDirectCommandPath()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var request = new Proto.SpoilPreparedBallotRequest
        {
            ElectionId = ElectionId.NewElectionId.ToString(),
            ActorPublicAddress = "voter-address",
            PreparedBallotId = Guid.NewGuid().ToString(),
            PreparedBallotHash = "prepared-hash-1",
            SpoiledTranscriptHash = "spoiled-transcript-hash",
            SpoilRecordHash = "spoil-record-hash",
            LocalVerifierVersion = "local-verifier-v1",
        };

        var act = async () => await sut.SpoilPreparedBallot(request, CreateMockServerCallContext());

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
                [ElectionWarningCode.LowAnonymitySet],
                protocolPackageValidation: Domain.ProtocolPackageBindingOpenValidation.NotReady(
                    ProtocolPackageBindingStatus.Missing,
                    null,
                    "Latest Protocol Omega package refs are missing.")));

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
        response.ProtocolPackageBindingStatus.Should().Be(ProtocolPackageBindingStatusProto.ProtocolPackageBindingMissing);
        response.ProtocolPackageBindingMessage.Should().Contain("Protocol Omega package refs are missing");
    }

    [Fact]
    public async Task GetElectionOpenReadiness_WithSp07ReadinessSummary_MapsPublicationProofProjection()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;
        var sp07Summary = new Domain.ElectionSp07OpenReadinessSummary(
            EvidenceExpected: true,
            PublicationProofMode: ElectionSp07ProfileIds.PublicationProofMode,
            ProofConstruction: ElectionSp07ProfileIds.ProofConstruction,
            StatementId: ElectionSp07ProfileIds.StatementId,
            ExternalReviewStatus: ElectionSp07ProfileIds.ExternalReviewStatus,
            IntendedAcceptedBallotCount: 501,
            CiphertextSlotCount: 8,
            PlannedChunkCount: 0,
            ReadinessBlockers:
            [
                new Domain.ElectionSp07OpenReadinessBlocker(
                    VerificationResultCodes.PublicationProofEnvelopeExceeded,
                    "SP-07 high-assurance v1 supports up to 500 accepted ballots.",
                    BlocksOpen: true,
                    BlocksFinalization: true)
            ]);

        mocker.GetMock<Domain.IElectionLifecycleService>()
            .Setup(x => x.EvaluateOpenReadinessAsync(It.IsAny<Domain.EvaluateElectionOpenReadinessRequest>()))
            .ReturnsAsync(Domain.ElectionOpenValidationResult.NotReady(
                ["sp07_publication_proof_envelope_exceeded: SP-07 high-assurance v1 supports up to 500 accepted ballots."],
                [],
                [],
                sp07Summary: sp07Summary));

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionOpenReadiness(
            new GetElectionOpenReadinessRequest { ElectionId = electionId.ToString() },
            CreateMockServerCallContext());

        response.IsReadyToOpen.Should().BeFalse();
        response.Sp07Evidence.Should().NotBeNull();
        response.Sp07Evidence.PublicationProofMode.Should().Be(ElectionSp07ProfileIds.PublicationProofMode);
        response.Sp07Evidence.AcceptedBallotCount.Should().Be(501);
        response.Sp07Evidence.CiphertextSlotCount.Should().Be(8);
        response.Sp07Evidence.LatestPubResultCode.Should().Be(VerificationResultCodes.PublicationProofEnvelopeExceeded);
        response.Sp07Evidence.Blockers.Should().ContainSingle(x =>
            x.Code == VerificationResultCodes.PublicationProofEnvelopeExceeded &&
            x.BlocksOpen);
    }

    [Fact]
    public async Task GetElectionOpenReadiness_WithSp08ReadinessSummary_MapsReleaseIntegrityProjection()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;
        var sp08Summary = new Domain.ElectionSp08OpenReadinessSummary(
            EvidenceExpected: true,
            EvidenceMode: ElectionSp08ProfileIds.EvidenceModeDevelopmentPlaceholder,
            NotForReleaseIntegrityClaims: true,
            BlocksHighAssurance: true,
            ReleaseManifestName: ElectionSp08ProfileIds.ReleaseManifestFileName,
            ReleaseManifestHash: string.Empty,
            ProtocolPackageManifestName: "ProtocolOmegaPackageManifest.json",
            ProtocolPackageManifestHash: "sha256:protocol",
            PrimaryResultCode: VerificationResultCodes.ReleaseIntegrityEvidenceModeNotAllowed,
            PrimaryIssue: "Official SP-08 release evidence is required before high-assurance elections can open.",
            ComponentCount: ElectionSp08ProfileIds.RequiredHighAssuranceComponentIds.Count,
            LifecycleBindingCount: 0,
            EvidenceFileCount: 0,
            MobileEvidenceIncluded: false,
            ReadinessBlockers:
            [
                new Domain.ElectionSp08OpenReadinessBlocker(
                    VerificationResultCodes.ReleaseIntegrityEvidenceModeNotAllowed,
                    "Official SP-08 release evidence is required before high-assurance elections can open.",
                    BlocksOpen: true,
                    BlocksFinalization: true)
            ]);

        mocker.GetMock<Domain.IElectionLifecycleService>()
            .Setup(x => x.EvaluateOpenReadinessAsync(It.IsAny<Domain.EvaluateElectionOpenReadinessRequest>()))
            .ReturnsAsync(Domain.ElectionOpenValidationResult.NotReady(
                ["release_integrity_evidence_mode_not_allowed: Official SP-08 release evidence is required before high-assurance elections can open."],
                [],
                [],
                sp08Summary: sp08Summary));

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionOpenReadiness(
            new GetElectionOpenReadinessRequest { ElectionId = electionId.ToString() },
            CreateMockServerCallContext());

        response.IsReadyToOpen.Should().BeFalse();
        response.Sp08ReleaseIntegrity.Should().NotBeNull();
        response.Sp08ReleaseIntegrity.EvidenceMode.Should().Be(ElectionSp08ProfileIds.EvidenceModeDevelopmentPlaceholder);
        response.Sp08ReleaseIntegrity.NotForReleaseIntegrityClaims.Should().BeTrue();
        response.Sp08ReleaseIntegrity.BlocksHighAssurance.Should().BeTrue();
        response.Sp08ReleaseIntegrity.PrimaryResultCode.Should().Be(
            VerificationResultCodes.ReleaseIntegrityEvidenceModeNotAllowed);
        response.Sp08ReleaseIntegrity.ProtocolPackageManifestHash.Should().Be("sha256:protocol");
    }

    [Fact]
    public async Task GetElectionOpenReadiness_WithSp08WarningSummary_MapsWarningCopyWithoutBlocking()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;
        const string warning =
            "Development placeholder SP-08 release evidence is present for this development profile. It is not official release evidence and must not support release-integrity claims.";
        var sp08Summary = new Domain.ElectionSp08OpenReadinessSummary(
            EvidenceExpected: true,
            EvidenceMode: ElectionSp08ProfileIds.EvidenceModeDevelopmentPlaceholder,
            NotForReleaseIntegrityClaims: true,
            BlocksHighAssurance: false,
            ReleaseManifestName: ElectionSp08ProfileIds.ReleaseManifestFileName,
            ReleaseManifestHash: string.Empty,
            ProtocolPackageManifestName: "ProtocolOmegaPackageManifest.json",
            ProtocolPackageManifestHash: "sha256:protocol",
            PrimaryResultCode: VerificationResultCodes.ReleaseIntegrityEvidencePending,
            PrimaryIssue: warning,
            ComponentCount: ElectionSp08ProfileIds.RequiredHighAssuranceComponentIds.Count,
            LifecycleBindingCount: 0,
            EvidenceFileCount: 0,
            MobileEvidenceIncluded: false,
            ReadinessBlockers: []);

        mocker.GetMock<Domain.IElectionLifecycleService>()
            .Setup(x => x.EvaluateOpenReadinessAsync(It.IsAny<Domain.EvaluateElectionOpenReadinessRequest>()))
            .ReturnsAsync(Domain.ElectionOpenValidationResult.Ready(
                [],
                sp08Summary: sp08Summary));

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionOpenReadiness(
            new GetElectionOpenReadinessRequest { ElectionId = electionId.ToString() },
            CreateMockServerCallContext());

        response.IsReadyToOpen.Should().BeTrue();
        response.Sp08ReleaseIntegrity.Should().NotBeNull();
        response.Sp08ReleaseIntegrity.BlocksHighAssurance.Should().BeFalse();
        response.Sp08ReleaseIntegrity.PrimaryResultCode.Should().Be(
            VerificationResultCodes.ReleaseIntegrityEvidencePending);
        response.Sp08ReleaseIntegrity.Message.Should().Be(warning);
    }

    [Fact]
    public async Task GetElectionOpenReadiness_WithOfficialSp08ReadinessSummary_MapsReleaseEvidenceRows()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;
        var component = new ElectionSp08ReleaseComponentArtifactRecord(
            ElectionSp08ProfileIds.ServerComponent,
            "container_image",
            ElectionSp08ProfileIds.EvidenceModeOfficial,
            "hushservernode",
            "sha256:server",
            "0123456789abcdef0123456789abcdef01234567",
            "HushServerNode-v1.2.3",
            "ghcr.io/hushnetwork-social/hushservernode@sha256:server",
            BuildWorkflowRunId: "1234567890",
            DistributionReference: "ghcr.io/hushnetwork-social/hushservernode@sha256:server",
            SigningFingerprint: null,
            IsPlaceholder: false);
        var lifecycleBinding = new ElectionSp08LifecycleReleaseBindingRecord(
            ElectionSp08ProfileIds.OpenLifecycleStage,
            "release-2026.05.11",
            "release-2026.05.11",
            "sha256:server",
            "sha256:server",
            MatchesSealedPolicy: true);
        var sp08Summary = new Domain.ElectionSp08OpenReadinessSummary(
            EvidenceExpected: true,
            EvidenceMode: ElectionSp08ProfileIds.EvidenceModeOfficial,
            NotForReleaseIntegrityClaims: false,
            BlocksHighAssurance: false,
            ReleaseManifestName: ElectionSp08ProfileIds.ReleaseManifestFileName,
            ReleaseManifestHash: "sha256:release",
            ProtocolPackageManifestName: "ProtocolOmegaPackageManifest.json",
            ProtocolPackageManifestHash: "sha256:protocol",
            PrimaryResultCode: VerificationResultCodes.ReleaseIntegrityEvidenceValid,
            PrimaryIssue: "Official SP-08 release-integrity evidence is ready for election open.",
            ComponentCount: 1,
            LifecycleBindingCount: 1,
            EvidenceFileCount: 1,
            MobileEvidenceIncluded: false,
            ReadinessBlockers: [])
        {
            PublicEvidenceAvailable = true,
            Components = [component],
            LifecycleBindings = [lifecycleBinding],
        };

        mocker.GetMock<Domain.IElectionLifecycleService>()
            .Setup(x => x.EvaluateOpenReadinessAsync(It.IsAny<Domain.EvaluateElectionOpenReadinessRequest>()))
            .ReturnsAsync(Domain.ElectionOpenValidationResult.Ready(
                [],
                sp08Summary: sp08Summary));

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionOpenReadiness(
            new GetElectionOpenReadinessRequest { ElectionId = electionId.ToString() },
            CreateMockServerCallContext());

        response.IsReadyToOpen.Should().BeTrue();
        response.Sp08ReleaseIntegrity.PublicEvidenceAvailable.Should().BeTrue();
        response.Sp08ReleaseIntegrity.EvidenceMode.Should().Be(ElectionSp08ProfileIds.EvidenceModeOfficial);
        response.Sp08ReleaseIntegrity.PrimaryResultCode.Should().Be(
            VerificationResultCodes.ReleaseIntegrityEvidenceValid);
        response.Sp08ReleaseIntegrity.Components.Should().ContainSingle(x =>
            x.ComponentId == ElectionSp08ProfileIds.ServerComponent &&
            !x.IsPlaceholder);
        response.Sp08ReleaseIntegrity.LifecycleBindings.Should().ContainSingle(x =>
            x.LifecycleStage == ElectionSp08ProfileIds.OpenLifecycleStage &&
            x.MatchesSealedPolicy);
    }

    [Fact]
    public async Task GetElectionOpenReadiness_WithMissingSp08ReadinessSummary_MapsManifestBlocker()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;
        var sp08Summary = new Domain.ElectionSp08OpenReadinessSummary(
            EvidenceExpected: true,
            EvidenceMode: string.Empty,
            NotForReleaseIntegrityClaims: false,
            BlocksHighAssurance: true,
            ReleaseManifestName: ElectionSp08ProfileIds.ReleaseManifestFileName,
            ReleaseManifestHash: string.Empty,
            ProtocolPackageManifestName: "ProtocolOmegaPackageManifest.json",
            ProtocolPackageManifestHash: "sha256:protocol",
            PrimaryResultCode: VerificationResultCodes.ReleaseIntegrityManifestMissing,
            PrimaryIssue: "Configured SP-08 release manifest was not found.",
            ComponentCount: ElectionSp08ProfileIds.RequiredHighAssuranceComponentIds.Count,
            LifecycleBindingCount: 0,
            EvidenceFileCount: 0,
            MobileEvidenceIncluded: false,
            ReadinessBlockers:
            [
                new Domain.ElectionSp08OpenReadinessBlocker(
                    VerificationResultCodes.ReleaseIntegrityManifestMissing,
                    "Configured SP-08 release manifest was not found.",
                    BlocksOpen: true,
                    BlocksFinalization: true)
            ]);

        mocker.GetMock<Domain.IElectionLifecycleService>()
            .Setup(x => x.EvaluateOpenReadinessAsync(It.IsAny<Domain.EvaluateElectionOpenReadinessRequest>()))
            .ReturnsAsync(Domain.ElectionOpenValidationResult.NotReady(
                ["release_integrity_manifest_missing: Configured SP-08 release manifest was not found."],
                [],
                [],
                sp08Summary: sp08Summary));

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionOpenReadiness(
            new GetElectionOpenReadinessRequest { ElectionId = electionId.ToString() },
            CreateMockServerCallContext());

        response.IsReadyToOpen.Should().BeFalse();
        response.Sp08ReleaseIntegrity.PublicEvidenceAvailable.Should().BeFalse();
        response.Sp08ReleaseIntegrity.BlocksHighAssurance.Should().BeTrue();
        response.Sp08ReleaseIntegrity.PrimaryResultCode.Should().Be(
            VerificationResultCodes.ReleaseIntegrityManifestMissing);
    }

    [Fact]
    public async Task GetElectionOpenReadiness_WithSp08LifecycleMismatch_MapsReleaseBindingRows()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;
        var lifecycleBinding = new ElectionSp08LifecycleReleaseBindingRecord(
            ElectionSp08ProfileIds.CloseLifecycleStage,
            "release-2026.05.11",
            "release-2026.05.12",
            "sha256:expected",
            "sha256:observed",
            MatchesSealedPolicy: false);
        var sp08Summary = new Domain.ElectionSp08OpenReadinessSummary(
            EvidenceExpected: true,
            EvidenceMode: ElectionSp08ProfileIds.EvidenceModeOfficial,
            NotForReleaseIntegrityClaims: false,
            BlocksHighAssurance: true,
            ReleaseManifestName: ElectionSp08ProfileIds.ReleaseManifestFileName,
            ReleaseManifestHash: "sha256:release",
            ProtocolPackageManifestName: "ProtocolOmegaPackageManifest.json",
            ProtocolPackageManifestHash: "sha256:protocol",
            PrimaryResultCode: VerificationResultCodes.ReleaseIntegrityLifecycleMismatch,
            PrimaryIssue: "SP-08 lifecycle release binding does not match the sealed release policy.",
            ComponentCount: ElectionSp08ProfileIds.RequiredHighAssuranceComponentIds.Count,
            LifecycleBindingCount: 1,
            EvidenceFileCount: 1,
            MobileEvidenceIncluded: false,
            ReadinessBlockers:
            [
                new Domain.ElectionSp08OpenReadinessBlocker(
                    VerificationResultCodes.ReleaseIntegrityLifecycleMismatch,
                    "SP-08 lifecycle release binding does not match the sealed release policy.",
                    BlocksOpen: true,
                    BlocksFinalization: true)
            ])
        {
            PublicEvidenceAvailable = true,
            LifecycleBindings = [lifecycleBinding],
        };

        mocker.GetMock<Domain.IElectionLifecycleService>()
            .Setup(x => x.EvaluateOpenReadinessAsync(It.IsAny<Domain.EvaluateElectionOpenReadinessRequest>()))
            .ReturnsAsync(Domain.ElectionOpenValidationResult.NotReady(
                ["release_integrity_lifecycle_mismatch: SP-08 lifecycle release binding does not match the sealed release policy."],
                [],
                [],
                sp08Summary: sp08Summary));

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionOpenReadiness(
            new GetElectionOpenReadinessRequest { ElectionId = electionId.ToString() },
            CreateMockServerCallContext());

        response.IsReadyToOpen.Should().BeFalse();
        response.Sp08ReleaseIntegrity.PrimaryResultCode.Should().Be(
            VerificationResultCodes.ReleaseIntegrityLifecycleMismatch);
        response.Sp08ReleaseIntegrity.LifecycleBindings.Should().ContainSingle(x =>
            x.LifecycleStage == ElectionSp08ProfileIds.CloseLifecycleStage &&
            !x.MatchesSealedPolicy);
    }

    [Theory]
    [InlineData(ProtocolPackageBindingStatus.Stale, ProtocolPackageBindingStatusProto.ProtocolPackageBindingStale)]
    [InlineData(ProtocolPackageBindingStatus.Incompatible, ProtocolPackageBindingStatusProto.ProtocolPackageBindingIncompatible)]
    [InlineData(ProtocolPackageBindingStatus.ReferenceOnly, ProtocolPackageBindingStatusProto.ProtocolPackageBindingReferenceOnly)]
    public async Task GetElectionOpenReadiness_WithProtocolPackageBindingGap_MapsBindingProjection(
        ProtocolPackageBindingStatus bindingStatus,
        ProtocolPackageBindingStatusProto expectedProtoStatus)
    {
        var mocker = new AutoMocker();
        var election = CreateAdminElection();
        var binding = CreateProtocolPackageBinding(election, bindingStatus);

        mocker.GetMock<Domain.IElectionLifecycleService>()
            .Setup(x => x.EvaluateOpenReadinessAsync(It.IsAny<Domain.EvaluateElectionOpenReadinessRequest>()))
            .ReturnsAsync(Domain.ElectionOpenValidationResult.NotReady(
                [$"Protocol Omega package refs are {bindingStatus}."],
                [],
                [],
                protocolPackageValidation: Domain.ProtocolPackageBindingOpenValidation.NotReady(
                    bindingStatus,
                    binding,
                    $"Protocol Omega package refs are {bindingStatus}.")));

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionOpenReadiness(
            new GetElectionOpenReadinessRequest { ElectionId = election.ElectionId.ToString() },
            CreateMockServerCallContext());

        response.IsReadyToOpen.Should().BeFalse();
        response.ProtocolPackageBindingStatus.Should().Be(expectedProtoStatus);
        response.ProtocolPackageBindingMessage.Should().Contain(bindingStatus.ToString());
        response.ProtocolPackageBinding.Should().NotBeNull();
        response.ProtocolPackageBinding!.Status.Should().Be(expectedProtoStatus);
        response.ProtocolPackageBinding.SpecPackageHash.Should().Be(binding.SpecPackageHash);
        response.ProtocolPackageBinding.ProofPackageHash.Should().Be(binding.ProofPackageHash);
        response.ProtocolPackageBinding.ReleaseManifestHash.Should().Be(binding.ReleaseManifestHash);
        response.ProtocolPackageBinding.SpecAccessLocations.Should().ContainSingle();
        response.ProtocolPackageBinding.ExternalReviewStatus.Should().Be(
            ProtocolPackageExternalReviewStatusProto.ProtocolPackageReviewedWithFindings);
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
                TestActorPublicAddress))
            .ReturnsAsync(new GetElectionCeremonyActionViewResponse
            {
                Success = true,
                ActorRole = ElectionCeremonyActorRoleProto.CeremonyActorTrustee,
                ActorPublicAddress = TestActorPublicAddress,
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
            ActorPublicAddress = TestActorPublicAddress,
        }, CreateSignedServerCallContext(
            nameof(ElectionsGrpcService.GetElectionCeremonyActionView),
            TestActorPublicAddress,
            new Dictionary<string, object?>
            {
                ["ElectionId"] = electionId.ToString(),
                ["ActorPublicAddress"] = TestActorPublicAddress,
            }));

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
                TestActorPublicAddress))
            .ReturnsAsync(new GetElectionEligibilityViewResponse
            {
                Success = true,
                ActorRole = ElectionEligibilityActorRoleProto.EligibilityActorLinkedVoter,
                ActorPublicAddress = TestActorPublicAddress,
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
            ActorPublicAddress = TestActorPublicAddress,
        }, CreateSignedServerCallContext(
            nameof(ElectionsGrpcService.GetElectionEligibilityView),
            TestActorPublicAddress,
            new Dictionary<string, object?>
            {
                ["ElectionId"] = electionId.ToString(),
                ["ActorPublicAddress"] = TestActorPublicAddress,
            }));

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
            .Setup(x => x.GetElectionHubViewAsync(TestActorPublicAddress))
            .ReturnsAsync(new GetElectionHubViewResponse
            {
                Success = true,
                ActorPublicAddress = TestActorPublicAddress,
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
            ActorPublicAddress = TestActorPublicAddress,
        }, CreateSignedServerCallContext(
            nameof(ElectionsGrpcService.GetElectionHubView),
            TestActorPublicAddress,
            new Dictionary<string, object?>
            {
                ["ActorPublicAddress"] = TestActorPublicAddress,
            }));

        response.Success.Should().BeTrue();
        response.ActorPublicAddress.Should().Be(TestActorPublicAddress);
        response.HasAnyElectionRoles.Should().BeTrue();
        response.Elections.Should().ContainSingle();
        response.Elections[0].Election.Title.Should().Be("Board Election");
        response.Elections[0].SuggestedAction.Should().Be(ElectionHubNextActionHintProto.ElectionHubActionOwnerManageDraft);
    }

    [Fact]
    public async Task SearchElectionDirectory_WithoutSignedHeaders_RejectsQuery()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var act = async () => await sut.SearchElectionDirectory(new SearchElectionDirectoryRequest
        {
            SearchTerm = "alice",
            Limit = 12,
            ActorPublicAddress = TestActorPublicAddress,
        }, CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        exception.Which.Status.Detail.Should().Contain("requires signed actor-bound headers");
    }

    [Fact]
    public async Task SearchElectionDirectory_WithValidRequest_ReturnsSearchPayload()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.SearchElectionDirectoryAsync(
                "alice",
                It.Is<IReadOnlyCollection<string>>(addresses =>
                    addresses.Count == 1 && addresses.Contains("owner-address")),
                12,
                TestActorPublicAddress))
            .ReturnsAsync(new SearchElectionDirectoryResponse
            {
                Success = true,
                SearchTerm = "alice",
                ActorPublicAddress = TestActorPublicAddress,
                Elections =
                {
                    new ElectionSummary
                    {
                        ElectionId = electionId.ToString(),
                        Title = "Board Election",
                        OwnerPublicAddress = "owner-address",
                    },
                },
                Entries =
                {
                    new SearchElectionDirectoryEntryView
                    {
                        Election = new ElectionSummary
                        {
                            ElectionId = electionId.ToString(),
                            Title = "Board Election",
                            OwnerPublicAddress = "owner-address",
                        },
                        ActorRoles = new ElectionApplicationRoleFlagsView
                        {
                            IsVoter = true,
                        },
                        CanOpenEligibility = false,
                        EligibilityDisabledReason = "This election is already linked to this Hush account.",
                    },
                },
            });

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.SearchElectionDirectory(new SearchElectionDirectoryRequest
        {
            SearchTerm = "alice",
            Limit = 12,
            OwnerPublicAddresses = { "owner-address" },
            ActorPublicAddress = TestActorPublicAddress,
        }, CreateSignedServerCallContext(
            nameof(ElectionsGrpcService.SearchElectionDirectory),
            TestActorPublicAddress,
            new Dictionary<string, object?>
            {
                ["SearchTerm"] = "alice",
                ["OwnerPublicAddresses"] = new[] { "owner-address" },
                ["Limit"] = 12,
                ["ActorPublicAddress"] = TestActorPublicAddress,
            }));

        response.Success.Should().BeTrue();
        response.SearchTerm.Should().Be("alice");
        response.ActorPublicAddress.Should().Be(TestActorPublicAddress);
        response.Elections.Should().ContainSingle();
        response.Elections[0].ElectionId.Should().Be(electionId.ToString());
        response.Elections[0].Title.Should().Be("Board Election");
        response.Entries.Should().ContainSingle();
        response.Entries[0].ActorRoles.IsVoter.Should().BeTrue();
        response.Entries[0].CanOpenEligibility.Should().BeFalse();
    }

    [Fact]
    public async Task GetElection_WithoutSignedHeaders_AllowsUnsignedRead()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionAsync(electionId, null))
            .ReturnsAsync(new GetElectionResponse
            {
                Success = true,
            });

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElection(new GetElectionRequest
        {
            ElectionId = electionId.ToString(),
        }, CreateMockServerCallContext());

        response.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetElection_WithValidSignedHeaders_PassesResolvedActorToQueryService()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionAsync(electionId, TestActorPublicAddress))
            .ReturnsAsync(new GetElectionResponse
            {
                Success = true,
            });

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElection(new GetElectionRequest
        {
            ElectionId = electionId.ToString(),
        }, CreateSignedServerCallContext(
            nameof(ElectionsGrpcService.GetElection),
            TestActorPublicAddress,
            new Dictionary<string, object?>
            {
                ["ElectionId"] = electionId.ToString(),
            }));

        response.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetElection_WithPartialSignedHeaders_RejectsQuery()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var electionId = ElectionId.NewElectionId;
        var requestHeaders = new Metadata
        {
            { "x-hush-election-query-signatory", TestActorPublicAddress },
        };

        var act = async () => await sut.GetElection(new GetElectionRequest
        {
            ElectionId = electionId.ToString(),
        }, new TestServerCallContext(requestHeaders));

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        exception.Which.Status.Detail.Should().Contain("requires signed actor-bound headers");
    }

    [Fact]
    public async Task GetElectionVotingView_WithValidRequest_ReturnsVotingPayload()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionVotingViewAsync(
                electionId,
                TestActorPublicAddress,
                "cast-key-1"))
            .ReturnsAsync(new GetElectionVotingViewResponse
            {
                Success = true,
                ActorPublicAddress = TestActorPublicAddress,
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
            ActorPublicAddress = TestActorPublicAddress,
            SubmissionIdempotencyKey = "cast-key-1",
        }, CreateSignedServerCallContext(
            nameof(ElectionsGrpcService.GetElectionVotingView),
            TestActorPublicAddress,
            new Dictionary<string, object?>
            {
                ["ElectionId"] = electionId.ToString(),
                ["ActorPublicAddress"] = TestActorPublicAddress,
                ["SubmissionIdempotencyKey"] = "cast-key-1",
            }));

        response.Success.Should().BeTrue();
        response.ActorPublicAddress.Should().Be(TestActorPublicAddress);
        response.CommitmentRegistered.Should().BeTrue();
        response.PersonalParticipationStatus.Should().Be(ElectionParticipationStatusProto.ParticipationCountedAsVoted);
        response.SubmissionStatus.Should().Be(ElectionVotingSubmissionStatusProto.VotingSubmissionStatusAlreadyUsed);
        response.DkgProfileId.Should().Be("dkg-prod-1of1");
        response.TallyPublicKeyFingerprint.Should().Be("tally-fingerprint");
    }

    [Fact]
    public async Task VerifyElectionReceipt_WithValidRequest_ReturnsVerificationPayload()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.VerifyElectionReceiptAsync(
                electionId,
                TestActorPublicAddress,
                "receipt-1",
                "acceptance-1",
                "proof-1",
                string.Empty,
                string.Empty))
            .ReturnsAsync(new VerifyElectionReceiptResponse
            {
                Success = true,
                ActorPublicAddress = TestActorPublicAddress,
                ElectionId = electionId.ToString(),
                LifecycleState = ElectionLifecycleStateProto.Open,
                HasAcceptedCheckoff = true,
                ReceiptMatchesAcceptedCheckoff = true,
                ParticipationCountedAsVoted = true,
                TallyVerificationAvailable = false,
                VerifiedReceiptId = "receipt-1",
                VerifiedAcceptanceId = "acceptance-1",
                VerifiedServerProof = "proof-1",
            });

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.VerifyElectionReceipt(new VerifyElectionReceiptRequest
        {
            ElectionId = electionId.ToString(),
            ActorPublicAddress = TestActorPublicAddress,
            ReceiptId = "receipt-1",
            AcceptanceId = "acceptance-1",
            ServerProof = "proof-1",
        }, CreateSignedServerCallContext(
            nameof(ElectionsGrpcService.VerifyElectionReceipt),
            TestActorPublicAddress,
            new Dictionary<string, object?>
            {
                ["ElectionId"] = electionId.ToString(),
                ["ActorPublicAddress"] = TestActorPublicAddress,
                ["ReceiptId"] = "receipt-1",
                ["AcceptanceId"] = "acceptance-1",
                ["ServerProof"] = "proof-1",
                ["ReceiptCommitment"] = string.Empty,
                ["PreparedBallotId"] = string.Empty,
            }));

        response.Success.Should().BeTrue();
        response.ActorPublicAddress.Should().Be(TestActorPublicAddress);
        response.ElectionId.Should().Be(electionId.ToString());
        response.HasAcceptedCheckoff.Should().BeTrue();
        response.ReceiptMatchesAcceptedCheckoff.Should().BeTrue();
        response.ParticipationCountedAsVoted.Should().BeTrue();
        response.VerifiedReceiptId.Should().Be("receipt-1");
        response.VerifiedAcceptanceId.Should().Be("acceptance-1");
        response.VerifiedServerProof.Should().Be("proof-1");
    }

    [Fact]
    public async Task GetElectionEnvelopeAccess_WithoutSignedHeaders_RejectsQuery()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var electionId = ElectionId.NewElectionId;

        var act = async () => await sut.GetElectionEnvelopeAccess(new GetElectionEnvelopeAccessRequest
        {
            ElectionId = electionId.ToString(),
            ActorPublicAddress = TestActorPublicAddress,
        }, CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        exception.Which.Status.Detail.Should().Contain("requires signed actor-bound headers");
    }

    [Fact]
    public async Task GetElectionEnvelopeAccess_WithValidRequest_ReturnsEnvelopeAccessPayload()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionEnvelopeAccessAsync(electionId, TestActorPublicAddress))
            .ReturnsAsync(new GetElectionEnvelopeAccessResponse
            {
                Success = true,
                ActorEncryptedElectionPrivateKey = "actor-private-key-wrap",
            });

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionEnvelopeAccess(new GetElectionEnvelopeAccessRequest
        {
            ElectionId = electionId.ToString(),
            ActorPublicAddress = TestActorPublicAddress,
        }, CreateSignedServerCallContext(
            nameof(ElectionsGrpcService.GetElectionEnvelopeAccess),
            TestActorPublicAddress,
            new Dictionary<string, object?>
            {
                ["ElectionId"] = electionId.ToString(),
                ["ActorPublicAddress"] = TestActorPublicAddress,
            }));

        response.Success.Should().BeTrue();
        response.ActorEncryptedElectionPrivateKey.Should().Be("actor-private-key-wrap");
    }

    [Fact]
    public async Task GetElectionAnomalyOwnThread_WithoutSignedHeaders_RejectsQuery()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var electionId = ElectionId.NewElectionId;

        var act = async () => await sut.GetElectionAnomalyOwnThread(new GetElectionAnomalyOwnThreadRequest
        {
            ElectionId = electionId.ToString(),
            ActorPublicAddress = TestActorPublicAddress,
        }, CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        exception.Which.Status.Detail.Should().Contain("requires signed actor-bound headers");
    }

    [Fact]
    public async Task GetElectionAnomalyOwnThread_WithNoThread_ReturnsSuccessfulEmptyProjection()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionAnomalyOwnThreadAsync(electionId, TestActorPublicAddress))
            .ReturnsAsync((ElectionAnomalyOwnThreadProjection?)null);

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionAnomalyOwnThread(new GetElectionAnomalyOwnThreadRequest
        {
            ElectionId = electionId.ToString(),
            ActorPublicAddress = TestActorPublicAddress,
        }, CreateSignedServerCallContext(
            nameof(ElectionsGrpcService.GetElectionAnomalyOwnThread),
            TestActorPublicAddress,
            new Dictionary<string, object?>
            {
                ["ElectionId"] = electionId.ToString(),
                ["ActorPublicAddress"] = TestActorPublicAddress,
            }));

        response.Success.Should().BeTrue();
        response.ActorPublicAddress.Should().Be(TestActorPublicAddress);
        response.HasThread.Should().BeFalse();
        response.Thread.Should().BeNull();
    }

    [Fact]
    public async Task GetElectionAnomalyOwnThread_WithValidRequest_ReturnsOwnThreadPayload()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;
        var threadId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var clarificationRequestId = Guid.NewGuid();
        var recordedAt = DateTime.UtcNow.AddMinutes(-3);

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionAnomalyOwnThreadAsync(electionId, TestActorPublicAddress))
            .ReturnsAsync(new ElectionAnomalyOwnThreadProjection(
                threadId,
                electionId,
                ElectionAnomalyCategoryIds.BallotCastingOrReceiptAnomaly,
                ElectionAnomalyCaseStateIds.AuthorityRequestedInformation,
                "sha256:thread",
                SeverityCandidateId: null,
                GovernedDecisionRef: null,
                HasOpenClarificationRequest: true,
                DateTime.UtcNow.AddMinutes(-5),
                DateTime.UtcNow.AddMinutes(-1),
                [
                    new ElectionAnomalyEncryptedMessageProjection(
                        messageId,
                        ElectionAnomalyMessageKindIds.AuthorityInformationRequest,
                        recordedAt,
                        "encrypted-body",
                        "sha256:body",
                        PlaintextCharacterCount: 22,
                        [
                            new ElectionAnomalyRecipientWrapProjection(
                                ElectionAnomalyRecipientRoleIds.Submitter,
                                ElectionAnomalyRecipientWrapStatusIds.Available,
                                TestActorPublicAddress,
                                "submitter-key",
                                "submitter-encrypted-content-key",
                                "x25519-aes-gcm"),
                            new ElectionAnomalyRecipientWrapProjection(
                                ElectionAnomalyRecipientRoleIds.ElectionOwner,
                                ElectionAnomalyRecipientWrapStatusIds.Available,
                                "owner-address",
                                "owner-key"),
                        ],
                        clarificationRequestId),
                ]));

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionAnomalyOwnThread(new GetElectionAnomalyOwnThreadRequest
        {
            ElectionId = electionId.ToString(),
            ActorPublicAddress = TestActorPublicAddress,
        }, CreateSignedServerCallContext(
            nameof(ElectionsGrpcService.GetElectionAnomalyOwnThread),
            TestActorPublicAddress,
            new Dictionary<string, object?>
            {
                ["ElectionId"] = electionId.ToString(),
                ["ActorPublicAddress"] = TestActorPublicAddress,
            }));

        response.Success.Should().BeTrue();
        response.HasThread.Should().BeTrue();
        response.Thread.AnomalyThreadId.Should().Be(threadId.ToString());
        response.Thread.CategoryId.Should().Be(ElectionAnomalyCategoryIds.BallotCastingOrReceiptAnomaly);
        response.Thread.HasOpenClarificationRequest.Should().BeTrue();
        response.Thread.Messages.Should().ContainSingle();
        response.Thread.Messages[0].MessageId.Should().Be(messageId.ToString());
        response.Thread.Messages[0].HasClarificationRequest.Should().BeTrue();
        response.Thread.Messages[0].ClarificationRequestId.Should().Be(clarificationRequestId.ToString());
        response.Thread.Messages[0].RecipientWraps.Should().HaveCount(2);
        response.Thread.Messages[0].RecipientWraps
            .Single(x => x.RecipientRoleId == ElectionAnomalyRecipientRoleIds.Submitter)
            .EncryptedContentKey.Should().Be("submitter-encrypted-content-key");
        response.Thread.Messages[0].RecipientWraps
            .Single(x => x.RecipientRoleId == ElectionAnomalyRecipientRoleIds.ElectionOwner)
            .EncryptedContentKey.Should().BeEmpty();
    }

    [Fact]
    public async Task GetElectionAnomalyTrusteeCounts_WithoutSignedHeaders_RejectsQuery()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var electionId = ElectionId.NewElectionId;

        var act = async () => await sut.GetElectionAnomalyTrusteeCounts(new GetElectionAnomalyTrusteeCountsRequest
        {
            ElectionId = electionId.ToString(),
            ActorPublicAddress = TestActorPublicAddress,
        }, CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        exception.Which.Status.Detail.Should().Contain("requires signed actor-bound headers");
    }

    [Fact]
    public async Task GetElectionAnomalyTrusteeCounts_WithUnavailableProjection_ReturnsDeniedPayload()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionAnomalyTrusteeCountsAsync(electionId, TestActorPublicAddress))
            .ReturnsAsync((ElectionAnomalyTrusteeCountsProjection?)null);

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionAnomalyTrusteeCounts(new GetElectionAnomalyTrusteeCountsRequest
        {
            ElectionId = electionId.ToString(),
            ActorPublicAddress = TestActorPublicAddress,
        }, CreateSignedServerCallContext(
            nameof(ElectionsGrpcService.GetElectionAnomalyTrusteeCounts),
            TestActorPublicAddress,
            new Dictionary<string, object?>
            {
                ["ElectionId"] = electionId.ToString(),
                ["ActorPublicAddress"] = TestActorPublicAddress,
            }));

        response.Success.Should().BeFalse();
        response.ActorPublicAddress.Should().Be(TestActorPublicAddress);
        response.HasCounts.Should().BeFalse();
        response.ErrorMessage.Should().Contain("unavailable");
        response.Counts.Should().BeNull();
    }

    [Fact]
    public async Task GetElectionAnomalyTrusteeCounts_WithValidRequest_ReturnsBodyFreeCounts()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionAnomalyTrusteeCountsAsync(electionId, TestActorPublicAddress))
            .ReturnsAsync(new ElectionAnomalyTrusteeCountsProjection(
                electionId,
                TotalThreadCount: 2,
                CategoryCounts:
                [
                    new ElectionAnomalyCategoryCountProjection(
                        ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern,
                        Count: 1),
                    new ElectionAnomalyCategoryCountProjection(
                        ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly,
                        Count: 1),
                ],
                CaseStateCounts:
                [
                    new ElectionAnomalyCaseStateCountProjection(
                        ElectionAnomalyCaseStateIds.Submitted,
                        Count: 1),
                    new ElectionAnomalyCaseStateCountProjection(
                        ElectionAnomalyCaseStateIds.AuthorityRequestedInformation,
                        Count: 1),
                ],
                ContinuitySummary: new ElectionAnomalyTrusteeContinuitySummaryProjection(
                    TrusteeContinuityThreadCount: 1,
                    OpenContinuityThreadCount: 1,
                    AwaitingInformationContinuityThreadCount: 1,
                    ClosedContinuityThreadCount: 0,
                    GovernedDecisionLinkedCount: 0,
                    HasContinuityIssue: true)));

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionAnomalyTrusteeCounts(new GetElectionAnomalyTrusteeCountsRequest
        {
            ElectionId = electionId.ToString(),
            ActorPublicAddress = TestActorPublicAddress,
        }, CreateSignedServerCallContext(
            nameof(ElectionsGrpcService.GetElectionAnomalyTrusteeCounts),
            TestActorPublicAddress,
            new Dictionary<string, object?>
            {
                ["ElectionId"] = electionId.ToString(),
                ["ActorPublicAddress"] = TestActorPublicAddress,
            }));

        response.Success.Should().BeTrue();
        response.HasCounts.Should().BeTrue();
        response.Counts.ElectionId.Should().Be(electionId.ToString());
        response.Counts.TotalThreadCount.Should().Be(2);
        response.Counts.CategoryCounts.Select(x => x.CategoryId)
            .Should()
            .Contain([ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern, ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly]);
        response.Counts.ContinuitySummary.HasContinuityIssue.Should().BeTrue();
        response.Counts.ContinuitySummary.AwaitingInformationContinuityThreadCount.Should().Be(1);
    }

    [Fact]
    public async Task GetElectionAnomalyOwnerTriage_WithoutSignedHeaders_RejectsQuery()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var electionId = ElectionId.NewElectionId;

        var act = async () => await sut.GetElectionAnomalyOwnerTriage(
            new GetElectionAnomalyOwnerTriageRequest
            {
                ElectionId = electionId.ToString(),
                ActorPublicAddress = TestActorPublicAddress,
            },
            CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        exception.Which.Status.Detail.Should().Contain("requires signed actor-bound headers");
    }

    [Fact]
    public async Task GetElectionAnomalyOwnerTriage_WithUnavailableProjection_ReturnsDeniedPayload()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionAnomalyOwnerTriageAsync(electionId, TestActorPublicAddress))
            .ReturnsAsync((ElectionAnomalyOwnerTriageProjection?)null);

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionAnomalyOwnerTriage(
            new GetElectionAnomalyOwnerTriageRequest
            {
                ElectionId = electionId.ToString(),
                ActorPublicAddress = TestActorPublicAddress,
            },
            CreateSignedServerCallContext(
                nameof(ElectionsGrpcService.GetElectionAnomalyOwnerTriage),
                TestActorPublicAddress,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = electionId.ToString(),
                    ["ActorPublicAddress"] = TestActorPublicAddress,
                }));

        response.Success.Should().BeFalse();
        response.ActorPublicAddress.Should().Be(TestActorPublicAddress);
        response.HasTriage.Should().BeFalse();
        response.ErrorMessage.Should().Contain("unavailable");
        response.Triage.Should().BeNull();
    }

    [Fact]
    public async Task GetElectionAnomalyOwnerTriage_WithValidRequest_ReturnsIdentityVisibleCallerWrap()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;
        var threadId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var recordedAt = DateTime.UtcNow.AddMinutes(-2);

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionAnomalyOwnerTriageAsync(electionId, TestActorPublicAddress))
            .ReturnsAsync(new ElectionAnomalyOwnerTriageProjection(
                electionId,
                TotalThreadCount: 1,
                OpenThreadCount: 1,
                AwaitingInformationThreadCount: 1,
                ResponsePresentThreadCount: 0,
                ExternalClaimantThreadCount: 0,
                DecryptableMessageCount: 1,
                PendingRewrapMessageCount: 1,
                MissingOwnerWrapMessageCount: 0,
                AttachmentManifestCount: 0,
                GovernedContinuityHandoffStatusId: "governed_path_unavailable",
                CategoryCounts:
                [
                    new ElectionAnomalyCategoryCountProjection(
                        ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly,
                        Count: 1),
                ],
                CaseStateCounts:
                [
                    new ElectionAnomalyCaseStateCountProjection(
                        ElectionAnomalyCaseStateIds.AuthorityRequestedInformation,
                        Count: 1),
                ],
                ContinuitySummary: new ElectionAnomalyTrusteeContinuitySummaryProjection(
                    TrusteeContinuityThreadCount: 1,
                    OpenContinuityThreadCount: 1,
                    AwaitingInformationContinuityThreadCount: 1,
                    ClosedContinuityThreadCount: 0,
                    GovernedDecisionLinkedCount: 0,
                    HasContinuityIssue: true),
                Threads:
                [
                    new ElectionAnomalyOwnerTriageThreadProjection(
                        threadId,
                        electionId,
                        ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly,
                        ElectionAnomalyCaseStateIds.AuthorityRequestedInformation,
                        "sha256:thread",
                        ElectionAnomalySeverityCandidateIds.RequiresAuthorityReview,
                        GovernedDecisionRef: null,
                        SubmitterActorPublicAddress: "submitter-address",
                        SubmitterRoleContextId: ElectionAnomalyActorRoleContextIds.Trustee,
                        LifecycleStateAtSubmission: ElectionLifecycleState.Draft,
                        HasOpenClarificationRequest: true,
                        OpenClarificationRequestId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                        CreatedAtUtc: DateTime.UtcNow.AddMinutes(-10),
                        UpdatedAtUtc: DateTime.UtcNow.AddMinutes(-1),
                        Messages:
                        [
                            new ElectionAnomalyOwnerMessageProjection(
                                messageId,
                                ElectionAnomalyMessageKindIds.InitialSubmission,
                                recordedAt,
                                "encrypted-body",
                                "sha256:body",
                                PlaintextCharacterCount: 31,
                                [
                                    new ElectionAnomalyRestrictedRecipientWrapProjection(
                                        ElectionAnomalyRecipientRoleIds.Submitter,
                                        ElectionAnomalyRecipientWrapStatusIds.Available),
                                    new ElectionAnomalyRestrictedRecipientWrapProjection(
                                        ElectionAnomalyRecipientRoleIds.DesignatedAuditor,
                                        ElectionAnomalyRecipientWrapStatusIds.PendingBackfill),
                                ],
                                new ElectionAnomalyOwnerCallerWrapProjection(
                                    ElectionAnomalyRecipientWrapStatusIds.Available,
                                    "owner-key-fingerprint",
                                    "owner-encrypted-content-key",
                                    "x25519-aes-gcm")),
                        ]),
                ]));

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionAnomalyOwnerTriage(
            new GetElectionAnomalyOwnerTriageRequest
            {
                ElectionId = electionId.ToString(),
                ActorPublicAddress = TestActorPublicAddress,
            },
            CreateSignedServerCallContext(
                nameof(ElectionsGrpcService.GetElectionAnomalyOwnerTriage),
                TestActorPublicAddress,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = electionId.ToString(),
                    ["ActorPublicAddress"] = TestActorPublicAddress,
                }));

        response.Success.Should().BeTrue();
        response.HasTriage.Should().BeTrue();
        response.Triage.ElectionId.Should().Be(electionId.ToString());
        response.Triage.TotalThreadCount.Should().Be(1);
        response.Triage.PendingRewrapMessageCount.Should().Be(1);
        response.Triage.GovernedContinuityHandoffStatusId.Should().Be("governed_path_unavailable");
        response.Triage.Threads.Should().ContainSingle();
        response.Triage.Threads[0].SubmitterActorPublicAddress.Should().Be("submitter-address");
        response.Triage.Threads[0].SubmitterRoleContextId.Should().Be(ElectionAnomalyActorRoleContextIds.Trustee);
        response.Triage.Threads[0].HasOpenClarificationRequestId.Should().BeTrue();
        response.Triage.Threads[0].Messages[0].HasCallerOwnerWrap.Should().BeTrue();
        response.Triage.Threads[0].Messages[0].CallerOwnerWrap.EncryptedContentKey
            .Should()
            .Be("owner-encrypted-content-key");
        typeof(ElectionAnomalyOwnerTriageThreadView).GetProperties()
            .Should()
            .NotContain(property => property.Name.Contains("PersonScope", StringComparison.OrdinalIgnoreCase));
        typeof(ElectionAnomalyOwnerCallerWrapView).GetProperties()
            .Should()
            .NotContain(property => property.Name.Contains("PublicAddress", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetElectionAnomalyAuditorRestrictedReview_WithoutSignedHeaders_RejectsQuery()
    {
        var mocker = new AutoMocker();
        var sut = mocker.CreateInstance<ElectionsGrpcService>();
        var electionId = ElectionId.NewElectionId;

        var act = async () => await sut.GetElectionAnomalyAuditorRestrictedReview(
            new GetElectionAnomalyAuditorRestrictedReviewRequest
            {
                ElectionId = electionId.ToString(),
                ActorPublicAddress = TestActorPublicAddress,
            },
            CreateMockServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        exception.Which.Status.Detail.Should().Contain("requires signed actor-bound headers");
    }

    [Fact]
    public async Task GetElectionAnomalyAuditorRestrictedReview_WithUnavailableProjection_ReturnsDeniedPayload()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionAnomalyAuditorRestrictedReviewAsync(electionId, TestActorPublicAddress))
            .ReturnsAsync((ElectionAnomalyAuditorRestrictedReviewProjection?)null);

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionAnomalyAuditorRestrictedReview(
            new GetElectionAnomalyAuditorRestrictedReviewRequest
            {
                ElectionId = electionId.ToString(),
                ActorPublicAddress = TestActorPublicAddress,
            },
            CreateSignedServerCallContext(
                nameof(ElectionsGrpcService.GetElectionAnomalyAuditorRestrictedReview),
                TestActorPublicAddress,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = electionId.ToString(),
                    ["ActorPublicAddress"] = TestActorPublicAddress,
                }));

        response.Success.Should().BeFalse();
        response.ActorPublicAddress.Should().Be(TestActorPublicAddress);
        response.HasReview.Should().BeFalse();
        response.ErrorMessage.Should().Contain("unavailable");
        response.Review.Should().BeNull();
    }

    [Fact]
    public async Task GetElectionAnomalyAuditorRestrictedReview_WithValidRequest_ReturnsIdentitySafeCallerWrap()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;
        var threadId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var recordedAt = DateTime.UtcNow.AddMinutes(-2);

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionAnomalyAuditorRestrictedReviewAsync(electionId, TestActorPublicAddress))
            .ReturnsAsync(new ElectionAnomalyAuditorRestrictedReviewProjection(
                electionId,
                [
                    new ElectionAnomalyAuditorRestrictedThreadProjection(
                        threadId,
                        electionId,
                        ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern,
                        ElectionAnomalyCaseStateIds.UnderReview,
                        "sha256:thread",
                        SeverityCandidateId: null,
                        GovernedDecisionRef: "proposal-1",
                        HasOpenClarificationRequest: false,
                        DateTime.UtcNow.AddMinutes(-10),
                        DateTime.UtcNow.AddMinutes(-1),
                        [
                            new ElectionAnomalyRestrictedMessageProjection(
                                messageId,
                                ElectionAnomalyMessageKindIds.InitialSubmission,
                                recordedAt,
                                "encrypted-body",
                                "sha256:body",
                                PlaintextCharacterCount: 31,
                                [
                                    new ElectionAnomalyRestrictedRecipientWrapProjection(
                                        ElectionAnomalyRecipientRoleIds.Submitter,
                                        ElectionAnomalyRecipientWrapStatusIds.Available),
                                    new ElectionAnomalyRestrictedRecipientWrapProjection(
                                        ElectionAnomalyRecipientRoleIds.DesignatedAuditor,
                                        ElectionAnomalyRecipientWrapStatusIds.Available),
                                ],
                                new ElectionAnomalyAuditorCallerWrapProjection(
                                    ElectionAnomalyRecipientWrapStatusIds.Available,
                                    "auditor-key-fingerprint",
                                    "auditor-encrypted-content-key",
                                    "x25519-aes-gcm")),
                        ]),
                ]));

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionAnomalyAuditorRestrictedReview(
            new GetElectionAnomalyAuditorRestrictedReviewRequest
            {
                ElectionId = electionId.ToString(),
                ActorPublicAddress = TestActorPublicAddress,
            },
            CreateSignedServerCallContext(
                nameof(ElectionsGrpcService.GetElectionAnomalyAuditorRestrictedReview),
                TestActorPublicAddress,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = electionId.ToString(),
                    ["ActorPublicAddress"] = TestActorPublicAddress,
                }));

        response.Success.Should().BeTrue();
        response.HasReview.Should().BeTrue();
        response.Review.ElectionId.Should().Be(electionId.ToString());
        response.Review.TotalThreadCount.Should().Be(1);
        response.Review.DecryptableMessageCount.Should().Be(1);
        response.Review.PendingRewrapMessageCount.Should().Be(0);
        response.Review.MissingWrapMessageCount.Should().Be(0);
        response.Review.Threads.Should().ContainSingle();
        response.Review.Threads[0].Messages.Should().ContainSingle();
        response.Review.Threads[0].Messages[0].MessageId.Should().Be(messageId.ToString());
        response.Review.Threads[0].Messages[0].RecipientStatuses.Should().HaveCount(2);
        response.Review.Threads[0].Messages[0].HasCallerAuditorWrap.Should().BeTrue();
        response.Review.Threads[0].Messages[0].CallerAuditorWrap.EncryptedContentKey
            .Should()
            .Be("auditor-encrypted-content-key");
        typeof(ElectionAnomalyRestrictedRecipientStatusView).GetProperties()
            .Should()
            .NotContain(property => property.Name.Contains("PublicAddress", StringComparison.OrdinalIgnoreCase));
        typeof(ElectionAnomalyAuditorCallerWrapView).GetProperties()
            .Should()
            .NotContain(property => property.Name.Contains("PublicAddress", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetElectionResultView_WithValidRequest_ReturnsResultPayload()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionResultViewAsync(electionId, TestActorPublicAddress))
            .ReturnsAsync(new GetElectionResultViewResponse
            {
                Success = true,
                ActorPublicAddress = TestActorPublicAddress,
                CanViewParticipantEncryptedResults = true,
                CanViewReportPackage = false,
            });

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionResultView(new GetElectionResultViewRequest
        {
            ElectionId = electionId.ToString(),
            ActorPublicAddress = TestActorPublicAddress,
        }, CreateSignedServerCallContext(
            nameof(ElectionsGrpcService.GetElectionResultView),
            TestActorPublicAddress,
            new Dictionary<string, object?>
            {
                ["ElectionId"] = electionId.ToString(),
                ["ActorPublicAddress"] = TestActorPublicAddress,
            }));

        response.Success.Should().BeTrue();
        response.ActorPublicAddress.Should().Be(TestActorPublicAddress);
        response.CanViewParticipantEncryptedResults.Should().BeTrue();
        response.CanViewReportPackage.Should().BeFalse();
    }

    [Fact]
    public async Task GetElectionVerificationPackageStatus_WithValidRequest_ReturnsStatusPayload()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionVerificationPackageStatusAsync(electionId, TestActorPublicAddress))
            .ReturnsAsync(new GetElectionVerificationPackageStatusResponse
            {
                Success = true,
                ElectionId = electionId.ToString(),
                ActorPublicAddress = TestActorPublicAddress,
                Status = new ElectionVerificationPackageStatusView
                {
                    ElectionId = electionId.ToString(),
                    ActorPublicAddress = TestActorPublicAddress,
                    IsVisible = true,
                    Status = ElectionVerificationPackageStatusProto.VerificationPackageReady,
                },
            });

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionVerificationPackageStatus(new GetElectionVerificationPackageStatusRequest
        {
            ElectionId = electionId.ToString(),
            ActorPublicAddress = TestActorPublicAddress,
        }, CreateSignedServerCallContext(
            nameof(ElectionsGrpcService.GetElectionVerificationPackageStatus),
            TestActorPublicAddress,
            new Dictionary<string, object?>
            {
                ["ElectionId"] = electionId.ToString(),
                ["ActorPublicAddress"] = TestActorPublicAddress,
            }));

        response.Success.Should().BeTrue();
        response.Status.Should().NotBeNull();
        response.Status.Status.Should().Be(ElectionVerificationPackageStatusProto.VerificationPackageReady);
    }

    [Fact]
    public async Task ExportElectionVerificationPackage_WithValidRequest_ReturnsExportPayload()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.ExportElectionVerificationPackageAsync(
                electionId,
                TestActorPublicAddress,
                ElectionVerificationPackageViewProto.VerificationPackagePublicAnonymous))
            .ReturnsAsync(new ExportElectionVerificationPackageResponse
            {
                Success = true,
                ElectionId = electionId.ToString(),
                ActorPublicAddress = TestActorPublicAddress,
                PackageView = ElectionVerificationPackageViewProto.VerificationPackagePublicAnonymous,
                PackageId = $"HushElectionPackage-{electionId}",
                PackageHash = new string('a', 64),
                Files =
                {
                    new ElectionVerificationPackageFileView
                    {
                        RelativePath = "ElectionRecord.json",
                        MediaType = "application/json",
                        Visibility = ElectionVerificationArtifactVisibilityProto.VerificationArtifactPublic,
                        Content = ByteString.CopyFromUtf8("{}"),
                    },
                },
            });

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.ExportElectionVerificationPackage(new ExportElectionVerificationPackageRequest
        {
            ElectionId = electionId.ToString(),
            ActorPublicAddress = TestActorPublicAddress,
            PackageView = ElectionVerificationPackageViewProto.VerificationPackagePublicAnonymous,
        }, CreateSignedServerCallContext(
            nameof(ElectionsGrpcService.ExportElectionVerificationPackage),
            TestActorPublicAddress,
            new Dictionary<string, object?>
            {
                ["ElectionId"] = electionId.ToString(),
                ["ActorPublicAddress"] = TestActorPublicAddress,
                ["PackageView"] = ElectionVerificationPackageViewProto.VerificationPackagePublicAnonymous,
            }));

        response.Success.Should().BeTrue();
        response.PackageHash.Should().Be(new string('a', 64));
        response.Files.Should().ContainSingle();
        response.Files[0].RelativePath.Should().Be("ElectionRecord.json");
    }

    [Fact]
    public async Task GetElectionReportAccessGrants_WithValidRequest_ReturnsGrantPayload()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionReportAccessGrantsAsync(electionId, TestActorPublicAddress))
            .ReturnsAsync(new GetElectionReportAccessGrantsResponse
            {
                Success = true,
                ActorPublicAddress = TestActorPublicAddress,
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
            ActorPublicAddress = TestActorPublicAddress,
        }, CreateSignedServerCallContext(
            nameof(ElectionsGrpcService.GetElectionReportAccessGrants),
            TestActorPublicAddress,
            new Dictionary<string, object?>
            {
                ["ElectionId"] = electionId.ToString(),
                ["ActorPublicAddress"] = TestActorPublicAddress,
            }));

        response.Success.Should().BeTrue();
        response.CanManageGrants.Should().BeTrue();
        response.Grants.Should().ContainSingle();
        response.Grants[0].ActorPublicAddress.Should().Be("auditor-address");
    }

    [Fact]
    public async Task GetElectionsByOwner_WithValidRequest_ReturnsOwnerScopedPayload()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        mocker.GetMock<IElectionQueryApplicationService>()
            .Setup(x => x.GetElectionsByOwnerAsync(TestActorPublicAddress))
            .ReturnsAsync(new GetElectionsByOwnerResponse
            {
                Elections =
                {
                    new ElectionSummary
                    {
                        ElectionId = electionId.ToString(),
                        Title = "Owned Election",
                    },
                },
            });

        var sut = mocker.CreateInstance<ElectionsGrpcService>();

        var response = await sut.GetElectionsByOwner(new GetElectionsByOwnerRequest
        {
            OwnerPublicAddress = TestActorPublicAddress,
        }, CreateSignedServerCallContext(
            nameof(ElectionsGrpcService.GetElectionsByOwner),
            TestActorPublicAddress,
            new Dictionary<string, object?>
            {
                ["OwnerPublicAddress"] = TestActorPublicAddress,
            }));

        response.Elections.Should().ContainSingle();
        response.Elections[0].Title.Should().Be("Owned Election");
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

    private static ProtocolPackageBindingRecord CreateProtocolPackageBinding(
        ElectionRecord election,
        ProtocolPackageBindingStatus status)
    {
        var catalogEntry = ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            packageId: "omega-hushvoting-v1",
            packageVersion: "v1.0.0",
            specPackageHash: Hash('a'),
            proofPackageHash: Hash('b'),
            releaseManifestHash: Hash('c'),
            compatibleProfileIds:
            [
                election.SelectedProfileId,
            ],
            approvalStatus: ProtocolPackageApprovalStatus.ApprovedInternal,
            isLatestForCompatibleProfiles: true,
            specAccessLocations:
            [
                CreateProtocolPackageAccessLocation(Hash('d')),
            ],
            proofAccessLocations:
            [
                CreateProtocolPackageAccessLocation(Hash('e')),
            ],
            externalReviewStatus: ProtocolPackageExternalReviewStatus.ReviewedWithFindings,
            approvedAt: DateTime.UtcNow.AddMinutes(-5));

        if (status == ProtocolPackageBindingStatus.ReferenceOnly)
        {
            return ElectionModelFactory.CreateMigrationBackfillProtocolPackageBinding(
                election.ElectionId,
                catalogEntry,
                election.SelectedProfileId,
                election.CurrentDraftRevision,
                "owner-address");
        }

        var binding = ElectionModelFactory.CreateProtocolPackageBindingFromCatalog(
            election.ElectionId,
            catalogEntry,
            election.SelectedProfileId,
            election.CurrentDraftRevision,
            "owner-address");

        return status switch
        {
            ProtocolPackageBindingStatus.Latest => binding,
            ProtocolPackageBindingStatus.Stale => binding.MarkStale(DateTime.UtcNow, "owner-address"),
            ProtocolPackageBindingStatus.Incompatible => binding.MarkIncompatible(DateTime.UtcNow, "owner-address"),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported test binding status."),
        };
    }

    private static ProtocolPackageAccessLocationRecord CreateProtocolPackageAccessLocation(string contentHash) =>
        ElectionModelFactory.CreateProtocolPackageAccessLocation(
            ProtocolPackageAccessLocationKind.PublicWebsite,
            "HushNetwork public protocol package",
            "https://www.hushnetwork.social/protocol-omega/hushvoting-v1/v1.0.0/package.zip",
            contentHash);

    private static string Hash(char value) =>
        new(char.ToLowerInvariant(value), 64);

    private static ServerCallContext CreateMockServerCallContext() => new TestServerCallContext();

    private static ServerCallContext CreateSignedServerCallContext(
        string method,
        string actorAddress,
        IReadOnlyDictionary<string, object?> request)
    {
        var signedAt = DateTimeOffset.UtcNow.ToString("O");
        var payload = BuildSignedPayload(method, actorAddress, signedAt, request);
        var requestHeaders = new Metadata
        {
            { "x-hush-election-query-signatory", actorAddress },
            { "x-hush-election-query-signed-at", signedAt },
            { "x-hush-election-query-signature", DigitalSignature.SignMessageCompactBase64(payload, TestSigningPrivateKey) },
        };

        return new TestServerCallContext(requestHeaders);
    }

    private static string BuildSignedPayload(
        string method,
        string actorAddress,
        string signedAt,
        IReadOnlyDictionary<string, object?> request)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["actorAddress"] = actorAddress,
            ["method"] = method,
            ["request"] = request.OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Value),
            ["signedAt"] = signedAt,
        };

        return JsonSerializer.Serialize(payload);
    }
}

public class TestServerCallContext : ServerCallContext
{
    private readonly Metadata _requestHeaders;

    public TestServerCallContext(Metadata? requestHeaders = null)
    {
        _requestHeaders = requestHeaders ?? new Metadata();
    }

    protected override string MethodCore => "TestMethod";
    protected override string HostCore => "TestHost";
    protected override string PeerCore => "TestPeer";
    protected override DateTime DeadlineCore => DateTime.MaxValue;
    protected override Metadata RequestHeadersCore => _requestHeaders;
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
