using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionDeploymentProofProfilePolicyTests
{
    [Fact]
    public void EvaluateOpen_WithAcceptedProductionServerProof_ShouldAllowOpenAndClaims()
    {
        var policy = new ElectionDeploymentProofProfilePolicy(ElectionDeploymentProofOptions.Default);
        var election = CreateElection(
            ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
            selectedProfileDevOnly: false,
            ElectionBindingStatus.Binding);

        var result = policy.EvaluateOpen(
            election,
            CreateContext(ElectionDeploymentProofEvidenceStatus.Accepted));

        policy.ResolveProfile(election).ProfileClass.Should()
            .Be(ElectionDeploymentProofProfileClass.HushManagedProductionLike);
        result.IsOpenAllowed.Should().BeTrue();
        result.ClaimEffect.Should().Be(ElectionDeploymentProofClaimEffect.Accepted);
        result.BlocksReadinessClaims.Should().BeFalse();
        result.FailureCodes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ElectionDeploymentProofEvidenceStatus.Missing, "deployment_proof_missing")]
    [InlineData(ElectionDeploymentProofEvidenceStatus.Stale, "deployment_proof_stale")]
    [InlineData(ElectionDeploymentProofEvidenceStatus.Superseded, "deployment_proof_superseded")]
    [InlineData(ElectionDeploymentProofEvidenceStatus.Blocked, "deployment_proof_blocked")]
    [InlineData(ElectionDeploymentProofEvidenceStatus.Unknown, "deployment_proof_unknown")]
    public void EvaluateOpen_WithBlockingProductionProviderStatus_ShouldFailClosed(
        ElectionDeploymentProofEvidenceStatus providerStatus,
        string expectedCode)
    {
        var policy = new ElectionDeploymentProofProfilePolicy(ElectionDeploymentProofOptions.Default);
        var election = CreateElection(
            ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId,
            selectedProfileDevOnly: false,
            ElectionBindingStatus.Binding,
            ElectionGovernanceMode.AdminOnly);

        var result = policy.EvaluateOpen(election, CreateContext(providerStatus));

        result.IsOpenAllowed.Should().BeFalse();
        result.ClaimEffect.Should().Be(ElectionDeploymentProofClaimEffect.Blocked);
        result.BlocksReadinessClaims.Should().BeTrue();
        result.FailureCodes.Should().Equal(expectedCode);
    }

    [Fact]
    public void EvaluateOpen_WithAcceptedProviderButMissingServerProof_ShouldFailClosed()
    {
        var policy = new ElectionDeploymentProofProfilePolicy(ElectionDeploymentProofOptions.Default);
        var election = CreateElection(
            ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
            selectedProfileDevOnly: false,
            ElectionBindingStatus.Binding);
        var context = CreateContext(ElectionDeploymentProofEvidenceStatus.Accepted) with
        {
            ServerProof = null,
        };

        var result = policy.EvaluateOpen(election, context);

        result.IsOpenAllowed.Should().BeFalse();
        result.FailureCodes.Should().Equal("deployment_proof_missing");
    }

    [Fact]
    public async Task EvaluateOpen_WithLocalDevProfileAndDefaultProvider_ShouldAllowOpenButBlockReadinessClaims()
    {
        var policy = new ElectionDeploymentProofProfilePolicy(ElectionDeploymentProofOptions.Default);
        var provider = new LocalDevelopmentActiveDeploymentProofProvider();
        var election = CreateElection(
            ElectionSelectableProfileCatalog.TrusteeDevProfileId,
            selectedProfileDevOnly: true,
            ElectionBindingStatus.NonBinding);
        var profile = policy.ResolveProfile(election);

        var context = await provider.GetActiveDeploymentProofContextAsync(
            profile,
            new DateTime(2026, 5, 26, 13, 0, 0, DateTimeKind.Utc));
        var result = policy.EvaluateOpen(election, context);

        profile.ProfileClass.Should().Be(ElectionDeploymentProofProfileClass.LocalDevelopment);
        context.ProviderStatus.Should().Be(ElectionDeploymentProofEvidenceStatus.NotRequired);
        result.IsOpenAllowed.Should().BeTrue();
        result.ClaimEffect.Should().Be(ElectionDeploymentProofClaimEffect.NoClaim);
        result.BlocksReadinessClaims.Should().BeTrue();
    }

    [Fact]
    public void EvaluateOpen_WithControlledPilotBlockedProof_ShouldFailClosed()
    {
        var options = ElectionDeploymentProofOptions.Default with
        {
            ControlledPilotProfileIds = ["controlled-pilot-v1"],
        };
        var policy = new ElectionDeploymentProofProfilePolicy(options);
        var election = CreateElection(
            "controlled-pilot-v1",
            selectedProfileDevOnly: false,
            ElectionBindingStatus.Binding);

        var result = policy.EvaluateOpen(
            election,
            CreateContext(ElectionDeploymentProofEvidenceStatus.Blocked));

        policy.ResolveProfile(election).ProfileClass.Should()
            .Be(ElectionDeploymentProofProfileClass.ControlledPilot);
        result.IsOpenAllowed.Should().BeFalse();
        result.FailureCodes.Should().Equal("deployment_proof_blocked");
    }

    [Fact]
    public void EvaluateOpen_WithUnknownBindingProfile_ShouldFailClosedBeforeClaims()
    {
        var policy = new ElectionDeploymentProofProfilePolicy(ElectionDeploymentProofOptions.Default);
        var election = CreateElection(
            "unknown-binding-profile-v1",
            selectedProfileDevOnly: false,
            ElectionBindingStatus.Binding);

        var result = policy.EvaluateOpen(
            election,
            CreateContext(ElectionDeploymentProofEvidenceStatus.Accepted));

        policy.ResolveProfile(election).ProfileClass.Should().Be(ElectionDeploymentProofProfileClass.Unsupported);
        result.IsOpenAllowed.Should().BeFalse();
        result.FailureCodes.Should().Equal("deployment_profile_unsupported");
    }

    [Fact]
    public async Task FixtureProvider_ShouldReturnConfiguredContextEventsAndProofFamilyStatus()
    {
        var profile = new ElectionDeploymentProofProfile(
            "controlled-pilot-v1",
            IsDevOnly: false,
            ElectionBindingStatus.Binding,
            ElectionGovernanceMode.TrusteeThreshold,
            ElectionDeploymentProofProfileClass.ControlledPilot);
        var observedAt = new DateTime(2026, 5, 26, 14, 0, 0, DateTimeKind.Utc);
        var deploymentEvent = new ActiveDeploymentProofEvent(
            "event-1",
            "release",
            "run-1",
            ElectionDeploymentProofComponentId.HushServerNode,
            "server-proof-v0",
            "server-proof-v1",
            ElectionDeploymentProofImpactClassification.VotingProtocolNoChange,
            "Routine release.",
            ["smoke-tests"],
            "passed",
            "release-manager-approved",
            observedAt.AddMinutes(-1),
            ElectionDeploymentProofEvidenceStatus.Accepted);
        var proofFamily = new ActiveProofFamilyStatus(
            ElectionDeploymentProofConstants.RetentionLogPrivacyProofFamilyId,
            "v1",
            "feat137-retention-log-privacy",
            Hash('b'),
            "readiness-register/feat137",
            ElectionDeploymentProofConstants.Feat137SourceFeature,
            ElectionDeploymentProofEvidenceStatus.Accepted,
            MismatchCode: null,
            "Privacy proof-family accepted.",
            observedAt);
        var provider = new FixtureActiveDeploymentProofProvider(
            CreateContext(ElectionDeploymentProofEvidenceStatus.Accepted),
            [deploymentEvent],
            [proofFamily]);

        var context = await provider.GetActiveDeploymentProofContextAsync(profile, observedAt);
        var events = await provider.GetDeploymentEventsSinceAsync(
            profile,
            observedAt.AddMinutes(-5),
            observedAt);
        var status = await provider.ResolveProofFamilyStatusAsync(
            ElectionDeploymentProofConstants.RetentionLogPrivacyProofFamilyId,
            "server-proof-v1");

        context.ObservedAtUtc.Should().Be(observedAt);
        context.DeploymentTarget.Should().Be("test-target");
        events.Should().ContainSingle().Which.EventPublicId.Should().Be("event-1");
        status.Should().BeEquivalentTo(proofFamily);
    }

    private static ActiveDeploymentProofContext CreateContext(
        ElectionDeploymentProofEvidenceStatus providerStatus) =>
        new(
            providerStatus,
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            DeploymentTarget: "test-target",
            ElectionDeploymentProofConstants.DeploymentProtocolVersion,
            PublicCatalogRef: "refs/tags/deployment-proof-v1",
            PlatformCeremonyId: "ceremony-public-1",
            ServerProof: CreateServerProof(providerStatus),
            ExpectedWebClientProof: null,
            ProviderErrors: Array.Empty<ActiveDeploymentProofProviderError>());

    private static ActiveDeploymentProofComponent? CreateServerProof(
        ElectionDeploymentProofEvidenceStatus providerStatus) =>
        providerStatus.BlocksDeploymentProofClaims()
            ? null
            : new ActiveDeploymentProofComponent(
                ElectionDeploymentProofComponentId.HushServerNode,
                "server-proof-v1",
                ElectionDeploymentProofEvidenceStatus.Accepted,
                "git:refs/tags/deployment-proof-v1",
                "sha256:" + Hash('a'),
                Hash('b'),
                "https://github.com/HushNetworkOrg/hush-deployment-proofs/tree/v1",
                PreviousProofId: null,
                SupersedesProofIds: Array.Empty<string>(),
                ElectionDeploymentProofObservationSource.Fixture);

    private static ElectionRecord CreateElection(
        string selectedProfileId,
        bool selectedProfileDevOnly,
        ElectionBindingStatus bindingStatus,
        ElectionGovernanceMode governanceMode = ElectionGovernanceMode.TrusteeThreshold) =>
        ElectionModelFactory.CreateDraftRecord(
            ElectionId.NewElectionId,
            title: "Deployment proof policy election",
            shortDescription: null,
            ownerPublicAddress: "owner-address",
            externalReferenceCode: null,
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus,
            selectedProfileId,
            selectedProfileDevOnly,
            governanceMode,
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
            requiredApprovalCount: governanceMode == ElectionGovernanceMode.AdminOnly ? null : 3);

    private static string Hash(char value) =>
        new(char.ToLowerInvariant(value), 64);
}
