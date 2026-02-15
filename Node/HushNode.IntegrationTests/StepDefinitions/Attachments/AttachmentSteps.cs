using System.Security.Cryptography;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Feeds.Model;
using HushServerNode.Testing;
using Olimpo;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.Attachments;

/// <summary>
/// FEAT-066: Step definitions for attachment storage infrastructure integration tests.
/// </summary>
[Binding]
public sealed class AttachmentSteps
{
    private readonly ScenarioContext _scenarioContext;

    // Test data stored per-scenario
    private byte[]? _uploadedOriginalBytes;
    private byte[]? _uploadedThumbnailBytes;
    private byte[]? _encryptedOriginalBytes;
    private byte[]? _encryptedThumbnailBytes;
    private string? _attachmentId;
    private string? _attachmentHash;
    private FeedMessageId? _lastMessageId;
    private SubmitSignedTransactionReply? _lastSubmitResponse;
    private List<AttachmentReference>? _lastAttachmentRefs;

    public AttachmentSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    // ===== WHEN Steps =====

    [When(@"Alice sends message ""(.*)"" with a (\d+)KB image attachment to the ChatFeed")]
    [When(@"Alice sends message ""(.*)"" with a (\d+)KB attachment to the ChatFeed")]
    public async Task WhenAliceSendsMessageWithAttachment(string message, int sizeKb)
    {
        var (feedId, aesKey) = GetChatFeedInfo();
        var sender = TestIdentities.Alice;

        // Generate deterministic test bytes
        _uploadedOriginalBytes = GenerateTestBytes(sizeKb * 1024);
        _attachmentHash = ComputeSha256Hex(_uploadedOriginalBytes);
        _attachmentId = Guid.NewGuid().ToString();

        // Encrypt the attachment with the feed AES key
        _encryptedOriginalBytes = EncryptBytes(_uploadedOriginalBytes, aesKey);

        var attachmentRef = new AttachmentReference(
            _attachmentId,
            _attachmentHash,
            "image/jpeg",
            _uploadedOriginalBytes.Length,
            "test-image.jpg");

        _lastAttachmentRefs = [attachmentRef];

        var (txJson, messageId) = TestTransactionFactory.CreateFeedMessageWithAttachments(
            sender, feedId, message, aesKey, _lastAttachmentRefs);
        _lastMessageId = messageId;

        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var request = new SubmitSignedTransactionRequest
        {
            SignedTransaction = txJson,
        };
        request.Attachments.Add(new AttachmentBlob
        {
            AttachmentId = _attachmentId,
            EncryptedOriginal = ByteString.CopyFrom(_encryptedOriginalBytes),
        });

        _lastSubmitResponse = await blockchainClient.SubmitSignedTransactionAsync(request);
        _lastSubmitResponse.Successfull.Should().BeTrue($"Message with attachment should succeed: {_lastSubmitResponse.Message}");
    }

    [When(@"Alice sends message ""(.*)"" with an attachment that has both original and thumbnail")]
    public async Task WhenAliceSendsMessageWithOriginalAndThumbnail(string message)
    {
        var (feedId, aesKey) = GetChatFeedInfo();
        var sender = TestIdentities.Alice;

        // Original: 10KB, Thumbnail: 2KB
        _uploadedOriginalBytes = GenerateTestBytes(10 * 1024);
        _uploadedThumbnailBytes = GenerateTestBytes(2 * 1024);
        _attachmentHash = ComputeSha256Hex(_uploadedOriginalBytes);
        _attachmentId = Guid.NewGuid().ToString();

        _encryptedOriginalBytes = EncryptBytes(_uploadedOriginalBytes, aesKey);
        _encryptedThumbnailBytes = EncryptBytes(_uploadedThumbnailBytes, aesKey);

        var attachmentRef = new AttachmentReference(
            _attachmentId,
            _attachmentHash,
            "image/jpeg",
            _uploadedOriginalBytes.Length,
            "photo.jpg");

        _lastAttachmentRefs = [attachmentRef];

        var (txJson, messageId) = TestTransactionFactory.CreateFeedMessageWithAttachments(
            sender, feedId, message, aesKey, _lastAttachmentRefs);
        _lastMessageId = messageId;

        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var request = new SubmitSignedTransactionRequest
        {
            SignedTransaction = txJson,
        };
        request.Attachments.Add(new AttachmentBlob
        {
            AttachmentId = _attachmentId,
            EncryptedOriginal = ByteString.CopyFrom(_encryptedOriginalBytes),
            EncryptedThumbnail = ByteString.CopyFrom(_encryptedThumbnailBytes),
        });

        _lastSubmitResponse = await blockchainClient.SubmitSignedTransactionAsync(request);
        _lastSubmitResponse.Successfull.Should().BeTrue($"Message with thumbnail should succeed: {_lastSubmitResponse.Message}");
    }

    [When(@"Alice sends a message with mismatched blob and metadata count")]
    public async Task WhenAliceSendsMessageWithMismatchedBlobAndMetadata()
    {
        var (feedId, aesKey) = GetChatFeedInfo();
        var sender = TestIdentities.Alice;

        _uploadedOriginalBytes = GenerateTestBytes(1024);
        _attachmentHash = ComputeSha256Hex(_uploadedOriginalBytes);
        _attachmentId = Guid.NewGuid().ToString();
        _encryptedOriginalBytes = EncryptBytes(_uploadedOriginalBytes, aesKey);

        // Create 1 metadata reference but 0 blobs (mismatch triggers validation error)
        var attachmentRef = new AttachmentReference(
            _attachmentId,
            _attachmentHash,
            "image/jpeg",
            _uploadedOriginalBytes.Length,
            "test.jpg");

        _lastAttachmentRefs = [attachmentRef];

        var (txJson, messageId) = TestTransactionFactory.CreateFeedMessageWithAttachments(
            sender, feedId, "Mismatched message", aesKey, _lastAttachmentRefs);
        _lastMessageId = messageId;

        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        // Submit with 0 blobs but 1 metadata reference = mismatch
        var request = new SubmitSignedTransactionRequest
        {
            SignedTransaction = txJson,
        };
        // Intentionally NO blob added despite having 1 metadata reference

        _lastSubmitResponse = await blockchainClient.SubmitSignedTransactionAsync(request);
    }

    [When(@"Alice sends a message with a (\d+)MB attachment to the ChatFeed")]
    public async Task WhenAliceSendsMessageWithLargeAttachment(int sizeMb)
    {
        var (feedId, aesKey) = GetChatFeedInfo();
        var sender = TestIdentities.Alice;

        _uploadedOriginalBytes = GenerateTestBytes(sizeMb * 1024 * 1024);
        _attachmentHash = ComputeSha256Hex(_uploadedOriginalBytes);
        _attachmentId = Guid.NewGuid().ToString();
        _encryptedOriginalBytes = EncryptBytes(_uploadedOriginalBytes, aesKey);

        var attachmentRef = new AttachmentReference(
            _attachmentId,
            _attachmentHash,
            "application/octet-stream",
            _uploadedOriginalBytes.Length,
            "large-file.bin");

        _lastAttachmentRefs = [attachmentRef];

        var (txJson, messageId) = TestTransactionFactory.CreateFeedMessageWithAttachments(
            sender, feedId, "Large attachment", aesKey, _lastAttachmentRefs);
        _lastMessageId = messageId;

        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var request = new SubmitSignedTransactionRequest
        {
            SignedTransaction = txJson,
        };
        request.Attachments.Add(new AttachmentBlob
        {
            AttachmentId = _attachmentId,
            EncryptedOriginal = ByteString.CopyFrom(_encryptedOriginalBytes),
        });

        _lastSubmitResponse = await blockchainClient.SubmitSignedTransactionAsync(request);
    }

    [When(@"Alice sends message ""(.*)"" with (\d+) attachments to the ChatFeed")]
    public async Task WhenAliceSendsMessageWithMultipleAttachments(string message, int count)
    {
        var (feedId, aesKey) = GetChatFeedInfo();
        var sender = TestIdentities.Alice;

        _lastAttachmentRefs = new List<AttachmentReference>();
        var blobs = new List<AttachmentBlob>();

        for (int i = 0; i < count; i++)
        {
            var originalBytes = GenerateTestBytes(1024);
            var hash = ComputeSha256Hex(originalBytes);
            var id = Guid.NewGuid().ToString();
            var encrypted = EncryptBytes(originalBytes, aesKey);

            _lastAttachmentRefs.Add(new AttachmentReference(
                id, hash, "image/png", originalBytes.Length, $"file-{i}.png"));

            blobs.Add(new AttachmentBlob
            {
                AttachmentId = id,
                EncryptedOriginal = ByteString.CopyFrom(encrypted),
            });
        }

        var (txJson, messageId) = TestTransactionFactory.CreateFeedMessageWithAttachments(
            sender, feedId, message, aesKey, _lastAttachmentRefs);
        _lastMessageId = messageId;

        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var request = new SubmitSignedTransactionRequest
        {
            SignedTransaction = txJson,
        };
        request.Attachments.AddRange(blobs);

        _lastSubmitResponse = await blockchainClient.SubmitSignedTransactionAsync(request);
    }

    [When(@"Alice sends a message with (\d+) attachments to the ChatFeed")]
    public async Task WhenAliceSendsMessageWithTooManyAttachments(int count)
    {
        await WhenAliceSendsMessageWithMultipleAttachments("Too many files", count);
    }

    // ===== THEN Steps =====

    [Then(@"the message should contain attachment metadata with id, hash, type, and size")]
    public async Task ThenMessageShouldContainAttachmentMetadata()
    {
        var identity = TestIdentities.Bob;
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0,
            LastReactionTallyVersion = 0
        });

        var (feedId, _) = GetChatFeedInfo();
        var messageProto = response.Messages
            .FirstOrDefault(m => m.FeedMessageId == _lastMessageId!.ToString());

        messageProto.Should().NotBeNull("Message should be returned from server");
        messageProto!.Attachments.Should().HaveCount(1);

        var attachment = messageProto.Attachments[0];
        attachment.Id.Should().Be(_attachmentId);
        attachment.Hash.Should().Be(_attachmentHash);
        attachment.MimeType.Should().Be("image/jpeg");
        attachment.Size.Should().Be(_uploadedOriginalBytes!.Length);
        attachment.FileName.Should().Be("test-image.jpg");
    }

    [Then(@"the encrypted attachment should be stored in PostgreSQL")]
    public async Task ThenEncryptedAttachmentShouldBeInPostgres()
    {
        var fixture = GetFixture();

        var count = await fixture.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM \"Feeds\".\"Attachment\" WHERE \"Id\" = '{_attachmentId}'");

        count.Should().Be(1, "Attachment should be stored in PostgreSQL");
    }

    [Then(@"Bob should be able to download the attachment via gRPC streaming")]
    public async Task ThenBobShouldDownloadAttachmentViaStreaming()
    {
        var (feedId, _) = GetChatFeedInfo();
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var downloadRequest = new DownloadAttachmentRequest
        {
            AttachmentId = _attachmentId!,
            FeedId = feedId.ToString(),
            RequesterUserAddress = TestIdentities.Bob.PublicSigningAddress,
            ThumbnailOnly = false
        };

        var downloadedBytes = new List<byte>();
        using var call = feedClient.DownloadAttachment(downloadRequest);

        await foreach (var chunk in call.ResponseStream.ReadAllAsync())
        {
            downloadedBytes.AddRange(chunk.Data.ToByteArray());
        }

        downloadedBytes.Should().NotBeEmpty("Downloaded bytes should not be empty");

        // Store for next assertion
        _scenarioContext["DownloadedBytes"] = downloadedBytes.ToArray();
    }

    [Then(@"the downloaded bytes should match the uploaded bytes")]
    public void ThenDownloadedBytesShouldMatchUploaded()
    {
        var downloadedBytes = (byte[])_scenarioContext["DownloadedBytes"];
        downloadedBytes.Should().BeEquivalentTo(_encryptedOriginalBytes,
            "Downloaded encrypted bytes should match uploaded encrypted bytes");
    }

    [Then(@"downloading with thumbnail_only should return only the thumbnail bytes")]
    public async Task ThenDownloadingWithThumbnailOnlyShouldReturnThumbnail()
    {
        var (feedId, _) = GetChatFeedInfo();
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var downloadRequest = new DownloadAttachmentRequest
        {
            AttachmentId = _attachmentId!,
            FeedId = feedId.ToString(),
            RequesterUserAddress = TestIdentities.Bob.PublicSigningAddress,
            ThumbnailOnly = true
        };

        var downloadedBytes = new List<byte>();
        using var call = feedClient.DownloadAttachment(downloadRequest);

        await foreach (var chunk in call.ResponseStream.ReadAllAsync())
        {
            downloadedBytes.AddRange(chunk.Data.ToByteArray());
        }

        downloadedBytes.ToArray().Should().BeEquivalentTo(_encryptedThumbnailBytes,
            "Thumbnail download should match encrypted thumbnail bytes");

        _scenarioContext["ThumbnailBytes"] = downloadedBytes.ToArray();
    }

    [Then(@"downloading without thumbnail_only should return only the original bytes")]
    public async Task ThenDownloadingWithoutThumbnailShouldReturnOriginal()
    {
        var (feedId, _) = GetChatFeedInfo();
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var downloadRequest = new DownloadAttachmentRequest
        {
            AttachmentId = _attachmentId!,
            FeedId = feedId.ToString(),
            RequesterUserAddress = TestIdentities.Bob.PublicSigningAddress,
            ThumbnailOnly = false
        };

        var downloadedBytes = new List<byte>();
        using var call = feedClient.DownloadAttachment(downloadRequest);

        await foreach (var chunk in call.ResponseStream.ReadAllAsync())
        {
            downloadedBytes.AddRange(chunk.Data.ToByteArray());
        }

        downloadedBytes.ToArray().Should().BeEquivalentTo(_encryptedOriginalBytes,
            "Original download should match encrypted original bytes");

        _scenarioContext["OriginalDownloadBytes"] = downloadedBytes.ToArray();
    }

    [Then(@"the thumbnail should be smaller than the original")]
    public void ThenThumbnailShouldBeSmallerThanOriginal()
    {
        var thumbnailBytes = (byte[])_scenarioContext["ThumbnailBytes"];
        var originalBytes = (byte[])_scenarioContext["OriginalDownloadBytes"];

        thumbnailBytes.Length.Should().BeLessThan(originalBytes.Length,
            "Thumbnail should be smaller than original");
    }

    [Then(@"a temp file should exist for the attachment")]
    public void ThenTempFileShouldExist()
    {
        // After mempool acceptance but before block production, temp file should exist
        var tempDir = Path.Combine(Path.GetTempPath(), "hush-attachment-temp");
        var tempFile = Path.Combine(tempDir, $"{_attachmentId}.original");

        File.Exists(tempFile).Should().BeTrue(
            $"Temp file should exist at {tempFile} after mempool acceptance");
    }

    [Then(@"the temp file should no longer exist")]
    public void ThenTempFileShouldNotExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "hush-attachment-temp");
        var tempFile = Path.Combine(tempDir, $"{_attachmentId}.original");

        File.Exists(tempFile).Should().BeFalse(
            "Temp file should be deleted after indexing");
    }

    [Then(@"the submission should be rejected")]
    public void ThenSubmissionShouldBeRejected()
    {
        _lastSubmitResponse.Should().NotBeNull();
        _lastSubmitResponse!.Successfull.Should().BeFalse("Transaction should be rejected");
    }

    [Then(@"the submission should be rejected with a size limit error")]
    public void ThenSubmissionShouldBeRejectedWithSizeError()
    {
        _lastSubmitResponse.Should().NotBeNull();
        _lastSubmitResponse!.Successfull.Should().BeFalse("Transaction should be rejected");
        _lastSubmitResponse.Message.Should().Contain("25MB",
            "Error message should mention the 25MB size limit");
    }

    [Then(@"the submission should be rejected with a count limit error")]
    public void ThenSubmissionShouldBeRejectedWithCountError()
    {
        _lastSubmitResponse.Should().NotBeNull();
        _lastSubmitResponse!.Successfull.Should().BeFalse("Transaction should be rejected");
        _lastSubmitResponse.Message.Should().Contain("maximum",
            "Error message should mention the maximum attachment count");
    }

    [Then(@"no temp files should exist for the attachment")]
    public void ThenNoTempFilesShouldExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "hush-attachment-temp");
        if (!Directory.Exists(tempDir))
        {
            return; // No temp dir means no temp files
        }

        var tempFile = Path.Combine(tempDir, $"{_attachmentId}.original");
        File.Exists(tempFile).Should().BeFalse(
            "Temp file should be cleaned up after rejection");
    }

    [Then(@"Alice should see the message with attachment metadata in the ChatFeed")]
    public async Task ThenAliceShouldSeeMessageWithAttachmentMetadata()
    {
        var identity = TestIdentities.Alice;
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0,
            LastReactionTallyVersion = 0
        });

        var messageProto = response.Messages
            .FirstOrDefault(m => m.FeedMessageId == _lastMessageId!.ToString());

        messageProto.Should().NotBeNull("Message should be returned from server");
        messageProto!.Attachments.Should().NotBeEmpty("Message should have attachment metadata");
    }

    [Then(@"Alice should see the message with (\d+) attachment references")]
    public async Task ThenAliceShouldSeeMessageWithNAttachments(int expectedCount)
    {
        var identity = TestIdentities.Alice;
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0,
            LastReactionTallyVersion = 0
        });

        var messageProto = response.Messages
            .FirstOrDefault(m => m.FeedMessageId == _lastMessageId!.ToString());

        messageProto.Should().NotBeNull("Message should be returned from server");
        messageProto!.Attachments.Should().HaveCount(expectedCount,
            $"Message should have exactly {expectedCount} attachment references");
    }

    [Then(@"the message should have an empty attachments list")]
    public async Task ThenMessageShouldHaveEmptyAttachmentsList()
    {
        var identity = TestIdentities.Bob;
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0,
            LastReactionTallyVersion = 0
        });

        var (feedId, _) = GetChatFeedInfo();
        var chatMessages = response.Messages
            .Where(m => m.FeedId == feedId.ToString())
            .ToList();

        chatMessages.Should().NotBeEmpty("Should have messages in ChatFeed");

        // Latest message should have empty attachments
        var latestMessage = chatMessages.Last();
        latestMessage.Attachments.Should().BeEmpty(
            "Text-only message should have empty attachments list");
    }

    // ===== Helper Methods =====

    private static byte[] GenerateTestBytes(int size)
    {
        var bytes = new byte[size];
        // Use a deterministic seed so tests are reproducible
        var rng = new Random(42);
        rng.NextBytes(bytes);
        return bytes;
    }

    private static string ComputeSha256Hex(byte[] data)
    {
        var hashBytes = SHA256.HashData(data);
        return Convert.ToHexStringLower(hashBytes);
    }

    private static byte[] EncryptBytes(byte[] plaintext, string aesKeyBase64)
    {
        // AES-GCM encryption matching the EncryptKeys pattern (nonce + ciphertext + tag)
        var key = Convert.FromBase64String(aesKeyBase64);
        var nonce = new byte[12]; // GCM nonce size
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16]; // GCM tag size
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Format: nonce + ciphertext + tag (matches EncryptKeys convention)
        var result = new byte[nonce.Length + ciphertext.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, result, nonce.Length, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, nonce.Length + ciphertext.Length, tag.Length);
        return result;
    }

    private (FeedId FeedId, string AesKey) GetChatFeedInfo()
    {
        // Find the Alice-Bob chat feed from context
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

    private BlockProductionControl GetBlockControl()
    {
        return _scenarioContext.Get<BlockProductionControl>(ScenarioHooks.BlockControlKey);
    }

    private HushTestFixture GetFixture()
    {
        return _scenarioContext.Get<HushTestFixture>(ScenarioHooks.FixtureKey);
    }
}
