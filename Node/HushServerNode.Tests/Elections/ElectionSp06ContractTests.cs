using System.Text.Json;
using FluentAssertions;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionSp06ContractTests
{
    [Fact]
    public void ControlDomainRecord_ShouldRepresentRestrictedTrusteeEvidence()
    {
        var record = CreateRestrictedControlDomainRecord();

        record.ControlDomainProfileId.Should().Be(ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1);
        record.ControlDomainProfileVersion.Should().Be(ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1Version);
        record.ThresholdProfileId.Should().Be(ElectionSelectableProfileCatalog.TrusteeProductionProfileId);
        record.TrusteeAccountId.Should().Be("trustee-account-1");
        record.TrusteePersonRef.Should().Be("person-ref-1");
        record.CustodyMode.Should().Be(ElectionSp06ProfileIds.TrusteeLocalSecureVaultV1);
        record.AcceptedBeforeOpen.Should().BeTrue();
        record.EvidenceStatus.Should().Be(ElectionTrusteeControlDomainEvidenceStatus.Accepted);
    }

    [Fact]
    public void PublicSp06Artifacts_ShouldExcludeRestrictedTrusteeControlMaterial()
    {
        var electionId = ElectionId.NewElectionId.ToString();
        var profile = new ElectionSp06ControlProfileArtifactRecord(
            electionId,
            ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1,
            ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1Version,
            ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
            TrusteeCount: 5,
            TrusteeThreshold: 3,
            HighAssuranceClaimed: true,
            AllowedCustodyModes:
            [
                ElectionSp06ProfileIds.TrusteeLocalSecureVaultV1,
                ElectionSp06ProfileIds.ManagedTrusteeAppV1,
            ],
            PublicPrivacyBoundary:
            [
                "no_trustee_account_id",
                "no_trustee_person_ref",
                "no_custody_domain_ref",
                "no_raw_trustee_share",
            ]);
        var summary = new ElectionSp06TrusteeControlSummaryArtifactRecord(
            electionId,
            ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1,
            ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1Version,
            ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
            TrusteeCount: 5,
            TrusteeThreshold: 3,
            AcceptedBeforeOpenCount: 5,
            CompleteEvidenceCount: 5,
            MissingEvidenceCount: 0,
            StaleEvidenceCount: 0,
            IncompatibleEvidenceCount: 0,
            AcceptedReleaseArtifactCount: 3,
            MissingReleaseArtifactCount: 2,
            RejectedReleaseArtifactCount: 0,
            FinalEncryptedTallyHash: "final-tally-hash",
            TargetTallyId: "target-tally-id",
            ExecutorSessionPublicKeyHash: "executor-public-key-hash",
            ExecutorKeyAlgorithm: "ecies-secp256k1-v1",
            Trustees:
            [
                new ElectionSp06TrusteeControlSummaryRowArtifactRecord(
                    TrusteeId: "trustee-1",
                    TrusteePseudonym: "trustee-ref-1",
                    ElectionTrusteeControlDomainEvidenceStatus.Accepted,
                    ElectionTrusteeReleaseArtifactStatus.Accepted,
                    AcceptedBeforeOpen: true,
                    AcceptedAt: DateTime.UtcNow,
                    PublicKeyCommitmentHash: "public-key-commitment-hash",
                    CustodyDomainEvidenceHash: "custody-domain-evidence-hash",
                    AdminDomainEvidenceHash: "admin-domain-evidence-hash",
                    ReleaseArtifactHash: "release-artifact-hash",
                    ShareMaterialHash: "share-material-hash",
                    FailureCode: null),
            ],
            ReadinessBlockers: [],
            PublicPrivacyBoundary: profile.PublicPrivacyBoundary);

        var json = JsonSerializer.Serialize(new { profile, summary }, VerificationJson.Options);

        json.Should().Contain("controlDomainProfileId");
        json.Should().Contain("trusteePseudonym");
        json.Should().Contain("custodyDomainEvidenceHash");
        json.Should().Contain("shareMaterialHash");
        json.Should().NotContain("trusteeAccountId");
        json.Should().NotContain("trusteePersonRef");
        json.Should().NotContain("custodyDomainRefHash");
        json.Should().NotContain("adminDomainRefHash");
        json.Should().NotContain("rawTrusteeShare");
        json.Should().NotContain("privateKey");
    }

    [Fact]
    public void RestrictedSp06Artifact_ShouldCarryCustodyAndPersonEvidence()
    {
        var electionId = ElectionId.NewElectionId;
        var artifact = new ElectionSp06RestrictedControlDomainEvidenceArtifactRecord(
            electionId.ToString(),
            [CreateRestrictedControlDomainRecord(electionId)]);

        var json = JsonSerializer.Serialize(artifact, VerificationJson.Options);

        json.Should().Contain("trusteeAccountId");
        json.Should().Contain("trusteePersonRef");
        json.Should().Contain("custodyDomainRefHash");
        json.Should().Contain("adminDomainRefHash");
    }

    [Fact]
    public void ControlDomainSummary_ShouldRepresentMissingEvidenceAsOpenBlocking()
    {
        var summary = new ElectionTrusteeControlDomainSummaryRecord(
            ElectionId.NewElectionId,
            ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1,
            ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1Version,
            ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
            RequiredTrusteeCount: 5,
            RequiredThreshold: 3,
            AcceptedBeforeOpenCount: 4,
            CompleteEvidenceCount: 4,
            MissingEvidenceCount: 1,
            StaleEvidenceCount: 0,
            IncompatibleEvidenceCount: 0,
            IsReadyForOpen: false,
            Trustees:
            [
                new ElectionTrusteeControlDomainSummaryRowRecord(
                    TrusteeId: "trustee-5",
                    TrusteePseudonym: "trustee-ref-5",
                    ElectionTrusteeControlDomainEvidenceStatus.Missing,
                    AcceptedBeforeOpen: false,
                    AcceptedAt: null,
                    PublicKeyCommitmentHash: null,
                    CustodyDomainEvidenceHash: null,
                    AdminDomainEvidenceHash: null,
                    ElectionTrusteeBackupStatus.Missing,
                    ElectionTrusteeExceptionStatus.None,
                    FailureCode: "control_domain_evidence_missing"),
            ],
            ReadinessBlockers:
            [
                new ElectionTrusteeControlDomainReadinessBlockerRecord(
                    Code: "control_domain_evidence_missing",
                    Message: "Trustee control-domain evidence is missing.",
                    TrusteeId: "trustee-5",
                    BlocksOpen: true,
                    BlocksFinalization: false),
            ]);

        summary.IsReadyForOpen.Should().BeFalse();
        summary.MissingEvidenceCount.Should().Be(1);
        summary.Trustees.Should().ContainSingle(x =>
            x.EvidenceStatus == ElectionTrusteeControlDomainEvidenceStatus.Missing &&
            x.FailureCode == "control_domain_evidence_missing");
        summary.ReadinessBlockers.Should().ContainSingle(x => x.BlocksOpen);
    }

    [Fact]
    public void Sp06PackageFileNames_ShouldSeparatePublicAndRestrictedEvidence()
    {
        VerificationPackageFileNames.Sp06TrusteeControlProfile.Should()
            .Be("artifacts/election-record/trustee-control-profile.json");
        VerificationPackageFileNames.Sp06TrusteeControlSummary.Should()
            .Be("artifacts/election-record/trustee-control-summary.json");
        VerificationPackageFileNames.Sp06TrusteeVerifierOutput.Should()
            .Be("artifacts/election-record/trustee-verifier-output.json");
        VerificationPackageFileNames.RestrictedSp06TrusteeControlDomains.Should().StartWith("artifacts/restricted/");
        VerificationPackageFileNames.RestrictedSp06TrusteeReleaseArtifacts.Should().StartWith("artifacts/restricted/");
    }

    [Theory]
    [InlineData("trusteeAccountId")]
    [InlineData("trusteePersonRef")]
    [InlineData("custodyDomainRefHash")]
    [InlineData("adminDomainRefHash")]
    [InlineData("legalEntityRefHash")]
    [InlineData("rawTrusteeShare")]
    public void PublicPrivacyBoundary_ShouldRejectSp06RestrictedFields(string fieldName)
    {
        VerificationPrivacyBoundary.IsForbiddenInPublicPackage(fieldName).Should().BeTrue();
    }

    [Fact]
    public void VerificationResultCodes_ShouldExposeStableCtrlCodes()
    {
        var codes = new[]
        {
            VerificationResultCodes.TrusteeControlDomainEvidenceValid,
            VerificationResultCodes.TrusteeControlProfileMissing,
            VerificationResultCodes.TrusteeThresholdProfileMismatch,
            VerificationResultCodes.TrusteeAcceptanceIncomplete,
            VerificationResultCodes.TrusteeDuplicateAccount,
            VerificationResultCodes.TrusteeDuplicatePerson,
            VerificationResultCodes.TrusteeDuplicateCustodyDomain,
            VerificationResultCodes.TrusteeAdminDomainThresholdViolation,
            VerificationResultCodes.TrusteeCustodyModeUnsupported,
            VerificationResultCodes.TrusteeReleaseWrongTarget,
            VerificationResultCodes.TrusteeReleaseThresholdNotMet,
            VerificationResultCodes.TrusteeRawMaterialLeaked,
            VerificationResultCodes.TrusteeExceptionPolicyViolation,
        };

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().AllSatisfy(x => x.Should().StartWith("trustee_"));
    }

    private static ElectionTrusteeControlDomainRecord CreateRestrictedControlDomainRecord(
        ElectionId? electionId = null) =>
        new(
            Guid.NewGuid(),
            electionId ?? ElectionId.NewElectionId,
            ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1,
            ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1Version,
            ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
            CeremonyVersionId: Guid.NewGuid(),
            TrusteeId: "trustee-1",
            TrusteeAccountId: "trustee-account-1",
            TrusteePersonRef: "person-ref-1",
            ElectionTrusteeRole.ExternalTrustee,
            CustodyMode: ElectionSp06ProfileIds.TrusteeLocalSecureVaultV1,
            CustodyDomainRefHash: "custody-domain-hash-1",
            AdminDomainRefHash: "admin-domain-hash-1",
            LegalEntityRefHash: "legal-entity-hash-1",
            PublicKeyCommitmentHash: "public-key-commitment-hash-1",
            AcceptedAt: DateTime.UtcNow.AddMinutes(-10),
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
            SourceBlockId: null);
}
