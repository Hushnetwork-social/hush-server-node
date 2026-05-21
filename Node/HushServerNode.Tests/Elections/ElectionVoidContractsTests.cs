using FluentAssertions;
using HushNode.Elections.gRPC;
using HushNetwork.proto;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionVoidContractsTests
{
    [Fact]
    public void VoidEnumValues_ShouldAppendWithoutRenumberingExistingLifecycleAndPackageValues()
    {
        ((int)ElectionLifecycleState.Draft).Should().Be(0);
        ((int)ElectionLifecycleState.Open).Should().Be(1);
        ((int)ElectionLifecycleState.Closed).Should().Be(2);
        ((int)ElectionLifecycleState.Finalized).Should().Be(3);
        ((int)ElectionLifecycleState.Voided).Should().Be(4);

        ((int)ElectionBoundaryArtifactType.Open).Should().Be(0);
        ((int)ElectionBoundaryArtifactType.Close).Should().Be(1);
        ((int)ElectionBoundaryArtifactType.TallyReady).Should().Be(2);
        ((int)ElectionBoundaryArtifactType.Finalize).Should().Be(3);
        ((int)ElectionBoundaryArtifactType.Void).Should().Be(4);

        ((int)ElectionReportPackageStatus.GenerationFailed).Should().Be(0);
        ((int)ElectionReportPackageStatus.Sealed).Should().Be(1);
        ((int)ElectionReportPackageStatus.SupersededByVoid).Should().Be(2);
        ((int)ElectionReportPackageKind.FinalResult).Should().Be(0);
        ((int)ElectionReportPackageKind.Void).Should().Be(1);
    }

    [Fact]
    public void ProtoValues_ShouldExposeVoidedAndSupersededByVoidWithoutRenumbering()
    {
        ((int)ElectionLifecycleStateProto.Draft).Should().Be(0);
        ((int)ElectionLifecycleStateProto.Open).Should().Be(1);
        ((int)ElectionLifecycleStateProto.Closed).Should().Be(2);
        ((int)ElectionLifecycleStateProto.Finalized).Should().Be(3);
        ((int)ElectionLifecycleStateProto.Voided).Should().Be(4);

        ((int)ElectionBoundaryArtifactTypeProto.OpenArtifact).Should().Be(0);
        ((int)ElectionBoundaryArtifactTypeProto.CloseArtifact).Should().Be(1);
        ((int)ElectionBoundaryArtifactTypeProto.TallyReadyArtifact).Should().Be(2);
        ((int)ElectionBoundaryArtifactTypeProto.FinalizeArtifact).Should().Be(3);
        ((int)ElectionBoundaryArtifactTypeProto.VoidArtifact).Should().Be(4);

        ((int)ElectionReportPackageStatusProto.ReportPackageGenerationFailed).Should().Be(0);
        ((int)ElectionReportPackageStatusProto.ReportPackageSealed).Should().Be(1);
        ((int)ElectionReportPackageStatusProto.ReportPackageSupersededByVoid).Should().Be(2);
        ((int)ElectionReportPackageKindProto.ReportPackageFinalResult).Should().Be(0);
        ((int)ElectionReportPackageKindProto.ReportPackageVoid).Should().Be(1);
        ((int)ElectionVerificationPackageStatusProto.VerificationPackageVoided).Should().Be(6);
        ((int)ElectionVoidPublicationAttemptStatusProto.VoidPublicationSealed).Should().Be(2);
        VerificationResultCodes.ElectionVoided.Should().Be("election_voided");
    }

    [Fact]
    public void ReportPackageSummaryProto_ShouldExposeSupersededVoidMetadata()
    {
        var election = CreateElection(ElectionLifecycleState.Finalized) with
        {
            TallyReadyArtifactId = Guid.NewGuid(),
            UnofficialResultArtifactId = Guid.NewGuid(),
            OfficialResultArtifactId = Guid.NewGuid(),
            FinalizeArtifactId = Guid.NewGuid(),
        };
        var voidDecisionId = Guid.NewGuid();
        var supersededAt = DateTime.UtcNow;
        var reportPackage = ElectionModelFactory.CreateSealedReportPackage(
                election.ElectionId,
                attemptNumber: 1,
                tallyReadyArtifactId: election.TallyReadyArtifactId.Value,
                unofficialResultArtifactId: election.UnofficialResultArtifactId.Value,
                officialResultArtifactId: election.OfficialResultArtifactId.Value,
                finalizeArtifactId: election.FinalizeArtifactId.Value,
                frozenEvidenceHash: [1, 2, 3],
                frozenEvidenceFingerprint: "final-package",
                packageHash: [4, 5, 6],
                artifactCount: 1,
                attemptedByPublicAddress: "owner-address")
            .SupersedeByVoid(voidDecisionId, supersededAt);

        var proto = InvokeReportPackageToProto(reportPackage);

        proto.Status.Should().Be(ElectionReportPackageStatusProto.ReportPackageSupersededByVoid);
        proto.PackageKind.Should().Be(ElectionReportPackageKindProto.ReportPackageFinalResult);
        proto.SupersededByVoidDecisionId.Should().Be(voidDecisionId.ToString());
        proto.HasSupersededAt.Should().BeTrue();
    }

    private static ElectionReportPackageSummaryView InvokeReportPackageToProto(
        ElectionReportPackageRecord reportPackage)
    {
        var mappingsType = typeof(ElectionQueryApplicationService).Assembly.GetType(
            "HushNode.Elections.gRPC.ElectionGrpcMappings");
        mappingsType.Should().NotBeNull();
        var method = mappingsType!.GetMethods(System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic)
            .Single(x =>
            {
                var parameters = x.GetParameters();
                return x.Name == "ToProto" &&
                    parameters.Length == 1 &&
                    parameters[0].ParameterType == typeof(ElectionReportPackageRecord);
            });

        return ((ElectionReportPackageSummaryView?)method.Invoke(null, [reportPackage]))!;
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    public void PublicJustificationValidator_ShouldRejectMissingOrTooShortText(string value)
    {
        var result = ElectionVoidPublicJustificationValidator.Validate(value);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(ElectionVoidValidationCodes.JustificationTooShort);
    }

    [Fact]
    public void PublicJustificationValidator_ShouldRejectRestrictedMaterialAndPersonalData()
    {
        var result = ElectionVoidPublicJustificationValidator.Validate(
            "The trustee key was lost. Contact voter@example.test with password details.");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(ElectionVoidValidationCodes.JustificationContainsRestrictedMaterial);
        result.Errors.Should().Contain(ElectionVoidValidationCodes.JustificationContainsPersonalData);
    }

    [Fact]
    public void PublicJustificationValidator_ShouldNormalizeValidText()
    {
        var result = ElectionVoidPublicJustificationValidator.Validate(
            "  Trustee threshold could not be satisfied after the close ceremony.  ");

        result.IsValid.Should().BeTrue();
        result.NormalizedJustification.Should().Be("Trustee threshold could not be satisfied after the close ceremony.");
    }

    [Fact]
    public void InternalEvidenceRefs_ShouldRequireInternalRecordId()
    {
        var act = () => ElectionModelFactory.CreateVoidEvidenceReference(
            ElectionVoidEvidenceReferenceKind.InternalAnomalyThread,
            "anomaly-thread-1");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*internal record id*");
    }

    [Fact]
    public void ExternalGovernanceEvidenceRefs_ShouldAllowOpaqueReference()
    {
        var reference = ElectionModelFactory.CreateVoidEvidenceReference(
            ElectionVoidEvidenceReferenceKind.ExternalGovernance,
            "board-minute-2026-05-21",
            externalReference: "Minutes approved by the election owner.");

        reference.IsInternal.Should().BeFalse();
        reference.ExternalReference.Should().Be("Minutes approved by the election owner.");
    }

    [Fact]
    public void VoidDecisionRecord_ShouldRequireOwnerRoleAndVoidedResult()
    {
        var decision = CreateVoidDecision(ElectionLifecycleState.Open);

        decision.ActorRole.Should().Be(ElectionVoidDecisionRecord.ElectionOwnerRole);
        decision.PreviousLifecycleState.Should().Be(ElectionLifecycleState.Open);
        decision.ResultingLifecycleState.Should().Be(ElectionLifecycleState.Voided);
        decision.PublicJustificationHash.Should().NotBeEmpty();
    }

    [Fact]
    public void VoidDecisionRecord_ShouldRejectFinalizedPreviousState()
    {
        var election = CreateElection(ElectionLifecycleState.Finalized);
        var boundaryArtifactId = Guid.NewGuid();

        var act = () => ElectionModelFactory.CreateVoidDecision(
            election,
            "owner-address",
            "Finalized elections cannot be voided in v1.",
            boundaryArtifactId);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*draft, open, or closed*");
    }

    [Fact]
    public void VoidPublicStatus_ShouldRequireExactVoidMarkerAndElectionVoidedResult()
    {
        var decision = CreateVoidDecision(ElectionLifecycleState.Closed);
        var attemptId = Guid.NewGuid();

        var status = new ElectionVoidPublicStatusRecord(
            decision.ElectionId,
            decision.Id,
            attemptId,
            "VOID",
            decision.PublicJustification,
            VerificationResultCodes.ElectionVoided,
            "void-package.zip",
            new string('a', 64),
            DateTime.UtcNow,
            [
                new ElectionVoidSupersededPublicArtifactReference(
                    ElectionVoidSupersededArtifactKind.ReportPackage,
                    "historical-package.zip",
                    new string('b', 64)),
            ]);

        status.Status.Should().Be("VOID");
        status.VerifierResultCode.Should().Be(VerificationResultCodes.ElectionVoided);
        status.SupersededArtifacts.Should().ContainSingle();
    }

    private static ElectionVoidDecisionRecord CreateVoidDecision(ElectionLifecycleState lifecycleState) =>
        ElectionModelFactory.CreateVoidDecision(
            CreateElection(lifecycleState),
            "owner-address",
            "Trustee threshold could not be satisfied after the close ceremony.",
            Guid.NewGuid());

    private static ElectionRecord CreateElection(ElectionLifecycleState lifecycleState) =>
        ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: "Board Election",
            shortDescription: "Annual board vote",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "ORG-2026-01",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
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
        };
}
