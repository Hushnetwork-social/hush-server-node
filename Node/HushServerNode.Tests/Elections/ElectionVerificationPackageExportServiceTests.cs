using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionVerificationPackageExportServiceTests
{
    [Fact]
    public void Export_PublicPackage_ShouldWriteRootFilesAndBindManifestEntries()
    {
        var result = Export(CreateRequest(VerificationPackageView.PublicAnonymous));

        result.Success.Should().BeTrue();
        result.Files.Select(x => x.RelativePath).Should().Contain(VerificationPackageFileNames.RootFiles);
        result.Files.Select(x => x.RelativePath).Should().Contain([
            VerificationPackageFileNames.Sp04Evidence,
            VerificationPackageFileNames.Sp04ReceiptCommitments,
            VerificationPackageFileNames.Sp05EligibilityPolicy,
            VerificationPackageFileNames.Sp05EligibilitySummary,
            VerificationPackageFileNames.Sp05EligibilityVerifierOutput,
            VerificationPackageFileNames.Sp06TrusteeControlProfile,
            VerificationPackageFileNames.Sp06TrusteeControlSummary,
            VerificationPackageFileNames.Sp06TrusteeVerifierOutput,
        ]);
        result.Files.Should().NotContain(x => x.RelativePath.StartsWith("artifacts/restricted/", StringComparison.OrdinalIgnoreCase));
        var publicPayload = string.Join(
            '\n',
            result.Files
                .Where(x => x.Visibility == VerificationArtifactVisibility.Public)
                .Select(x => x.ContentText));
        publicPayload.Should().NotContain("voter-1");
        publicPayload.Should().NotContain("actor-voter-1");
        publicPayload.Should().NotContain("voter-1@example.test");

        var manifest = ReadFile<AuditPackageManifestRecord>(result, VerificationPackageFileNames.AuditPackageManifest);
        foreach (var entry in manifest.Entries)
        {
            var file = result.Files.Single(x => x.RelativePath == entry.Path);
            VerificationCanonicalHash.ComputeManifestFileSha256(file.Content).Should().Be(entry.Sha256Hash);
        }
    }

    [Fact]
    public void Export_TrusteeThresholdWithoutSp06Profile_ShouldTreatControlEvidenceAsNotApplicable()
    {
        var request = CreateRequest(VerificationPackageView.PublicAnonymous);
        request = request with
        {
            Election = request.Election with
            {
                GovernanceMode = ElectionGovernanceMode.TrusteeThreshold,
                SelectedProfileId = ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
                RequiredApprovalCount = 3,
            },
        };

        var result = Export(request);

        result.Success.Should().BeTrue();
        var profile = ReadFile<ElectionSp06ControlProfileArtifactRecord>(
            result,
            VerificationPackageFileNames.Sp06TrusteeControlProfile);
        profile.HighAssuranceClaimed.Should().BeFalse();
        profile.ControlDomainProfileId.Should().Be("not_applicable");

        var verifierOutput = ReadFile<ElectionSp06VerifierOutputArtifactRecord>(
            result,
            VerificationPackageFileNames.Sp06TrusteeVerifierOutput);
        verifierOutput.Results.Should().Contain(x =>
            x.CheckCode == "CTRL-000" &&
            x.Status == VerificationCheckStatus.NotApplicable);
        verifierOutput.Results.Should().NotContain(x =>
            x.ResultCode == VerificationResultCodes.TrusteeAcceptanceIncomplete &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public void Export_RestrictedPackageWithoutAuthorization_ShouldFailDeterministically()
    {
        var result = Export(CreateRequest(
            VerificationPackageView.RestrictedOwnerAuditor,
            restrictedAccessAuthorized: false));

        result.Success.Should().BeFalse();
        result.Code.Should().Be(VerificationResultCodes.RestrictedExportUnauthorized);
        result.Files.Should().BeEmpty();
    }

    [Fact]
    public void Export_RestrictedPackageWithAuthorization_ShouldIsolateRestrictedEvidence()
    {
        var result = Export(CreateRequest(
            VerificationPackageView.RestrictedOwnerAuditor,
            restrictedAccessAuthorized: true,
            profileId: VerificationProfileIds.RestrictedOwnerAuditorV1));

        result.Success.Should().BeTrue();
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedRosterCheckoff &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp04CeremonyRecords &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp04PreparedBallotCommitments &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp04SpoilMarkers &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedRosterImportEvidence &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedRoster &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedLinkingEvidence &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedActivationEvents &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedCheckoffLedger &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedDisputes &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp06TrusteeControlDomains &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp06TrusteeReleaseArtifacts &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp07PublicationProofSession &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp07WitnessDeletionLog &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
    }

    [Fact]
    public void Export_PublicPackageWithSp07Evidence_ShouldIncludePublicationProofArtifacts()
    {
        var request = CreateHighAssuranceTrusteeRequest();
        var witnessSetId = Guid.NewGuid();
        var session = CreatePublicationProofSession(request, witnessSetId);
        var transcript = CreatePublicationProofTranscript(request, session);
        var deletionReceipt = CreatePublicationWitnessDeletionReceipt(request, session, transcript);
        request = request with
        {
            PublicationProofSessions = [session],
            PublicationProofTranscripts = [transcript],
            PublicationWitnessDeletionReceipts = [deletionReceipt],
        };

        var result = Export(request);

        result.Success.Should().BeTrue();
        result.Files.Select(x => x.RelativePath).Should().Contain([
            VerificationPackageFileNames.Sp07PublicationProofTranscript,
            VerificationPackageFileNames.Sp07PublicationProofVerifierOutput,
            VerificationPackageFileNames.Sp07WitnessDeletionReceipt,
        ]);

        var tallyReplay = ReadFile<TallyReplayArtifactRecord>(result, VerificationPackageFileNames.TallyReplay);
        tallyReplay.EvidenceStatus.Should().Be(VerificationCheckStatus.Pass);
        tallyReplay.ResultCode.Should().Be(VerificationResultCodes.PublicationProofEvidenceValid);
        tallyReplay.PublicationProofTranscriptHash.Should().Be(transcript.TranscriptHash);

        var verifierOutput = ReadFile<ElectionSp07VerifierOutputArtifactRecord>(
            result,
            VerificationPackageFileNames.Sp07PublicationProofVerifierOutput);
        verifierOutput.Results.Should().ContainSingle(x =>
            x.CheckCode == "VFY-SP07-000" &&
            x.Status == VerificationCheckStatus.Pass &&
            x.ResultCode == VerificationResultCodes.PublicationProofEvidenceValid);

        var publicPayload = string.Join(
            '\n',
            result.Files
                .Where(x => x.Visibility == VerificationArtifactVisibility.Public)
                .Select(x => x.ContentText));
        publicPayload.Should().NotContain("hiddenPermutation");
        publicPayload.Should().NotContain("shuffleMap");
        publicPayload.Should().NotContain("rerandomizationRandomness");
        publicPayload.Should().NotContain("sealedWitnessMaterial");
        publicPayload.Should().NotContain("plaintextVote");
    }

    [Fact]
    public void Export_BeforeFinalization_ShouldFailWithoutMutatingPackageFiles()
    {
        var request = CreateRequest(VerificationPackageView.PublicAnonymous);
        request = request with
        {
            Election = request.Election with
            {
                LifecycleState = ElectionLifecycleState.Closed,
            },
        };

        var result = Export(request);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(VerificationResultCodes.ElectionNotFinalized);
        result.Files.Should().BeEmpty();
    }

    private static ElectionVerificationPackageExportResult Export(
        ElectionVerificationPackageExportRequest request) =>
        new ElectionVerificationPackageExportService().Export(request);

    private static T ReadFile<T>(ElectionVerificationPackageExportResult result, string path)
    {
        var file = result.Files.Single(x => x.RelativePath == path);
        return JsonSerializer.Deserialize<T>(file.Content, VerificationJson.Options)!;
    }

    internal static ElectionVerificationPackageExportRequest CreateRequest(
        VerificationPackageView view,
        bool restrictedAccessAuthorized = false,
        string profileId = VerificationProfileIds.DevelopmentCurrentV1)
    {
        var electionId = ElectionId.NewElectionId;
        var draftElection = ElectionModelFactory.CreateDraftRecord(
            electionId,
            title: "Verifier package election",
            shortDescription: "FEAT-113 test",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "FEAT-113",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            governanceMode: ElectionGovernanceMode.AdminOnly,
            disclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            participationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            voteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            eligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            outcomeRule: CreatePassFailRule(),
            approvedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushvoting", "1.0.0"),
            ],
            protocolOmegaVersion: "omega-v1.1.1",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", "Approve", 1, false),
                new ElectionOptionDefinition("no", "No", "Reject", 2, false),
            ],
            officialResultVisibilityPolicy: OfficialResultVisibilityPolicy.PublicPlaintext);

        var openedAt = DateTime.UtcNow.AddMinutes(-10);
        var ballotDefinitionSeal = ElectionModelFactory.CreateBallotDefinitionSeal(
            ElectionBallotDefinitionCanonicalizer.CurrentVersion,
            ElectionBallotDefinitionCanonicalizer.ComputeHash(draftElection),
            openedAt);
        var sealedElection = draftElection with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = openedAt,
            BallotDefinitionVersion = ballotDefinitionSeal.BallotDefinitionVersion,
            BallotDefinitionHash = ballotDefinitionSeal.BallotDefinitionHash,
            BallotDefinitionSealedAt = ballotDefinitionSeal.SealedAt,
            BallotDefinitionMutationPolicy = ballotDefinitionSeal.MutationPolicy,
        };

        var voter1FinalPreparedId = Guid.NewGuid();
        var voter2FinalPreparedId = Guid.NewGuid();
        var acceptedBallots = new[]
        {
            ElectionModelFactory.CreateAcceptedBallotRecord(
                electionId,
                "ballot-a",
                "proof-a",
                "nullifier-a",
                preparedBallotId: voter1FinalPreparedId,
                preparedBallotHash: "prepared-final-a",
                receiptCommitment: "receipt-a",
                receiptCommitmentScheme: "sha256(receipt_secret|prepared_ballot_hash|accepted_ballot_id)",
                ballotDefinitionVersion: ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionHash: ballotDefinitionSeal.BallotDefinitionHash),
            ElectionModelFactory.CreateAcceptedBallotRecord(
                electionId,
                "ballot-b",
                "proof-b",
                "nullifier-b",
                preparedBallotId: voter2FinalPreparedId,
                preparedBallotHash: "prepared-final-b",
                receiptCommitment: "receipt-b",
                receiptCommitmentScheme: "sha256(receipt_secret|prepared_ballot_hash|accepted_ballot_id)",
                ballotDefinitionVersion: ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionHash: ballotDefinitionSeal.BallotDefinitionHash),
        };
        var voter1SpoiledPreparedId = Guid.NewGuid();
        var voter2SpoiledPreparedId = Guid.NewGuid();
        var spoiledPreparedBallots = new[]
        {
            ElectionModelFactory.CreateSpoiledPreparedBallotRecord(
                electionId,
                voter1SpoiledPreparedId,
                "prepared-spoiled-a",
                "spoiled-transcript-a",
                "spoil-record-a",
                "local-verifier-v1",
                openedAt.AddMinutes(2)),
            ElectionModelFactory.CreateSpoiledPreparedBallotRecord(
                electionId,
                voter2SpoiledPreparedId,
                "prepared-spoiled-b",
                "spoiled-transcript-b",
                "spoil-record-b",
                "local-verifier-v1",
                openedAt.AddMinutes(2)),
        };
        var preparedBallots = new[]
        {
            ElectionModelFactory.CreatePreparedBallotCommitmentRecord(
                electionId,
                "voter-1",
                "actor-voter-1",
                "prepared-spoiled-a",
                ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionSeal.BallotDefinitionHash,
                "sp04-proof",
                openedAt.AddMinutes(1),
                preparedBallotId: voter1SpoiledPreparedId) with
            {
                State = ElectionPreparedBallotState.Spoiled,
                SpoilMarkerId = spoiledPreparedBallots[0].Id,
                SpoiledAt = spoiledPreparedBallots[0].SpoiledAt,
            },
            ElectionModelFactory.CreatePreparedBallotCommitmentRecord(
                electionId,
                "voter-1",
                "actor-voter-1",
                "prepared-final-a",
                ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionSeal.BallotDefinitionHash,
                "sp04-proof",
                openedAt.AddMinutes(3),
                preparedBallotId: voter1FinalPreparedId) with
            {
                State = ElectionPreparedBallotState.Cast,
                AcceptedBallotId = acceptedBallots[0].Id,
                CastAt = openedAt.AddMinutes(4),
            },
            ElectionModelFactory.CreatePreparedBallotCommitmentRecord(
                electionId,
                "voter-2",
                "actor-voter-2",
                "prepared-spoiled-b",
                ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionSeal.BallotDefinitionHash,
                "sp04-proof",
                openedAt.AddMinutes(1),
                preparedBallotId: voter2SpoiledPreparedId) with
            {
                State = ElectionPreparedBallotState.Spoiled,
                SpoilMarkerId = spoiledPreparedBallots[1].Id,
                SpoiledAt = spoiledPreparedBallots[1].SpoiledAt,
            },
            ElectionModelFactory.CreatePreparedBallotCommitmentRecord(
                electionId,
                "voter-2",
                "actor-voter-2",
                "prepared-final-b",
                ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionSeal.BallotDefinitionHash,
                "sp04-proof",
                openedAt.AddMinutes(3),
                preparedBallotId: voter2FinalPreparedId) with
            {
                State = ElectionPreparedBallotState.Cast,
                AcceptedBallotId = acceptedBallots[1].Id,
                CastAt = openedAt.AddMinutes(4),
            },
        };
        var ceremonies = new[]
        {
            ElectionModelFactory.CreateVoterCeremonyRecord(
                electionId,
                "voter-1",
                "actor-voter-1",
                ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionSeal.BallotDefinitionHash,
                createdAt: openedAt.AddMinutes(1)) with
            {
                PreparedPackageCount = 2,
                SpoiledPackageCount = 1,
                FinalState = ElectionVoterCeremonyFinalState.FinalCastAccepted,
                FinalAcceptedBallotId = acceptedBallots[0].Id,
                LastUpdatedAt = openedAt.AddMinutes(4),
            },
            ElectionModelFactory.CreateVoterCeremonyRecord(
                electionId,
                "voter-2",
                "actor-voter-2",
                ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionSeal.BallotDefinitionHash,
                createdAt: openedAt.AddMinutes(1)) with
            {
                PreparedPackageCount = 2,
                SpoiledPackageCount = 1,
                FinalState = ElectionVoterCeremonyFinalState.FinalCastAccepted,
                FinalAcceptedBallotId = acceptedBallots[1].Id,
                LastUpdatedAt = openedAt.AddMinutes(4),
            },
        };
        var publishedBallots = new[]
        {
            ElectionModelFactory.CreatePublishedBallotRecord(electionId, 1, "published-a", "proof-a"),
            ElectionModelFactory.CreatePublishedBallotRecord(electionId, 2, "published-b", "proof-b"),
        };
        var closeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Close,
            sealedElection,
            recordedByPublicAddress: "owner-address",
            acceptedBallotCount: acceptedBallots.Length,
            acceptedBallotSetHash: VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(acceptedBallots),
            publishedBallotCount: publishedBallots.Length,
            publishedBallotStreamHash: VerificationCanonicalHash.ComputePublishedBallotStreamHash(publishedBallots),
            finalEncryptedTallyHash: HashBytes("tally"));
        var tallyReadyArtifactId = Guid.NewGuid();
        var officialResultArtifactId = Guid.NewGuid();
        var unofficialResultArtifactId = Guid.NewGuid();
        var finalizeArtifactId = Guid.NewGuid();
        var finalizedElection = sealedElection with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            ClosedAt = DateTime.UtcNow.AddMinutes(-5),
            FinalizedAt = DateTime.UtcNow,
            CloseArtifactId = closeArtifact.Id,
            TallyReadyArtifactId = tallyReadyArtifactId,
            OfficialResultArtifactId = officialResultArtifactId,
            UnofficialResultArtifactId = unofficialResultArtifactId,
            FinalizeArtifactId = finalizeArtifactId,
        };
        var binding = CreateSealedProtocolBinding(electionId, profileId);
        var reportPackage = ElectionModelFactory.CreateSealedReportPackage(
            electionId,
            attemptNumber: 1,
            tallyReadyArtifactId,
            unofficialResultArtifactId,
            officialResultArtifactId,
            finalizeArtifactId,
            frozenEvidenceHash: HashBytes("frozen"),
            frozenEvidenceFingerprint: "sha256:frozen",
            packageHash: HashBytes("report-package"),
            artifactCount: 1,
            attemptedByPublicAddress: "owner-address",
            closeBoundaryArtifactId: closeArtifact.Id);
        var reportArtifact = ElectionModelFactory.CreateReportArtifact(
            reportPackage.Id,
            electionId,
            ElectionReportArtifactKind.MachineManifest,
            ElectionReportArtifactFormat.Json,
            ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
            sortOrder: 1,
            title: "Machine manifest",
            fileName: "canonical-manifest.json",
            mediaType: "application/json",
            contentHash: HashBytes("{\"ok\":true}"),
            content: "{\"ok\":true}");
        var rosterEntries = new[]
        {
            CreateRosterEntry(electionId, "voter-1", "actor-voter-1"),
            CreateRosterEntry(electionId, "voter-2", "actor-voter-2"),
        };
        var participationRecords = new[]
        {
            ElectionModelFactory.CreateParticipationRecord(
                electionId,
                "voter-1",
                ElectionParticipationStatus.CountedAsVoted,
                recordedAt: DateTime.UtcNow),
            ElectionModelFactory.CreateParticipationRecord(
                electionId,
                "voter-2",
                ElectionParticipationStatus.CountedAsVoted,
                recordedAt: DateTime.UtcNow),
        };
        var commitmentRegistrations = new[]
        {
            ElectionModelFactory.CreateCommitmentRegistrationRecord(
                electionId,
                "voter-1",
                "actor-voter-1",
                "commitment-a",
                registeredAt: openedAt.AddMinutes(3)),
            ElectionModelFactory.CreateCommitmentRegistrationRecord(
                electionId,
                "voter-2",
                "actor-voter-2",
                "commitment-b",
                registeredAt: openedAt.AddMinutes(3)),
        };
        var checkoffConsumptions = new[]
        {
            ElectionModelFactory.CreateCheckoffConsumptionRecord(
                electionId,
                "voter-1",
                consumedAt: openedAt.AddMinutes(4)),
            ElectionModelFactory.CreateCheckoffConsumptionRecord(
                electionId,
                "voter-2",
                consumedAt: openedAt.AddMinutes(4)),
        };
        var rosterCanonicalHash = ElectionEligibilityContracts.ComputeRosterCanonicalHash(rosterEntries);

        return new ElectionVerificationPackageExportRequest(
            finalizedElection,
            binding,
            reportPackage,
            [reportArtifact],
            [closeArtifact],
            acceptedBallots,
            publishedBallots,
            FinalizationSessions: [],
            FinalizationShares: [],
            ReleaseEvidenceRecords: [],
            RosterEntries: rosterEntries,
            ParticipationRecords: participationRecords,
            view,
            profileId,
            restrictedAccessAuthorized,
            ExportedAt: DateTime.UnixEpoch,
            VoterCeremonyRecords: ceremonies,
            PreparedBallotCommitments: preparedBallots,
            SpoiledPreparedBallots: spoiledPreparedBallots,
            RosterImportEvidences:
            [
                ElectionModelFactory.CreateRosterImportEvidence(
                    electionId,
                    rosterImportVersion: 1,
                    rosterSourceFileHash: HashHex("source-roster"),
                    rosterCanonicalHash,
                    ElectionSp05ProfileIds.RosterCanonicalizationV1,
                    ElectionEligibilityContracts.RosterCanonicalizationVersionHash,
                    acceptedRowCount: 2,
                    rejectedRowCount: 0,
                    invalidRowRejectionCount: 0,
                    duplicateIdRejectionCount: 0,
                    duplicateContactWarningCount: 0,
                    importedByActor: "owner-address",
                    importedAt: openedAt)
            ],
            EligibilityPolicyEvidences:
            [
                ElectionModelFactory.CreateEligibilityPolicyEvidence(
                    electionId,
                    eligibilityPolicyVersion: "1.0.0",
                    EligibilityMutationPolicy.FrozenAtOpen,
                    ElectionIdentityLinkPolicy.ContactCodeV1,
                    ElectionCheckoffVisibilityPolicy.RestrictedOwnerAuditor,
                    ElectionActorLinkMultiplicityPolicy.SingleRosterEntryPerActor,
                    ElectionContactCodeProviderReadiness.Ready,
                    ElectionEligibilityContracts.EligibilityPolicyCanonicalizationVersionHash,
                    declaredByActor: "owner-address",
                    declaredAt: openedAt)
            ],
            CommitmentSchemeEvidences:
            [
                ElectionModelFactory.CreateCommitmentSchemeEvidence(
                    electionId,
                    ElectionEligibilityContracts.CommitmentSchemeVersionHash,
                    ElectionEligibilityContracts.NullifierSchemeVersionHash,
                    ElectionEligibilityContracts.RosterCanonicalizationVersionHash,
                    ElectionEligibilityContracts.EligibilityPolicyCanonicalizationVersionHash,
                    declaredByActor: "owner-address",
                    declaredAt: openedAt)
            ],
            CommitmentRegistrations: commitmentRegistrations,
            CheckoffConsumptions: checkoffConsumptions,
            EligibilityActivationEvents: []);
    }

    internal static ElectionVerificationPackageExportRequest CreateHighAssuranceTrusteeRequest()
    {
        var request = CreateRequest(
            VerificationPackageView.PublicAnonymous,
            profileId: VerificationProfileIds.HighAssuranceV1);
        var trusteeElection = request.Election with
        {
            GovernanceMode = ElectionGovernanceMode.TrusteeThreshold,
            SelectedProfileId = ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
            SelectedProfileDevOnly = false,
            RequiredApprovalCount = 3,
        };
        var trustees = Enumerable.Range(1, 5)
            .Select(x => new ElectionTrusteeReference($"trustee-{x}@hush.test", $"Trustee {x}"))
            .ToArray();
        var ceremonySnapshot = ElectionModelFactory.CreateCeremonyBindingSnapshot(
            Guid.NewGuid(),
            ceremonyVersionNumber: 1,
            ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
            boundTrusteeCount: 5,
            requiredApprovalCount: 3,
            trustees,
            tallyPublicKeyFingerprint: "tally-public-key-fingerprint",
            tallyPublicKey: [1, 2, 3, 4]);
        var closeArtifact = request.BoundaryArtifacts.Single(x => x.ArtifactType == ElectionBoundaryArtifactType.Close);
        var finalizationSession = ElectionModelFactory.CreateFinalizationSession(
            trusteeElection,
            closeArtifact.Id,
            closeArtifact.AcceptedBallotSetHash!,
            closeArtifact.FinalEncryptedTallyHash!,
            ElectionFinalizationSessionPurpose.CloseCounting,
            ceremonySnapshot,
            requiredShareCount: 3,
            trustees,
            createdByPublicAddress: "owner-address",
            createdAt: DateTime.UnixEpoch.AddHours(1));
        var shares = trustees.Take(3)
            .Select((trustee, index) => ElectionModelFactory.CreateAcceptedFinalizationShare(
                finalizationSession.Id,
                trusteeElection.ElectionId,
                trustee.TrusteeUserAddress,
                trustee.TrusteeDisplayName,
                trustee.TrusteeUserAddress,
                shareIndex: index + 1,
                shareVersion: "share-v1",
                ElectionFinalizationTargetType.AggregateTally,
                finalizationSession.CloseArtifactId,
                finalizationSession.AcceptedBallotSetHash,
                finalizationSession.FinalEncryptedTallyHash,
                finalizationSession.TargetTallyId,
                ceremonySnapshot.CeremonyVersionId,
                ceremonySnapshot.TallyPublicKeyFingerprint,
                shareMaterial: $"executor-encrypted-share-{index + 1}",
                executorKeyAlgorithm: "ecies-secp256k1-v1",
                submittedAt: DateTime.UnixEpoch.AddHours(2).AddMinutes(index)))
            .ToArray();
        var controlDomains = trustees
            .Select((trustee, index) => new ElectionTrusteeControlDomainRecord(
                Guid.NewGuid(),
                trusteeElection.ElectionId,
                ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1,
                ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1Version,
                ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
                ceremonySnapshot.CeremonyVersionId,
                TrusteeId: $"trustee-{index + 1:00}",
                TrusteeAccountId: trustee.TrusteeUserAddress,
                TrusteePersonRef: $"person-ref-{index + 1}",
                ElectionTrusteeRole.ExternalTrustee,
                CustodyMode: ElectionSp06ProfileIds.TrusteeLocalSecureVaultV1,
                CustodyDomainRefHash: $"custody-domain-hash-{index + 1}",
                AdminDomainRefHash: $"admin-domain-hash-{index + 1}",
                LegalEntityRefHash: null,
                PublicKeyCommitmentHash: $"public-key-commitment-{index + 1}",
                AcceptedAt: DateTime.UnixEpoch.AddMinutes(index),
                AcceptedBeforeOpen: true,
                ElectionTrusteeBackupStatus.Registered,
                ElectionTrusteeExceptionStatus.None,
                ElectionTrusteeControlDomainEvidenceStatus.Accepted,
                EvidenceFailureCode: null,
                EvidenceFailureReason: null,
                RecordedAt: DateTime.UnixEpoch.AddMinutes(index),
                RecordedByPublicAddress: "owner-address",
                SourceTransactionId: null,
                SourceBlockHeight: null,
                SourceBlockId: null))
            .ToArray();

        return request with
        {
            Election = trusteeElection,
            FinalizationSessions = [finalizationSession],
            FinalizationShares = shares,
            TrusteeControlDomainRecords = controlDomains,
        };
    }

    private static ElectionPublicationProofSessionRecord CreatePublicationProofSession(
        ElectionVerificationPackageExportRequest request,
        Guid witnessSetId) =>
        new(
            Guid.NewGuid(),
            request.Election.ElectionId,
            witnessSetId,
            ElectionSp07ProfileIds.PublicationProofMode,
            ElectionSp07ProfileIds.ProofConstruction,
            ElectionSp07ProfileIds.StatementId,
            ElectionPublicationProofSessionStatus.WitnessDeleted,
            DateTime.UnixEpoch.AddHours(3),
            DateTime.UnixEpoch.AddHours(3).AddMinutes(2),
            request.AcceptedBallots.Count,
            request.PublishedBallots.Count,
            ChunkCount: 1,
            RetryCount: 0,
            FailureCode: null,
            FailureReason: null,
            VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(request.AcceptedBallots)),
            VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputePublishedBallotStreamHash(request.PublishedBallots)),
            TranscriptHash: "sp07-transcript-hash",
            ProofHash: HashHex("synthetic-proof-bytes"),
            ServerVerifierOutputHash: "sp07-server-verifier-output-hash",
            DeletionReceiptId: null);

    private static ElectionPublicationProofTranscriptRecord CreatePublicationProofTranscript(
        ElectionVerificationPackageExportRequest request,
        ElectionPublicationProofSessionRecord session) =>
        new(
            Guid.NewGuid(),
            request.Election.ElectionId,
            session.Id,
            session.WitnessSetId,
            ElectionSp07ProfileIds.TranscriptVersion,
            ElectionSp07ProfileIds.PublicationProofMode,
            ElectionSp07ProfileIds.ProofConstruction,
            ElectionSp07ProfileIds.StatementId,
            VerificationProfileIds.HighAssuranceV1,
            VerificationCanonicalHash.ToLowerHex(request.Election.BallotDefinitionHash),
            BallotEncryptionSchemeVersion: "babyjubjub-elgamal-vector-ballot-v1",
            ElectionPublicKeyId: "election-public-key-id",
            session.AcceptedBallotSetHash!,
            session.PublishedBallotStreamHash!,
            request.AcceptedBallots.Count,
            request.PublishedBallots.Count,
            CiphertextSlotCount: request.Election.Options.Count,
            ElectionSp07ProfileIds.ProofSystemVersion,
            ProofBytes: "synthetic-proof-bytes",
            session.ProofHash!,
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

    private static ElectionPublicationWitnessDeletionReceiptRecord CreatePublicationWitnessDeletionReceipt(
        ElectionVerificationPackageExportRequest request,
        ElectionPublicationProofSessionRecord session,
        ElectionPublicationProofTranscriptRecord transcript) =>
        new(
            Guid.NewGuid(),
            request.Election.ElectionId,
            session.Id,
            session.WitnessSetId,
            WitnessSetHash: "witness-set-hash",
            WitnessCount: request.AcceptedBallots.Count,
            transcript.TranscriptHash,
            transcript.ProofHash,
            ElectionPublicationWitnessDeletionStatus.Completed,
            DateTime.UnixEpoch.AddHours(3).AddMinutes(3),
            DeletionActorRef: "proof-worker",
            FailureCode: null,
            FailureReason: null);

    private static ProtocolPackageBindingRecord CreateSealedProtocolBinding(
        ElectionId electionId,
        string profileId)
    {
        var accessLocation = ElectionModelFactory.CreateProtocolPackageAccessLocation(
            ProtocolPackageAccessLocationKind.Repository,
            "Repository",
            "https://example.test/protocol",
            HashHex("access"));
        var catalogEntry = ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            "omega-hushvoting-v1",
            "v1.1.1",
            HashHex("spec"),
            HashHex("proof"),
            HashHex("release"),
            [profileId],
            ProtocolPackageApprovalStatus.DraftPrivate,
            isLatestForCompatibleProfiles: true,
            [accessLocation],
            [accessLocation]);

        return ElectionModelFactory.CreateProtocolPackageBindingFromCatalog(
                electionId,
                catalogEntry,
                profileId,
                draftRevision: 1,
                boundByPublicAddress: "owner-address")
            .SealAtOpen(DateTime.UtcNow, "owner-address");
    }

    private static ElectionRosterEntryRecord CreateRosterEntry(
        ElectionId electionId,
        string organizationVoterId,
        string actorPublicAddress) =>
        new(
            electionId,
            organizationVoterId,
            ElectionRosterContactType.Email,
            $"{organizationVoterId}@example.test",
            ElectionVoterLinkStatus.Linked,
            actorPublicAddress,
            DateTime.UtcNow,
            ElectionVotingRightStatus.Active,
            DateTime.UtcNow,
            WasPresentAtOpen: true,
            WasActiveAtOpen: true,
            LastActivatedAt: DateTime.UtcNow,
            LastActivatedByPublicAddress: "owner-address",
            LastUpdatedAt: DateTime.UtcNow,
            LatestTransactionId: null,
            LatestBlockHeight: null,
            LatestBlockId: null);

    private static OutcomeRuleDefinition CreatePassFailRule() =>
        new(
            OutcomeRuleKind.PassFail,
            TemplateKey: "pass-fail-simple-majority",
            SeatCount: 1,
            BlankVoteCountsForTurnout: true,
            BlankVoteExcludedFromWinnerSelection: true,
            BlankVoteExcludedFromThresholdDenominator: true,
            TieResolutionRule: "reject-on-tie",
            CalculationBasis: "counted-votes");

    private static byte[] HashBytes(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static string HashHex(string value) =>
        Convert.ToHexString(HashBytes(value)).ToLowerInvariant();
}
