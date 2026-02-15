using FluentAssertions;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using HushShared.Feeds.Model;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for FEAT-066 data layer: AttachmentReference model, NewFeedMessagePayload extension,
/// and FeedMessage attachment metadata.
/// </summary>
public class AttachmentReferenceTests
{
    #region AttachmentReference Model Tests

    [Fact]
    public void AttachmentReference_ValidData_SetsAllProperties()
    {
        // Arrange
        var id = Guid.NewGuid().ToString();
        var hash = new string('a', 64); // SHA-256 hex = 64 chars
        var mimeType = "image/jpeg";
        var size = 1024L;
        var fileName = "photo.jpg";

        // Act
        var reference = new AttachmentReference(id, hash, mimeType, size, fileName);

        // Assert
        reference.Id.Should().Be(id);
        reference.Hash.Should().Be(hash);
        reference.MimeType.Should().Be(mimeType);
        reference.Size.Should().Be(size);
        reference.FileName.Should().Be(fileName);
    }

    [Fact]
    public void AttachmentReference_RecordEquality_WorksCorrectly()
    {
        // Arrange
        var id = Guid.NewGuid().ToString();
        var hash = new string('b', 64);
        var ref1 = new AttachmentReference(id, hash, "image/png", 2048, "image.png");
        var ref2 = new AttachmentReference(id, hash, "image/png", 2048, "image.png");

        // Act & Assert
        ref1.Should().Be(ref2);
    }

    [Fact]
    public void AttachmentReference_DifferentIds_AreNotEqual()
    {
        // Arrange
        var ref1 = new AttachmentReference(Guid.NewGuid().ToString(), new string('a', 64), "image/jpeg", 1024, "a.jpg");
        var ref2 = new AttachmentReference(Guid.NewGuid().ToString(), new string('a', 64), "image/jpeg", 1024, "a.jpg");

        // Act & Assert
        ref1.Should().NotBe(ref2);
    }

    #endregion

    #region NewFeedMessagePayload Extension Tests

    [Fact]
    public void NewFeedMessagePayload_WithAttachments_ContainsReferences()
    {
        // Arrange
        var feedId = new FeedId(Guid.NewGuid());
        var messageId = new FeedMessageId(Guid.NewGuid());
        var attachments = new List<AttachmentReference>
        {
            new(Guid.NewGuid().ToString(), new string('a', 64), "image/jpeg", 1024, "photo1.jpg"),
            new(Guid.NewGuid().ToString(), new string('b', 64), "image/png", 2048, "photo2.png"),
        };

        // Act
        var payload = new NewFeedMessagePayload(
            messageId, feedId, "Check out these photos!",
            Attachments: attachments);

        // Assert
        payload.Attachments.Should().NotBeNull();
        payload.Attachments.Should().HaveCount(2);
        payload.Attachments![0].MimeType.Should().Be("image/jpeg");
        payload.Attachments[1].MimeType.Should().Be("image/png");
    }

    [Fact]
    public void NewFeedMessagePayload_WithoutAttachments_HasNullAttachments()
    {
        // Arrange
        var feedId = new FeedId(Guid.NewGuid());
        var messageId = new FeedMessageId(Guid.NewGuid());

        // Act
        var payload = new NewFeedMessagePayload(messageId, feedId, "Plain text message");

        // Assert
        payload.Attachments.Should().BeNull();
    }

    [Fact]
    public void NewFeedMessagePayload_WithEmptyAttachments_HasEmptyList()
    {
        // Arrange
        var feedId = new FeedId(Guid.NewGuid());
        var messageId = new FeedMessageId(Guid.NewGuid());

        // Act
        var payload = new NewFeedMessagePayload(
            messageId, feedId, "Message with no attachments",
            Attachments: new List<AttachmentReference>());

        // Assert
        payload.Attachments.Should().NotBeNull();
        payload.Attachments.Should().BeEmpty();
    }

    #endregion

    #region FeedMessage Attachment Metadata Tests

    [Fact]
    public void FeedMessage_WithAttachments_CarriesMetadata()
    {
        // Arrange
        var feedId = new FeedId(Guid.NewGuid());
        var messageId = new FeedMessageId(Guid.NewGuid());
        var attachments = new List<AttachmentReference>
        {
            new(Guid.NewGuid().ToString(), new string('c', 64), "application/pdf", 5000000, "document.pdf"),
        };

        // Act
        var message = new FeedMessage(
            messageId, feedId, "Here's the document",
            "0xsender", new Timestamp(DateTime.UtcNow), new BlockIndex(42))
        {
            Attachments = attachments
        };

        // Assert
        message.Attachments.Should().NotBeNull();
        message.Attachments.Should().HaveCount(1);
        message.Attachments![0].FileName.Should().Be("document.pdf");
        message.Attachments[0].Size.Should().Be(5000000);
    }

    [Fact]
    public void FeedMessage_WithoutAttachments_HasNullAttachments()
    {
        // Arrange & Act
        var message = new FeedMessage(
            new FeedMessageId(Guid.NewGuid()),
            new FeedId(Guid.NewGuid()),
            "Plain text",
            "0xsender",
            new Timestamp(DateTime.UtcNow),
            new BlockIndex(1));

        // Assert
        message.Attachments.Should().BeNull();
    }

    #endregion

    #region AttachmentEntity Tests

    [Fact]
    public void AttachmentEntity_ValidData_SetsAllProperties()
    {
        // Arrange
        var id = Guid.NewGuid().ToString();
        var encrypted = new byte[] { 1, 2, 3, 4, 5 };
        var thumbnail = new byte[] { 10, 20, 30 };
        var feedMessageId = new FeedMessageId(Guid.NewGuid());

        // Act
        var entity = new AttachmentEntity(
            id, encrypted, thumbnail, feedMessageId,
            1024, 256, "image/jpeg", "photo.jpg", "abc123hash", DateTime.UtcNow);

        // Assert
        entity.Id.Should().Be(id);
        entity.EncryptedOriginal.Should().BeEquivalentTo(encrypted);
        entity.EncryptedThumbnail.Should().BeEquivalentTo(thumbnail);
        entity.FeedMessageId.Should().Be(feedMessageId);
        entity.OriginalSize.Should().Be(1024);
        entity.ThumbnailSize.Should().Be(256);
        entity.MimeType.Should().Be("image/jpeg");
        entity.FileName.Should().Be("photo.jpg");
    }

    [Fact]
    public void AttachmentEntity_NullThumbnail_IsAllowed()
    {
        // Arrange & Act
        var entity = new AttachmentEntity(
            Guid.NewGuid().ToString(),
            new byte[] { 1, 2, 3 },
            null, // No thumbnail for non-image files
            new FeedMessageId(Guid.NewGuid()),
            512, 0, "application/pdf", "report.pdf", "abc123hash", DateTime.UtcNow);

        // Assert
        entity.EncryptedThumbnail.Should().BeNull();
        entity.ThumbnailSize.Should().Be(0);
    }

    #endregion
}
