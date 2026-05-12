using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushNode.IntegrationTests;

[Trait("Category", "FEAT-120")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class ElectionSp10OperationalSecurityIntegrationTests
{
    private static readonly string[] Sp10PublicFiles =
    [
        VerificationPackageFileNames.Sp10OperationalSecuritySummary,
        VerificationPackageFileNames.Sp10OperationalDeploymentEvidence,
        VerificationPackageFileNames.Sp10OperationalCustodyEvidence,
        VerificationPackageFileNames.Sp10OperationalVerifierOutput,
    ];

    private static readonly string[] Sp10RestrictedFiles =
    [
        VerificationPackageFileNames.RestrictedSp10AccessControlSnapshot,
        VerificationPackageFileNames.RestrictedSp10LoggingEvidence,
        VerificationPackageFileNames.RestrictedSp10BackupRestoreEvidence,
        VerificationPackageFileNames.RestrictedSp10IncidentEvidence,
        VerificationPackageFileNames.RestrictedSp10AuditorRoomAccessLog,
    ];

    private static readonly string[] ForbiddenMaterialMarkers =
    [
        "rawLogLine",
        "kmsPlaintextKey",
        "executorPrivateKey",
        "iamPolicyDocument",
        "incidentWorkpaper",
        "regulatoryWorkpaper",
        "authorityPrivateCorrespondence",
    ];

    [Fact]
    public async Task PublicPackage_WithManagedProfileOperationalEvidence_ReplaysOpsWithoutRestrictedLeak()
    {
        using var package = CreateManagedPackage(
            VerificationPackageView.PublicAnonymous,
            VerificationProfileIds.DevelopmentCurrentV1,
            includeRegulatoryClaim: true);

        var manifest = await ReadArtifactAsync<AuditPackageManifestRecord>(
            package.PackagePath,
            VerificationPackageFileNames.AuditPackageManifest);
        manifest.PackageView.Should().Be(VerificationPackageView.PublicAnonymous);
        manifest.Entries.Select(x => x.Path).Should().Contain(Sp10PublicFiles);
        manifest.Entries.Select(x => x.Path).Should().Contain(VerificationPackageFileNames.Sp11RegulatoryClaimState);
        manifest.Entries.Should().NotContain(x => VerificationPrivacyBoundary.IsRestrictedArtifactEntry(x));

        var operationalStatus = await ReadArtifactAsync<ElectionSp10OperationalSecurityStatusArtifactRecord>(
            package.PackagePath,
            VerificationPackageFileNames.Sp10OperationalSecuritySummary);
        operationalStatus.EvidenceState.Should().Be(ElectionSp10ProfileIds.EvidenceStateManagedProfileEvidenceAvailable);
        operationalStatus.PrimaryResultCode.Should().Be(VerificationResultCodes.OperationalSecurityEvidenceValid);
        operationalStatus.RestrictedEvidenceFiles.Should().BeEmpty();
        operationalStatus.DoesNotCompleteFeat106Readiness.Should().BeTrue();

        var claim = await ReadArtifactAsync<ElectionSp11RegulatoryClaimStateArtifactRecord>(
            package.PackagePath,
            VerificationPackageFileNames.Sp11RegulatoryClaimState);
        claim.ClaimState.Should().Be(ElectionSp11ProfileIds.ClaimStateAllowedWithLimitation);
        claim.RestrictedWorkpaperRef.Should().BeNull();
        claim.RestrictedEvidenceFiles.Should().BeEmpty();

        AssertPackagePayloadDoesNotContainForbiddenMaterial(package.PackagePath);

        var result = await VerifyPackageAsync(package.PackagePath, VerificationProfileIds.DevelopmentCurrentV1);

        result.ExitCode.Should().Be(0, DescribeFailures(result));
        result.Output.Results
            .Where(x => ElectionSp10ProfileIds.OperationalCheckCodes.Contains(x.CheckCode))
            .Should()
            .OnlyContain(x => x.Status == VerificationCheckStatus.Pass);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp11ProfileIds.RegulatoryClaimShapeValidCheckCode &&
            x.ResultCode == VerificationResultCodes.RegulatoryClaimShapeValid &&
            x.Status == VerificationCheckStatus.Pass);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp11ProfileIds.ClaimAllowedByRegisterCheckCode &&
            x.ResultCode == VerificationResultCodes.RegulatoryClaimAllowedByRegister &&
            x.Status == VerificationCheckStatus.Pass);
    }

    [Fact]
    public async Task RestrictedPackage_WithAuthorizedOperationalEvidence_ReplaysAndContainsRestrictedArtifacts()
    {
        using var package = CreateManagedPackage(
            VerificationPackageView.RestrictedOwnerAuditor,
            VerificationProfileIds.RestrictedOwnerAuditorV1,
            includeRegulatoryClaim: true,
            includeRestrictedRegulatoryWorkpaper: true);

        var manifest = await ReadArtifactAsync<AuditPackageManifestRecord>(
            package.PackagePath,
            VerificationPackageFileNames.AuditPackageManifest);
        manifest.PackageView.Should().Be(VerificationPackageView.RestrictedOwnerAuditor);
        manifest.Entries.Select(x => x.Path).Should().Contain(Sp10RestrictedFiles);
        manifest.Entries.Should().Contain(x =>
            x.Path == VerificationPackageFileNames.RestrictedSp11RegulatoryJurisdictionWorkpaper &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        manifest.Entries.Where(VerificationPrivacyBoundary.IsRestrictedArtifactEntry)
            .Should()
            .Contain(x => x.Path == VerificationPackageFileNames.RestrictedSp10AccessControlSnapshot);

        var operationalStatus = await ReadArtifactAsync<ElectionSp10OperationalSecurityStatusArtifactRecord>(
            package.PackagePath,
            VerificationPackageFileNames.Sp10OperationalSecuritySummary);
        operationalStatus.RestrictedEvidenceFiles.Should().BeEquivalentTo(Sp10RestrictedFiles);

        var workpaper = await ReadArtifactAsync<ElectionSp11RestrictedJurisdictionWorkpaperArtifactRecord>(
            package.PackagePath,
            VerificationPackageFileNames.RestrictedSp11RegulatoryJurisdictionWorkpaper);
        workpaper.ReviewState.Should().Be(ElectionSp11ProfileIds.ClaimStateAllowedWithLimitation);

        AssertPackagePayloadDoesNotContainForbiddenMaterial(package.PackagePath);

        var result = await VerifyPackageAsync(package.PackagePath, VerificationProfileIds.RestrictedOwnerAuditorV1);

        result.ExitCode.Should().Be(0, DescribeFailures(result));
        result.Output.Results
            .Where(x => ElectionSp10ProfileIds.OperationalCheckCodes.Contains(x.CheckCode))
            .Should()
            .OnlyContain(x => x.Status == VerificationCheckStatus.Pass);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp11ProfileIds.RestrictedWorkpaperBoundaryCheckCode &&
            x.Status == VerificationCheckStatus.Pass);
    }

    [Theory]
    [InlineData(
        "release_mismatch",
        ElectionSp10ProfileIds.ReleaseDeploymentBindingCheckCode,
        VerificationResultCodes.OperationalSecurityReleaseBindingMissing,
        VerificationCheckStatus.Fail)]
    [InlineData(
        "unsupported_custody",
        ElectionSp10ProfileIds.CustodyModeDeclaredCheckCode,
        VerificationResultCodes.OperationalSecurityCustodyModeMissing,
        VerificationCheckStatus.Fail)]
    [InlineData(
        "missing_incident_status",
        ElectionSp10ProfileIds.IncidentDeclarationCheckCode,
        VerificationResultCodes.OperationalSecurityIncidentDeclarationMissing,
        VerificationCheckStatus.Fail)]
    [InlineData(
        "forbidden_leak",
        ElectionSp10ProfileIds.ForbiddenMaterialScanCheckCode,
        VerificationResultCodes.OperationalSecurityForbiddenMaterial,
        VerificationCheckStatus.Fail)]
    [InlineData(
        "false_regulatory_claim",
        ElectionSp11ProfileIds.BlockedCertificationClaimCheckCode,
        VerificationResultCodes.RegulatoryClaimBlockedCertification,
        VerificationCheckStatus.Fail)]
    public async Task PublicPackage_WithFeat120Tamper_FailsExpectedOpsOrRegCode(
        string fixtureName,
        string expectedCheckCode,
        string expectedResultCode,
        VerificationCheckStatus expectedStatus)
    {
        using var package = CreateManagedPackage(
            VerificationPackageView.PublicAnonymous,
            VerificationProfileIds.DevelopmentCurrentV1,
            includeRegulatoryClaim: true);
        await ApplyFeat120TamperAsync(package.PackagePath, fixtureName);

        var result = await VerifyPackageAsync(package.PackagePath, VerificationProfileIds.DevelopmentCurrentV1);

        result.ExitCode.Should().Be(expectedStatus == VerificationCheckStatus.Fail ? 1 : 0);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == expectedCheckCode &&
            x.ResultCode == expectedResultCode &&
            x.Status == expectedStatus);
    }

    private static TemporaryPackageDirectory CreateManagedPackage(
        VerificationPackageView view,
        string profileId,
        bool includeRegulatoryClaim = false,
        bool includeRestrictedRegulatoryWorkpaper = false)
    {
        var directory = new TemporaryPackageDirectory();
        try
        {
            var request = WithCompleteSp10OperationalSecurityStatus(
                WithOfficialSp08ReleaseManifest(CreateRequest(
                    view,
                    restrictedAccessAuthorized: view == VerificationPackageView.RestrictedOwnerAuditor,
                    profileId)),
                includeRestrictedEvidenceFiles: view == VerificationPackageView.RestrictedOwnerAuditor);
            if (includeRegulatoryClaim)
            {
                request = request with
                {
                    Sp11RegulatoryClaimState = CreateRegulatoryClaimState(
                        includeRestrictedRegulatoryWorkpaper
                            ? "sha256:jurisdiction-workpaper"
                            : null),
                };
            }

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

    private static ElectionVerificationPackageExportRequest CreateRequest(
        VerificationPackageView view,
        bool restrictedAccessAuthorized,
        string profileId)
    {
        var electionId = ElectionId.NewElectionId;
        var draftElection = ElectionModelFactory.CreateDraftRecord(
            electionId,
            title: "FEAT-120 operational package integration",
            shortDescription: "SP-10/SP-11 non-E2E package replay",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "FEAT-120",
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
            protocolOmegaVersion: "omega-v1.1.11",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.GovernedReviewWindowReserved,
            ownerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", "Approve", 1, false),
                new ElectionOptionDefinition("no", "No", "Reject", 2, false),
            ],
            officialResultVisibilityPolicy: OfficialResultVisibilityPolicy.PublicPlaintext);

        var openedAt = DateTime.UnixEpoch.AddHours(1);
        var ballotDefinitionSeal = ElectionModelFactory.CreateBallotDefinitionSeal(
            ElectionBallotDefinitionCanonicalizer.CurrentVersion,
            ElectionBallotDefinitionCanonicalizer.ComputeHash(draftElection),
            openedAt);
        var openedElection = draftElection with
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
            openedElection,
            recordedByPublicAddress: "owner-address",
            acceptedBallotCount: acceptedBallots.Length,
            acceptedBallotSetHash: VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(acceptedBallots),
            publishedBallotCount: publishedBallots.Length,
            publishedBallotStreamHash: VerificationCanonicalHash.ComputePublishedBallotStreamHash(publishedBallots),
            finalEncryptedTallyHash: HashBytes("final-encrypted-tally"));
        var tallyReadyArtifactId = Guid.NewGuid();
        var unofficialResultArtifactId = Guid.NewGuid();
        var officialResultArtifactId = Guid.NewGuid();
        var finalizeArtifactId = Guid.NewGuid();
        var finalizedElection = openedElection with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            ClosedAt = openedAt.AddHours(1),
            FinalizedAt = openedAt.AddHours(2),
            CloseArtifactId = closeArtifact.Id,
            TallyReadyArtifactId = tallyReadyArtifactId,
            UnofficialResultArtifactId = unofficialResultArtifactId,
            OfficialResultArtifactId = officialResultArtifactId,
            FinalizeArtifactId = finalizeArtifactId,
        };

        var reportPackage = ElectionModelFactory.CreateSealedReportPackage(
            electionId,
            attemptNumber: 1,
            tallyReadyArtifactId,
            unofficialResultArtifactId,
            officialResultArtifactId,
            finalizeArtifactId,
            frozenEvidenceHash: HashBytes("frozen-evidence"),
            frozenEvidenceFingerprint: "sha256:frozen-evidence",
            packageHash: HashBytes("report-package"),
            artifactCount: 1,
            attemptedByPublicAddress: "owner-address",
            attemptedAt: openedAt.AddHours(2),
            sealedAt: openedAt.AddHours(2).AddMinutes(1),
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
                consumedAt: openedAt.AddMinutes(4)),
            ElectionModelFactory.CreateCheckoffConsumptionRecord(
                electionId,
                "voter-2",
                consumedAt: openedAt.AddMinutes(4)),
        };
        var rosterCanonicalHash = ElectionEligibilityContracts.ComputeRosterCanonicalHash(rosterEntries);

        return new ElectionVerificationPackageExportRequest(
            finalizedElection,
            CreateProtocolPackageBinding(finalizedElection, profileId),
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
            ExportedAt: openedAt.AddHours(3),
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

    private static ElectionVerificationPackageExportRequest WithOfficialSp08ReleaseManifest(
        ElectionVerificationPackageExportRequest request) =>
        request with
        {
            Sp08ReleaseManifest = CreateOfficialSp08ReleaseManifest(request),
        };

    private static ElectionVerificationPackageExportRequest WithCompleteSp10OperationalSecurityStatus(
        ElectionVerificationPackageExportRequest request,
        bool includeRestrictedEvidenceFiles)
    {
        var releaseManifest = request.Sp08ReleaseManifest!;
        var releaseManifestHash = ElectionSp08ReleaseManifestHasher.ComputeReleaseManifestHash(releaseManifest);
        var serverComponent = releaseManifest.Components.Single(x =>
            string.Equals(x.ComponentId, ElectionSp08ProfileIds.ServerComponent, StringComparison.Ordinal));
        var evidenceState = ElectionSp10ProfileIds.EvidenceStateManagedProfileEvidenceAvailable;

        return request with
        {
            Sp10OperationalSecurityStatus = new ElectionSp10OperationalSecurityStatusArtifactRecord(
                Schema: ElectionSp10ProfileIds.OperationalSecuritySummarySchema,
                request.Election.ElectionId.ToString(),
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
                PublicEvidenceFiles: Sp10PublicFiles,
                RestrictedEvidenceFiles: includeRestrictedEvidenceFiles ? Sp10RestrictedFiles : [],
                PublicPrivacyBoundary: BuildSp10PublicPrivacyBoundary()),
        };
    }

    private static ElectionSp08ReleaseManifestArtifactRecord CreateOfficialSp08ReleaseManifest(
        ElectionVerificationPackageExportRequest request)
    {
        var releaseId = "release-2026.05.12";
        var sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var sourceTag = "hush-voting-2026.05.12";
        var serverDigest = Sp08Digest("server");
        var webDigest = Sp08Digest("web-client");
        var verifierDigest = Sp08Digest("standalone-verifier");
        var sp07Digest = Sp08Digest("sp07-worker");
        var protocolDigest = $"sha256:{request.ProtocolPackageBinding!.ReleaseManifestHash}";
        var exporterDigest = Sp08Digest("audit-package-exporter");

        return ElectionSp08ReleaseManifestGenerator.Generate(new ElectionSp08ReleaseManifestArtifactRecord(
            Schema: ElectionSp08ProfileIds.ReleaseManifestSchema,
            ManifestId: "release-manifest-2026-05-12",
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
            ]));
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
            $"ghcr.io/hushnetwork/{componentId}@{digest}",
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

    private static ElectionSp11RegulatoryClaimStateArtifactRecord CreateRegulatoryClaimState(
        string? restrictedWorkpaperRef = null) =>
        new(
            ElectionSp11ProfileIds.RegulatoryClaimStateSchema,
            JurisdictionId: "CH",
            ClaimId: "organizational-remote-voting-market-intelligence",
            TrackerVersion: ElectionSp11ProfileIds.RegulatoryTrackerVersion,
            ClaimState: ElectionSp11ProfileIds.ClaimStateAllowedWithLimitation,
            SourceCheckedAt: DateTimeOffset.UtcNow.AddDays(-1),
            NextReviewAt: DateTimeOffset.UtcNow.AddDays(30),
            SourceRef: "https://www.bk.admin.ch/bk/en/home/politische-rechte/e-voting.html",
            Owner: "protocol-omega-regulatory-tracker",
            IsLegalAdvice: false,
            RequiresAuthorityEvidence: false,
            AuthorityEvidenceRef: null,
            RestrictedWorkpaperRef: restrictedWorkpaperRef,
            AllowedWording: ElectionSp11RegulatoryRules.GetAllowedWordingForClaimState(
                ElectionSp11ProfileIds.ClaimStateAllowedWithLimitation),
            PublicEvidenceFiles:
            [
                VerificationPackageFileNames.Sp11RegulatoryClaimState,
            ],
            RestrictedEvidenceFiles: restrictedWorkpaperRef is null
                ? []
                : [VerificationPackageFileNames.RestrictedSp11RegulatoryJurisdictionWorkpaper],
            PublicPrivacyBoundary:
            [
                "no_legal_advice",
                "no_authority_private_correspondence",
                "no_jurisdiction_workpaper_body",
            ]);

    private static async Task ApplyFeat120TamperAsync(string packagePath, string fixtureName)
    {
        switch (fixtureName)
        {
            case "release_mismatch":
                await MutateSp10StatusAndRefreshAsync(
                    packagePath,
                    status => status with { ReleaseManifestHash = "sha256:tampered-release-manifest" });
                return;
            case "unsupported_custody":
                await MutateSp10StatusAndRefreshAsync(
                    packagePath,
                    status => status with { CustodyMode = "unsupported_custody_mode" });
                return;
            case "missing_incident_status":
                await MutateSp10StatusAndRefreshAsync(
                    packagePath,
                    status => status with { IncidentStatus = null });
                return;
            case "forbidden_leak":
                await MutateSp10StatusAndRefreshAsync(
                    packagePath,
                    status => status with
                    {
                        PublicPrivacyBoundary = ["rawLogLine"],
                        Feat106ReadinessCaveat = "Certified for public elections.",
                    });
                return;
            case "false_regulatory_claim":
                await MutateSp11ClaimAndRefreshAsync(
                    packagePath,
                    claim => claim with
                    {
                        ClaimState = ElectionSp11ProfileIds.ClaimStateBlockedUntilCertification,
                        RequiresAuthorityEvidence = true,
                        AuthorityEvidenceRef = null,
                        AllowedWording = ElectionSp11RegulatoryRules.GetAllowedWordingForClaimState(
                            ElectionSp11ProfileIds.ClaimStateBlockedUntilCertification),
                    });
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(fixtureName), fixtureName, "Unknown FEAT-120 tamper fixture.");
        }
    }

    private static async Task MutateSp10StatusAndRefreshAsync(
        string packagePath,
        Func<ElectionSp10OperationalSecurityStatusArtifactRecord, ElectionSp10OperationalSecurityStatusArtifactRecord> mutate)
    {
        var status = await ReadArtifactAsync<ElectionSp10OperationalSecurityStatusArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp10OperationalSecuritySummary);
        await WriteArtifactAsync(packagePath, VerificationPackageFileNames.Sp10OperationalSecuritySummary, mutate(status));
        await RefreshAuditManifestAsync(packagePath);
    }

    private static async Task MutateSp11ClaimAndRefreshAsync(
        string packagePath,
        Func<ElectionSp11RegulatoryClaimStateArtifactRecord, ElectionSp11RegulatoryClaimStateArtifactRecord> mutate)
    {
        var claim = await ReadArtifactAsync<ElectionSp11RegulatoryClaimStateArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp11RegulatoryClaimState);
        await WriteArtifactAsync(packagePath, VerificationPackageFileNames.Sp11RegulatoryClaimState, mutate(claim));
        await RefreshAuditManifestAsync(packagePath);
    }

    private static ProtocolPackageBindingRecord CreateProtocolPackageBinding(ElectionRecord election, string profileId)
    {
        var catalogEntry = ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            "omega-hushvoting-v1",
            "v1.1.11",
            Hash('a'),
            Hash('b'),
            Hash('c'),
            compatibleProfileIds: [profileId],
            approvalStatus: ProtocolPackageApprovalStatus.DraftPrivate,
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
                profileId,
                election.CurrentDraftRevision,
                election.OwnerPublicAddress)
            .SealAtOpen(election.OpenedAt!.Value, election.OwnerPublicAddress);
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
            DateTime.UnixEpoch.AddMinutes(1),
            ElectionVotingRightStatus.Active,
            DateTime.UnixEpoch.AddMinutes(1),
            WasPresentAtOpen: true,
            WasActiveAtOpen: true,
            LastActivatedAt: DateTime.UnixEpoch.AddMinutes(1),
            LastActivatedByPublicAddress: "owner-address",
            LastUpdatedAt: DateTime.UnixEpoch.AddMinutes(1),
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

    private static IReadOnlyList<string> BuildSp10PublicPrivacyBoundary() =>
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
    ];

    private static Task<HushVotingPackageVerificationResult> VerifyPackageAsync(
        string packagePath,
        string profileId) =>
        new HushVotingPackageVerifier().VerifyAsync(new(packagePath, profileId));

    private static async Task<T> ReadArtifactAsync<T>(string packagePath, string relativePath) =>
        JsonSerializer.Deserialize<T>(
            await File.ReadAllTextAsync(ResolvePackagePath(packagePath, relativePath)),
            VerificationJson.Options)!;

    private static async Task WriteArtifactAsync<T>(string packagePath, string relativePath, T value) =>
        await File.WriteAllTextAsync(
            ResolvePackagePath(packagePath, relativePath),
            JsonSerializer.Serialize(value, VerificationJson.Options));

    private static async Task RefreshAuditManifestAsync(string packagePath)
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

    private static void AssertPackagePayloadDoesNotContainForbiddenMaterial(string packagePath)
    {
        var payload = string.Join(
            '\n',
            Directory.EnumerateFiles(packagePath, "*.json", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        foreach (var marker in ForbiddenMaterialMarkers)
        {
            payload.Should().NotContain(marker);
        }
    }

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
            $"hush-sp10-integration-{Guid.NewGuid():N}");

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
}
