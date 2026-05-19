using FluentAssertions;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class AdminOnlyProtectedTallyCustodyDataContractTests
{
    [Fact]
    public void CreateAdminOnlyProtectedTallyEnvelope_WithCustodyMetadata_NormalizesAndPreservesFields()
    {
        var createdAt = new DateTime(2026, 5, 19, 1, 0, 0, DateTimeKind.Utc);
        var keyCreatedAt = createdAt.AddSeconds(1);
        var metadata = new ElectionAdminOnlyProtectedTallyCustodyMetadata(
            CustodyMode: $" {ElectionAdminOnlyProtectedTallyCustodyModes.AwsKmsPerElectionEnvelopeV1} ",
            CustodyProvider: " aws-kms ",
            CustodyProviderProfile: " admin-prod-eu-central-1 ",
            KmsKeyId: " key-123 ",
            KmsKeyArn: " arn:aws:kms:eu-central-1:111122223333:key/key-123 ",
            KmsAlias: " alias/hush-election/test ",
            KmsRegion: " eu-central-1 ",
            KmsAccountBoundary: " aws-account:111122223333 ",
            KmsTagSetHash: " sha256:tags ",
            KmsTagsVerifiedAt: keyCreatedAt,
            EncryptionContextVersion: " admin-only-protected-tally-v1 ",
            EncryptionContextHash: " sha256:context ",
            CustodyLifecycleState: ElectionAdminOnlyProtectedTallyCustodyLifecycleState.OpenReady,
            CustodyLastAction: " verify-decrypt-authority ",
            CustodyLastErrorCode: " ",
            CustodyLastErrorMessage: null,
            CustodyRetryCount: -1,
            KmsKeyCreatedAt: keyCreatedAt,
            DeletionWindowDays: 7,
            CustodyActionServiceIdentity: " kms-runtime-role ",
            PublicCustodyReferenceHash: " sha256:public-ref ",
            SealedEnvelopeHash: " sha256:sealed-envelope ");

        var envelope = ElectionModelFactory.CreateAdminOnlyProtectedTallyEnvelope(
            ElectionId.NewElectionId,
            " admin-prod-1of1 ",
            [0x01, 0x02],
            " tally-fingerprint ",
            " sealed-scalar ",
            " scalar-decimal-v1 ",
            " aws-kms-per-election-v1 ",
            sealedByServiceIdentity: " kms-runtime-role ",
            createdAt: createdAt,
            custodyMetadata: metadata);

        envelope.SelectedProfileId.Should().Be("admin-prod-1of1");
        envelope.CustodyMode.Should().Be(ElectionAdminOnlyProtectedTallyCustodyModes.AwsKmsPerElectionEnvelopeV1);
        envelope.CustodyProvider.Should().Be("aws-kms");
        envelope.CustodyProviderProfile.Should().Be("admin-prod-eu-central-1");
        envelope.KmsKeyId.Should().Be("key-123");
        envelope.KmsKeyArn.Should().Be("arn:aws:kms:eu-central-1:111122223333:key/key-123");
        envelope.KmsAlias.Should().Be("alias/hush-election/test");
        envelope.KmsRegion.Should().Be("eu-central-1");
        envelope.KmsAccountBoundary.Should().Be("aws-account:111122223333");
        envelope.KmsTagSetHash.Should().Be("sha256:tags");
        envelope.EncryptionContextVersion.Should().Be("admin-only-protected-tally-v1");
        envelope.EncryptionContextHash.Should().Be("sha256:context");
        envelope.CustodyLifecycleState.Should().Be(ElectionAdminOnlyProtectedTallyCustodyLifecycleState.OpenReady);
        envelope.CustodyLastAction.Should().Be("verify-decrypt-authority");
        envelope.CustodyLastErrorCode.Should().BeNull();
        envelope.CustodyRetryCount.Should().Be(0);
        envelope.KmsKeyCreatedAt.Should().Be(keyCreatedAt);
        envelope.DeletionWindowDays.Should().Be(7);
        envelope.CustodyActionServiceIdentity.Should().Be("kms-runtime-role");
        envelope.PublicCustodyReferenceHash.Should().Be("sha256:public-ref");
        envelope.SealedEnvelopeHash.Should().Be("sha256:sealed-envelope");
        envelope.HasPerElectionKmsCustody.Should().BeTrue();
        envelope.ResolveCustodyLifecycleState().Should().Be(ElectionAdminOnlyProtectedTallyCustodyLifecycleState.OpenReady);
    }

    [Fact]
    public void LegacyStaticKmsEnvelope_WithoutLifecycleMetadata_IsClassifiedAsLegacy()
    {
        var envelope = ElectionModelFactory.CreateAdminOnlyProtectedTallyEnvelope(
            ElectionId.NewElectionId,
            "admin-prod-1of1",
            [0x01],
            "fingerprint",
            "sealed",
            "scalar-decimal-v1",
            "aws-kms-v1");

        envelope.CustodyMode.Should().BeNull();
        envelope.HasPerElectionKmsCustody.Should().BeFalse();
        envelope.ResolveCustodyLifecycleState()
            .Should()
            .Be(ElectionAdminOnlyProtectedTallyCustodyLifecycleState.LegacyStaticKms);
    }

    [Fact]
    public void PublicCustodyEvidenceContract_ExcludesRestrictedKeyFields()
    {
        var publicPropertyNames = typeof(ElectionAdminOnlyProtectedTallyCustodyPublicEvidence)
            .GetProperties()
            .Select(x => x.Name)
            .ToArray();

        publicPropertyNames.Should().NotContain([
            "KmsKeyId",
            "KmsKeyArn",
            "KmsAlias",
            "KmsRawTagSet",
            "IamRoleReference",
            "PrivateCustodyRowReference",
            "ProviderErrorMessage",
        ]);
    }

    [Fact]
    public void ReadinessFragment_RequiresAllCustodyGatesBeforeTargetScoreIncrease()
    {
        var publicEvidence = new ElectionAdminOnlyProtectedTallyCustodyPublicEvidence(
            ElectionAdminOnlyProtectedTallyCustodyReadinessIds.EvidenceId,
            ElectionId.NewElectionId,
            "admin-prod-1of1",
            ElectionAdminOnlyProtectedTallyCustodyModes.AwsKmsPerElectionEnvelopeV1,
            "aws-kms",
            "tally-fingerprint",
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.DeletionScheduled,
            ElectionAdminOnlyProtectedTallyCustodyReadinessIds.RequiredGateIds,
            ["open_passed", "finalization_cleanup_passed", "reconciliation_passed"],
            "sha256:public-ref",
            "passed",
            DateTime.UtcNow);
        var accepted = new ElectionAdminOnlyProtectedTallyCustodyReadinessFragment(
            publicEvidence,
            RestrictedEvidence: null,
            Exceptions: [],
            AcceptedGateIds: ElectionAdminOnlyProtectedTallyCustodyReadinessIds.RequiredGateIds,
            ResidualRiskIds: ["cloud_provider_incident", "iam_drift"],
            ProposedScore: 8);
        var partial = accepted with
        {
            AcceptedGateIds =
            [
                ElectionAdminOnlyProtectedTallyCustodyReadinessIds.OpenGateId,
                ElectionAdminOnlyProtectedTallyCustodyReadinessIds.FinalizationGateId,
            ],
        };

        accepted.CanProposeTargetScoreIncrease.Should().BeTrue();
        partial.HasAcceptedAllRequiredGates.Should().BeFalse();
        partial.CanProposeTargetScoreIncrease.Should().BeFalse();
    }
}
