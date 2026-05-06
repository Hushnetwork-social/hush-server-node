using System.Text;
using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushNode.IntegrationTests;

[Trait("Category", "FEAT-116")]
[Trait("Category", "NON_E2E")]
public sealed class ElectionSp06PackageIntegrationTests
{
    [Fact]
    public async Task HighAssuranceTrusteePackage_WithMaterializedControlEvidence_VerifiesPublicAndRestrictedViews()
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
            acceptedBallotSetHash: [1, 2, 3],
            finalEncryptedTallyHash: [4, 5, 6],
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
        var publicRequest = CreateRequest(
            election,
            VerificationPackageView.PublicAnonymous,
            VerificationProfileIds.PublicAnonymousV1,
            finalizationSession,
            acceptedShares,
            controlDomains);
        var restrictedRequest = CreateRequest(
            election,
            VerificationPackageView.RestrictedOwnerAuditor,
            VerificationProfileIds.RestrictedOwnerAuditorV1,
            finalizationSession,
            acceptedShares,
            controlDomains);
        var exportService = new ElectionVerificationPackageExportService();
        var publicExport = exportService.Export(publicRequest);
        var restrictedExport = exportService.Export(restrictedRequest);
        var repeatedPublicExport = exportService.Export(publicRequest);

        publicExport.Success.Should().BeTrue();
        restrictedExport.Success.Should().BeTrue();
        publicExport.Files
            .Select(x => new
            {
                x.RelativePath,
                Hash = VerificationCanonicalHash.ComputeManifestFileSha256(x.Content),
            })
            .Should()
            .BeEquivalentTo(
                repeatedPublicExport.Files.Select(x => new
                {
                    x.RelativePath,
                    Hash = VerificationCanonicalHash.ComputeManifestFileSha256(x.Content),
                }),
                options => options.WithStrictOrdering());
        repeatedPublicExport.PackageHash.Should().Be(publicExport.PackageHash);
        Encoding.UTF8.GetString(publicExport.Files.Single(x =>
                x.RelativePath == VerificationPackageFileNames.TrusteeReleaseEvidence).Content)
            .Should()
            .NotContain("@hush.test");

        using var publicPackage = new TemporaryPackageDirectory();
        using var restrictedPackage = new TemporaryPackageDirectory();
        ElectionVerificationPackageExportService.WritePackageToDirectory(publicExport, publicPackage.PackagePath);
        ElectionVerificationPackageExportService.WritePackageToDirectory(restrictedExport, restrictedPackage.PackagePath);

        var publicVerification = await new HushVotingPackageVerifier().VerifyAsync(new(
            publicPackage.PackagePath,
            VerificationProfileIds.PublicAnonymousV1));
        var restrictedVerification = await new HushVotingPackageVerifier().VerifyAsync(new(
            restrictedPackage.PackagePath,
            VerificationProfileIds.RestrictedOwnerAuditorV1));

        publicVerification.Output.Results.Should().Contain(x =>
            x.CheckCode == "CTRL-000" &&
            x.ResultCode == VerificationResultCodes.TrusteeControlDomainEvidenceValid &&
            x.Status == VerificationCheckStatus.Pass);
        restrictedVerification.Output.Results.Should().Contain(x =>
            x.CheckCode == "CTRL-000" &&
            x.ResultCode == VerificationResultCodes.TrusteeControlDomainEvidenceValid &&
            x.Status == VerificationCheckStatus.Pass);
    }

    private static ElectionVerificationPackageExportRequest CreateRequest(
        ElectionRecord election,
        VerificationPackageView packageView,
        string verifierProfileId,
        ElectionFinalizationSessionRecord finalizationSession,
        IReadOnlyList<ElectionFinalizationShareRecord> acceptedShares,
        IReadOnlyList<ElectionTrusteeControlDomainRecord> controlDomains) =>
        new(
            election,
            CreateProtocolPackageBinding(election),
            CreateReportPackage(election),
            ReportArtifacts: [],
            BoundaryArtifacts: [],
            AcceptedBallots: [],
            PublishedBallots: [],
            FinalizationSessions: [finalizationSession],
            FinalizationShares: acceptedShares,
            ReleaseEvidenceRecords: [],
            RosterEntries: [],
            ParticipationRecords: [],
            packageView,
            verifierProfileId,
            RestrictedAccessAuthorized: true,
            ExportedAt: DateTime.UnixEpoch.AddHours(4),
            TrusteeControlDomainRecords: controlDomains);

    private static ElectionRecord CreateFinalizedHighAssuranceElection()
    {
        var election = ElectionModelFactory.CreateDraftRecord(
            ElectionId.NewElectionId,
            "FEAT-116 high assurance integration",
            "SP-06 package integration",
            "owner-address",
            "FEAT-116",
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
            approvedClientApplications: [new ApprovedClientApplicationRecord("hushsocial", "1.0.0")],
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

    private static string Hash(char value) =>
        new(char.ToLowerInvariant(value), 64);

    private sealed class TemporaryPackageDirectory : IDisposable
    {
        public string PackagePath { get; } = Path.Combine(
            Path.GetTempPath(),
            $"hush-sp06-integration-{Guid.NewGuid():N}");

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
