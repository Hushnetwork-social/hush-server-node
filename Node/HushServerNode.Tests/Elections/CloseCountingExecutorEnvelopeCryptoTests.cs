using System.Text;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using Moq;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class CloseCountingExecutorEnvelopeCryptoTests
{
    [Fact]
    public void AwsKmsProvider_WithoutKeyId_IsUnavailable()
    {
        var provider = CloseCountingExecutorEnvelopeCryptoFactory.Create(
            new CloseCountingExecutorEnvelopeCryptoOptions(
                CloseCountingExecutorEnvelopeCryptoOptions.ProviderAwsKms));

        provider.IsAvailable(out var error).Should().BeFalse();
        error.Should().Contain("unavailable");
    }

    [Fact]
    public void SealPrivateKey_WithAwsKms_UsesConfiguredKeyAndJobContext()
    {
        var closeCountingJobId = Guid.NewGuid();
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

        var sealedKey = crypto.SealPrivateKey(
            "  executor-private-key  ",
            closeCountingJobId,
            "x25519-xsalsa20-poly1305-v1");

        sealedKey.Should().Be(Convert.ToBase64String([0x01, 0x02, 0x03]));
        capturedRequest.Should().NotBeNull();
        capturedRequest!.KeyId.Should().Be("alias/hush-election-close-counting-test");
        Encoding.UTF8.GetString(capturedRequest.Plaintext.ToArray()).Should().Be("executor-private-key");
        capturedRequest.EncryptionContext.Should().Contain(
            new KeyValuePair<string, string>("hush-purpose", "hush:elections:close-counting-executor-session-key:v1"));
        capturedRequest.EncryptionContext.Should().Contain(
            new KeyValuePair<string, string>("close-counting-job-id", closeCountingJobId.ToString("D")));
        capturedRequest.EncryptionContext.Should().Contain(
            new KeyValuePair<string, string>("key-algorithm", "x25519-xsalsa20-poly1305-v1"));
    }

    [Fact]
    public void TryUnsealPrivateKey_WithAwsKms_UsesEnvelopeContextAndOmitsCurrentKeyId()
    {
        var closeCountingJobId = Guid.NewGuid();
        DecryptRequest? capturedRequest = null;
        var kmsClient = new Mock<IAmazonKeyManagementService>();
        kmsClient
            .Setup(x => x.DecryptAsync(
                It.IsAny<DecryptRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<DecryptRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new DecryptResponse
            {
                Plaintext = new MemoryStream(Encoding.UTF8.GetBytes("executor-private-key")),
            });

        var crypto = CreateAwsKmsCrypto(kmsClient.Object);
        var envelope = ElectionModelFactory.CreateExecutorSessionKeyEnvelope(
            closeCountingJobId,
            "executor-public-key",
            Convert.ToBase64String([0x04, 0x05, 0x06]),
            "x25519-xsalsa20-poly1305-v1",
            crypto.SealAlgorithm,
            sealedByServiceIdentity: crypto.SealedByServiceIdentity);

        var privateKey = crypto.TryUnsealPrivateKey(envelope, out var error);

        privateKey.Should().Be("executor-private-key");
        error.Should().BeEmpty();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.KeyId.Should().BeNullOrEmpty();
        capturedRequest.CiphertextBlob.ToArray().Should().Equal([0x04, 0x05, 0x06]);
        capturedRequest.EncryptionContext.Should().Contain(
            new KeyValuePair<string, string>("close-counting-job-id", closeCountingJobId.ToString("D")));
        capturedRequest.EncryptionContext.Should().Contain(
            new KeyValuePair<string, string>("key-algorithm", "x25519-xsalsa20-poly1305-v1"));
    }

    [Fact]
    public void TryUnsealPrivateKey_WithDifferentAlgorithm_ReturnsError()
    {
        var kmsClient = new Mock<IAmazonKeyManagementService>();
        var crypto = CreateAwsKmsCrypto(kmsClient.Object);
        var envelope = ElectionModelFactory.CreateExecutorSessionKeyEnvelope(
            Guid.NewGuid(),
            "executor-public-key",
            Convert.ToBase64String([0x04, 0x05, 0x06]),
            "x25519-xsalsa20-poly1305-v1",
            "windows-dpapi-current-user-v1");

        var privateKey = crypto.TryUnsealPrivateKey(envelope, out var error);

        privateKey.Should().BeNull();
        error.Should().Contain("Seal algorithm mismatch");
        kmsClient.Verify(
            x => x.DecryptAsync(It.IsAny<DecryptRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static AwsKmsCloseCountingExecutorEnvelopeCrypto CreateAwsKmsCrypto(
        IAmazonKeyManagementService kmsClient) =>
        new(
            new CloseCountingExecutorEnvelopeCryptoOptions(
                CloseCountingExecutorEnvelopeCryptoOptions.ProviderAwsKms,
                AwsKmsKeyId: "alias/hush-election-close-counting-test",
                AwsKmsRegion: "eu-central-1"),
            kmsClient);
}
