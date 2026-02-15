using FluentAssertions;
using HushNode.Feeds.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for AttachmentTempStorageService file operations.
/// Uses a temporary directory that is cleaned up after each test.
/// </summary>
public class AttachmentTempStorageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AttachmentTempStorageService _sut;

    public AttachmentTempStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hush-test-{Guid.NewGuid()}");
        var logger = new Mock<ILogger<AttachmentTempStorageService>>();
        _sut = new AttachmentTempStorageService(_tempDir, logger.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task SaveAndRetrieve_OriginalAndThumbnail_RoundTrip()
    {
        // Arrange
        var id = Guid.NewGuid().ToString();
        var original = new byte[] { 1, 2, 3, 4, 5 };
        var thumbnail = new byte[] { 10, 20, 30 };

        // Act
        await _sut.SaveAsync(id, original, thumbnail);
        var result = await _sut.RetrieveAsync(id);

        // Assert
        result.Should().NotBeNull();
        result!.Value.EncryptedOriginal.Should().BeEquivalentTo(original);
        result.Value.EncryptedThumbnail.Should().BeEquivalentTo(thumbnail);
    }

    [Fact]
    public async Task SaveAndRetrieve_NullThumbnail_OnlyOriginalStored()
    {
        // Arrange
        var id = Guid.NewGuid().ToString();
        var original = new byte[] { 1, 2, 3 };

        // Act
        await _sut.SaveAsync(id, original, null);
        var result = await _sut.RetrieveAsync(id);

        // Assert
        result.Should().NotBeNull();
        result!.Value.EncryptedOriginal.Should().BeEquivalentTo(original);
        result.Value.EncryptedThumbnail.Should().BeNull();
    }

    [Fact]
    public async Task Delete_ExistingAttachment_RemovesFiles()
    {
        // Arrange
        var id = Guid.NewGuid().ToString();
        await _sut.SaveAsync(id, new byte[] { 1, 2, 3 }, new byte[] { 4, 5, 6 });

        // Act
        await _sut.DeleteAsync(id);
        var result = await _sut.RetrieveAsync(id);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Retrieve_NonExistentUuid_ReturnsNull()
    {
        // Arrange
        var id = Guid.NewGuid().ToString();

        // Act
        var result = await _sut.RetrieveAsync(id);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CleanupOrphans_DeletesOldFiles_PreservesRecentFiles()
    {
        // Arrange
        var oldId = Guid.NewGuid().ToString();
        var recentId = Guid.NewGuid().ToString();

        await _sut.SaveAsync(oldId, new byte[] { 1 }, null);
        await _sut.SaveAsync(recentId, new byte[] { 2 }, null);

        // Make the old file appear old by modifying its timestamp
        var oldFilePath = Path.Combine(_tempDir, $"{oldId}.original");
        File.SetLastWriteTimeUtc(oldFilePath, DateTime.UtcNow.AddMinutes(-15));

        // Act
        await _sut.CleanupOrphansAsync(TimeSpan.FromMinutes(10));

        // Assert
        var oldResult = await _sut.RetrieveAsync(oldId);
        var recentResult = await _sut.RetrieveAsync(recentId);

        oldResult.Should().BeNull("old file should have been cleaned up");
        recentResult.Should().NotBeNull("recent file should be preserved");
    }

    [Fact]
    public async Task Delete_NonExistentAttachment_DoesNotThrow()
    {
        // Arrange
        var id = Guid.NewGuid().ToString();

        // Act & Assert - should not throw
        await _sut.DeleteAsync(id);
    }

    [Fact]
    public async Task Save_EmptyThumbnail_DoesNotCreateThumbnailFile()
    {
        // Arrange
        var id = Guid.NewGuid().ToString();

        // Act
        await _sut.SaveAsync(id, new byte[] { 1, 2 }, Array.Empty<byte>());
        var result = await _sut.RetrieveAsync(id);

        // Assert
        result.Should().NotBeNull();
        result!.Value.EncryptedOriginal.Should().HaveCount(2);
        result.Value.EncryptedThumbnail.Should().BeNull("empty byte array should not create a thumbnail file");
    }
}
