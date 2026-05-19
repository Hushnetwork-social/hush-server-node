using System.Text;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using FluentAssertions;
using HushNode.Elections;
using HushNode.Reactions.Crypto;
using HushShared.Elections.Model;
using Moq;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class AdminOnlyProtectedTallyEnvelopeCryptoTests
{
    [Fact]
    public void AwsKmsProvider_WithoutKeyId_IsUnavailable()
    {
        var provider = AdminOnlyProtectedTallyEnvelopeCryptoFactory.Create(
            new AdminOnlyProtectedTallyEnvelopeCryptoOptions(
                AdminOnlyProtectedTallyEnvelopeCryptoOptions.ProviderAwsKms));

        provider.IsAvailable(out var error).Should().BeFalse();
        error.Should().Contain("no KMS key id or alias");
    }

    [Fact]
    public void CustodyLifecycleFactory_WithPerElectionProvider_SelectsPerElectionAuthority()
    {
        var authority = AdminOnlyProtectedTallyCustodyLifecycleAuthorityFactory.Create(
            new AdminOnlyProtectedTallyEnvelopeCryptoOptions(
                AdminOnlyProtectedTallyEnvelopeCryptoOptions.ProviderAwsKmsPerElection,
                AwsKmsRegion: "eu-central-1",
                CustodyProviderProfile: "admin-prod-eu-central-1"));

        try
        {
            authority.Should().BeOfType<AwsKmsPerElectionAdminOnlyProtectedTallyCustodyLifecycleAuthority>();
        }
        finally
        {
            (authority as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void CustodyLifecycleFactory_WithStaticAwsKmsProvider_DoesNotSelectPerElectionAuthority()
    {
        var authority = AdminOnlyProtectedTallyCustodyLifecycleAuthorityFactory.Create(
            new AdminOnlyProtectedTallyEnvelopeCryptoOptions(
                AdminOnlyProtectedTallyEnvelopeCryptoOptions.ProviderAwsKms,
                AwsKmsKeyId: "alias/static-admin-only-tally"));

        authority.Should().BeSameAs(NoOpAdminOnlyProtectedTallyCustodyLifecycleAuthority.Instance);
    }

    [Fact]
    public void SealPrivateScalar_WithAwsKms_UsesConfiguredKeyAndElectionContext()
    {
        var electionId = ElectionId.NewElectionId;
        EncryptRequest? capturedRequest = null;
        var kmsClient = new Mock<IAmazonKeyManagementService>();
        kmsClient
            .Setup(x => x.EncryptAsync(
                It.IsAny<EncryptRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<EncryptRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new EncryptResponse
            {
                CiphertextBlob = new MemoryStream([0x01, 0x02, 0x03]),
            });

        var crypto = CreateAwsKmsCrypto(kmsClient.Object);

        var sealedScalar = crypto.SealPrivateScalar(
            "  12345  ",
            electionId,
            "admin-prod-1of1");

        sealedScalar.Should().Be(Convert.ToBase64String([0x01, 0x02, 0x03]));
        capturedRequest.Should().NotBeNull();
        capturedRequest!.KeyId.Should().Be("alias/hush-election-admin-only-tally-test");
        Encoding.UTF8.GetString(capturedRequest.Plaintext.ToArray()).Should().Be("12345");
        capturedRequest.EncryptionContext.Should().Contain(
            new KeyValuePair<string, string>("hush-purpose", "hush:elections:admin-only-protected-tally-scalar:v1"));
        capturedRequest.EncryptionContext.Should().Contain(
            new KeyValuePair<string, string>("election-id", electionId.ToString()));
        capturedRequest.EncryptionContext.Should().Contain(
            new KeyValuePair<string, string>("selected-profile-id", "admin-prod-1of1"));
        capturedRequest.EncryptionContext.Should().Contain(
            new KeyValuePair<string, string>(
                "scalar-encoding",
                AdminOnlyProtectedTallyEnvelopeCryptoConstants.ScalarEncoding));
    }

    [Fact]
    public void TryUnsealPrivateScalar_WithAwsKms_UsesEnvelopeContext()
    {
        var electionId = ElectionId.NewElectionId;
        DecryptRequest? capturedRequest = null;
        var kmsClient = new Mock<IAmazonKeyManagementService>();
        kmsClient
            .Setup(x => x.DecryptAsync(
                It.IsAny<DecryptRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<DecryptRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new DecryptResponse
            {
                Plaintext = new MemoryStream(Encoding.UTF8.GetBytes("67890")),
            });

        var crypto = CreateAwsKmsCrypto(kmsClient.Object);
        var envelope = ElectionModelFactory.CreateAdminOnlyProtectedTallyEnvelope(
            electionId,
            "admin-prod-1of1",
            [0x08, 0x09],
            "fingerprint",
            Convert.ToBase64String([0x04, 0x05, 0x06]),
            AdminOnlyProtectedTallyEnvelopeCryptoConstants.ScalarEncoding,
            crypto.SealAlgorithm,
            crypto.SealedByServiceIdentity);

        var scalar = crypto.TryUnsealPrivateScalar(envelope, out var error);

        scalar.Should().Be("67890");
        error.Should().BeEmpty();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.KeyId.Should().BeNullOrEmpty();
        capturedRequest.CiphertextBlob.ToArray().Should().Equal([0x04, 0x05, 0x06]);
        capturedRequest.EncryptionContext.Should().Contain(
            new KeyValuePair<string, string>("election-id", electionId.ToString()));
        capturedRequest.EncryptionContext.Should().Contain(
            new KeyValuePair<string, string>("selected-profile-id", "admin-prod-1of1"));
    }

    [Fact]
    public void PerElectionCustodyAuthority_EvaluateOpenReadiness_DoesNotCreateKey()
    {
        var kmsClient = new Mock<IAmazonKeyManagementService>(MockBehavior.Strict);
        var authority = CreatePerElectionAuthority(kmsClient.Object);
        var election = CreateAdminElection();
        var profile = CreateAdminProfile();

        var ready = authority.EvaluateOpenReadiness(election, profile, out var error);

        ready.Should().BeTrue();
        error.Should().BeEmpty();
        kmsClient.VerifyNoOtherCalls();
    }

    [Fact]
    public void PerElectionCustodyAuthority_PrepareOpenCustody_CreatesTaggedKeyAliasAndVerifiesDecrypt()
    {
        var recordedAt = DateTime.UtcNow;
        var election = CreateAdminElection();
        var profile = CreateAdminProfile();
        CreateKeyRequest? capturedCreateKey = null;
        CreateAliasRequest? capturedAlias = null;
        EncryptRequest? capturedEncrypt = null;
        DecryptRequest? capturedDecrypt = null;
        byte[]? encryptedPlaintext = null;
        var kmsClient = new Mock<IAmazonKeyManagementService>();
        kmsClient
            .Setup(x => x.CreateKeyAsync(
                It.IsAny<CreateKeyRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateKeyRequest, CancellationToken>((request, _) => capturedCreateKey = request)
            .ReturnsAsync(new CreateKeyResponse
            {
                KeyMetadata = new KeyMetadata
                {
                    KeyId = "key-123",
                    Arn = "arn:aws:kms:eu-central-1:111122223333:key/key-123",
                    CreationDate = recordedAt.AddSeconds(-1),
                },
            });
        kmsClient
            .Setup(x => x.CreateAliasAsync(
                It.IsAny<CreateAliasRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateAliasRequest, CancellationToken>((request, _) => capturedAlias = request)
            .ReturnsAsync(new CreateAliasResponse());
        kmsClient
            .Setup(x => x.EncryptAsync(
                It.IsAny<EncryptRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<EncryptRequest, CancellationToken>((request, _) =>
            {
                capturedEncrypt = request;
                encryptedPlaintext = request.Plaintext.ToArray();
            })
            .ReturnsAsync(new EncryptResponse
            {
                CiphertextBlob = new MemoryStream([0x0A, 0x0B, 0x0C]),
            });
        kmsClient
            .Setup(x => x.DecryptAsync(
                It.IsAny<DecryptRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<DecryptRequest, CancellationToken>((request, _) => capturedDecrypt = request)
            .ReturnsAsync(() => new DecryptResponse
            {
                Plaintext = new MemoryStream(encryptedPlaintext!),
            });

        var authority = CreatePerElectionAuthority(kmsClient.Object);

        var result = authority.PrepareOpenCustody(
            election,
            profile,
            existingEnvelope: null,
            new BabyJubJubCurve(),
            recordedAt);

        result.IsSuccess.Should().BeTrue();
        result.Snapshot.Should().NotBeNull();
        result.EnvelopeToPersist.Should().NotBeNull();
        result.EnvelopeToPersist!.CustodyMode.Should()
            .Be(ElectionAdminOnlyProtectedTallyCustodyModes.AwsKmsPerElectionEnvelopeV1);
        result.EnvelopeToPersist.CustodyLifecycleState.Should()
            .Be(ElectionAdminOnlyProtectedTallyCustodyLifecycleState.OpenBound);
        result.EnvelopeToPersist.KmsKeyId.Should().Be("key-123");
        result.EnvelopeToPersist.KmsAlias.Should().StartWith("alias/hush-voting/admin-only/admin-prod-1of1/");
        result.EnvelopeToPersist.KmsAccountBoundary.Should().Be("aws-account:111122223333");
        result.EnvelopeToPersist.EncryptionContextHash.Should().NotBeNullOrWhiteSpace();
        result.EnvelopeToPersist.SealedEnvelopeHash.Should().NotBeNullOrWhiteSpace();
        capturedCreateKey.Should().NotBeNull();
        capturedCreateKey!.Tags.Should().Contain(x =>
            x.TagKey == "hush:election-id" &&
            x.TagValue == election.ElectionId.ToString());
        capturedAlias.Should().NotBeNull();
        capturedAlias!.TargetKeyId.Should().Be("key-123");
        capturedEncrypt.Should().NotBeNull();
        capturedEncrypt!.KeyId.Should().Be("key-123");
        capturedEncrypt.EncryptionContext.Should().Contain(
            new KeyValuePair<string, string>("election-id", election.ElectionId.ToString()));
        capturedEncrypt.EncryptionContext.Should().Contain(
            new KeyValuePair<string, string>("selected-profile-id", "admin-prod-1of1"));
        capturedDecrypt.Should().NotBeNull();
        capturedDecrypt!.EncryptionContext.Should().Contain(
            new KeyValuePair<string, string>("election-id", election.ElectionId.ToString()));
    }

    [Fact]
    public void TransparentTestCustodyAuthority_CanSimulateExistingCustodyDrift()
    {
        var recordedAt = DateTime.UtcNow;
        var election = CreateAdminElection();
        var profile = CreateAdminProfile();
        var authority = new TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority();
        var initial = authority.PrepareOpenCustody(
            election,
            profile,
            existingEnvelope: null,
            new BabyJubJubCurve(),
            recordedAt);

        authority.DetectExistingEnvelopeDrift = true;
        var drift = authority.PrepareOpenCustody(
            election,
            profile,
            initial.EnvelopeToPersist,
            new BabyJubJubCurve(),
            recordedAt.AddMinutes(1));

        drift.IsSuccess.Should().BeFalse();
        drift.EnvelopeToPersist.Should().NotBeNull();
        drift.EnvelopeToPersist!.CustodyLifecycleState.Should()
            .Be(ElectionAdminOnlyProtectedTallyCustodyLifecycleState.ExceptionRequired);
        drift.EnvelopeToPersist.CustodyLastErrorCode.Should().Be("FAKE_CUSTODY_DRIFT_DETECTED");
        authority.CreatedEnvelopeCount.Should().Be(1);
    }

    [Fact]
    public void PerElectionCustodyAuthority_PrepareOpenCustody_RejectsExistingAliasDrift()
    {
        var recordedAt = DateTime.UtcNow;
        var election = CreateAdminElection();
        var profile = CreateAdminProfile();
        var fakeAuthority = new TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority();
        var existing = fakeAuthority.PrepareOpenCustody(
            election,
            profile,
            existingEnvelope: null,
            new BabyJubJubCurve(),
            recordedAt).EnvelopeToPersist!;
        var kmsClient = new Mock<IAmazonKeyManagementService>(MockBehavior.Strict);
        var authority = CreatePerElectionAuthority(kmsClient.Object);

        var result = authority.PrepareOpenCustody(
            election,
            profile,
            existing,
            new BabyJubJubCurve(),
            recordedAt.AddMinutes(1));

        result.IsSuccess.Should().BeFalse();
        result.EnvelopeToPersist.Should().NotBeNull();
        result.EnvelopeToPersist!.CustodyLifecycleState.Should()
            .Be(ElectionAdminOnlyProtectedTallyCustodyLifecycleState.ExceptionRequired);
        result.EnvelopeToPersist.CustodyLastErrorCode.Should().Be("KMS_ALIAS_MISMATCH");
        kmsClient.VerifyNoOtherCalls();
    }

    [Fact]
    public void PerElectionCustodyAuthority_PrepareOpenCustody_WhenKmsCreateFails_ReturnsFailure()
    {
        var election = CreateAdminElection();
        var profile = CreateAdminProfile();
        var kmsClient = new Mock<IAmazonKeyManagementService>();
        kmsClient
            .Setup(x => x.CreateKeyAsync(
                It.IsAny<CreateKeyRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Amazon.KeyManagementService.AmazonKeyManagementServiceException("access denied"));
        var authority = CreatePerElectionAuthority(kmsClient.Object);

        var result = authority.PrepareOpenCustody(
            election,
            profile,
            existingEnvelope: null,
            new BabyJubJubCurve(),
            DateTime.UtcNow);

        result.IsSuccess.Should().BeFalse();
        result.EnvelopeToPersist.Should().BeNull();
        result.Error.Should().Contain("AWS KMS per-election admin-only protected tally custody failed");
    }

    [Fact]
    public void PerElectionEnvelopeCrypto_TryUnsealPrivateScalar_UsesEnvelopeKeyAndContext()
    {
        var electionId = ElectionId.NewElectionId;
        DecryptRequest? capturedRequest = null;
        var kmsClient = new Mock<IAmazonKeyManagementService>();
        kmsClient
            .Setup(x => x.DecryptAsync(
                It.IsAny<DecryptRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<DecryptRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new DecryptResponse
            {
                Plaintext = new MemoryStream(Encoding.UTF8.GetBytes("12345")),
            });
        var crypto = new AwsKmsPerElectionAdminOnlyProtectedTallyEnvelopeCrypto(
            CreatePerElectionOptions(),
            kmsClient.Object);
        var envelope = ElectionModelFactory.CreateAdminOnlyProtectedTallyEnvelope(
            electionId,
            "admin-prod-1of1",
            [0x08, 0x09],
            "fingerprint",
            Convert.ToBase64String([0x04, 0x05, 0x06]),
            AdminOnlyProtectedTallyEnvelopeCryptoConstants.ScalarEncoding,
            crypto.SealAlgorithm,
            custodyMetadata: new ElectionAdminOnlyProtectedTallyCustodyMetadata(
                CustodyMode: ElectionAdminOnlyProtectedTallyCustodyModes.AwsKmsPerElectionEnvelopeV1,
                KmsKeyId: "key-envelope-123"));

        var scalar = crypto.TryUnsealPrivateScalar(envelope, out var error);

        scalar.Should().Be("12345");
        error.Should().BeEmpty();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.KeyId.Should().Be("key-envelope-123");
        capturedRequest.EncryptionContext.Should().Contain(
            new KeyValuePair<string, string>("election-id", electionId.ToString()));
    }

    [Fact]
    public void PerElectionCustodyAuthority_BuildFinalizationCleanup_DisablesAndSchedulesDeletion()
    {
        var destroyedAt = DateTime.UtcNow;
        DisableKeyRequest? capturedDisable = null;
        ScheduleKeyDeletionRequest? capturedDeletion = null;
        var kmsClient = new Mock<IAmazonKeyManagementService>();
        kmsClient
            .Setup(x => x.DisableKeyAsync(
                It.IsAny<DisableKeyRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<DisableKeyRequest, CancellationToken>((request, _) => capturedDisable = request)
            .ReturnsAsync(new DisableKeyResponse());
        kmsClient
            .Setup(x => x.ScheduleKeyDeletionAsync(
                It.IsAny<ScheduleKeyDeletionRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<ScheduleKeyDeletionRequest, CancellationToken>((request, _) => capturedDeletion = request)
            .ReturnsAsync(new ScheduleKeyDeletionResponse
            {
                DeletionDate = destroyedAt.AddDays(7),
            });
        var authority = CreatePerElectionAuthority(kmsClient.Object);
        var envelope = ElectionModelFactory.CreateAdminOnlyProtectedTallyEnvelope(
            ElectionId.NewElectionId,
            "admin-prod-1of1",
            [0x01],
            "fingerprint",
            Convert.ToBase64String([0x02]),
            AdminOnlyProtectedTallyEnvelopeCryptoConstants.ScalarEncoding,
            "aws-kms-v1",
            custodyMetadata: new ElectionAdminOnlyProtectedTallyCustodyMetadata(
                CustodyMode: ElectionAdminOnlyProtectedTallyCustodyModes.AwsKmsPerElectionEnvelopeV1,
                KmsKeyId: "key-cleanup-123",
                DeletionWindowDays: 7));

        var cleanup = authority.BuildFinalizationCleanup(envelope, destroyedAt);

        cleanup.Handled.Should().BeTrue();
        cleanup.Error.Should().BeEmpty();
        cleanup.EnvelopeToPersist.Should().NotBeNull();
        cleanup.EnvelopeToPersist!.SealedTallyPrivateScalar.Should()
            .Be(AdminOnlyProtectedTallyEnvelopeCryptoConstants.DestroyedEnvelopeMarker);
        cleanup.EnvelopeToPersist.CustodyLifecycleState.Should()
            .Be(ElectionAdminOnlyProtectedTallyCustodyLifecycleState.DeletionScheduled);
        cleanup.EnvelopeToPersist.KmsKeyDisabledAt.Should().Be(destroyedAt);
        cleanup.EnvelopeToPersist.KmsDeletionScheduledAt.Should().Be(destroyedAt);
        cleanup.EnvelopeToPersist.KmsDeletionDate.Should().Be(destroyedAt.AddDays(7));
        capturedDisable.Should().NotBeNull();
        capturedDisable!.KeyId.Should().Be("key-cleanup-123");
        capturedDeletion.Should().NotBeNull();
        capturedDeletion!.PendingWindowInDays.Should().Be(7);
    }

    [Fact]
    public void PerElectionCustodyAuthority_BuildFinalizationCleanup_WhenDisableFails_KeepsScalarDestroyedAndRecordsRetry()
    {
        var destroyedAt = DateTime.UtcNow;
        var kmsClient = new Mock<IAmazonKeyManagementService>();
        kmsClient
            .Setup(x => x.DisableKeyAsync(
                It.IsAny<DisableKeyRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Amazon.KeyManagementService.AmazonKeyManagementServiceException("disable denied"));
        var authority = CreatePerElectionAuthority(kmsClient.Object);
        var envelope = ElectionModelFactory.CreateAdminOnlyProtectedTallyEnvelope(
            ElectionId.NewElectionId,
            "admin-prod-1of1",
            [0x01],
            "fingerprint",
            Convert.ToBase64String([0x02]),
            AdminOnlyProtectedTallyEnvelopeCryptoConstants.ScalarEncoding,
            "aws-kms-v1",
            custodyMetadata: new ElectionAdminOnlyProtectedTallyCustodyMetadata(
                CustodyMode: ElectionAdminOnlyProtectedTallyCustodyModes.AwsKmsPerElectionEnvelopeV1,
                KmsKeyId: "key-cleanup-123",
                DeletionWindowDays: 7));

        var cleanup = authority.BuildFinalizationCleanup(envelope, destroyedAt);

        cleanup.Handled.Should().BeTrue();
        cleanup.Error.Should().Contain("requires retry");
        cleanup.EnvelopeToPersist.Should().NotBeNull();
        cleanup.EnvelopeToPersist!.SealedTallyPrivateScalar.Should()
            .Be(AdminOnlyProtectedTallyEnvelopeCryptoConstants.DestroyedEnvelopeMarker);
        cleanup.EnvelopeToPersist.CustodyLifecycleState.Should()
            .Be(ElectionAdminOnlyProtectedTallyCustodyLifecycleState.RetryRequired);
        cleanup.EnvelopeToPersist.CustodyLastErrorCode.Should().Be("KMS_FINALIZATION_CLEANUP_FAILED");
        cleanup.EnvelopeToPersist.DestroyedAt.Should().Be(destroyedAt);
    }

    [Fact]
    public void TryUnsealPrivateScalar_WithDifferentAlgorithm_ReturnsError()
    {
        var kmsClient = new Mock<IAmazonKeyManagementService>();
        var crypto = CreateAwsKmsCrypto(kmsClient.Object);
        var envelope = ElectionModelFactory.CreateAdminOnlyProtectedTallyEnvelope(
            ElectionId.NewElectionId,
            "admin-prod-1of1",
            [0x08, 0x09],
            "fingerprint",
            Convert.ToBase64String([0x04, 0x05, 0x06]),
            AdminOnlyProtectedTallyEnvelopeCryptoConstants.ScalarEncoding,
            "windows-dpapi-current-user-v1");

        var scalar = crypto.TryUnsealPrivateScalar(envelope, out var error);

        scalar.Should().BeNull();
        error.Should().Contain("Seal algorithm mismatch");
        kmsClient.Verify(
            x => x.DecryptAsync(It.IsAny<DecryptRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static AwsKmsAdminOnlyProtectedTallyEnvelopeCrypto CreateAwsKmsCrypto(
        IAmazonKeyManagementService kmsClient) =>
        new(
            new AdminOnlyProtectedTallyEnvelopeCryptoOptions(
                AdminOnlyProtectedTallyEnvelopeCryptoOptions.ProviderAwsKms,
                AwsKmsKeyId: "alias/hush-election-admin-only-tally-test",
                AwsKmsRegion: "eu-central-1"),
            kmsClient);

    private static AwsKmsPerElectionAdminOnlyProtectedTallyCustodyLifecycleAuthority CreatePerElectionAuthority(
        IAmazonKeyManagementService kmsClient) =>
        new(CreatePerElectionOptions(), kmsClient);

    private static AdminOnlyProtectedTallyEnvelopeCryptoOptions CreatePerElectionOptions() =>
        new(
            AdminOnlyProtectedTallyEnvelopeCryptoOptions.ProviderAwsKmsPerElection,
            AwsKmsRegion: "eu-central-1",
            AwsKmsDeletionWindowDays: 7,
            CustodyProviderProfile: "unit-test");

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
