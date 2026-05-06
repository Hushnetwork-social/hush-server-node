using System.Text.Json;
using FluentAssertions;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionSp05ContractTests
{
    [Fact]
    public void RosterImportEvidence_ShouldRepresentBlockingRejectionsAndRestrictedWarnings()
    {
        var evidence = new ElectionRosterImportEvidenceRecord(
            Guid.NewGuid(),
            ElectionId.NewElectionId,
            RosterImportVersion: 1,
            RosterSourceFileHash: "source-hash",
            RosterCanonicalHash: "canonical-hash",
            RosterCanonicalizationVersion: ElectionSp05ProfileIds.RosterCanonicalizationV1,
            RosterCanonicalizationVersionHash: "canonical-version-hash",
            AcceptedRowCount: 2,
            RejectedRowCount: 2,
            InvalidRowRejectionCount: 1,
            DuplicateIdRejectionCount: 1,
            DuplicateContactWarningCount: 1,
            ImportedAt: DateTime.UtcNow,
            ImportedByActor: "owner-address",
            RejectedRows:
            [
                new ElectionRosterRejectedRowRecord(
                    SourceRowNumber: 2,
                    OrganizationVoterId: "member-1",
                    ReasonCode: "duplicate_organization_voter_id",
                    Reason: "Duplicate organization voter id.",
                    RestrictedRowValues: new Dictionary<string, string>
                    {
                        ["organization_voter_id"] = "member-1",
                    }),
            ],
            DuplicateContactWarnings:
            [
                new ElectionRosterDuplicateContactWarningRecord(
                    ElectionRosterContactType.Email,
                    ContactMatchKey: "shared@example.test",
                    OrganizationVoterIds: ["member-2", "member-3"],
                    WarningCode: "duplicate_contact",
                    Warning: "Multiple roster entries share the same contact channel."),
            ]);

        evidence.AcceptedRowCount.Should().Be(2);
        evidence.RejectedRows.Should().ContainSingle();
        evidence.DuplicateContactWarnings.Should().ContainSingle();
        evidence.DuplicateContactWarnings[0].OrganizationVoterIds.Should().BeEquivalentTo("member-2", "member-3");
    }

    [Fact]
    public void PublicSp05Artifacts_ShouldExcludeNamedRosterAndLinkMaterial()
    {
        var policy = new ElectionSp05PolicyArtifactRecord(
            ElectionId.NewElectionId.ToString(),
            ElectionSp05ProfileIds.OrganizationalEligibilityCheckoffV1,
            EligibilityPolicyVersion: "1.0.0",
            EligibilityMutationPolicy.FrozenAtOpen,
            ElectionIdentityLinkPolicy.ContactCodeV1,
            ElectionCheckoffVisibilityPolicy.RestrictedOwnerAuditor,
            ElectionActorLinkMultiplicityPolicy.SingleRosterEntryPerActor,
            ElectionContactCodeProviderReadiness.Ready,
            ElectionSp05ProfileIds.RosterCanonicalizationV1,
            RosterCanonicalizationVersionHash: "roster-version-hash",
            ElectionSp05ProfileIds.EligibilityPolicyCanonicalizationV1,
            EligibilityPolicyCanonicalizationVersionHash: "policy-version-hash",
            ElectionSp05ProfileIds.VoteCommitmentPreimageV1,
            CommitmentSchemeVersionHash: "commitment-version-hash",
            ElectionSp05ProfileIds.VoteNullifierPreimageV1,
            NullifierSchemeVersionHash: "nullifier-version-hash");
        var summary = new ElectionSp05SummaryArtifactRecord(
            policy.ElectionId,
            RosterSourceFileHash: "source-hash",
            RosterCanonicalHash: "canonical-hash",
            RosterOpenHash: "open-hash",
            ActiveDenominatorOpenHash: "denominator-open-hash",
            ActiveDenominatorCloseHash: "denominator-close-hash",
            CommitmentTreeRoot: "commitment-root",
            RosteredCount: 3,
            LinkedCount: 2,
            ActiveDenominatorCount: 2,
            CommitmentCount: 2,
            CountedParticipationCount: 1,
            BlankCount: 0,
            DidNotVoteCount: 1,
            LateActivationCount: 0,
            BlockedActivationCount: 0,
            PublicPrivacyBoundary:
            [
                "no_organization_voter_id",
                "no_contact_value",
                "no_linked_actor",
                "no_checkoff_id",
            ]);

        var json = JsonSerializer.Serialize(new { policy, summary }, VerificationJson.Options);

        json.Should().Contain("rosterCanonicalHash");
        json.Should().Contain("commitmentTreeRoot");
        json.Should().NotContain("organizationVoterId");
        json.Should().NotContain("contactValue");
        json.Should().NotContain("linkedActorPublicAddress");
        json.Should().NotContain("checkoffId");
        json.Should().NotContain("voteSecret");
    }

    [Fact]
    public void Sp05PackageFileNames_ShouldSeparatePublicAndRestrictedEvidence()
    {
        VerificationPackageFileNames.Sp05EligibilityPolicy.Should()
            .Be("artifacts/election-record/eligibility-policy.json");
        VerificationPackageFileNames.Sp05EligibilitySummary.Should()
            .Be("artifacts/election-record/eligibility-summary.json");
        VerificationPackageFileNames.Sp05EligibilityVerifierOutput.Should()
            .Be("artifacts/election-record/eligibility-verifier-output.json");
        VerificationPackageFileNames.RestrictedRosterImportEvidence.Should().StartWith("artifacts/restricted/");
        VerificationPackageFileNames.RestrictedRoster.Should().StartWith("artifacts/restricted/");
        VerificationPackageFileNames.RestrictedLinkingEvidence.Should().StartWith("artifacts/restricted/");
        VerificationPackageFileNames.RestrictedActivationEvents.Should().StartWith("artifacts/restricted/");
        VerificationPackageFileNames.RestrictedCheckoffLedger.Should().StartWith("artifacts/restricted/");
        VerificationPackageFileNames.RestrictedDisputes.Should().StartWith("artifacts/restricted/");
    }

    [Theory]
    [InlineData("organizationVoterId")]
    [InlineData("contactValue")]
    [InlineData("linkedActorPublicAddress")]
    [InlineData("eligibilityLinkId")]
    [InlineData("checkoffId")]
    [InlineData("displayLabel")]
    [InlineData("voteSecret")]
    public void PublicPrivacyBoundary_ShouldRejectSp05NamedFields(string fieldName)
    {
        VerificationPrivacyBoundary.IsForbiddenInSp05PublicEligibilityArtifact(fieldName).Should().BeTrue();
    }

    [Fact]
    public void PublicPrivacyBoundary_ShouldAllowGenericDisplayLabelOutsideSp05()
    {
        VerificationPrivacyBoundary.IsForbiddenInPublicPackage("displayLabel").Should().BeFalse();
    }

    [Fact]
    public void VerificationResultCodes_ShouldExposeStableEliCodes()
    {
        var codes = new[]
        {
            VerificationResultCodes.EligibilityEvidenceValid,
            VerificationResultCodes.EligibilitySchemaInvalid,
            VerificationResultCodes.EligibilityPolicyMissing,
            VerificationResultCodes.EligibilityRosterHashMismatch,
            VerificationResultCodes.EligibilityOpenFreezeViolation,
            VerificationResultCodes.EligibilityLateActivationPolicyViolation,
            VerificationResultCodes.EligibilityLinkEvidenceMissing,
            VerificationResultCodes.EligibilityCommitmentInvalid,
            VerificationResultCodes.EligibilityCommitmentConsumedRight,
            VerificationResultCodes.EligibilityConsumptionWithoutAcceptedCast,
            VerificationResultCodes.EligibilityFailedCastConsumedRight,
            VerificationResultCodes.EligibilityCountReconciliationMismatch,
            VerificationResultCodes.EligibilityPublicPrivacyBoundaryViolation,
            VerificationResultCodes.EligibilityBallotPrivacyBoundaryViolation,
            VerificationResultCodes.EligibilityDevOnlyVerificationBlocked,
        };

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().AllSatisfy(x => x.Should().StartWith("eligibility_"));
    }
}
