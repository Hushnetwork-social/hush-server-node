using FluentAssertions;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Xunit;

namespace HushNode.Feeds.Tests.Storage;

/// <summary>
/// Tests for AttachmentRepository using in-memory database.
/// FEAT-066: Attachment Storage Infrastructure.
/// </summary>
public class AttachmentRepositoryTests : IClassFixture<FeedsInMemoryDbContextFixture>
{
    private readonly FeedsInMemoryDbContextFixture _fixture;

    public AttachmentRepositoryTests(FeedsInMemoryDbContextFixture fixture)
    {
        _fixture = fixture;
    }

    private AttachmentRepository CreateRepository(FeedsDbContext context)
    {
        var repository = new AttachmentRepository();
        repository.SetContext(context);
        return repository;
    }

    [Fact]
    public async Task CreateAndGetById_ValidAttachment_ReturnsAllFields()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var id = Guid.NewGuid().ToString();
        var messageId = TestDataFactory.CreateFeedMessageId();
        var entity = new AttachmentEntity(
            id,
            new byte[] { 1, 2, 3, 4 },
            new byte[] { 10, 20 },
            messageId,
            1024, 256, "image/jpeg", "photo.jpg", DateTime.UtcNow);

        // Act
        await repository.CreateAttachmentAsync(entity);
        await context.SaveChangesAsync();
        var result = await repository.GetByIdAsync(id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.EncryptedOriginal.Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4 });
        result.EncryptedThumbnail.Should().BeEquivalentTo(new byte[] { 10, 20 });
        result.FeedMessageId.Should().Be(messageId);
        result.OriginalSize.Should().Be(1024);
        result.MimeType.Should().Be("image/jpeg");
        result.FileName.Should().Be("photo.jpg");
    }

    [Fact]
    public async Task GetByMessageId_MultipleAttachments_ReturnsAll()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var messageId = TestDataFactory.CreateFeedMessageId();

        var att1 = new AttachmentEntity(
            Guid.NewGuid().ToString(), new byte[] { 1 }, null, messageId,
            100, 0, "image/png", "a.png", DateTime.UtcNow);
        var att2 = new AttachmentEntity(
            Guid.NewGuid().ToString(), new byte[] { 2 }, null, messageId,
            200, 0, "image/jpeg", "b.jpg", DateTime.UtcNow);

        await repository.CreateAttachmentAsync(att1);
        await repository.CreateAttachmentAsync(att2);
        await context.SaveChangesAsync();

        // Act
        var results = (await repository.GetByMessageIdAsync(messageId)).ToList();

        // Assert
        results.Should().HaveCount(2);
        results.Select(r => r.FileName).Should().Contain(new[] { "a.png", "b.jpg" });
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNull()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        // Act
        var result = await repository.GetByIdAsync(Guid.NewGuid().ToString());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateAttachment_NullThumbnail_Succeeds()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var id = Guid.NewGuid().ToString();
        var entity = new AttachmentEntity(
            id, new byte[] { 1, 2, 3 }, null,
            TestDataFactory.CreateFeedMessageId(),
            512, 0, "application/pdf", "doc.pdf", DateTime.UtcNow);

        // Act
        await repository.CreateAttachmentAsync(entity);
        await context.SaveChangesAsync();
        var result = await repository.GetByIdAsync(id);

        // Assert
        result.Should().NotBeNull();
        result!.EncryptedThumbnail.Should().BeNull();
    }
}
