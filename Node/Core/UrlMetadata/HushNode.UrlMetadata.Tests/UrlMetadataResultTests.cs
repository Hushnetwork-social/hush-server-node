using FluentAssertions;
using HushNode.UrlMetadata.Models;
using Xunit;

namespace HushNode.UrlMetadata.Tests;

/// <summary>
/// Tests for UrlMetadataResult - factory methods and truncation.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class UrlMetadataResultTests
{
    #region CreateSuccess tests

    [Fact]
    public void CreateSuccess_WithValidData_CreatesSuccessfulResult()
    {
        // Arrange
        var url = "https://example.com";
        var domain = "example.com";
        var title = "Test Title";
        var description = "Test Description";
        var imageUrl = "https://example.com/image.jpg";
        var imageBase64 = "base64data";

        // Act
        var result = UrlMetadataResult.CreateSuccess(url, domain, title, description, imageUrl, imageBase64);

        // Assert
        result.Success.Should().BeTrue();
        result.Url.Should().Be(url);
        result.Domain.Should().Be(domain);
        result.Title.Should().Be(title);
        result.Description.Should().Be(description);
        result.ImageUrl.Should().Be(imageUrl);
        result.ImageBase64.Should().Be(imageBase64);
        result.ErrorMessage.Should().BeNull();
        result.FetchedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CreateSuccess_WithNullValues_AllowsNullables()
    {
        // Arrange & Act
        var result = UrlMetadataResult.CreateSuccess("https://example.com", "example.com", null, null, null, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Title.Should().BeNull();
        result.Description.Should().BeNull();
        result.ImageUrl.Should().BeNull();
        result.ImageBase64.Should().BeNull();
    }

    #endregion

    #region CreateFailure tests

    [Fact]
    public void CreateFailure_WithErrorMessage_CreatesFailedResult()
    {
        // Arrange
        var url = "https://example.com";
        var domain = "example.com";
        var errorMessage = "Request timed out";

        // Act
        var result = UrlMetadataResult.CreateFailure(url, domain, errorMessage);

        // Assert
        result.Success.Should().BeFalse();
        result.Url.Should().Be(url);
        result.Domain.Should().Be(domain);
        result.ErrorMessage.Should().Be(errorMessage);
        result.Title.Should().BeNull();
        result.Description.Should().BeNull();
        result.ImageUrl.Should().BeNull();
        result.ImageBase64.Should().BeNull();
    }

    #endregion

    #region Title truncation tests

    [Fact]
    public void CreateSuccess_WithLongTitle_TruncatesTo200CharsWithEllipsis()
    {
        // Arrange
        var longTitle = new string('A', 300);
        var expectedLength = UrlMetadataResult.MaxTitleLength;

        // Act
        var result = UrlMetadataResult.CreateSuccess(
            "https://example.com",
            "example.com",
            longTitle,
            null,
            null,
            null);

        // Assert
        result.Title.Should().HaveLength(expectedLength);
        result.Title.Should().EndWith("...");
    }

    [Fact]
    public void CreateSuccess_WithExactlyMaxLengthTitle_DoesNotTruncate()
    {
        // Arrange
        var exactTitle = new string('A', UrlMetadataResult.MaxTitleLength);

        // Act
        var result = UrlMetadataResult.CreateSuccess(
            "https://example.com",
            "example.com",
            exactTitle,
            null,
            null,
            null);

        // Assert
        result.Title.Should().HaveLength(UrlMetadataResult.MaxTitleLength);
        result.Title.Should().NotEndWith("...");
    }

    [Fact]
    public void CreateSuccess_WithShortTitle_DoesNotTruncate()
    {
        // Arrange
        var shortTitle = "Short Title";

        // Act
        var result = UrlMetadataResult.CreateSuccess(
            "https://example.com",
            "example.com",
            shortTitle,
            null,
            null,
            null);

        // Assert
        result.Title.Should().Be(shortTitle);
    }

    #endregion

    #region Description truncation tests

    [Fact]
    public void CreateSuccess_WithLongDescription_TruncatesTo500CharsWithEllipsis()
    {
        // Arrange
        var longDescription = new string('B', 600);
        var expectedLength = UrlMetadataResult.MaxDescriptionLength;

        // Act
        var result = UrlMetadataResult.CreateSuccess(
            "https://example.com",
            "example.com",
            null,
            longDescription,
            null,
            null);

        // Assert
        result.Description.Should().HaveLength(expectedLength);
        result.Description.Should().EndWith("...");
    }

    [Fact]
    public void CreateSuccess_WithExactlyMaxLengthDescription_DoesNotTruncate()
    {
        // Arrange
        var exactDescription = new string('B', UrlMetadataResult.MaxDescriptionLength);

        // Act
        var result = UrlMetadataResult.CreateSuccess(
            "https://example.com",
            "example.com",
            null,
            exactDescription,
            null,
            null);

        // Assert
        result.Description.Should().HaveLength(UrlMetadataResult.MaxDescriptionLength);
        result.Description.Should().NotEndWith("...");
    }

    #endregion

    #region Edge cases

    [Fact]
    public void CreateSuccess_WithEmptyStrings_PreservesEmptyStrings()
    {
        // Arrange & Act
        var result = UrlMetadataResult.CreateSuccess(
            "https://example.com",
            "example.com",
            "",
            "",
            null,
            null);

        // Assert
        result.Title.Should().Be("");
        result.Description.Should().Be("");
    }

    [Fact]
    public void CreateSuccess_WithVeryShortString_DoesNotCrash()
    {
        // Arrange & Act
        var result = UrlMetadataResult.CreateSuccess(
            "https://example.com",
            "example.com",
            "A",
            "B",
            null,
            null);

        // Assert
        result.Title.Should().Be("A");
        result.Description.Should().Be("B");
    }

    #endregion
}
