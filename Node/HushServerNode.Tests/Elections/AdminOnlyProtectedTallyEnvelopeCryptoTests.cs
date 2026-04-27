using System.Text;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using FluentAssertions;
using HushNode.Elections;
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
        capturedRequest!.KeyId.Should().Be("alias/hush-election-admin-only-tally-test");
        capturedRequest.CiphertextBlob.ToArray().Should().Equal([0x04, 0x05, 0x06]);
        capturedRequest.EncryptionContext.Should().Contain(
            new KeyValuePair<string, string>("election-id", electionId.ToString()));
        capturedRequest.EncryptionContext.Should().Contain(
            new KeyValuePair<string, string>("selected-profile-id", "admin-prod-1of1"));
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
}
