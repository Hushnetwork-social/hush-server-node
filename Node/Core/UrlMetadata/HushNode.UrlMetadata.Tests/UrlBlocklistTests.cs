using FluentAssertions;
using HushNode.UrlMetadata;
using Xunit;

namespace HushNode.UrlMetadata.Tests;

/// <summary>
/// Tests for UrlBlocklist - domain blocking functionality.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class UrlBlocklistTests
{
    #region Blocked domain tests

    [Theory]
    [InlineData("https://malware-distribution.example/page")]
    [InlineData("https://phishing-site.example/login")]
    [InlineData("https://malicious-redirects.example/redirect")]
    public void IsBlocked_WithBlockedDomain_ReturnsTrue(string url)
    {
        // Arrange
        var blocklist = new UrlBlocklist();

        // Act
        var result = blocklist.IsBlocked(url);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsBlocked_WithBlockedDomainCaseInsensitive_ReturnsTrue()
    {
        // Arrange
        var blocklist = new UrlBlocklist();

        // Act
        var result = blocklist.IsBlocked("https://MALWARE-DISTRIBUTION.EXAMPLE/page");

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Allowed domain tests

    [Theory]
    [InlineData("https://example.com/article")]
    [InlineData("https://google.com/search")]
    [InlineData("https://github.com/repo")]
    public void IsBlocked_WithAllowedDomain_ReturnsFalse(string url)
    {
        // Arrange
        var blocklist = new UrlBlocklist();

        // Act
        var result = blocklist.IsBlocked(url);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Subdomain tests

    [Theory]
    [InlineData("https://sub.malware-distribution.example/page")]
    [InlineData("https://www.malware-distribution.example/page")]
    [InlineData("https://deep.nested.malware-distribution.example/page")]
    public void IsBlocked_WithSubdomainOfBlockedDomain_ReturnsTrue(string url)
    {
        // Arrange
        var blocklist = new UrlBlocklist();

        // Act
        var result = blocklist.IsBlocked(url);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Edge cases

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsBlocked_WithEmptyOrNullUrl_ReturnsFalse(string? url)
    {
        // Arrange
        var blocklist = new UrlBlocklist();

        // Act
        var result = blocklist.IsBlocked(url!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsBlocked_WithInvalidUrl_ReturnsFalse()
    {
        // Arrange
        var blocklist = new UrlBlocklist();

        // Act
        var result = blocklist.IsBlocked("not-a-valid-url");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsBlocked_WithSimilarButNotBlockedDomain_ReturnsFalse()
    {
        // Arrange
        var blocklist = new UrlBlocklist();

        // Act - "safe-malware-distribution.example" should NOT be blocked
        // because it doesn't end with ".malware-distribution.example"
        var result = blocklist.IsBlocked("https://safe-malware-distribution.example/page");

        // Assert
        result.Should().BeFalse();
    }

    #endregion
}
