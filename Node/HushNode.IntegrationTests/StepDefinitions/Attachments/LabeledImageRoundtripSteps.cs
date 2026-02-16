using System.Security.Cryptography;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Feeds.Model;
using HushServerNode.Testing;
using SkiaSharp;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.Attachments;

/// <summary>
/// Twin test: verifies labeled PNG images from TestImageGenerator survive
/// the full gRPC upload → PostgreSQL storage → gRPC streaming download pipeline.
/// Tests both original and thumbnail variants.
/// </summary>
[Binding]
public sealed class LabeledImageRoundtripSteps
{
    private readonly ScenarioContext _scenarioContext;

    private byte[]? _originalPngBytes;
    private byte[]? _thumbnailPngBytes;
    private byte[]? _encryptedOriginal;
    private byte[]? _encryptedThumbnail;
    private string? _attachmentId;
    private string? _attachmentHash;

    // Downloaded bytes from gRPC streaming
    private byte[]? _downloadedOriginal;
    private byte[]? _downloadedThumbnail;

    // AES key for decryption verification
    private string? _aesKey;

    public LabeledImageRoundtripSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [When(@"Alice sends a labeled image (\d+) for ""(.*)"" with thumbnail to the ChatFeed")]
    public async Task WhenAliceSendsLabeledImageWithThumbnail(int imageIndex, string targetName)
    {
        var (feedId, aesKey) = GetChatFeedInfo();
        _aesKey = aesKey;
        var sender = TestIdentities.Alice;

        // Generate labeled PNG using TestImageGenerator (same as E2E tests)
        var (fileName, pngBytes) = TestImageGenerator.GenerateTestAttachment(imageIndex, "Alice", targetName);
        _originalPngBytes = pngBytes;

        // Generate a smaller thumbnail (half-size)
        _thumbnailPngBytes = GenerateSmallThumbnail(pngBytes);

        // Compute hash of the original plaintext
        _attachmentHash = ComputeSha256Hex(_originalPngBytes);
        _attachmentId = Guid.NewGuid().ToString();

        // Encrypt both with feed AES key
        _encryptedOriginal = EncryptBytes(_originalPngBytes, aesKey);
        _encryptedThumbnail = EncryptBytes(_thumbnailPngBytes, aesKey);

        var attachmentRef = new AttachmentReference(
            _attachmentId,
            _attachmentHash,
            "image/png",
            _originalPngBytes.Length,
            fileName);

        var (txJson, _) = TestTransactionFactory.CreateFeedMessageWithAttachments(
            sender, feedId, "Labeled image test", aesKey, [attachmentRef]);

        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var request = new SubmitSignedTransactionRequest
        {
            SignedTransaction = txJson,
        };
        request.Attachments.Add(new AttachmentBlob
        {
            AttachmentId = _attachmentId,
            EncryptedOriginal = ByteString.CopyFrom(_encryptedOriginal),
            EncryptedThumbnail = ByteString.CopyFrom(_encryptedThumbnail),
        });

        var response = await blockchainClient.SubmitSignedTransactionAsync(request);
        response.Successfull.Should().BeTrue($"Labeled image submission should succeed: {response.Message}");

        Console.WriteLine($"[TwinTest] Uploaded labeled image: {fileName} ({_originalPngBytes.Length} bytes original, {_thumbnailPngBytes.Length} bytes thumbnail)");
    }

    [Then(@"Bob should download the original and it should match the uploaded encrypted bytes")]
    public async Task ThenBobDownloadsOriginalAndMatches()
    {
        _downloadedOriginal = await DownloadAttachment(thumbnailOnly: false);

        _downloadedOriginal.Should().BeEquivalentTo(_encryptedOriginal,
            "Downloaded original encrypted bytes should exactly match uploaded encrypted bytes");

        Console.WriteLine($"[TwinTest] Original download verified: {_downloadedOriginal.Length} bytes match");
    }

    [Then(@"Bob should download the thumbnail and it should match the uploaded encrypted thumbnail bytes")]
    public async Task ThenBobDownloadsThumbnailAndMatches()
    {
        _downloadedThumbnail = await DownloadAttachment(thumbnailOnly: true);

        _downloadedThumbnail.Should().BeEquivalentTo(_encryptedThumbnail,
            "Downloaded thumbnail encrypted bytes should exactly match uploaded encrypted thumbnail bytes");

        Console.WriteLine($"[TwinTest] Thumbnail download verified: {_downloadedThumbnail.Length} bytes match");
    }

    [Then(@"the downloaded original should be a valid PNG when decrypted")]
    public void ThenDownloadedOriginalShouldBeValidPng()
    {
        var decrypted = DecryptBytes(_downloadedOriginal!, _aesKey!);

        // Should match the original plaintext PNG
        decrypted.Should().BeEquivalentTo(_originalPngBytes,
            "Decrypted original should match the plaintext PNG bytes");

        // Should be decodable as a valid image
        using var bitmap = SKBitmap.Decode(decrypted);
        bitmap.Should().NotBeNull("Decrypted original should be a valid PNG image");
        bitmap!.Width.Should().Be(400, "Labeled test images are 400px wide");
        bitmap.Height.Should().Be(200, "Labeled test images are 200px tall");

        Console.WriteLine($"[TwinTest] Original decrypts to valid {bitmap.Width}x{bitmap.Height} PNG");
    }

    [Then(@"the downloaded thumbnail should be a valid PNG when decrypted")]
    public void ThenDownloadedThumbnailShouldBeValidPng()
    {
        var decrypted = DecryptBytes(_downloadedThumbnail!, _aesKey!);

        // Should match the original plaintext thumbnail
        decrypted.Should().BeEquivalentTo(_thumbnailPngBytes,
            "Decrypted thumbnail should match the plaintext thumbnail bytes");

        // Should be decodable as a valid image
        using var bitmap = SKBitmap.Decode(decrypted);
        bitmap.Should().NotBeNull("Decrypted thumbnail should be a valid PNG image");

        Console.WriteLine($"[TwinTest] Thumbnail decrypts to valid {bitmap!.Width}x{bitmap.Height} PNG");
    }

    // ===== Helpers =====

    private async Task<byte[]> DownloadAttachment(bool thumbnailOnly)
    {
        var (feedId, _) = GetChatFeedInfo();
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var downloadRequest = new DownloadAttachmentRequest
        {
            AttachmentId = _attachmentId!,
            FeedId = feedId.ToString(),
            RequesterUserAddress = TestIdentities.Bob.PublicSigningAddress,
            ThumbnailOnly = thumbnailOnly
        };

        var downloadedBytes = new List<byte>();
        using var call = feedClient.DownloadAttachment(downloadRequest);

        await foreach (var chunk in call.ResponseStream.ReadAllAsync())
        {
            downloadedBytes.AddRange(chunk.Data.ToByteArray());
        }

        downloadedBytes.Should().NotBeEmpty(
            $"Downloaded {(thumbnailOnly ? "thumbnail" : "original")} bytes should not be empty");

        return downloadedBytes.ToArray();
    }

    /// <summary>
    /// Generates a smaller thumbnail from the original PNG (200x100).
    /// Simulates what the client-side thumbnail generator does.
    /// </summary>
    private static byte[] GenerateSmallThumbnail(byte[] originalPngBytes)
    {
        using var originalBitmap = SKBitmap.Decode(originalPngBytes);
        using var resized = originalBitmap!.Resize(
            new SKImageInfo(200, 100), SKSamplingOptions.Default);
        using var image = SKImage.FromBitmap(resized!);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static string ComputeSha256Hex(byte[] data)
    {
        var hashBytes = SHA256.HashData(data);
        return Convert.ToHexStringLower(hashBytes);
    }

    private static byte[] EncryptBytes(byte[] plaintext, string aesKeyBase64)
    {
        var key = Convert.FromBase64String(aesKeyBase64);
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Format: nonce + ciphertext + tag
        var result = new byte[nonce.Length + ciphertext.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, result, nonce.Length, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, nonce.Length + ciphertext.Length, tag.Length);
        return result;
    }

    private static byte[] DecryptBytes(byte[] encrypted, string aesKeyBase64)
    {
        var key = Convert.FromBase64String(aesKeyBase64);
        var nonce = encrypted[..12];
        var tag = encrypted[^16..];
        var ciphertext = encrypted[12..^16];

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    private (FeedId FeedId, string AesKey) GetChatFeedInfo()
    {
        foreach (var key in _scenarioContext.Keys)
        {
            if (key.StartsWith("ChatFeed_") && !key.Contains("AesKey"))
            {
                var chatKey = key["ChatFeed_".Length..];
                var feedId = (FeedId)_scenarioContext[$"ChatFeed_{chatKey}"];
                var aesKey = (string)_scenarioContext[$"ChatFeedAesKey_{chatKey}"];
                return (feedId, aesKey);
            }
        }

        throw new InvalidOperationException("No chat feed found in context");
    }

    private GrpcClientFactory GetGrpcFactory()
    {
        return _scenarioContext.Get<GrpcClientFactory>(ScenarioHooks.GrpcFactoryKey);
    }
}
