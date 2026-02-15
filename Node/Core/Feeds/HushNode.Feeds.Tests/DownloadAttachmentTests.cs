using FluentAssertions;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.Feeds.gRPC;
using HushNode.Feeds.Storage;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for FEAT-066 DownloadAttachment streaming endpoint in FeedsGrpcService.
/// </summary>
public class DownloadAttachmentTests
{
    [Fact]
    public async Task DownloadAttachment_AuthorizedUserFullSize_StreamsCorrectChunks()
    {
        // Arrange
        var mocker = new AutoMocker();
        mocker.Use<IConfiguration>(CreateConfiguration());
        var feedId = new FeedId(Guid.NewGuid());
        var attachmentId = Guid.NewGuid().ToString();
        var userAddress = "user-address-123";

        // 200KB original → should produce 4 chunks (3x64KB + 1x8KB)
        var originalBytes = new byte[200 * 1024];
        Random.Shared.NextBytes(originalBytes);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.IsUserParticipantOfFeedAsync(feedId, userAddress))
            .ReturnsAsync(true);

        mocker.GetMock<IAttachmentStorageService>()
            .Setup(x => x.GetByIdAsync(attachmentId))
            .ReturnsAsync(new AttachmentEntity(
                attachmentId, originalBytes, new byte[1024], new FeedMessageId(Guid.NewGuid()),
                200 * 1024, 1024, "image/jpeg", "photo.jpg", "abc123", DateTime.UtcNow));

        var sut = mocker.CreateInstance<FeedsGrpcService>();
        var request = new DownloadAttachmentRequest
        {
            AttachmentId = attachmentId,
            FeedId = feedId.Value.ToString(),
            RequesterUserAddress = userAddress,
            ThumbnailOnly = false
        };

        var writtenChunks = new List<AttachmentChunk>();
        var mockStream = new Mock<IServerStreamWriter<AttachmentChunk>>();
        mockStream.Setup(x => x.WriteAsync(It.IsAny<AttachmentChunk>()))
            .Callback<AttachmentChunk>(chunk => writtenChunks.Add(chunk))
            .Returns(Task.CompletedTask);

        var mockContext = new Mock<ServerCallContext>();

        // Act
        await sut.DownloadAttachment(request, mockStream.Object, mockContext.Object);

        // Assert
        writtenChunks.Should().HaveCount(4);
        writtenChunks[0].TotalChunks.Should().Be(4);
        writtenChunks[0].TotalSize.Should().Be(200 * 1024);
        writtenChunks[0].ChunkIndex.Should().Be(0);

        // Subsequent chunks should have TotalChunks=0 and TotalSize=0
        writtenChunks[1].TotalChunks.Should().Be(0);
        writtenChunks[1].TotalSize.Should().Be(0);
        writtenChunks[1].ChunkIndex.Should().Be(1);

        // All chunks combined should equal original bytes
        var reassembled = writtenChunks.SelectMany(c => c.Data.ToByteArray()).ToArray();
        reassembled.Should().BeEquivalentTo(originalBytes);
    }

    [Fact]
    public async Task DownloadAttachment_AuthorizedUserThumbnail_StreamsThumbnailBytes()
    {
        // Arrange
        var mocker = new AutoMocker();
        mocker.Use<IConfiguration>(CreateConfiguration());
        var feedId = new FeedId(Guid.NewGuid());
        var attachmentId = Guid.NewGuid().ToString();
        var userAddress = "user-address-456";
        var thumbnailBytes = new byte[5000];
        Random.Shared.NextBytes(thumbnailBytes);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.IsUserParticipantOfFeedAsync(feedId, userAddress))
            .ReturnsAsync(true);

        mocker.GetMock<IAttachmentStorageService>()
            .Setup(x => x.GetByIdAsync(attachmentId))
            .ReturnsAsync(new AttachmentEntity(
                attachmentId, new byte[100000], thumbnailBytes, new FeedMessageId(Guid.NewGuid()),
                100000, 5000, "image/jpeg", "photo.jpg", "abc123", DateTime.UtcNow));

        var sut = mocker.CreateInstance<FeedsGrpcService>();
        var request = new DownloadAttachmentRequest
        {
            AttachmentId = attachmentId,
            FeedId = feedId.Value.ToString(),
            RequesterUserAddress = userAddress,
            ThumbnailOnly = true
        };

        var writtenChunks = new List<AttachmentChunk>();
        var mockStream = new Mock<IServerStreamWriter<AttachmentChunk>>();
        mockStream.Setup(x => x.WriteAsync(It.IsAny<AttachmentChunk>()))
            .Callback<AttachmentChunk>(chunk => writtenChunks.Add(chunk))
            .Returns(Task.CompletedTask);

        var mockContext = new Mock<ServerCallContext>();

        // Act
        await sut.DownloadAttachment(request, mockStream.Object, mockContext.Object);

        // Assert
        writtenChunks.Should().HaveCount(1); // 5KB fits in a single 64KB chunk
        var reassembled = writtenChunks.SelectMany(c => c.Data.ToByteArray()).ToArray();
        reassembled.Should().BeEquivalentTo(thumbnailBytes);
    }

    [Fact]
    public async Task DownloadAttachment_NonParticipant_ThrowsPermissionDenied()
    {
        // Arrange
        var mocker = new AutoMocker();
        mocker.Use<IConfiguration>(CreateConfiguration());
        var feedId = new FeedId(Guid.NewGuid());
        var userAddress = "unauthorized-user";

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.IsUserParticipantOfFeedAsync(feedId, userAddress))
            .ReturnsAsync(false);

        var sut = mocker.CreateInstance<FeedsGrpcService>();
        var request = new DownloadAttachmentRequest
        {
            AttachmentId = Guid.NewGuid().ToString(),
            FeedId = feedId.Value.ToString(),
            RequesterUserAddress = userAddress,
            ThumbnailOnly = false
        };

        var mockStream = new Mock<IServerStreamWriter<AttachmentChunk>>();
        var mockContext = new Mock<ServerCallContext>();

        // Act
        var act = () => sut.DownloadAttachment(request, mockStream.Object, mockContext.Object);

        // Assert
        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task DownloadAttachment_NonExistentAttachment_ThrowsNotFound()
    {
        // Arrange
        var mocker = new AutoMocker();
        mocker.Use<IConfiguration>(CreateConfiguration());
        var feedId = new FeedId(Guid.NewGuid());
        var userAddress = "user-address-789";

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.IsUserParticipantOfFeedAsync(feedId, userAddress))
            .ReturnsAsync(true);

        mocker.GetMock<IAttachmentStorageService>()
            .Setup(x => x.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((AttachmentEntity?)null);

        var sut = mocker.CreateInstance<FeedsGrpcService>();
        var request = new DownloadAttachmentRequest
        {
            AttachmentId = Guid.NewGuid().ToString(),
            FeedId = feedId.Value.ToString(),
            RequesterUserAddress = userAddress,
            ThumbnailOnly = false
        };

        var mockStream = new Mock<IServerStreamWriter<AttachmentChunk>>();
        var mockContext = new Mock<ServerCallContext>();

        // Act
        var act = () => sut.DownloadAttachment(request, mockStream.Object, mockContext.Object);

        // Assert
        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    #region Helpers

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Feeds:MaxMessagesPerResponse"] = "100"
            })
            .Build();

    #endregion
}
