using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushNode.IntegrationTests;

[Trait("Category", "FEAT-119")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class ElectionSp09ExternalReviewIntegrationTests
{
    [Fact]
    public async Task PublicPackage_WithPlannedExternalReviewStatus_ReplaysWithExpectedWarnings()
    {
        using var package = CreatePackage();

        var status = await ReadArtifactAsync<ElectionSp09ExternalReviewStatusArtifactRecord>(
            package.PackagePath,
            VerificationPackageFileNames.Sp09ExternalReviewStatus);
        status.ProgramVersion.Should().Be(ElectionSp09ProfileIds.ExternalExaminationProgramVersion);
        status.ReviewScope.Should().Be(ElectionSp09ProfileIds.ReviewScopeProtocolOmegaV1);
        status.Availability.Should().Be(ElectionSp09ProfileIds.AvailabilityPlanned);
        status.ClaimState.Should().Be(ElectionSp09ProfileIds.ClaimStateProgramDefined);
        status.RestrictedEvidenceFiles.Should().BeEmpty();

        var result = await VerifyPackageAsync(package.PackagePath);

        result.ExitCode.Should().Be(0, DescribeFailures(result));
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Warn);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp09ProfileIds.ReviewStatusValidCheckCode &&
            x.ResultCode == VerificationResultCodes.ExternalReviewStatusValid &&
            x.Status == VerificationCheckStatus.Pass);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp09ProfileIds.ReviewNotCompleteCheckCode &&
            x.ResultCode == VerificationResultCodes.ExternalReviewNotComplete &&
            x.Status == VerificationCheckStatus.Warn);
    }

    [Fact]
    public async Task PublicPackage_WithReviewedLimitationsStatus_CoversPackageHashesWithoutSp09Failure()
    {
        using var package = CreatePackage();
        await WriteArtifactAsync(
            package.PackagePath,
            VerificationPackageFileNames.Sp09ExternalReviewStatus,
            await CreateReviewedSp09StatusAsync(
                package.PackagePath,
                ElectionSp09ProfileIds.StatusReviewedWithLimitations));
        await RefreshAuditManifestAsync(package.PackagePath);

        var result = await VerifyPackageAsync(package.PackagePath);

        result.ExitCode.Should().Be(0, DescribeFailures(result));
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp09ProfileIds.ReviewStatusValidCheckCode &&
            x.ResultCode == VerificationResultCodes.ExternalReviewStatusValid &&
            x.Status == VerificationCheckStatus.Pass);
        result.Output.Results.Should().NotContain(x =>
            ElectionSp09ProfileIds.ExternalReviewCheckCodes.Contains(x.CheckCode) &&
            x.Status == VerificationCheckStatus.Fail);
        result.Output.Results.Should().NotContain(x =>
            x.CheckCode == ElectionSp09ProfileIds.ReviewNotCompleteCheckCode &&
            x.ResultCode == VerificationResultCodes.ExternalReviewNotComplete);
    }

    [Theory]
    [InlineData(
        "false_claim",
        ElectionSp09ProfileIds.ClaimNotAllowedCheckCode,
        VerificationResultCodes.ExternalReviewClaimNotAllowed)]
    [InlineData(
        "scope_mismatch",
        ElectionSp09ProfileIds.ScopeMismatchCheckCode,
        VerificationResultCodes.ExternalReviewScopeMismatch)]
    [InlineData(
        "public_boundary",
        ElectionSp09ProfileIds.PublicBoundaryViolationCheckCode,
        VerificationResultCodes.ExternalReviewPublicBoundaryViolation)]
    [InlineData(
        "open_finding",
        ElectionSp09ProfileIds.OpenFindingsBlockClaimsCheckCode,
        VerificationResultCodes.ExternalReviewOpenFindingsBlockClaims)]
    [InlineData(
        "requires_redesign",
        ElectionSp09ProfileIds.RequiresRedesignCheckCode,
        VerificationResultCodes.ExternalReviewRequiresRedesign)]
    public async Task PublicPackage_WithExternalReviewTamper_FailsExpectedRevCode(
        string fixtureName,
        string expectedCheckCode,
        string expectedResultCode)
    {
        using var package = CreatePackage();
        await ApplySp09TamperAsync(package.PackagePath, fixtureName);
        await RefreshAuditManifestAsync(package.PackagePath);

        var result = await VerifyPackageAsync(package.PackagePath);

        result.ExitCode.Should().Be(1);
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Fail);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == expectedCheckCode &&
            x.ResultCode == expectedResultCode &&
            x.Status == VerificationCheckStatus.Fail);
    }

    private static TemporaryPackageDirectory CreatePackage()
    {
        var directory = new TemporaryPackageDirectory();
        try
        {
            var export = new ElectionVerificationPackageExportService().Export(CreateRequest());
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

    private static ElectionVerificationPackageExportRequest CreateRequest()
    {
        var electionId = ElectionId.NewElectionId;
        var draftElection = ElectionModelFactory.CreateDraftRecord(
            electionId,
            title: "FEAT-119 external review integration",
            shortDescription: "SP-09 external examination package integration",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "FEAT-119",
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
            protocolOmegaVersion: "omega-v1.1.10",
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

        var firstPreparedId = Guid.NewGuid();
        var secondPreparedId = Guid.NewGuid();
        var acceptedBallots = new[]
        {
            ElectionModelFactory.CreateAcceptedBallotRecord(
                electionId,
                "encrypted-ballot-a",
                "proof-bundle-a",
                "nullifier-a",
                preparedBallotId: firstPreparedId,
                preparedBallotHash: "prepared-ballot-a",
                receiptCommitment: "receipt-a",
                receiptCommitmentScheme: "sha256(receipt_secret|prepared_ballot_hash|accepted_ballot_id)",
                ballotDefinitionVersion: ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionHash: ballotDefinitionSeal.BallotDefinitionHash),
            ElectionModelFactory.CreateAcceptedBallotRecord(
                electionId,
                "encrypted-ballot-b",
                "proof-bundle-b",
                "nullifier-b",
                preparedBallotId: secondPreparedId,
                preparedBallotHash: "prepared-ballot-b",
                receiptCommitment: "receipt-b",
                receiptCommitmentScheme: "sha256(receipt_secret|prepared_ballot_hash|accepted_ballot_id)",
                ballotDefinitionVersion: ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionHash: ballotDefinitionSeal.BallotDefinitionHash),
        };
        var preparedBallots = new[]
        {
            ElectionModelFactory.CreatePreparedBallotCommitmentRecord(
                electionId,
                "voter-1",
                "actor-voter-1",
                "prepared-ballot-a",
                ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionSeal.BallotDefinitionHash,
                "sp04-proof",
                openedAt.AddMinutes(1),
                preparedBallotId: firstPreparedId) with
            {
                State = ElectionPreparedBallotState.Cast,
                AcceptedBallotId = acceptedBallots[0].Id,
                CastAt = openedAt.AddMinutes(2),
            },
            ElectionModelFactory.CreatePreparedBallotCommitmentRecord(
                electionId,
                "voter-2",
                "actor-voter-2",
                "prepared-ballot-b",
                ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionSeal.BallotDefinitionHash,
                "sp04-proof",
                openedAt.AddMinutes(1),
                preparedBallotId: secondPreparedId) with
            {
                State = ElectionPreparedBallotState.Cast,
                AcceptedBallotId = acceptedBallots[1].Id,
                CastAt = openedAt.AddMinutes(2),
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
        var finalizedElection = openedElection with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            ClosedAt = openedAt.AddHours(1),
            FinalizedAt = openedAt.AddHours(2),
            CloseArtifactId = closeArtifact.Id,
            TallyReadyArtifactId = Guid.NewGuid(),
            UnofficialResultArtifactId = Guid.NewGuid(),
            OfficialResultArtifactId = Guid.NewGuid(),
            FinalizeArtifactId = Guid.NewGuid(),
        };

        return new ElectionVerificationPackageExportRequest(
            finalizedElection,
            CreateProtocolPackageBinding(finalizedElection),
            CreateReportPackage(finalizedElection),
            ReportArtifacts: [],
            BoundaryArtifacts: [closeArtifact],
            acceptedBallots,
            publishedBallots,
            FinalizationSessions: [],
            FinalizationShares: [],
            ReleaseEvidenceRecords: [],
            RosterEntries: [],
            ParticipationRecords: [],
            VerificationPackageView.PublicAnonymous,
            VerificationProfileIds.DevelopmentCurrentV1,
            RestrictedAccessAuthorized: false,
            ExportedAt: DateTime.UnixEpoch.AddHours(3),
            PreparedBallotCommitments: preparedBallots,
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
                    declaredAt: openedAt),
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
                    declaredAt: openedAt),
            ]);
    }

    private static ProtocolPackageBindingRecord CreateProtocolPackageBinding(ElectionRecord election)
    {
        var catalogEntry = ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            "omega-hushvoting-v1",
            "v1.1.10",
            Hash('a'),
            Hash('b'),
            Hash('c'),
            compatibleProfileIds: [election.SelectedProfileId],
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
            frozenEvidenceHash: HashBytes("frozen-evidence"),
            frozenEvidenceFingerprint: "sha256:frozen-evidence",
            packageHash: HashBytes("report-package"),
            artifactCount: 1,
            attemptedByPublicAddress: election.OwnerPublicAddress,
            attemptedAt: DateTime.UnixEpoch.AddHours(3),
            sealedAt: DateTime.UnixEpoch.AddHours(3).AddMinutes(1));

    private static OutcomeRuleDefinition CreatePassFailRule() =>
        new(
            OutcomeRuleKind.SingleWinner,
            "single_winner",
            SeatCount: 1,
            BlankVoteCountsForTurnout: true,
            BlankVoteExcludedFromWinnerSelection: true,
            BlankVoteExcludedFromThresholdDenominator: false,
            TieResolutionRule: "tie_unresolved",
            CalculationBasis: "highest_non_blank_votes");

    private static async Task ApplySp09TamperAsync(string packagePath, string fixtureName)
    {
        switch (fixtureName)
        {
            case "false_claim":
                var claimTable = await ReadArtifactAsync<ElectionSp09ExternalReviewClaimTableArtifactRecord>(
                    packagePath,
                    VerificationPackageFileNames.Sp09ExternalReviewClaimTable);
                await WriteArtifactAsync(
                    packagePath,
                    VerificationPackageFileNames.Sp09ExternalReviewClaimTable,
                    claimTable with
                    {
                        Claims =
                        [
                            claimTable.Claims[0] with { AllowedWording = "Certified for public elections." },
                            .. claimTable.Claims.Skip(1),
                        ],
                    });
                break;
            case "scope_mismatch":
                await WriteArtifactAsync(
                    packagePath,
                    VerificationPackageFileNames.Sp09ExternalReviewStatus,
                    await CreateReviewedSp09StatusAsync(packagePath) with
                    {
                        ReviewScopeMatchesElection = false,
                        Availability = ElectionSp09ProfileIds.AvailabilityAvailable,
                    });
                break;
            case "public_boundary":
                var publicBoundaryStatus = await ReadArtifactAsync<ElectionSp09ExternalReviewStatusArtifactRecord>(
                    packagePath,
                    VerificationPackageFileNames.Sp09ExternalReviewStatus);
                await WriteArtifactAsync(
                    packagePath,
                    VerificationPackageFileNames.Sp09ExternalReviewStatus,
                    publicBoundaryStatus with { PublicPrivacyBoundary = ["fullReportBody"] });
                break;
            case "open_finding":
                await WriteArtifactAsync(
                    packagePath,
                    VerificationPackageFileNames.Sp09ExternalReviewStatus,
                    await CreateReviewedSp09StatusAsync(
                        packagePath,
                        ElectionSp09ProfileIds.StatusReviewedWithLimitations,
                        findingSummary:
                        [
                            new ElectionSp09FindingSeverityCountRecord(
                                "high",
                                OpenCount: 1,
                                FixedCount: 0,
                                AcceptedLimitationCount: 0),
                        ]));
                break;
            case "requires_redesign":
                await WriteArtifactAsync(
                    packagePath,
                    VerificationPackageFileNames.Sp09ExternalReviewStatus,
                    await CreateReviewedSp09StatusAsync(
                        packagePath,
                        ElectionSp09ProfileIds.StatusRequiresRedesign,
                        claimState: ElectionSp09ProfileIds.ClaimStateReviewedForDeclaredScope,
                        primaryResultCode: VerificationResultCodes.ExternalReviewRequiresRedesign));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fixtureName), fixtureName, "Unknown SP-09 tamper fixture.");
        }
    }

    private static async Task<ElectionSp09ExternalReviewStatusArtifactRecord> CreateReviewedSp09StatusAsync(
        string packagePath,
        string detailedStatus = ElectionSp09ProfileIds.StatusReviewedForDeclaredScope,
        string? claimState = null,
        string primaryResultCode = VerificationResultCodes.ExternalReviewStatusValid,
        IReadOnlyList<ElectionSp09FindingSeverityCountRecord>? findingSummary = null)
    {
        var status = await ReadArtifactAsync<ElectionSp09ExternalReviewStatusArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp09ExternalReviewStatus);
        var electionRecord = await ReadArtifactAsync<ElectionRecordReferenceRecord>(
            packagePath,
            VerificationPackageFileNames.ElectionRecord);

        return status with
        {
            DetailedStatus = detailedStatus,
            Availability = ElectionSp09ExternalReviewRules.ProjectAvailability(detailedStatus),
            ClaimState = claimState ?? ElectionSp09ExternalReviewRules.GetDefaultClaimState(detailedStatus),
            PrimaryResultCode = primaryResultCode,
            PrimaryIssue = null,
            ReviewerEvidenceRef = "reviewer:engagement-42",
            ReportHashOrRestrictedRef = "sha256:review-report",
            CustomerSafeSummaryHash = "sha256:customer-safe-summary",
            ReviewedArtifacts = BuildSp09ReviewedArtifacts(electionRecord),
            FindingSummary = findingSummary ?? [],
        };
    }

    private static IReadOnlyList<ElectionSp09ReviewedArtifactRecord> BuildSp09ReviewedArtifacts(
        ElectionRecordReferenceRecord electionRecord) =>
    [
        new(
            "protocol-specification",
            "protocol_specification",
            "Protocol Omega HushVoting v1 specification package",
            BuildSp09Sha256Ref(electionRecord.ProtocolSpecificationHash),
            electionRecord.ProtocolPackageVersion,
            ElectionSp09ProfileIds.ReviewScopeProtocolOmegaV1),
        new(
            "protocol-proof",
            "protocol_proof",
            "Protocol Omega HushVoting v1 proof package",
            BuildSp09Sha256Ref(electionRecord.ProtocolProofPackageHash),
            electionRecord.ProtocolPackageVersion,
            ElectionSp09ProfileIds.ReviewScopeProtocolOmegaV1),
        new(
            "protocol-release-manifest",
            "protocol_release_manifest",
            "Protocol Omega HushVoting v1 release manifest",
            BuildSp09Sha256Ref(electionRecord.ProtocolReleaseManifestHash),
            electionRecord.ProtocolPackageVersion,
            ElectionSp09ProfileIds.ReviewScopeProtocolOmegaV1),
    ];

    private static string BuildSp09Sha256Ref(string hash) =>
        hash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? hash : $"sha256:{hash}";

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

    private static Task<HushVotingPackageVerificationResult> VerifyPackageAsync(string packagePath) =>
        new HushVotingPackageVerifier().VerifyAsync(new(
            packagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

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

    private static string Hash(char value) =>
        new(char.ToLowerInvariant(value), 64);

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
            $"hush-sp09-integration-{Guid.NewGuid():N}");

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
