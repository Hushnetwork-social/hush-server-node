using FluentAssertions;
using HushShared.Elections.Verification.Model;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class FailedFinalizeContinuityContractTests
{
    [Fact]
    public void CreateFailedFinalizeContinuityDecision_WithRequiredEvidence_KeepsLifecycleClosedAndOmitsResults()
    {
        var election = CreateClosedElection();

        var decision = ElectionModelFactory.CreateFailedFinalizeContinuityDecision(
            election,
            " owner-address ",
            ElectionGovernedOutcomeConstants.ElectionOwnerAuthorityRole,
            ElectionGovernedOutcomeConstants.Feat140AuthoritySource,
            "hush-documents/PrivateServer_ElectronicVoting/Legal-Governance-Boundary/package/legal-governance-boundary-feat155-handoff.json",
            "1551551551551551551551551551551551551551551551551551551551551551",
            "governance:decision:failed-finalize",
            "authority-hash",
            "governance-rule:failed-finalize-v1",
            election.CloseArtifactId!.Value,
            "Clean finalization could not be verified; no official result is produced.",
            missingFinalizeEvidenceRefs: ["missing:clean-finalization-proof"],
            continuityIncidentEvidenceRefs: ["incident:failed-finalize:1"],
            availableTrusteeAcknowledgementRefs: ["ack:available-trustee:1"],
            keyLostTrusteeDecisionIds: [Guid.NewGuid()],
            remedyRuleRef: "governance-rule:customer-remedy-v1");

        decision.DecisionType.Should().Be(ElectionGovernedOutcomeDecisionType.RecordFailedFinalizeContinuity);
        decision.OutcomeStatus.Should().Be(ElectionOutcomeStatus.FailedToFinalize);
        decision.CleanFinalization.Should().BeFalse();
        decision.FinalizationMode.Should().Be(ElectionGovernedOutcomeFinalizationMode.FailedFinalization);
        decision.PreviousLifecycleState.Should().Be(ElectionLifecycleState.Closed);
        decision.ResultingLifecycleState.Should().Be(ElectionLifecycleState.Closed);
        decision.ActorPublicAddress.Should().Be("owner-address");
        decision.CloseArtifactId.Should().Be(election.CloseArtifactId!.Value);
        decision.UnofficialResultArtifactId.Should().BeNull();
        decision.OfficialResultArtifactId.Should().BeNull();
        decision.OfficialResultSourceArtifactId.Should().BeNull();
        decision.FinalizeArtifactId.Should().BeNull();
        decision.HasFailedFinalizeContinuityEvidence.Should().BeTrue();
    }

    [Fact]
    public void FailedFinalizeContinuityDecision_WhenCleanFinalizationIsClaimed_ShouldThrow()
    {
        var act = () => CreateRawFailedFinalizeDecision(cleanFinalization: true);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("Failed-finalize governed outcome decisions cannot claim clean finalization.*");
    }

    [Fact]
    public void FailedFinalizeContinuityDecision_WhenOfficialResultReferenceExists_ShouldThrow()
    {
        var act = () => CreateRawFailedFinalizeDecision(officialResultArtifactId: Guid.NewGuid());

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("Failed-finalize governed outcome decisions cannot contain result artifact references.*");
    }

    [Fact]
    public void FailedFinalizeContinuityDecision_WhenFinalizeBoundaryExists_ShouldThrow()
    {
        var act = () => CreateRawFailedFinalizeDecision(finalizeArtifactId: Guid.NewGuid());

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("Failed-finalize governed outcome decisions cannot contain finalize boundary artifact references.*");
    }

    [Fact]
    public void FixedUnofficialResultWithAnomalyDecision_StillRequiresResultArtifacts()
    {
        var election = CreateClosedElection();
        var act = () => new ElectionGovernedOutcomeDecisionRecord(
            Guid.NewGuid(),
            election.ElectionId,
            ElectionGovernedOutcomeDecisionType.AcceptFixedUnofficialResultWithAnomaly,
            ElectionOutcomeStatus.FinalizedWithAnomaly,
            CleanFinalization: false,
            ElectionGovernedOutcomeFinalizationMode.AbnormalFinalization,
            ElectionLifecycleState.Closed,
            ElectionLifecycleState.Finalized,
            "owner-address",
            ElectionGovernedOutcomeConstants.ElectionOwnerAuthorityRole,
            ElectionGovernedOutcomeConstants.Feat140AuthoritySource,
            "feat140:handoff",
            "feat140-hash",
            "authority:decision",
            "authority-hash",
            "governance-rule:abnormal-finalization-v1",
            "finality-rule:fixed-result-copy-v1",
            "remedy-rule:key-lost-v1",
            election.CloseArtifactId!.Value,
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ["missing:clean-threshold-finalize"],
            ["incident:key-lost:1"],
            ["ack:available-trustee:1"],
            [Guid.NewGuid()],
            "Fixed tally-ready result accepted with abnormal-finalization disclosure.",
            DateTime.UtcNow,
            DateTime.UtcNow,
            Guid.NewGuid(),
            42,
            Guid.NewGuid());

        act.Should().Throw<ArgumentException>().WithMessage("Value is required.*");
    }

    [Fact]
    public void VerificationConstants_DefineFailedFinalizeOutcomeAndResultCodes()
    {
        FailedFinalizeVerificationIds.OutcomeStatusFailedToFinalize.Should().Be("failed_to_finalize");
        FailedFinalizeVerificationIds.FinalizationModeFailedFinalization.Should().Be("failed_finalization");
        VerificationResultCodes.FailedFinalizeContinuityValid.Should().Be("failed_finalize_continuity_valid");
        VerificationResultCodes.FailedFinalizeCleanResultConflict.Should().Be("failed_finalize_clean_result_conflict");
        VerificationPackageFileNames.FailedFinalizePublicStatus.Should().Be("failed-finalize-public-status.json");
        VerificationPackageFileNames.RestrictedFailedFinalizeEvidenceIndex.Should()
            .Be("restricted/failed-finalize-restricted-evidence-index.json");
    }

    private static ElectionGovernedOutcomeDecisionRecord CreateRawFailedFinalizeDecision(
        bool cleanFinalization = false,
        Guid? officialResultArtifactId = null,
        Guid? finalizeArtifactId = null)
    {
        var election = CreateClosedElection();
        return new ElectionGovernedOutcomeDecisionRecord(
            Guid.NewGuid(),
            election.ElectionId,
            ElectionGovernedOutcomeDecisionType.RecordFailedFinalizeContinuity,
            ElectionOutcomeStatus.FailedToFinalize,
            cleanFinalization,
            ElectionGovernedOutcomeFinalizationMode.FailedFinalization,
            ElectionLifecycleState.Closed,
            ElectionLifecycleState.Closed,
            "owner-address",
            ElectionGovernedOutcomeConstants.ElectionOwnerAuthorityRole,
            ElectionGovernedOutcomeConstants.Feat140AuthoritySource,
            "feat140:handoff",
            "feat140-hash",
            "authority:decision",
            "authority-hash",
            "governance-rule:failed-finalize-v1",
            null,
            "remedy-rule:customer-owned-v1",
            election.CloseArtifactId!.Value,
            null,
            null,
            officialResultArtifactId,
            null,
            finalizeArtifactId,
            ["missing:clean-finalization-proof"],
            ["incident:failed-finalize:1"],
            [],
            [],
            "Clean finalization could not be verified; no official result is produced.",
            DateTime.UtcNow,
            DateTime.UtcNow,
            null,
            null,
            null);
    }

    private static ElectionRecord CreateClosedElection()
    {
        var now = DateTime.UtcNow;
        return ElectionModelFactory.CreateDraftRecord(
            ElectionId.NewElectionId,
            title: "Failed finalize contract election",
            shortDescription: null,
            ownerPublicAddress: "owner-address",
            externalReferenceCode: null,
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            selectedProfileId: ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId,
            selectedProfileDevOnly: false,
            governanceMode: ElectionGovernanceMode.AdminOnly,
            disclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            participationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            voteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            eligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            outcomeRule: new OutcomeRuleDefinition(
                OutcomeRuleKind.PassFail,
                "pass-fail-simple-majority",
                SeatCount: 1,
                BlankVoteCountsForTurnout: true,
                BlankVoteExcludedFromWinnerSelection: true,
                BlankVoteExcludedFromThresholdDenominator: true,
                TieResolutionRule: "reject-on-tie",
                CalculationBasis: "counted-votes"),
            approvedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushvoting", "1.0.0"),
            ],
            protocolOmegaVersion: "omega-v1",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", null, 1, false),
                new ElectionOptionDefinition("no", "No", null, 2, false),
            ],
            requiredApprovalCount: null) with
        {
            LifecycleState = ElectionLifecycleState.Closed,
            ClosedAt = now,
            LastUpdatedAt = now,
            CloseArtifactId = Guid.NewGuid(),
        };
    }
}
