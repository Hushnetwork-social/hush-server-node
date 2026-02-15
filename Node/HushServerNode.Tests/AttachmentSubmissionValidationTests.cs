using FluentAssertions;
using Google.Protobuf;
using HushNetwork.proto;
using HushNode.Blockchain.gRPC;
using HushShared.Feeds.Model;
using Xunit;

namespace HushServerNode.Tests;

/// <summary>
/// Tests for FEAT-066 attachment blob validation in BlockchainGrpcService.
/// Covers acceptance tests F2-008 (size validation) and F2-009 (count validation).
/// </summary>
public class AttachmentSubmissionValidationTests
{
    #region Count Validation (F2-009)

    [Fact]
    [Trait("Category", "F2-009")]
    public void ValidateAttachmentBlobs_ZeroAttachments_ReturnsNull()
    {
        // Arrange
        var request = CreateRequest();
        List<AttachmentReference>? refs = null;

        // Act
        var result = BlockchainGrpcService.ValidateAttachmentBlobs(request, refs);

        // Assert
        result.Should().BeNull("zero attachments should be valid");
    }

    [Fact]
    [Trait("Category", "F2-009")]
    public void ValidateAttachmentBlobs_FiveAttachments_ReturnsNull()
    {
        // Arrange
        var refs = CreateAttachmentRefs(5);
        var request = CreateRequestWithBlobs(refs);

        // Act
        var result = BlockchainGrpcService.ValidateAttachmentBlobs(request, refs);

        // Assert
        result.Should().BeNull("5 attachments is within the limit");
    }

    [Fact]
    [Trait("Category", "F2-009")]
    public void ValidateAttachmentBlobs_SixAttachments_ReturnsCountError()
    {
        // Arrange
        var refs = CreateAttachmentRefs(6);
        var request = CreateRequestWithBlobs(refs);

        // Act
        var result = BlockchainGrpcService.ValidateAttachmentBlobs(request, refs);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("6").And.Contain("maximum of 5");
    }

    #endregion

    #region Size Validation (F2-008)

    [Fact]
    [Trait("Category", "F2-008")]
    public void ValidateAttachmentBlobs_BlobUnder25MB_ReturnsNull()
    {
        // Arrange
        var id = Guid.NewGuid().ToString();
        var refs = new List<AttachmentReference> { new(id, "hash", "image/jpeg", 1024, "photo.jpg") };
        var request = CreateRequest();
        request.Attachments.Add(new AttachmentBlob
        {
            AttachmentId = id,
            EncryptedOriginal = ByteString.CopyFrom(new byte[1024]),
            EncryptedThumbnail = ByteString.Empty,
        });

        // Act
        var result = BlockchainGrpcService.ValidateAttachmentBlobs(request, refs);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "F2-008")]
    public void ValidateAttachmentBlobs_BlobExceeds25MB_ReturnsSizeError()
    {
        // Arrange
        var id = Guid.NewGuid().ToString();
        var refs = new List<AttachmentReference> { new(id, "hash", "image/jpeg", 30_000_000, "huge.jpg") };
        var oversizedBlob = new byte[26 * 1024 * 1024]; // 26MB
        var request = CreateRequest();
        request.Attachments.Add(new AttachmentBlob
        {
            AttachmentId = id,
            EncryptedOriginal = ByteString.CopyFrom(oversizedBlob),
            EncryptedThumbnail = ByteString.Empty,
        });

        // Act
        var result = BlockchainGrpcService.ValidateAttachmentBlobs(request, refs);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("25MB");
    }

    #endregion

    #region ID Matching

    [Fact]
    public void ValidateAttachmentBlobs_MatchingIds_ReturnsNull()
    {
        // Arrange
        var id1 = Guid.NewGuid().ToString();
        var id2 = Guid.NewGuid().ToString();
        var refs = new List<AttachmentReference>
        {
            new(id1, "hash1", "image/jpeg", 100, "a.jpg"),
            new(id2, "hash2", "image/png", 200, "b.png"),
        };
        var request = CreateRequest();
        request.Attachments.Add(CreateSmallBlob(id1));
        request.Attachments.Add(CreateSmallBlob(id2));

        // Act
        var result = BlockchainGrpcService.ValidateAttachmentBlobs(request, refs);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAttachmentBlobs_MismatchedIds_ReturnsMismatchError()
    {
        // Arrange
        var refId = Guid.NewGuid().ToString();
        var blobId = Guid.NewGuid().ToString();
        var refs = new List<AttachmentReference>
        {
            new(refId, "hash", "image/jpeg", 100, "a.jpg"),
        };
        var request = CreateRequest();
        request.Attachments.Add(CreateSmallBlob(blobId));

        // Act
        var result = BlockchainGrpcService.ValidateAttachmentBlobs(request, refs);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Missing attachment blobs");
    }

    [Fact]
    public void ValidateAttachmentBlobs_BlobCountMismatch_ReturnsCountMismatchError()
    {
        // Arrange
        var id1 = Guid.NewGuid().ToString();
        var refs = new List<AttachmentReference>
        {
            new(id1, "hash", "image/jpeg", 100, "a.jpg"),
        };
        var request = CreateRequest();
        // No blobs added (count = 0, refs = 1)

        // Act
        var result = BlockchainGrpcService.ValidateAttachmentBlobs(request, refs);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("does not match");
    }

    #endregion

    #region Backward Compatibility

    [Fact]
    public void ValidateAttachmentBlobs_NullRefsZeroBlobs_ReturnsNull()
    {
        // Arrange
        var request = CreateRequest();

        // Act
        var result = BlockchainGrpcService.ValidateAttachmentBlobs(request, null);

        // Assert
        result.Should().BeNull("no attachments should be backward compatible");
    }

    #endregion

    #region Helpers

    private static SubmitSignedTransactionRequest CreateRequest() =>
        new() { SignedTransaction = "{}" };

    private static SubmitSignedTransactionRequest CreateRequestWithBlobs(List<AttachmentReference> refs)
    {
        var request = CreateRequest();
        foreach (var r in refs)
        {
            request.Attachments.Add(CreateSmallBlob(r.Id));
        }
        return request;
    }

    private static List<AttachmentReference> CreateAttachmentRefs(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new AttachmentReference(
                Guid.NewGuid().ToString(), $"hash{i}", "image/jpeg", 1024, $"file{i}.jpg"))
            .ToList();

    private static AttachmentBlob CreateSmallBlob(string id) =>
        new()
        {
            AttachmentId = id,
            EncryptedOriginal = ByteString.CopyFrom(new byte[100]),
            EncryptedThumbnail = ByteString.Empty,
        };

    #endregion
}
