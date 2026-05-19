using System.Text.Json;
using FluentAssertions;
using HushNode.Elections;
using HushNode.Reactions.Crypto;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class AdminOnlyProtectedTallyCustodyEvidenceBuilderTests
{
    [Fact]
    public void BuildOpenEvidence_WithOpenBoundEnvelope_MapsToReadinessGateAndRedactsPublicOutput()
    {
        var recordedAt = DateTime.Parse("2026-05-19T01:00:00Z").ToUniversalTime();
        var election = CreateAdminElection();
        var envelope = CreateOpenEnvelope(election, recordedAt);

        var fragment = ElectionAdminOnlyProtectedTallyCustodyEvidenceBuilder.BuildOpenEvidence(
            election,
            envelope,
            recordedAt,
            includeRestrictedEvidence: true);

        fragment.DimensionId.Should().Be(ElectionAdminOnlyProtectedTallyCustodyReadinessIds.DimensionId);
        fragment.PublicEvidence.GateIds.Should().ContainSingle()
            .Which.Should().Be(ElectionAdminOnlyProtectedTallyCustodyReadinessIds.OpenGateId);
        fragment.AcceptedGateIds.Should().Contain(ElectionAdminOnlyProtectedTallyCustodyReadinessIds.OpenGateId);
        fragment.PublicEvidence.PublicResultCodes.Should().Contain(
            ElectionAdminOnlyProtectedTallyCustodyResultCodes.OpenBound);
        fragment.PublicEvidence.PublicRecordSecretScanStatus.Should().Be(
            ElectionAdminOnlyProtectedTallyCustodyResultCodes.PublicSecretScanPassed);

        var publicJson = JsonSerializer.Serialize(fragment.PublicEvidence, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        publicJson.Should().NotContain(envelope.KmsKeyId!);
        publicJson.Should().NotContain(envelope.KmsKeyArn!);
        publicJson.Should().NotContain(envelope.KmsAlias!);
        publicJson.Should().NotContain(envelope.SealedTallyPrivateScalar);
        fragment.RestrictedEvidence.Should().NotBeNull();
        fragment.RestrictedEvidence!.KmsKeyId.Should().Be(envelope.KmsKeyId);
    }

    [Fact]
    public void BuildOpenEvidence_WithRetryFailure_UsesSafeReasonCodeAndKeepsProviderDetailsRestricted()
    {
        var recordedAt = DateTime.Parse("2026-05-19T01:00:00Z").ToUniversalTime();
        var election = CreateAdminElection();
        var envelope = CreateOpenEnvelope(election, recordedAt) with
        {
            CustodyLifecycleState = ElectionAdminOnlyProtectedTallyCustodyLifecycleState.RetryRequired,
            CustodyLastErrorCode = "KMS_PROVIDER_DENIED",
            CustodyLastErrorMessage = "provider denied key fake-key-secret",
        };

        var fragment = ElectionAdminOnlyProtectedTallyCustodyEvidenceBuilder.BuildOpenEvidence(
            election,
            envelope,
            recordedAt,
            includeRestrictedEvidence: true);

        fragment.AcceptedGateIds.Should().BeEmpty();
        fragment.Exceptions.Should().ContainSingle();
        fragment.PublicEvidence.PublicResultCodes.Should().Contain(
            ElectionAdminOnlyProtectedTallyCustodyResultCodes.OpenRetryRequired);
        var publicJson = JsonSerializer.Serialize(fragment.PublicEvidence, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        publicJson.Should().NotContain("provider denied");
        publicJson.Should().NotContain("fake-key-secret");
        fragment.RestrictedEvidence.Should().NotBeNull();
        fragment.RestrictedEvidence!.ProviderErrorMessage.Should().Contain("provider denied");
    }

    [Fact]
    public void BuildFinalizationCleanupEvidence_WithDeletionScheduledEnvelope_MapsToFinalizationGate()
    {
        var recordedAt = DateTime.Parse("2026-05-19T01:00:00Z").ToUniversalTime();
        var election = CreateAdminElection();
        var authority = new TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority();
        var envelope = CreateOpenEnvelope(election, recordedAt);
        var cleanup = authority.BuildFinalizationCleanup(envelope, recordedAt.AddHours(1));

        var fragment = ElectionAdminOnlyProtectedTallyCustodyEvidenceBuilder.BuildFinalizationCleanupEvidence(
            election,
            cleanup.EnvelopeToPersist,
            recordedAt.AddHours(1));

        fragment.PublicEvidence.GateIds.Should().ContainSingle()
            .Which.Should().Be(ElectionAdminOnlyProtectedTallyCustodyReadinessIds.FinalizationGateId);
        fragment.AcceptedGateIds.Should().Contain(ElectionAdminOnlyProtectedTallyCustodyReadinessIds.FinalizationGateId);
        fragment.PublicEvidence.PublicResultCodes.Should().Contain(
            ElectionAdminOnlyProtectedTallyCustodyResultCodes.FinalizationDeletionScheduled);
    }

    private static ElectionAdminOnlyProtectedTallyEnvelopeRecord CreateOpenEnvelope(
        ElectionRecord election,
        DateTime recordedAt)
    {
        var authority = new TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority();
        var result = authority.PrepareOpenCustody(
            election,
            CreateAdminProfile(),
            existingEnvelope: null,
            new BabyJubJubCurve(),
            recordedAt);

        result.IsSuccess.Should().BeTrue();
        return result.EnvelopeToPersist!;
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
