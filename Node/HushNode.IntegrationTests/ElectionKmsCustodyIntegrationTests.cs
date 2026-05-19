using FluentAssertions;
using HushNode.Elections;
using HushNode.Reactions.Crypto;
using HushShared.Elections.Model;
using Xunit;

namespace HushNode.IntegrationTests;

[Trait("Category", "FEAT-131")]
[Trait("Category", "HV-KMS-CUSTODY")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class ElectionKmsCustodyIntegrationTests
{
    [Fact]
    public void ProtectedAdminOnlyCustodyLifecycle_WithFakeProvider_ProducesAcceptedOpenFinalizeReconciliationEvidence()
    {
        var recordedAt = DateTime.UtcNow;
        var election = CreateAdminElection();
        var profile = CreateAdminProfile();
        var authority = new TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority();

        authority.RequiresPerElectionCustody(election, profile).Should().BeTrue();
        authority.EvaluateOpenReadiness(election, profile, out var readinessError).Should().BeTrue();
        readinessError.Should().BeEmpty();

        var open = authority.PrepareOpenCustody(
            election,
            profile,
            existingEnvelope: null,
            new BabyJubJubCurve(),
            recordedAt);

        open.IsSuccess.Should().BeTrue();
        open.EnvelopeToPersist.Should().NotBeNull();
        var openEnvelope = open.EnvelopeToPersist!;
        openEnvelope.CustodyLifecycleState.Should()
            .Be(ElectionAdminOnlyProtectedTallyCustodyLifecycleState.OpenBound);
        openEnvelope.KmsKeyId.Should().NotBeNullOrWhiteSpace();
        openEnvelope.KmsAlias.Should().NotBeNullOrWhiteSpace();
        openEnvelope.SealedEnvelopeHash.Should().NotBeNullOrWhiteSpace();

        var openEvidence = ElectionAdminOnlyProtectedTallyCustodyEvidenceBuilder.BuildOpenEvidence(
            election,
            openEnvelope,
            recordedAt);
        openEvidence.AcceptedGateIds.Should()
            .Contain(ElectionAdminOnlyProtectedTallyCustodyReadinessIds.OpenGateId);
        ElectionAdminOnlyProtectedTallyCustodyEvidenceBuilder
            .PublicEvidenceContainsRestrictedMaterial(openEvidence.PublicEvidence, openEnvelope)
            .Should()
            .BeFalse();

        var finalization = authority.BuildFinalizationCleanup(openEnvelope, recordedAt.AddHours(2));

        finalization.Handled.Should().BeTrue();
        finalization.Error.Should().BeEmpty();
        var finalizedEnvelope = finalization.EnvelopeToPersist!;
        finalizedEnvelope.SealedTallyPrivateScalar.Should()
            .Be(AdminOnlyProtectedTallyEnvelopeCryptoConstants.DestroyedEnvelopeMarker);
        finalizedEnvelope.CustodyLifecycleState.Should()
            .Be(ElectionAdminOnlyProtectedTallyCustodyLifecycleState.DeletionScheduled);
        finalizedEnvelope.KmsKeyDisabledAt.Should().NotBeNull();
        finalizedEnvelope.KmsDeletionScheduledAt.Should().NotBeNull();

        var finalizationEvidence =
            ElectionAdminOnlyProtectedTallyCustodyEvidenceBuilder.BuildFinalizationCleanupEvidence(
                election,
                finalizedEnvelope,
                recordedAt.AddHours(2));
        finalizationEvidence.AcceptedGateIds.Should()
            .Contain(ElectionAdminOnlyProtectedTallyCustodyReadinessIds.FinalizationGateId);

        var finalizedElection = election with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
        };
        var reconciliation = AdminOnlyProtectedTallyCustodyReconciliationEngine.Run(
            new AdminOnlyProtectedTallyCustodyReconciliationRequest(
                AdminOnlyProtectedTallyCustodyReconciliationRunMode.DryRun,
                [finalizedEnvelope],
                [finalizedElection],
                [CreateProviderObservation(finalizedEnvelope)],
                "integration",
                "fake-kms",
                "integration-test",
                recordedAt.AddHours(3)),
            authority);

        reconciliation.Summary.BlocksReadinessGate.Should().BeFalse();
        reconciliation.ReadinessFragment.AcceptedGateIds.Should()
            .Contain(ElectionAdminOnlyProtectedTallyCustodyReadinessIds.ReconciliationGateId);

        var aggregate = ElectionAdminOnlyProtectedTallyCustodyEvidenceBuilder.BuildAggregateReadinessEvidence(
            finalizedElection,
            finalizedEnvelope,
            recordedAt.AddHours(3),
            [openEvidence, finalizationEvidence, reconciliation.ReadinessFragment]);

        aggregate.CanProposeTargetScoreIncrease.Should().BeTrue();
        aggregate.ProposedScore.Should().Be(8);
        aggregate.ResidualRiskIds.Should().Contain([
            "cloud_provider_incident",
            "iam_drift",
            "regional_kms_availability",
            "deployment_variant",
        ]);
    }

    [Fact]
    public void ProtectedAdminOnlyCustodyLifecycle_WithUnavailableFakeProvider_BlocksOpenGate()
    {
        var recordedAt = DateTime.UtcNow;
        var election = CreateAdminElection();
        var profile = CreateAdminProfile();
        var authority = new TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority(ready: false);

        authority.EvaluateOpenReadiness(election, profile, out var readinessError).Should().BeFalse();
        readinessError.Should().Contain("not ready");

        var open = authority.PrepareOpenCustody(
            election,
            profile,
            existingEnvelope: null,
            new BabyJubJubCurve(),
            recordedAt);

        open.IsSuccess.Should().BeFalse();
        open.EnvelopeToPersist.Should().BeNull();
        var openEvidence = ElectionAdminOnlyProtectedTallyCustodyEvidenceBuilder.BuildOpenEvidence(
            election,
            open.EnvelopeToPersist,
            recordedAt,
            failureDetail: open.Error);

        openEvidence.AcceptedGateIds.Should()
            .NotContain(ElectionAdminOnlyProtectedTallyCustodyReadinessIds.OpenGateId);
        openEvidence.Exceptions.Should().ContainSingle(x =>
            x.BlocksReadinessScoreIncrease &&
            x.ReasonCode == ElectionAdminOnlyProtectedTallyCustodyResultCodes.OpenProviderUnavailable);
    }

    private static AdminOnlyProtectedTallyCustodyProviderKeyObservation CreateProviderObservation(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope) =>
        new(
            envelope.PublicCustodyReferenceHash!,
            envelope.KmsKeyId,
            envelope.KmsKeyArn,
            envelope.KmsAlias,
            envelope.KmsRegion,
            envelope.KmsAccountBoundary,
            Exists: true,
            Enabled: false,
            AliasMatches: true,
            TagsMatch: true,
            DeletionScheduled: true,
            envelope.KmsDeletionDate);

    private static ElectionRecord CreateAdminElection() =>
        ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: "FEAT-131 Protected Admin Custody",
            shortDescription: "Focused custody lifecycle TwinTest",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "FEAT-131-TWIN",
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
            ]);

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
