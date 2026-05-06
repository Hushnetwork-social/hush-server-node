using FluentAssertions;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionSp06ControlDomainPolicyTests
{
    [Fact]
    public void EvaluateHighAssuranceV1_WithCompleteIndependentEvidence_ShouldBeReadyForOpen()
    {
        var election = CreateElection();
        var trustees = CreateTrustees();
        var domains = CreateControlDomains(election.ElectionId, trustees);

        var result = ElectionSp06ControlDomainPolicy.EvaluateHighAssuranceV1(
            election,
            CreateProductionThresholdProfile(),
            trustees,
            domains);

        result.IsReadyForOpen.Should().BeTrue();
        result.ReadinessBlockers.Should().BeEmpty();
        result.RequiredTrusteeCount.Should().Be(5);
        result.RequiredThreshold.Should().Be(3);
        result.CompleteEvidenceCount.Should().Be(5);
        result.AcceptedBeforeOpenCount.Should().Be(5);
    }

    [Fact]
    public void EvaluateHighAssuranceV1_WithMissingControlDomainEvidence_ShouldBlockOpen()
    {
        var election = CreateElection();
        var trustees = CreateTrustees();
        var domains = CreateControlDomains(election.ElectionId, trustees).Take(4).ToArray();

        var result = ElectionSp06ControlDomainPolicy.EvaluateHighAssuranceV1(
            election,
            CreateProductionThresholdProfile(),
            trustees,
            domains);

        result.IsReadyForOpen.Should().BeFalse();
        result.MissingEvidenceCount.Should().Be(1);
        result.ReadinessBlockers.Should().Contain(x =>
            x.Code == "control_domain_evidence_missing" &&
            x.BlocksOpen);
    }

    [Fact]
    public void EvaluateHighAssuranceV1_WithDevThresholdProfile_ShouldBlockOpen()
    {
        var election = CreateElection() with
        {
            SelectedProfileId = ElectionSelectableProfileCatalog.TrusteeDevProfileId,
            SelectedProfileDevOnly = true,
        };
        var trustees = CreateTrustees();

        var result = ElectionSp06ControlDomainPolicy.EvaluateHighAssuranceV1(
            election,
            CreateDevThresholdProfile(),
            trustees,
            CreateControlDomains(election.ElectionId, trustees));

        result.IsReadyForOpen.Should().BeFalse();
        result.ReadinessBlockers.Should().Contain(x =>
            x.Code == "trustee_threshold_profile_mismatch" &&
            x.BlocksOpen &&
            x.BlocksFinalization);
    }

    [Fact]
    public void EvaluateHighAssuranceV1_WithDuplicatePersonCustodyOrAdminThreshold_ShouldBlockOpen()
    {
        var election = CreateElection();
        var trustees = CreateTrustees();
        var domains = CreateControlDomains(election.ElectionId, trustees)
            .Select((domain, index) => index switch
            {
                0 => domain with { AdminDomainRefHash = "shared-admin-domain" },
                1 => domain with { TrusteePersonRef = "person-ref-1", AdminDomainRefHash = "shared-admin-domain" },
                2 => domain with { CustodyDomainRefHash = "custody-domain-hash-1", AdminDomainRefHash = "shared-admin-domain" },
                _ => domain,
            })
            .ToArray();

        var result = ElectionSp06ControlDomainPolicy.EvaluateHighAssuranceV1(
            election,
            CreateProductionThresholdProfile(),
            trustees,
            domains);

        result.IsReadyForOpen.Should().BeFalse();
        result.ReadinessBlockers.Should().Contain(x => x.Code == "trustee_duplicate_person");
        result.ReadinessBlockers.Should().Contain(x => x.Code == "trustee_duplicate_custody_domain");
        result.ReadinessBlockers.Should().Contain(x => x.Code == "trustee_admin_domain_threshold_violation");
    }

    [Fact]
    public void EvaluateHighAssuranceV1_WithUnsupportedCustodyMode_ShouldBlockOpenAndFinalization()
    {
        var election = CreateElection();
        var trustees = CreateTrustees();
        var domains = CreateControlDomains(election.ElectionId, trustees)
            .Select((domain, index) => index == 0
                ? domain with { CustodyMode = ElectionSp06ProfileIds.SharedOperatorCustodyV1 }
                : domain)
            .ToArray();

        var result = ElectionSp06ControlDomainPolicy.EvaluateHighAssuranceV1(
            election,
            CreateProductionThresholdProfile(),
            trustees,
            domains);

        result.IsReadyForOpen.Should().BeFalse();
        result.ReadinessBlockers.Should().Contain(x =>
            x.Code == "trustee_custody_mode_unsupported" &&
            x.BlocksOpen &&
            x.BlocksFinalization);
    }

    private static ElectionRecord CreateElection() =>
        ElectionModelFactory.CreateDraftRecord(
            ElectionId.NewElectionId,
            title: "SP-06 election",
            shortDescription: null,
            ownerPublicAddress: "owner-address",
            externalReferenceCode: null,
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            selectedProfileId: ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
            selectedProfileDevOnly: false,
            governanceMode: ElectionGovernanceMode.TrusteeThreshold,
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
            requiredApprovalCount: 3);

    private static ElectionCeremonyProfileRecord CreateProductionThresholdProfile() =>
        new(
            ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
            "Production 3-of-5",
            "Production threshold ceremony profile.",
            "built-in",
            "omega-v1.0.0-dkg-prod-3of5",
            TrusteeCount: 5,
            RequiredApprovalCount: 3,
            DevOnly: false,
            RegisteredAt: DateTime.UtcNow,
            LastUpdatedAt: DateTime.UtcNow);

    private static ElectionCeremonyProfileRecord CreateDevThresholdProfile() =>
        CreateProductionThresholdProfile() with
        {
            ProfileId = ElectionSelectableProfileCatalog.TrusteeDevProfileId,
            DevOnly = true,
        };

    private static IReadOnlyList<ElectionTrusteeReference> CreateTrustees() =>
        Enumerable.Range(1, 5)
            .Select(x => new ElectionTrusteeReference($"trustee-{x}@hush.test", $"Trustee {x}"))
            .ToArray();

    private static IReadOnlyList<ElectionTrusteeControlDomainRecord> CreateControlDomains(
        ElectionId electionId,
        IReadOnlyList<ElectionTrusteeReference> trustees) =>
        trustees
            .Select((trustee, index) => new ElectionTrusteeControlDomainRecord(
                Guid.NewGuid(),
                electionId,
                ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1,
                ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1Version,
                ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
                CeremonyVersionId: Guid.NewGuid(),
                TrusteeId: $"trustee-{index + 1:00}",
                TrusteeAccountId: trustee.TrusteeUserAddress,
                TrusteePersonRef: $"person-ref-{index + 1}",
                ElectionTrusteeRole.ExternalTrustee,
                CustodyMode: ElectionSp06ProfileIds.TrusteeLocalSecureVaultV1,
                CustodyDomainRefHash: $"custody-domain-hash-{index + 1}",
                AdminDomainRefHash: $"admin-domain-hash-{index + 1}",
                LegalEntityRefHash: null,
                PublicKeyCommitmentHash: $"public-key-commitment-{index + 1}",
                AcceptedAt: DateTime.UtcNow.AddMinutes(-10 + index),
                AcceptedBeforeOpen: true,
                ElectionTrusteeBackupStatus.Registered,
                ElectionTrusteeExceptionStatus.None,
                ElectionTrusteeControlDomainEvidenceStatus.Accepted,
                EvidenceFailureCode: null,
                EvidenceFailureReason: null,
                RecordedAt: DateTime.UtcNow,
                RecordedByPublicAddress: "owner-address",
                SourceTransactionId: null,
                SourceBlockHeight: null,
                SourceBlockId: null))
            .ToArray();
}
