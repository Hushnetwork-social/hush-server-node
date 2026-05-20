using System.Security.Cryptography;
using System.Text;
using HushNode.Elections;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;

namespace VerifierCorpusPromoter;

internal static class SyntheticElectionRequestFactory
{
    public static ElectionVerificationPackageExportRequest CreatePublicAnonymousRequest(DateTimeOffset generatedAt)
    {
        var baseTime = generatedAt.UtcDateTime;
        var electionId = new ElectionId(StableGuid("public-corpus-election"));
        var draftElection = ElectionModelFactory.CreateDraftRecord(
            electionId,
            title: "Synthetic verifier corpus election",
            shortDescription: "Synthetic public verifier corpus sample",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "synthetic-public-corpus",
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
            protocolOmegaVersion: "omega-v1.2.0",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", "Approve", 1, false),
                new ElectionOptionDefinition("no", "No", "Reject", 2, false),
            ],
            createdAt: baseTime.AddHours(-1),
            officialResultVisibilityPolicy: OfficialResultVisibilityPolicy.PublicPlaintext);

        var openedAt = baseTime.AddMinutes(-30);
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

        var voter1FinalPreparedId = StableGuid("voter-1-final-prepared");
        var voter2FinalPreparedId = StableGuid("voter-2-final-prepared");
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
                ballotDefinitionHash: ballotDefinitionSeal.BallotDefinitionHash,
                acceptedAt: openedAt.AddMinutes(4)) with
            {
                Id = StableGuid("accepted-ballot-a"),
            },
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
                ballotDefinitionHash: ballotDefinitionSeal.BallotDefinitionHash,
                acceptedAt: openedAt.AddMinutes(4)) with
            {
                Id = StableGuid("accepted-ballot-b"),
            },
        };

        var voter1SpoiledPreparedId = StableGuid("voter-1-spoiled-prepared");
        var voter2SpoiledPreparedId = StableGuid("voter-2-spoiled-prepared");
        var spoiledPreparedBallots = new[]
        {
            ElectionModelFactory.CreateSpoiledPreparedBallotRecord(
                electionId,
                voter1SpoiledPreparedId,
                "prepared-spoiled-a",
                "spoiled-transcript-a",
                "spoil-record-a",
                "local-verifier-v1",
                openedAt.AddMinutes(2)) with
            {
                Id = StableGuid("spoiled-prepared-marker-a"),
            },
            ElectionModelFactory.CreateSpoiledPreparedBallotRecord(
                electionId,
                voter2SpoiledPreparedId,
                "prepared-spoiled-b",
                "spoiled-transcript-b",
                "spoil-record-b",
                "local-verifier-v1",
                openedAt.AddMinutes(2)) with
            {
                Id = StableGuid("spoiled-prepared-marker-b"),
            },
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
                Id = StableGuid("voter-ceremony-a"),
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
                Id = StableGuid("voter-ceremony-b"),
                PreparedPackageCount = 2,
                SpoiledPackageCount = 1,
                FinalState = ElectionVoterCeremonyFinalState.FinalCastAccepted,
                FinalAcceptedBallotId = acceptedBallots[1].Id,
                LastUpdatedAt = openedAt.AddMinutes(4),
            },
        };
        var publishedBallots = new[]
        {
            ElectionModelFactory.CreatePublishedBallotRecord(
                electionId,
                1,
                "published-a",
                "proof-a",
                publishedAt: openedAt.AddMinutes(5)) with
            {
                Id = StableGuid("published-ballot-a"),
            },
            ElectionModelFactory.CreatePublishedBallotRecord(
                electionId,
                2,
                "published-b",
                "proof-b",
                publishedAt: openedAt.AddMinutes(5)) with
            {
                Id = StableGuid("published-ballot-b"),
            },
        };
        var closeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Close,
            sealedElection,
            recordedByPublicAddress: "owner-address",
            recordedAt: baseTime.AddMinutes(-10),
            acceptedBallotCount: acceptedBallots.Length,
            acceptedBallotSetHash: VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(acceptedBallots),
            publishedBallotCount: publishedBallots.Length,
            publishedBallotStreamHash: VerificationCanonicalHash.ComputePublishedBallotStreamHash(publishedBallots),
            finalEncryptedTallyHash: HashBytes("tally")) with
        {
            Id = StableGuid("close-artifact"),
        };
        var tallyReadyArtifactId = StableGuid("tally-ready-artifact");
        var officialResultArtifactId = StableGuid("official-result-artifact");
        var unofficialResultArtifactId = StableGuid("unofficial-result-artifact");
        var finalizeArtifactId = StableGuid("finalize-artifact");
        var finalizedElection = sealedElection with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            ClosedAt = baseTime.AddMinutes(-10),
            FinalizedAt = baseTime,
            CloseArtifactId = closeArtifact.Id,
            TallyReadyArtifactId = tallyReadyArtifactId,
            OfficialResultArtifactId = officialResultArtifactId,
            UnofficialResultArtifactId = unofficialResultArtifactId,
            FinalizeArtifactId = finalizeArtifactId,
        };
        var binding = CreateSealedProtocolBinding(electionId, VerificationProfileIds.PublicAnonymousV1);
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
            closeBoundaryArtifactId: closeArtifact.Id,
            attemptedAt: baseTime.AddMinutes(-2),
            sealedAt: baseTime.AddMinutes(-1),
            preassignedPackageId: StableGuid("report-package"));
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
            content: "{\"ok\":true}",
            recordedAt: baseTime.AddMinutes(-1),
            preassignedArtifactId: StableGuid("report-artifact"));
        var rosterEntries = new[]
        {
            CreateRosterEntry(electionId, "voter-1", "actor-voter-1", baseTime),
            CreateRosterEntry(electionId, "voter-2", "actor-voter-2", baseTime),
        };
        var participationRecords = new[]
        {
            ElectionModelFactory.CreateParticipationRecord(
                electionId,
                "voter-1",
                ElectionParticipationStatus.CountedAsVoted,
                recordedAt: openedAt.AddMinutes(4)),
            ElectionModelFactory.CreateParticipationRecord(
                electionId,
                "voter-2",
                ElectionParticipationStatus.CountedAsVoted,
                recordedAt: openedAt.AddMinutes(4)),
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
                consumedAt: openedAt.AddMinutes(4)) with
            {
                Id = StableGuid("checkoff-consumption-a"),
            },
            ElectionModelFactory.CreateCheckoffConsumptionRecord(
                electionId,
                "voter-2",
                consumedAt: openedAt.AddMinutes(4)) with
            {
                Id = StableGuid("checkoff-consumption-b"),
            },
        };
        var rosterCanonicalHash = ElectionEligibilityContracts.ComputeRosterCanonicalHash(rosterEntries);

        var request = new ElectionVerificationPackageExportRequest(
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
            VerificationPackageView.PublicAnonymous,
            VerificationProfileIds.PublicAnonymousV1,
            RestrictedAccessAuthorized: false,
            ExportedAt: baseTime,
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
                    importedAt: openedAt) with
                {
                    RosterImportId = StableGuid("roster-import-evidence"),
                }
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
                    declaredAt: openedAt) with
                {
                    Id = StableGuid("eligibility-policy-evidence"),
                }
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
                    declaredAt: openedAt) with
                {
                    Id = StableGuid("commitment-scheme-evidence"),
                }
            ],
            CommitmentRegistrations: commitmentRegistrations,
            CheckoffConsumptions: checkoffConsumptions,
            EligibilityActivationEvents: []);

        request = WithSyntheticSp07Evidence(request, baseTime);
        request = WithCompleteSp10OperationalSecurityStatus(WithOfficialSp08ReleaseManifest(request));
        return request;
    }

    private static ElectionVerificationPackageExportRequest WithSyntheticSp07Evidence(
        ElectionVerificationPackageExportRequest request,
        DateTime baseTime)
    {
        var witnessSetId = StableGuid("sp07-witness-set");
        var proofBytes = "synthetic-proof-bytes";
        var proofHash = HashHex(proofBytes);
        var session = new ElectionPublicationProofSessionRecord(
            StableGuid("sp07-session"),
            request.Election.ElectionId,
            witnessSetId,
            ElectionSp07ProfileIds.PublicationProofMode,
            ElectionSp07ProfileIds.ProofConstruction,
            ElectionSp07ProfileIds.StatementId,
            ElectionPublicationProofSessionStatus.WitnessDeleted,
            baseTime.AddMinutes(-6),
            baseTime.AddMinutes(-5),
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
            ProofHash: proofHash,
            ServerVerifierOutputHash: "sp07-server-verifier-output-hash",
            DeletionReceiptId: null);
        var transcript = new ElectionPublicationProofTranscriptRecord(
            StableGuid("sp07-transcript"),
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
            proofBytes,
            proofHash,
            session.TranscriptHash!,
            ElectionSp07ProfileIds.ExternalReviewStatus,
            baseTime.AddMinutes(-4),
            GeneratorReleaseHash: "generator-release-hash",
            VerifierReleaseHash: "verifier-release-hash",
            PublicPrivacyBoundary:
            [
                "no_hidden_permutation",
                "no_shuffle_map",
                "no_rerandomization_randomness",
                "no_raw_witness",
            ]);
        var receipt = new ElectionPublicationWitnessDeletionReceiptRecord(
            StableGuid("sp07-deletion-receipt"),
            request.Election.ElectionId,
            session.Id,
            session.WitnessSetId,
            WitnessSetHash: "witness-set-hash",
            WitnessCount: request.AcceptedBallots.Count,
            transcript.TranscriptHash,
            transcript.ProofHash,
            ElectionPublicationWitnessDeletionStatus.Completed,
            baseTime.AddMinutes(-3),
            DeletionActorRef: "proof-worker",
            FailureCode: null,
            FailureReason: null);

        return request with
        {
            PublicationProofSessions = [session],
            PublicationProofTranscripts = [transcript],
            PublicationWitnessDeletionReceipts = [receipt],
        };
    }

    private static ElectionVerificationPackageExportRequest WithOfficialSp08ReleaseManifest(
        ElectionVerificationPackageExportRequest request) =>
        request with
        {
            Sp08ReleaseManifest = CreateOfficialSp08ReleaseManifest(request),
        };

    private static ElectionVerificationPackageExportRequest WithCompleteSp10OperationalSecurityStatus(
        ElectionVerificationPackageExportRequest request)
    {
        var requestWithRelease = request.Sp08ReleaseManifest is null
            ? WithOfficialSp08ReleaseManifest(request)
            : request;
        var releaseManifest = requestWithRelease.Sp08ReleaseManifest!;
        var releaseManifestHash = ElectionSp08ReleaseManifestHasher.ComputeReleaseManifestHash(releaseManifest);
        var serverComponent = releaseManifest.Components.Single(x =>
            string.Equals(x.ComponentId, ElectionSp08ProfileIds.ServerComponent, StringComparison.Ordinal));
        var evidenceState = ElectionSp10ProfileIds.EvidenceStateManagedProfileEvidenceAvailable;

        return requestWithRelease with
        {
            Sp10OperationalSecurityStatus = new ElectionSp10OperationalSecurityStatusArtifactRecord(
                Schema: ElectionSp10ProfileIds.OperationalSecuritySummarySchema,
                requestWithRelease.Election.ElectionId.ToString(),
                ElectionSp10ProfileIds.OperationalSecurityProgramVersion,
                ElectionSp10ProfileIds.DeploymentProfileManagedAwsContainerV1,
                evidenceState,
                DoesNotCompleteFeat106Readiness: true,
                Feat106ReadinessCaveat: ElectionSp10OperationalSecurityRules.GetAllowedWordingForEvidenceState(evidenceState),
                ReleaseEvidenceMode: releaseManifest.EvidenceMode,
                ReleaseManifestHash: releaseManifestHash,
                ImmutableDeploymentRef: serverComponent.ImmutableReference,
                CustodyMode: ElectionSp10ProfileIds.CustodyModeAwsKmsPerElectionEnvelopeV1,
                ExecutorKeyLifecycle: ElectionSp10ProfileIds.ExecutorKeyLifecycleEphemeralMemoryV1,
                AccessSnapshotHashOrRestrictedRef: "sha256:access-snapshot",
                BackupRestoreHashOrRestrictedRef: "sha256:backup-restore",
                IncidentStatus: ElectionSp10ProfileIds.IncidentStatusNoIncidentDeclared,
                AuditorRoomAccessLogHashOrRestrictedRef: "sha256:auditor-room-access-log",
                BlocksHighAssurance: false,
                PrimaryResultCode: VerificationResultCodes.OperationalSecurityEvidenceValid,
                PrimaryIssue: null,
                PublicEvidenceFiles:
                [
                    VerificationPackageFileNames.Sp10OperationalSecuritySummary,
                    VerificationPackageFileNames.Sp10OperationalDeploymentEvidence,
                    VerificationPackageFileNames.Sp10OperationalCustodyEvidence,
                    VerificationPackageFileNames.Sp10OperationalVerifierOutput,
                ],
                RestrictedEvidenceFiles: [],
                PublicPrivacyBoundary:
                [
                    "no_raw_log_line",
                    "no_raw_audit_log",
                    "no_ip_address",
                    "no_device_id",
                    "no_kms_plaintext_key",
                    "no_kms_unwrapped_key",
                    "no_executor_private_key",
                    "no_iam_policy_document",
                    "no_security_group_rule_dump",
                    "no_raw_backup_archive",
                    "no_incident_workpaper",
                    "no_regulatory_workpaper",
                    "no_authority_private_correspondence",
                    "no_voter_detail",
                    "no_plaintext_vote",
                    "no_raw_trustee_share",
                    "no_proof_witness",
                ]),
        };
    }

    private static ElectionSp08ReleaseManifestArtifactRecord CreateOfficialSp08ReleaseManifest(
        ElectionVerificationPackageExportRequest request)
    {
        var releaseId = "release-2026.05.20";
        var sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var sourceTag = "hush-voting-2026.05.20";
        var serverDigest = Sp08Digest("server");
        var webDigest = Sp08Digest("web-client");
        var verifierDigest = Sp08Digest("standalone-verifier");
        var sp07Digest = Sp08Digest("sp07-worker");
        var protocolDigest = $"sha256:{request.ProtocolPackageBinding!.ReleaseManifestHash}";
        var exporterDigest = Sp08Digest("audit-package-exporter");

        return new ElectionSp08ReleaseManifestArtifactRecord(
            Schema: ElectionSp08ProfileIds.ReleaseManifestSchema,
            ManifestId: "release-manifest-2026-05-20",
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
            ]);
    }

    private static ElectionSp08ReleaseComponentArtifactRecord CreateOfficialSp08Component(
        string componentId,
        string digest,
        string sourceCommit,
        string sourceTag) =>
        new(
            componentId,
            componentId,
            ElectionSp08ProfileIds.EvidenceModeOfficial,
            $"{componentId}.artifact",
            digest,
            sourceCommit,
            sourceTag,
            $"{componentId}@{digest}",
            BuildWorkflowRunId: "1234567890",
            DistributionReference: null,
            SigningFingerprint: null,
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

    private static ProtocolPackageBindingRecord CreateSealedProtocolBinding(
        ElectionId electionId,
        string profileId)
    {
        var accessLocation = ElectionModelFactory.CreateProtocolPackageAccessLocation(
            ProtocolPackageAccessLocationKind.Repository,
            "Repository",
            "https://github.com/Hushnetwork-social/protocol-omega-packages",
            HashHex("access"));
        var catalogEntry = ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            "omega-hushvoting-v1",
            "v1.2.0",
            HashHex("spec"),
            HashHex("proof"),
            HashHex("release"),
            [profileId],
            ProtocolPackageApprovalStatus.ApprovedInternal,
            isLatestForCompatibleProfiles: true,
            [accessLocation],
            [accessLocation],
            approvedAt: DateTime.UnixEpoch);

        var binding = ElectionModelFactory.CreateProtocolPackageBindingFromCatalog(
                electionId,
                catalogEntry,
                profileId,
                draftRevision: 1,
                boundByPublicAddress: "owner-address",
                boundAt: DateTime.UnixEpoch)
            .SealAtOpen(DateTime.UnixEpoch, "owner-address");

        return binding with
        {
            Id = StableGuid($"protocol-binding-{profileId}"),
        };
    }

    private static ElectionRosterEntryRecord CreateRosterEntry(
        ElectionId electionId,
        string organizationVoterId,
        string actorPublicAddress,
        DateTime recordedAt) =>
        new(
            electionId,
            organizationVoterId,
            ElectionRosterContactType.Email,
            $"{organizationVoterId}@example.test",
            ElectionVoterLinkStatus.Linked,
            actorPublicAddress,
            recordedAt,
            ElectionVotingRightStatus.Active,
            recordedAt,
            WasPresentAtOpen: true,
            WasActiveAtOpen: true,
            LastActivatedAt: recordedAt,
            LastActivatedByPublicAddress: "owner-address",
            LastUpdatedAt: recordedAt,
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

    private static Guid StableGuid(string value)
    {
        Span<byte> bytes = stackalloc byte[16];
        SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16).CopyTo(bytes);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private static byte[] HashBytes(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static string HashHex(string value) =>
        Convert.ToHexString(HashBytes(value)).ToLowerInvariant();

    private static string Sp08Digest(string value) =>
        $"sha256:{HashHex(value)}";
}
