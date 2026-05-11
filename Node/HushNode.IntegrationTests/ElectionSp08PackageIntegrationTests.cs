using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushNode.IntegrationTests;

[Trait("Category", "FEAT-118")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class ElectionSp08PackageIntegrationTests
{
    [Fact]
    public async Task HighAssurancePackage_WithOfficialReleaseIntegrityEvidence_VerifiesReleaseIntegrity()
    {
        using var package = CreateHighAssurancePackage(officialSp08: true);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.HighAssuranceV1));

        result.ExitCode.Should().Be(0, DescribeFailures(result));
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Warn);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp08ProfileIds.ReleaseIntegrityAcceptedCheckCode &&
            x.ResultCode == VerificationResultCodes.ReleaseIntegrityEvidenceValid &&
            x.Status == VerificationCheckStatus.Pass);
        result.Output.Results.Should().NotContain(x =>
            ElectionSp08ProfileIds.ReleaseIntegrityCheckCodes.Contains(x.CheckCode) &&
            x.Status == VerificationCheckStatus.Fail);

        var releaseManifest = await ReadArtifactAsync<ElectionSp08ReleaseManifestArtifactRecord>(
            package.PackagePath,
            VerificationPackageFileNames.Sp08ReleaseManifest);
        releaseManifest.EvidenceMode.Should().Be(ElectionSp08ProfileIds.EvidenceModeOfficial);
        releaseManifest.NotForReleaseIntegrityClaims.Should().BeFalse();
        releaseManifest.Components.Select(x => x.ComponentId)
            .Should()
            .Contain(ElectionSp08ProfileIds.RequiredHighAssuranceComponentIds);
        releaseManifest.Components.Should().Contain(x =>
            x.ComponentId == ElectionSp08ProfileIds.MobileAppComponent &&
            !string.IsNullOrWhiteSpace(x.DistributionReference) &&
            !string.IsNullOrWhiteSpace(x.SigningFingerprint));
    }

    [Fact]
    public async Task HighAssurancePackage_WithDevelopmentPlaceholderReleaseIntegrityEvidence_FailsClosed()
    {
        using var package = CreateHighAssurancePackage(officialSp08: false);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.HighAssuranceV1));

        result.ExitCode.Should().Be(1);
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Fail);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp08ProfileIds.EvidenceModeAllowedCheckCode &&
            x.ResultCode == VerificationResultCodes.ReleaseIntegrityEvidenceModeNotAllowed &&
            x.Status == VerificationCheckStatus.Fail);
        result.Output.Results.Should().NotContain(x =>
            x.CheckCode == ElectionSp08ProfileIds.ReleaseIntegrityAcceptedCheckCode &&
            x.Status == VerificationCheckStatus.Pass);
    }

    [Fact]
    public async Task HighAssurancePackage_WithTamperedSp08ComponentDigest_FailsReleaseIntegrity()
    {
        using var package = CreateHighAssurancePackage(officialSp08: true);
        await MutateSp08ManifestAndRefreshAsync(
            package.PackagePath,
            manifest => manifest with
            {
                Components =
                [
                    manifest.Components[0] with { ArtifactDigest = "missing-sha256-prefix" },
                    .. manifest.Components.Skip(1),
                ],
            });

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.HighAssuranceV1));

        result.ExitCode.Should().Be(1);
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Fail);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp08ProfileIds.ComponentHashesCheckCode &&
            x.ResultCode == VerificationResultCodes.ReleaseIntegrityComponentHashMismatch &&
            x.Status == VerificationCheckStatus.Fail);
        result.Output.Results.Should().NotContain(x =>
            x.ResultCode == VerificationResultCodes.PackageManifestArtifactHashMismatch);
    }

    private static TemporaryPackageDirectory CreateHighAssurancePackage(bool officialSp08)
    {
        var directory = new TemporaryPackageDirectory();
        try
        {
            var request = CreateHighAssuranceRequest(officialSp08);
            var export = new ElectionVerificationPackageExportService().Export(request);
            export.Success.Should().BeTrue(export.Message);
            ElectionVerificationPackageExportService.WritePackageToDirectory(export, directory.PackagePath);
            return directory;
        }
        catch
        {
            directory.Dispose();
            throw;
        }
    }

    private static ElectionVerificationPackageExportRequest CreateHighAssuranceRequest(bool officialSp08)
    {
        var election = CreateFinalizedHighAssuranceElection();
        var trustees = CreateTrustees();
        var ceremonyVersionId = Guid.NewGuid();
        var ceremonyVersion = CreateReadyCeremonyVersion(election, trustees, ceremonyVersionId);
        var invitations = CreateAcceptedInvitations(election, trustees);
        var trusteeStates = CreateCompletedTrusteeStates(election, trustees, ceremonyVersionId);
        var custodyRecords = trustees
            .Select(x => ElectionModelFactory.CreateCeremonyShareCustodyRecord(
                election.ElectionId,
                ceremonyVersionId,
                x.TrusteeUserAddress,
                "share-v1"))
            .ToArray();
        var controlDomains = ElectionSp06ControlDomainMaterializer.BuildFromCeremonyEvidence(
            election,
            ceremonyVersion,
            invitations,
            trusteeStates,
            custodyRecords,
            DateTime.UnixEpoch.AddHours(1));
        var acceptedBallots = CreateAcceptedBallots(election);
        var publishedBallots = CreatePublishedBallots(election);
        var preparedBallots = CreatePreparedBallotCommitments(election, acceptedBallots);
        var ceremonySnapshot = ElectionModelFactory.CreateCeremonyBindingSnapshot(
            ceremonyVersionId,
            ceremonyVersion.VersionNumber,
            ceremonyVersion.ProfileId,
            boundTrusteeCount: trustees.Count,
            requiredApprovalCount: 3,
            trustees,
            tallyPublicKeyFingerprint: "tally-public-key-fingerprint",
            tallyPublicKey: [1, 2, 3, 4]);
        var finalizationSession = ElectionModelFactory.CreateFinalizationSession(
            election,
            closeArtifactId: Guid.NewGuid(),
            acceptedBallotSetHash: VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(acceptedBallots),
            finalEncryptedTallyHash: HashBytes("final-encrypted-tally"),
            ElectionFinalizationSessionPurpose.CloseCounting,
            ceremonySnapshot,
            requiredShareCount: 3,
            eligibleTrustees: trustees,
            createdByPublicAddress: election.OwnerPublicAddress,
            createdAt: DateTime.UnixEpoch.AddHours(2));
        var acceptedShares = trustees.Take(3)
            .Select((trustee, index) => ElectionModelFactory.CreateAcceptedFinalizationShare(
                finalizationSession.Id,
                election.ElectionId,
                trustee.TrusteeUserAddress,
                trustee.TrusteeDisplayName,
                trustee.TrusteeUserAddress,
                index + 1,
                "share-v1",
                ElectionFinalizationTargetType.AggregateTally,
                finalizationSession.CloseArtifactId,
                finalizationSession.AcceptedBallotSetHash,
                finalizationSession.FinalEncryptedTallyHash,
                finalizationSession.TargetTallyId,
                ceremonyVersionId,
                ceremonySnapshot.TallyPublicKeyFingerprint,
                $"executor-encrypted-share-{index + 1}",
                executorKeyAlgorithm: "ecies-secp256k1-v1",
                submittedAt: DateTime.UnixEpoch.AddHours(3).AddMinutes(index)))
            .ToArray();
        var protocolBinding = CreateProtocolPackageBinding(election);
        var sp07Evidence = CreateSp07Evidence(election, acceptedBallots, publishedBallots);
        var request = new ElectionVerificationPackageExportRequest(
            election,
            protocolBinding,
            CreateReportPackage(election),
            ReportArtifacts: [],
            BoundaryArtifacts: [],
            acceptedBallots,
            publishedBallots,
            FinalizationSessions: [finalizationSession],
            FinalizationShares: acceptedShares,
            ReleaseEvidenceRecords: [],
            RosterEntries: [],
            ParticipationRecords: [],
            VerificationPackageView.PublicAnonymous,
            VerificationProfileIds.HighAssuranceV1,
            RestrictedAccessAuthorized: true,
            ExportedAt: DateTime.UnixEpoch.AddHours(4),
            PreparedBallotCommitments: preparedBallots,
            EligibilityPolicyEvidences: [CreateEligibilityPolicyEvidence(election)],
            CommitmentSchemeEvidences: [CreateCommitmentSchemeEvidence(election)],
            TrusteeControlDomainRecords: controlDomains,
            PublicationProofTranscripts: [sp07Evidence.Transcript],
            PublicationProofSessions: [sp07Evidence.Session],
            PublicationWitnessDeletionReceipts: [sp07Evidence.DeletionReceipt]);

        return officialSp08
            ? request with { Sp08ReleaseManifest = CreateOfficialSp08ReleaseManifest(request) }
            : request;
    }

    private static ElectionRecord CreateFinalizedHighAssuranceElection()
    {
        var election = ElectionModelFactory.CreateDraftRecord(
            ElectionId.NewElectionId,
            "FEAT-118 high assurance integration",
            "SP-08 release-integrity package integration",
            "owner-address",
            "FEAT-118",
            ElectionClass.OrganizationalRemoteVoting,
            ElectionBindingStatus.Binding,
            ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
            selectedProfileDevOnly: false,
            ElectionGovernanceMode.TrusteeThreshold,
            ElectionDisclosureMode.FinalResultsOnly,
            ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            VoteUpdatePolicy.SingleSubmissionOnly,
            EligibilitySourceType.OrganizationImportedRoster,
            EligibilityMutationPolicy.FrozenAtOpen,
            new OutcomeRuleDefinition(
                OutcomeRuleKind.SingleWinner,
                "single_winner",
                SeatCount: 1,
                BlankVoteCountsForTurnout: true,
                BlankVoteExcludedFromWinnerSelection: true,
                BlankVoteExcludedFromThresholdDenominator: false,
                TieResolutionRule: "tie_unresolved",
                CalculationBasis: "highest_non_blank_votes"),
            approvedClientApplications: [new ApprovedClientApplicationRecord("hushvoting", "1.0.0")],
            "omega-v1.0.0",
            ReportingPolicy.DefaultPhaseOnePackage,
            ReviewWindowPolicy.GovernedReviewWindowReserved,
            ownerOptions:
            [
                new ElectionOptionDefinition("alice", "Alice", null, 1, IsBlankOption: false),
                new ElectionOptionDefinition("bob", "Bob", null, 2, IsBlankOption: false),
            ],
            requiredApprovalCount: 3);
        var ballotSeal = ElectionModelFactory.CreateBallotDefinitionSeal(
            1,
            [9, 8, 7, 6],
            DateTime.UnixEpoch.AddMinutes(30));

        return election with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            OpenedAt = DateTime.UnixEpoch.AddHours(1),
            ClosedAt = DateTime.UnixEpoch.AddHours(2),
            FinalizedAt = DateTime.UnixEpoch.AddHours(4),
            TallyReadyArtifactId = Guid.NewGuid(),
            UnofficialResultArtifactId = Guid.NewGuid(),
            OfficialResultArtifactId = Guid.NewGuid(),
            FinalizeArtifactId = Guid.NewGuid(),
            BallotDefinitionVersion = ballotSeal.BallotDefinitionVersion,
            BallotDefinitionHash = ballotSeal.BallotDefinitionHash,
            BallotDefinitionSealedAt = ballotSeal.SealedAt,
            BallotDefinitionMutationPolicy = ballotSeal.MutationPolicy,
            ControlDomainProfileId = ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1,
            ControlDomainProfileVersion = ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1Version,
            ThresholdProfileId = ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
        };
    }

    private static IReadOnlyList<ElectionAcceptedBallotRecord> CreateAcceptedBallots(ElectionRecord election) =>
    [
        ElectionModelFactory.CreateAcceptedBallotRecord(
            election.ElectionId,
            "ballot-a",
            "proof-a",
            "nullifier-a",
            preparedBallotId: Guid.NewGuid(),
            preparedBallotHash: "prepared-ballot-a",
            receiptCommitment: "receipt-a",
            receiptCommitmentScheme: "sha256(receipt_secret|prepared_ballot_hash|accepted_ballot_id)",
            ballotDefinitionVersion: election.BallotDefinitionVersion!.Value,
            ballotDefinitionHash: election.BallotDefinitionHash!),
        ElectionModelFactory.CreateAcceptedBallotRecord(
            election.ElectionId,
            "ballot-b",
            "proof-b",
            "nullifier-b",
            preparedBallotId: Guid.NewGuid(),
            preparedBallotHash: "prepared-ballot-b",
            receiptCommitment: "receipt-b",
            receiptCommitmentScheme: "sha256(receipt_secret|prepared_ballot_hash|accepted_ballot_id)",
            ballotDefinitionVersion: election.BallotDefinitionVersion!.Value,
            ballotDefinitionHash: election.BallotDefinitionHash!),
    ];

    private static IReadOnlyList<ElectionPublishedBallotRecord> CreatePublishedBallots(ElectionRecord election) =>
    [
        ElectionModelFactory.CreatePublishedBallotRecord(election.ElectionId, 1, "published-ballot-a", "proof-bundle-a"),
        ElectionModelFactory.CreatePublishedBallotRecord(election.ElectionId, 2, "published-ballot-b", "proof-bundle-b"),
    ];

    private static IReadOnlyList<ElectionPreparedBallotCommitmentRecord> CreatePreparedBallotCommitments(
        ElectionRecord election,
        IReadOnlyList<ElectionAcceptedBallotRecord> acceptedBallots) =>
        acceptedBallots
            .Select((ballot, index) => ElectionModelFactory.CreatePreparedBallotCommitmentRecord(
                    election.ElectionId,
                    $"voter-{index + 1}",
                    $"actor-voter-{index + 1}",
                    ballot.PreparedBallotHash!,
                    election.BallotDefinitionVersion!.Value,
                    election.BallotDefinitionHash!,
                    "sp04-proof",
                    election.OpenedAt!.Value.AddMinutes(index + 1),
                    preparedBallotId: ballot.PreparedBallotId!.Value) with
                {
                    State = ElectionPreparedBallotState.Cast,
                    AcceptedBallotId = ballot.Id,
                    CastAt = ballot.AcceptedAt,
                })
            .ToArray();

    private static ElectionEligibilityPolicyEvidenceRecord CreateEligibilityPolicyEvidence(ElectionRecord election) =>
        ElectionModelFactory.CreateEligibilityPolicyEvidence(
            election.ElectionId,
            eligibilityPolicyVersion: "1.0.0",
            EligibilityMutationPolicy.FrozenAtOpen,
            ElectionIdentityLinkPolicy.ContactCodeV1,
            ElectionCheckoffVisibilityPolicy.RestrictedOwnerAuditor,
            ElectionActorLinkMultiplicityPolicy.SingleRosterEntryPerActor,
            ElectionContactCodeProviderReadiness.Ready,
            ElectionEligibilityContracts.EligibilityPolicyCanonicalizationVersionHash,
            declaredByActor: election.OwnerPublicAddress,
            declaredAt: election.OpenedAt!.Value);

    private static ElectionCommitmentSchemeEvidenceRecord CreateCommitmentSchemeEvidence(ElectionRecord election) =>
        ElectionModelFactory.CreateCommitmentSchemeEvidence(
            election.ElectionId,
            ElectionEligibilityContracts.CommitmentSchemeVersionHash,
            ElectionEligibilityContracts.NullifierSchemeVersionHash,
            ElectionEligibilityContracts.RosterCanonicalizationVersionHash,
            ElectionEligibilityContracts.EligibilityPolicyCanonicalizationVersionHash,
            declaredByActor: election.OwnerPublicAddress,
            declaredAt: election.OpenedAt!.Value);

    private static Sp07PackageEvidence CreateSp07Evidence(
        ElectionRecord election,
        IReadOnlyList<ElectionAcceptedBallotRecord> acceptedBallots,
        IReadOnlyList<ElectionPublishedBallotRecord> publishedBallots)
    {
        var witnessSetId = Guid.NewGuid();
        var proofBytes = "synthetic-proof-bytes";
        var proofHash = HashHex(proofBytes);
        var acceptedHash = VerificationCanonicalHash.ToLowerHex(
            VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(acceptedBallots));
        var publishedHash = VerificationCanonicalHash.ToLowerHex(
            VerificationCanonicalHash.ComputePublishedBallotStreamHash(publishedBallots));
        var session = new ElectionPublicationProofSessionRecord(
            Guid.NewGuid(),
            election.ElectionId,
            witnessSetId,
            ElectionSp07ProfileIds.PublicationProofMode,
            ElectionSp07ProfileIds.ProofConstruction,
            ElectionSp07ProfileIds.StatementId,
            ElectionPublicationProofSessionStatus.WitnessDeleted,
            DateTime.UnixEpoch.AddHours(3),
            DateTime.UnixEpoch.AddHours(3).AddMinutes(2),
            acceptedBallots.Count,
            publishedBallots.Count,
            ChunkCount: 1,
            RetryCount: 0,
            FailureCode: null,
            FailureReason: null,
            acceptedHash,
            publishedHash,
            TranscriptHash: "sp07-transcript-hash",
            ProofHash: proofHash,
            ServerVerifierOutputHash: "sp07-server-verifier-output-hash",
            DeletionReceiptId: null);
        var transcript = new ElectionPublicationProofTranscriptRecord(
            Guid.NewGuid(),
            election.ElectionId,
            session.Id,
            session.WitnessSetId,
            ElectionSp07ProfileIds.TranscriptVersion,
            ElectionSp07ProfileIds.PublicationProofMode,
            ElectionSp07ProfileIds.ProofConstruction,
            ElectionSp07ProfileIds.StatementId,
            VerificationProfileIds.HighAssuranceV1,
            VerificationCanonicalHash.ToLowerHex(election.BallotDefinitionHash),
            BallotEncryptionSchemeVersion: "babyjubjub-elgamal-vector-ballot-v1",
            ElectionPublicKeyId: "election-public-key-id",
            acceptedHash,
            publishedHash,
            acceptedBallots.Count,
            publishedBallots.Count,
            CiphertextSlotCount: election.Options.Count,
            ElectionSp07ProfileIds.ProofSystemVersion,
            proofBytes,
            proofHash,
            session.TranscriptHash!,
            ElectionSp07ProfileIds.ExternalReviewStatus,
            DateTime.UnixEpoch.AddHours(3).AddMinutes(1),
            GeneratorReleaseHash: "generator-release-hash",
            VerifierReleaseHash: "verifier-release-hash",
            PublicPrivacyBoundary:
            [
                "no_hidden_permutation",
                "no_shuffle_map",
                "no_rerandomization_randomness",
                "no_raw_witness",
            ]);
        var deletionReceipt = new ElectionPublicationWitnessDeletionReceiptRecord(
            Guid.NewGuid(),
            election.ElectionId,
            session.Id,
            session.WitnessSetId,
            WitnessSetHash: "witness-set-hash",
            WitnessCount: acceptedBallots.Count,
            transcript.TranscriptHash,
            transcript.ProofHash,
            ElectionPublicationWitnessDeletionStatus.Completed,
            DateTime.UnixEpoch.AddHours(3).AddMinutes(3),
            DeletionActorRef: "proof-worker",
            FailureCode: null,
            FailureReason: null);

        return new Sp07PackageEvidence(session, transcript, deletionReceipt);
    }

    private static ElectionSp08ReleaseManifestArtifactRecord CreateOfficialSp08ReleaseManifest(
        ElectionVerificationPackageExportRequest request)
    {
        var releaseId = "release-2026.05.11";
        var sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var sourceTag = "hush-voting-2026.05.11";
        var serverDigest = Sp08Digest("server");
        var webDigest = Sp08Digest("web-client");
        var verifierDigest = Sp08Digest("standalone-verifier");
        var sp07Digest = Sp08Digest("sp07-worker");
        var protocolDigest = $"sha256:{request.ProtocolPackageBinding!.ReleaseManifestHash}";
        var exporterDigest = Sp08Digest("audit-package-exporter");
        var mobileDigest = Sp08Digest("mobile-app");

        return ElectionSp08ReleaseManifestGenerator.Generate(new ElectionSp08ReleaseManifestArtifactRecord(
            Schema: ElectionSp08ProfileIds.ReleaseManifestSchema,
            ManifestId: "release-manifest-2026-05-11",
            releaseId,
            ElectionSp08ProfileIds.EvidenceModeOfficial,
            NotForReleaseIntegrityClaims: false,
            GeneratedAt: DateTime.UnixEpoch,
            SourceAuthority: "github-actions",
            sourceCommit,
            sourceTag,
            Components:
            [
                CreateOfficialSp08Component(ElectionSp08ProfileIds.ServerComponent, serverDigest, sourceCommit, sourceTag),
                CreateOfficialSp08Component(ElectionSp08ProfileIds.WebClientComponent, webDigest, sourceCommit, sourceTag),
                CreateOfficialSp08Component(ElectionSp08ProfileIds.StandaloneVerifierComponent, verifierDigest, sourceCommit, sourceTag),
                CreateOfficialSp08Component(ElectionSp08ProfileIds.Sp07ProofWorkerComponent, sp07Digest, sourceCommit, sourceTag),
                CreateOfficialSp08Component(ElectionSp08ProfileIds.ProtocolPackageComponent, protocolDigest, sourceCommit, sourceTag),
                CreateOfficialSp08Component(ElectionSp08ProfileIds.AuditPackageExporterComponent, exporterDigest, sourceCommit, sourceTag),
                CreateOfficialSp08Component(
                    ElectionSp08ProfileIds.MobileAppComponent,
                    mobileDigest,
                    sourceCommit,
                    sourceTag,
                    distributionReference: "app-store:hushvoting@2026.05.11",
                    signingFingerprint: Sp08Digest("mobile-signing-key")),
            ],
            CircuitAndKeys:
            [
                new ElectionSp08CircuitKeyArtifactRecord(
                    CircuitId: "protocol-omega-publication-proof-v1",
                    CircuitHash: Sp08Digest("circuit"),
                    ProvingKeyHash: Sp08Digest("proving-key"),
                    VerifyingKeyHash: Sp08Digest("verifying-key"),
                    ProtocolPackageManifestHash: request.ProtocolPackageBinding.ReleaseManifestHash),
            ],
            LifecycleBindings:
            [
                CreateOfficialSp08Lifecycle(ElectionSp08ProfileIds.OpenLifecycleStage, releaseId, serverDigest),
                CreateOfficialSp08Lifecycle(ElectionSp08ProfileIds.CloseLifecycleStage, releaseId, serverDigest),
                CreateOfficialSp08Lifecycle(ElectionSp08ProfileIds.ProofWorkerLifecycleStage, releaseId, sp07Digest),
                CreateOfficialSp08Lifecycle(ElectionSp08ProfileIds.ExporterLifecycleStage, releaseId, exporterDigest),
                CreateOfficialSp08Lifecycle(ElectionSp08ProfileIds.ClientReleaseSetLifecycleStage, releaseId, webDigest),
            ],
            PublicPrivacyBoundary:
            [
                "no_private_host_state",
                "no_per_voter_device_identifier",
                "no_raw_attestation_token",
                "no_ip_address",
            ]));
    }

    private static ElectionSp08ReleaseComponentArtifactRecord CreateOfficialSp08Component(
        string componentId,
        string digest,
        string sourceCommit,
        string sourceTag,
        string? distributionReference = null,
        string? signingFingerprint = null) =>
        new(
            componentId,
            componentId,
            ElectionSp08ProfileIds.EvidenceModeOfficial,
            $"{componentId}.artifact",
            digest,
            sourceCommit,
            sourceTag,
            $"ghcr.io/hushnetwork/{componentId}@{digest}",
            BuildWorkflowRunId: "1234567890",
            distributionReference,
            signingFingerprint,
            IsPlaceholder: false);

    private static ElectionSp08LifecycleReleaseBindingRecord CreateOfficialSp08Lifecycle(
        string lifecycleStage,
        string releaseId,
        string digest) =>
        new(
            lifecycleStage,
            releaseId,
            releaseId,
            digest,
            digest,
            MatchesSealedPolicy: true);

    private static ElectionCeremonyVersionRecord CreateReadyCeremonyVersion(
        ElectionRecord election,
        IReadOnlyList<ElectionTrusteeReference> trustees,
        Guid ceremonyVersionId) =>
        ElectionModelFactory.CreateCeremonyVersion(
                election.ElectionId,
                versionNumber: 1,
                ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
                requiredApprovalCount: 3,
                trustees,
                startedByPublicAddress: election.OwnerPublicAddress,
                startedAt: DateTime.UnixEpoch.AddMinutes(10))
            .MarkReady(
                DateTime.UnixEpoch.AddMinutes(40),
                "tally-public-key-fingerprint",
                [1, 2, 3, 4]) with
            {
                Id = ceremonyVersionId,
            };

    private static IReadOnlyList<ElectionTrusteeInvitationRecord> CreateAcceptedInvitations(
        ElectionRecord election,
        IReadOnlyList<ElectionTrusteeReference> trustees) =>
        trustees
            .Select(x => ElectionModelFactory.CreateTrusteeInvitation(
                    election.ElectionId,
                    x.TrusteeUserAddress,
                    x.TrusteeDisplayName,
                    election.OwnerPublicAddress,
                    election.CurrentDraftRevision)
                .Accept(
                    DateTime.UnixEpoch.AddMinutes(20),
                    election.CurrentDraftRevision,
                    ElectionLifecycleState.Draft))
            .ToArray();

    private static IReadOnlyList<ElectionCeremonyTrusteeStateRecord> CreateCompletedTrusteeStates(
        ElectionRecord election,
        IReadOnlyList<ElectionTrusteeReference> trustees,
        Guid ceremonyVersionId) =>
        trustees
            .Select((trustee, index) => ElectionModelFactory.CreateCeremonyTrusteeState(
                    election.ElectionId,
                    ceremonyVersionId,
                    trustee.TrusteeUserAddress,
                    trustee.TrusteeDisplayName,
                    ElectionTrusteeCeremonyState.AcceptedTrustee)
                .PublishTransportKey($"transport-{index + 1}", DateTime.UnixEpoch.AddMinutes(21))
                .MarkJoined(DateTime.UnixEpoch.AddMinutes(22))
                .RecordSelfTestSuccess(DateTime.UnixEpoch.AddMinutes(23))
                .RecordMaterialSubmitted(DateTime.UnixEpoch.AddMinutes(24), "share-v1", [1, 2, 3, (byte)(index + 1)])
                .MarkCompleted(DateTime.UnixEpoch.AddMinutes(25), "share-v1"))
            .ToArray();

    private static IReadOnlyList<ElectionTrusteeReference> CreateTrustees() =>
    [
        new("trustee-1@hush.test", "Trustee 1"),
        new("trustee-2@hush.test", "Trustee 2"),
        new("trustee-3@hush.test", "Trustee 3"),
        new("trustee-4@hush.test", "Trustee 4"),
        new("trustee-5@hush.test", "Trustee 5"),
    ];

    private static ProtocolPackageBindingRecord CreateProtocolPackageBinding(ElectionRecord election)
    {
        var catalogEntry = ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            "omega-hushvoting-v1",
            "v1.0.0",
            Hash('a'),
            Hash('b'),
            Hash('c'),
            compatibleProfileIds: [election.SelectedProfileId],
            approvalStatus: ProtocolPackageApprovalStatus.ApprovedInternal,
            isLatestForCompatibleProfiles: true,
            specAccessLocations:
            [
                ElectionModelFactory.CreateProtocolPackageAccessLocation(
                    ProtocolPackageAccessLocationKind.Repository,
                    "Spec package",
                    "https://example.test/spec.zip",
                    Hash('d')),
            ],
            proofAccessLocations:
            [
                ElectionModelFactory.CreateProtocolPackageAccessLocation(
                    ProtocolPackageAccessLocationKind.Repository,
                    "Proof package",
                    "https://example.test/proof.zip",
                    Hash('e')),
            ],
            approvedAt: DateTime.UnixEpoch.AddMinutes(1));

        return ElectionModelFactory.CreateProtocolPackageBindingFromCatalog(
                election.ElectionId,
                catalogEntry,
                election.SelectedProfileId,
                election.CurrentDraftRevision,
                election.OwnerPublicAddress)
            .SealAtOpen(election.OpenedAt!.Value, election.OwnerPublicAddress);
    }

    private static ElectionReportPackageRecord CreateReportPackage(ElectionRecord election) =>
        ElectionModelFactory.CreateSealedReportPackage(
            election.ElectionId,
            attemptNumber: 1,
            tallyReadyArtifactId: election.TallyReadyArtifactId!.Value,
            unofficialResultArtifactId: election.UnofficialResultArtifactId!.Value,
            officialResultArtifactId: election.OfficialResultArtifactId!.Value,
            finalizeArtifactId: election.FinalizeArtifactId!.Value,
            frozenEvidenceHash: [1, 2, 3],
            frozenEvidenceFingerprint: "package-fingerprint",
            packageHash: [4, 5, 6],
            artifactCount: 1,
            attemptedByPublicAddress: election.OwnerPublicAddress,
            attemptedAt: DateTime.UnixEpoch.AddHours(4),
            sealedAt: DateTime.UnixEpoch.AddHours(4).AddMinutes(1));

    private static async Task MutateSp08ManifestAndRefreshAsync(
        string packagePath,
        Func<ElectionSp08ReleaseManifestArtifactRecord, ElectionSp08ReleaseManifestArtifactRecord> mutate)
    {
        var releaseManifest = await ReadArtifactAsync<ElectionSp08ReleaseManifestArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp08ReleaseManifest);
        var mutatedManifest = mutate(releaseManifest);
        await WriteArtifactAsync(packagePath, VerificationPackageFileNames.Sp08ReleaseManifest, mutatedManifest);

        var releaseIntegrity = await ReadArtifactAsync<ElectionSp08ReleaseIntegrityArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp08ReleaseIntegrity);
        await WriteArtifactAsync(
            packagePath,
            VerificationPackageFileNames.Sp08ReleaseIntegrity,
            releaseIntegrity with
            {
                ReleaseManifestHash = ElectionSp08ReleaseManifestHasher.ComputeReleaseManifestHash(mutatedManifest),
            });
        await RefreshAuditManifestEntriesAsync(packagePath);
    }

    private static async Task RefreshAuditManifestEntriesAsync(string packagePath)
    {
        var manifest = await ReadArtifactAsync<AuditPackageManifestRecord>(
            packagePath,
            VerificationPackageFileNames.AuditPackageManifest);
        var refreshedEntries = new List<AuditPackageManifestEntryRecord>();
        foreach (var entry in manifest.Entries)
        {
            var path = ResolvePackagePath(packagePath, entry.Path);
            var bytes = await File.ReadAllBytesAsync(path);
            refreshedEntries.Add(entry with
            {
                Sha256Hash = VerificationCanonicalHash.ComputeManifestFileSha256(bytes),
                SizeBytes = bytes.LongLength,
            });
        }

        await WriteArtifactAsync(
            packagePath,
            VerificationPackageFileNames.AuditPackageManifest,
            manifest with { Entries = refreshedEntries });
    }

    private static async Task<T> ReadArtifactAsync<T>(string packagePath, string relativePath) =>
        JsonSerializer.Deserialize<T>(
            await File.ReadAllTextAsync(ResolvePackagePath(packagePath, relativePath)),
            VerificationJson.Options)!;

    private static async Task WriteArtifactAsync<T>(string packagePath, string relativePath, T value) =>
        await File.WriteAllTextAsync(
            ResolvePackagePath(packagePath, relativePath),
            JsonSerializer.Serialize(value, VerificationJson.Options));

    private static string ResolvePackagePath(string packagePath, string relativePath) =>
        Path.Combine(packagePath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static byte[] HashBytes(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static string HashHex(string value) =>
        Convert.ToHexString(HashBytes(value)).ToLowerInvariant();

    private static string Hash(char value) =>
        new(char.ToLowerInvariant(value), 64);

    private static string Sp08Digest(string value) =>
        $"sha256:{HashHex($"sp08:{value}")}";

    private static string DescribeFailures(HushVotingPackageVerificationResult result) =>
        string.Join(
            Environment.NewLine,
            result.Output.Results
                .Where(x => x.Status == VerificationCheckStatus.Fail)
                .Select(x => $"{x.CheckCode}:{x.ResultCode}:{x.Message}"));

    private sealed class TemporaryPackageDirectory : IDisposable
    {
        public string PackagePath { get; } = Path.Combine(
            Path.GetTempPath(),
            $"hush-sp08-integration-{Guid.NewGuid():N}");

        public TemporaryPackageDirectory()
        {
            Directory.CreateDirectory(PackagePath);
        }

        public void Dispose()
        {
            if (Directory.Exists(PackagePath))
            {
                Directory.Delete(PackagePath, recursive: true);
            }
        }
    }

    private sealed record Sp07PackageEvidence(
        ElectionPublicationProofSessionRecord Session,
        ElectionPublicationProofTranscriptRecord Transcript,
        ElectionPublicationWitnessDeletionReceiptRecord DeletionReceipt);
}
