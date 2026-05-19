using FluentAssertions;
using HushNode.Elections;
using HushNode.Reactions.Crypto;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

[Trait("Category", "FEAT-131")]
[Trait("Category", "HV-KMS-CUSTODY")]
public class AdminOnlyProtectedTallyCustodyReconciliationTests
{
    [Fact]
    public void Run_DryRun_WithProviderOnlyKey_ReportsOrphanWithoutRepair()
    {
        var request = CreateRequest(
            AdminOnlyProtectedTallyCustodyReconciliationRunMode.DryRun,
            rows: [],
            providerKeys:
            [
                CreateProviderObservation("sha256:orphan", keyId: "orphan-key"),
            ]);

        var report = AdminOnlyProtectedTallyCustodyReconciliationEngine.Run(
            request,
            new TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority());

        report.Items.Should().ContainSingle();
        report.Items[0].ConditionCode.Should()
            .Be(AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.OrphanedProviderKey);
        report.Items[0].RepairResult.Should().Be("dry_run_no_change");
        report.Summary.BlocksReadinessGate.Should().BeTrue();
        report.ReadinessFragment.AcceptedGateIds.Should().BeEmpty();
    }

    [Fact]
    public void Run_DryRun_WithMismatchedProviderMetadata_ReportsAliasAndTagDrift()
    {
        var election = CreateAdminElection(ElectionLifecycleState.Finalized);
        var envelope = CreateDeletionScheduledEnvelope(election);
        var request = CreateRequest(
            AdminOnlyProtectedTallyCustodyReconciliationRunMode.DryRun,
            rows: [envelope],
            elections: [election],
            providerKeys:
            [
                CreateProviderObservation(
                    envelope.PublicCustodyReferenceHash!,
                    envelope.KmsKeyId!,
                    aliasMatches: false,
                    tagsMatch: false,
                    deletionScheduled: true,
                    deletionDate: envelope.KmsDeletionDate),
            ]);

        var report = AdminOnlyProtectedTallyCustodyReconciliationEngine.Run(
            request,
            new TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority());

        report.Items.Should().ContainSingle(x =>
            x.ConditionCode == AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.ProviderMetadataMismatch);
        report.Items.Should().OnlyContain(x => x.EnvelopeToPersist == null);
        report.Summary.HighCount.Should().Be(1);
    }

    [Fact]
    public void Run_DryRun_WithProviderReferenceMismatchButSameKey_ReportsMetadataMismatch()
    {
        var election = CreateAdminElection(ElectionLifecycleState.Finalized);
        var envelope = CreateDeletionScheduledEnvelope(election);
        var request = CreateRequest(
            AdminOnlyProtectedTallyCustodyReconciliationRunMode.DryRun,
            rows: [envelope],
            elections: [election],
            providerKeys:
            [
                CreateProviderObservation(
                    "sha256:different-public-reference",
                    envelope.KmsKeyId!,
                    deletionScheduled: true,
                    deletionDate: envelope.KmsDeletionDate),
            ]);

        var report = AdminOnlyProtectedTallyCustodyReconciliationEngine.Run(
            request,
            new TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority());

        report.Items.Should().ContainSingle(x =>
            x.ConditionCode == AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.ProviderMetadataMismatch);
        report.Items.Should().NotContain(x =>
            x.ConditionCode == AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.ProviderKeyMissing);
    }

    [Fact]
    public void Run_DryRun_WithDeletionScheduleDrift_ReportsReadinessBlocker()
    {
        var election = CreateAdminElection(ElectionLifecycleState.Finalized);
        var envelope = CreateDeletionScheduledEnvelope(election);
        var request = CreateRequest(
            AdminOnlyProtectedTallyCustodyReconciliationRunMode.DryRun,
            rows: [envelope],
            elections: [election],
            providerKeys:
            [
                CreateProviderObservation(
                    envelope.PublicCustodyReferenceHash!,
                    envelope.KmsKeyId!,
                    deletionScheduled: false,
                    deletionDate: null),
            ]);

        var report = AdminOnlyProtectedTallyCustodyReconciliationEngine.Run(
            request,
            new TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority());

        report.Items.Should().ContainSingle(x =>
            x.ConditionCode == AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.DeletionScheduleDrift);
        report.Summary.ReadinessGateId.Should().Be(ElectionAdminOnlyProtectedTallyCustodyReadinessIds.ReconciliationGateId);
        report.Summary.BlocksReadinessGate.Should().BeTrue();
    }

    [Fact]
    public void Run_DryRun_WithDeletionWindowOutsidePolicy_ReportsReadinessBlocker()
    {
        var election = CreateAdminElection(ElectionLifecycleState.Finalized);
        var envelope = CreateDeletionScheduledEnvelope(election) with
        {
            DeletionWindowDays = 14,
            KmsDeletionDate = DateTime.UnixEpoch.AddHours(1).AddDays(14),
        };
        var request = CreateRequest(
            AdminOnlyProtectedTallyCustodyReconciliationRunMode.DryRun,
            rows: [envelope],
            elections: [election],
            providerKeys:
            [
                CreateProviderObservation(
                    envelope.PublicCustodyReferenceHash!,
                    envelope.KmsKeyId!,
                    deletionScheduled: true,
                    deletionDate: envelope.KmsDeletionDate),
            ]);

        var report = AdminOnlyProtectedTallyCustodyReconciliationEngine.Run(
            request,
            new TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority());

        report.Items.Should().ContainSingle(x =>
            x.ConditionCode == AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.DeletionScheduleDrift);
        report.Summary.BlocksReadinessGate.Should().BeTrue();
    }

    [Fact]
    public void Run_DryRun_WithMissingProviderKey_ReportsProviderMissing()
    {
        var election = CreateAdminElection(ElectionLifecycleState.Open);
        var envelope = CreateOpenEnvelope(election);
        var request = CreateRequest(
            AdminOnlyProtectedTallyCustodyReconciliationRunMode.DryRun,
            rows: [envelope],
            elections: [election],
            providerKeys: []);

        var report = AdminOnlyProtectedTallyCustodyReconciliationEngine.Run(
            request,
            new TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority());

        report.Items.Should().Contain(x =>
            x.ConditionCode == AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.ProviderKeyMissing);
    }

    [Fact]
    public void Run_DryRun_WithDraftElectionCustodyRow_ReportsStaleCustodyRow()
    {
        var election = CreateAdminElection(ElectionLifecycleState.Draft);
        var envelope = CreateOpenEnvelope(election);
        var request = CreateRequest(
            AdminOnlyProtectedTallyCustodyReconciliationRunMode.DryRun,
            rows: [envelope],
            elections: [election],
            providerKeys:
            [
                CreateProviderObservation(envelope.PublicCustodyReferenceHash!, envelope.KmsKeyId!),
            ]);

        var report = AdminOnlyProtectedTallyCustodyReconciliationEngine.Run(
            request,
            new TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority());

        report.Items.Should().Contain(x =>
            x.ConditionCode == AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.StaleCustodyRow);
    }

    [Fact]
    public void Run_DryRun_WithProviderPermissionFailure_ReportsOperatorAction()
    {
        var election = CreateAdminElection(ElectionLifecycleState.Open);
        var envelope = CreateOpenEnvelope(election);
        var request = CreateRequest(
            AdminOnlyProtectedTallyCustodyReconciliationRunMode.DryRun,
            rows: [envelope],
            elections: [election],
            providerKeys:
            [
                CreateProviderObservation(
                    envelope.PublicCustodyReferenceHash!,
                    envelope.KmsKeyId!,
                    providerErrorCode: "AccessDeniedException"),
            ]);

        var report = AdminOnlyProtectedTallyCustodyReconciliationEngine.Run(
            request,
            new TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority());

        report.Items.Should().ContainSingle(x =>
            x.ConditionCode == AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.ProviderInventoryError);
        report.Summary.CriticalCount.Should().Be(1);
    }

    [Fact]
    public void Run_DryRun_WithUnresolvedException_ReportsExceptionRequired()
    {
        var election = CreateAdminElection(ElectionLifecycleState.Open);
        var envelope = CreateOpenEnvelope(election) with
        {
            CustodyLifecycleState = ElectionAdminOnlyProtectedTallyCustodyLifecycleState.ExceptionRequired,
            CustodyExceptionId = "custody-exception-1",
            CustodyLastErrorCode = "KMS_ALIAS_MISMATCH",
        };
        var request = CreateRequest(
            AdminOnlyProtectedTallyCustodyReconciliationRunMode.DryRun,
            rows: [envelope],
            elections: [election],
            providerKeys:
            [
                CreateProviderObservation(envelope.PublicCustodyReferenceHash!, envelope.KmsKeyId!),
            ]);

        var report = AdminOnlyProtectedTallyCustodyReconciliationEngine.Run(
            request,
            new TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority());

        report.Items.Should().ContainSingle(x =>
            x.ConditionCode == AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.ExceptionRequired);
        report.Summary.HasUnresolvedExceptions.Should().BeTrue();
        report.ReadinessFragment.Exceptions.Should().Contain(x =>
            x.ReasonCode == AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.ExceptionRequired);
    }

    [Fact]
    public void Run_Repair_WithScalarDestroyedEnvelope_ProducesDeletionScheduledEnvelope()
    {
        var election = CreateAdminElection(ElectionLifecycleState.Finalized);
        var envelope = CreateOpenEnvelope(election) with
        {
            SealedTallyPrivateScalar = AdminOnlyProtectedTallyEnvelopeCryptoConstants.DestroyedEnvelopeMarker,
            DestroyedAt = DateTime.UnixEpoch.AddHours(1),
            CustodyLifecycleState = ElectionAdminOnlyProtectedTallyCustodyLifecycleState.ScalarDestroyed,
            CustodyLastAction = "finalization-scalar-destroyed",
        };
        var request = CreateRequest(
            AdminOnlyProtectedTallyCustodyReconciliationRunMode.Repair,
            rows: [envelope],
            elections: [election],
            providerKeys:
            [
                CreateProviderObservation(envelope.PublicCustodyReferenceHash!, envelope.KmsKeyId!),
            ]);

        var report = AdminOnlyProtectedTallyCustodyReconciliationEngine.Run(
            request,
            new TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority());

        report.Items.Should().ContainSingle(x =>
            x.ConditionCode == AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.FinalizedKeyStillEnabled);
        var repairItem = report.Items.Single(x =>
            x.ConditionCode == AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.FinalizedKeyStillEnabled);
        repairItem.RepairResult.Should().Be("repair_applied");
        repairItem.EnvelopeToPersist.Should().NotBeNull();
        repairItem.EnvelopeToPersist!.CustodyLifecycleState.Should()
            .Be(ElectionAdminOnlyProtectedTallyCustodyLifecycleState.DeletionScheduled);
        repairItem.EnvelopeToPersist.SealedTallyPrivateScalar.Should()
            .Be(AdminOnlyProtectedTallyEnvelopeCryptoConstants.DestroyedEnvelopeMarker);
    }

    private static AdminOnlyProtectedTallyCustodyReconciliationRequest CreateRequest(
        AdminOnlyProtectedTallyCustodyReconciliationRunMode runMode,
        IReadOnlyList<ElectionAdminOnlyProtectedTallyEnvelopeRecord> rows,
        IReadOnlyList<AdminOnlyProtectedTallyCustodyProviderKeyObservation> providerKeys,
        IReadOnlyList<ElectionRecord>? elections = null) =>
        new(
            runMode,
            rows,
            elections ?? [],
            providerKeys,
            EnvironmentName: "unit-test",
            ProviderProfile: "unit-test",
            OperatorServiceIdentity: "test-host",
            StartedAt: DateTime.UnixEpoch.AddHours(2));

    private static AdminOnlyProtectedTallyCustodyProviderKeyObservation CreateProviderObservation(
        string publicRefHash,
        string keyId,
        bool exists = true,
        bool enabled = false,
        bool aliasMatches = true,
        bool tagsMatch = true,
        bool deletionScheduled = false,
        DateTime? deletionDate = null,
        string? providerErrorCode = null) =>
        new(
            publicRefHash,
            keyId,
            KmsKeyArn: $"arn:aws:kms:eu-central-1:111122223333:key/{keyId}",
            KmsAlias: $"alias/hush-voting/admin-only/test/{keyId}",
            KmsRegion: "eu-central-1",
            KmsAccountBoundary: "aws-account:111122223333",
            exists,
            enabled,
            aliasMatches,
            tagsMatch,
            deletionScheduled,
            deletionDate,
            ProviderErrorCode: providerErrorCode);

    private static ElectionAdminOnlyProtectedTallyEnvelopeRecord CreateDeletionScheduledEnvelope(
        ElectionRecord election)
    {
        var envelope = CreateOpenEnvelope(election);
        var destroyedAt = DateTime.UnixEpoch.AddHours(1);
        return envelope with
        {
            SealedTallyPrivateScalar = AdminOnlyProtectedTallyEnvelopeCryptoConstants.DestroyedEnvelopeMarker,
            DestroyedAt = destroyedAt,
            LastUpdatedAt = destroyedAt,
            CustodyLifecycleState = ElectionAdminOnlyProtectedTallyCustodyLifecycleState.DeletionScheduled,
            CustodyLastAction = "finalization-kms-schedule-deletion",
            KmsKeyDisabledAt = destroyedAt,
            KmsDeletionScheduledAt = destroyedAt,
            KmsDeletionDate = destroyedAt.AddDays(7),
            DeletionWindowDays = 7,
        };
    }

    private static ElectionAdminOnlyProtectedTallyEnvelopeRecord CreateOpenEnvelope(ElectionRecord election)
    {
        var authority = new TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority();
        var result = authority.PrepareOpenCustody(
            election,
            CreateAdminProfile(),
            existingEnvelope: null,
            new BabyJubJubCurve(),
            DateTime.UnixEpoch);

        result.IsSuccess.Should().BeTrue();
        return result.EnvelopeToPersist!;
    }

    private static ElectionRecord CreateAdminElection(ElectionLifecycleState lifecycleState) =>
        ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: "Board Election",
            shortDescription: "Annual board vote",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "ORG-2026-01",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            selectedProfileId: "admin-prod-1of1",
            selectedProfileDevOnly: false,
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
            ]) with
        {
            LifecycleState = lifecycleState,
            OpenedAt = lifecycleState is ElectionLifecycleState.Open or ElectionLifecycleState.Closed or ElectionLifecycleState.Finalized
                ? DateTime.UnixEpoch
                : null,
            ClosedAt = lifecycleState is ElectionLifecycleState.Closed or ElectionLifecycleState.Finalized
                ? DateTime.UnixEpoch.AddMinutes(30)
                : null,
            FinalizedAt = lifecycleState == ElectionLifecycleState.Finalized
                ? DateTime.UnixEpoch.AddHours(1)
                : null,
        };

    private static ElectionCeremonyProfileRecord CreateAdminProfile() =>
        ElectionModelFactory.CreateCeremonyProfile(
            "admin-prod-1of1",
            displayName: "admin-prod-1of1",
            description: "Admin production test profile",
            providerKey: "hush-prod",
            profileVersion: "v1",
            trusteeCount: 1,
            requiredApprovalCount: 1,
            devOnly: false);
}
