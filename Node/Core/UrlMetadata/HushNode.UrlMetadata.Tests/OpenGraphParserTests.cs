using FluentAssertions;
using HushNode.UrlMetadata;
using HushNode.UrlMetadata.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using Xunit;

namespace HushNode.UrlMetadata.Tests;

/// <summary>
/// Tests for OpenGraphParser - HTML metadata extraction.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class OpenGraphParserTests
{
    #region Parse HTML with Open Graph tags

    [Fact]
    public async Task ParseAsync_WithAllOgTags_ExtractsAllMetadata()
    {
        // Arrange
        var html = """
            <html>
            <head>
                <meta property="og:title" content="Test Title" />
                <meta property="og:description" content="Test Description" />
                <meta property="og:image" content="https://example.com/image.jpg" />
            </head>
            <body></body>
            </html>
            """;
        var parser = CreateParserWithMockResponse("https://example.com", html);

        // Act
        var result = await parser.ParseAsync("https://example.com");

        // Assert
        result.Success.Should().BeTrue();
        result.Title.Should().Be("Test Title");
        result.Description.Should().Be("Test Description");
        result.ImageUrl.Should().Be("https://example.com/image.jpg");
    }

    [Fact]
    public async Task ParseAsync_WithOgTagsUsingNameAttribute_ExtractsMetadata()
    {
        // Arrange
        var html = """
            <html>
            <head>
                <meta name="og:title" content="Name Attribute Title" />
                <meta name="og:description" content="Name Attribute Description" />
            </head>
            <body></body>
            </html>
            """;
        var parser = CreateParserWithMockResponse("https://example.com", html);

        // Act
        var result = await parser.ParseAsync("https://example.com");

        // Assert
        result.Success.Should().BeTrue();
        result.Title.Should().Be("Name Attribute Title");
        result.Description.Should().Be("Name Attribute Description");
    }

    #endregion

    #region Fallback to standard HTML tags

    [Fact]
    public async Task ParseAsync_WithNoOgTags_FallsBackToHtmlTitle()
    {
        // Arrange
        var html = """
            <html>
            <head>
                <title>HTML Title</title>
            </head>
            <body></body>
            </html>
            """;
        var parser = CreateParserWithMockResponse("https://example.com", html);

        // Act
        var result = await parser.ParseAsync("https://example.com");

        // Assert
        result.Success.Should().BeTrue();
        result.Title.Should().Be("HTML Title");
        result.Description.Should().BeNull();
        result.ImageUrl.Should().BeNull();
    }

    [Fact]
    public async Task ParseAsync_WithNoOgDescription_FallsBackToMetaDescription()
    {
        // Arrange
        var html = """
            <html>
            <head>
                <meta property="og:title" content="OG Title" />
                <meta name="description" content="Meta Description Fallback" />
            </head>
            <body></body>
            </html>
            """;
        var parser = CreateParserWithMockResponse("https://example.com", html);

        // Act
        var result = await parser.ParseAsync("https://example.com");

        // Assert
        result.Success.Should().BeTrue();
        result.Title.Should().Be("OG Title");
        result.Description.Should().Be("Meta Description Fallback");
    }

    [Fact]
    public async Task ParseAsync_WithNoMetaTags_ReturnsEmptyMetadata()
    {
        // Arrange
        var html = """
            <html>
            <head></head>
            <body><h1>Hello World</h1></body>
            </html>
            """;
        var parser = CreateParserWithMockResponse("https://example.com", html);

        // Act
        var result = await parser.ParseAsync("https://example.com");

        // Assert
        result.Success.Should().BeTrue();
        result.Title.Should().BeNull();
        result.Description.Should().BeNull();
        result.ImageUrl.Should().BeNull();
    }

    #endregion

    #region HTML entity decoding

    [Fact]
    public async Task ParseAsync_WithHtmlEntitiesInTitle_DecodesEntities()
    {
        // Arrange
        var html = """
            <html>
            <head>
                <title>Tom &amp; Jerry&#39;s Adventure</title>
            </head>
            <body></body>
            </html>
            """;
        var parser = CreateParserWithMockResponse("https://example.com", html);

        // Act
        var result = await parser.ParseAsync("https://example.com");

        // Assert
        result.Success.Should().BeTrue();
        result.Title.Should().Be("Tom & Jerry's Adventure");
    }

    #endregion

    #region Malformed HTML handling

    [Fact]
    public async Task ParseAsync_WithMalformedHtml_ExtractsWhateverAvailable()
    {
        // Arrange
        var html = """
            <html>
            <head>
                <meta property="og:title" content="Malformed Title"
                <title>Unclosed Title
            </head>
            <body>
            """;
        var parser = CreateParserWithMockResponse("https://example.com", html);

        // Act
        var result = await parser.ParseAsync("https://example.com");

        // Assert
        result.Success.Should().BeTrue();
        // HtmlAgilityPack should still extract what it can
        result.Title.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ParseAsync_WithEmptyHtml_ReturnsSuccessWithNullValues()
    {
        // Arrange
        var html = "";
        var parser = CreateParserWithMockResponse("https://example.com", html);

        // Act
        var result = await parser.ParseAsync("https://example.com");

        // Assert
        result.Success.Should().BeTrue();
        result.Title.Should().BeNull();
        result.Description.Should().BeNull();
    }

    #endregion

    #region HTTP error handling

    [Fact]
    public async Task ParseAsync_WithHttpError_ReturnsFailure()
    {
        // Arrange
        var parser = CreateParserWithErrorResponse("https://example.com", HttpStatusCode.NotFound);

        // Act
        var result = await parser.ParseAsync("https://example.com");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("404");
    }

    [Fact]
    public async Task ParseAsync_WithServerError_ReturnsFailure()
    {
        // Arrange
        var parser = CreateParserWithErrorResponse("https://example.com", HttpStatusCode.InternalServerError);

        // Act
        var result = await parser.ParseAsync("https://example.com");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("500");
    }

    #endregion

    #region Content type handling

    [Fact]
    public async Task ParseAsync_WithJsonContentType_ReturnsFailure()
    {
        // Arrange
        var parser = CreateParserWithMockResponse("https://example.com", "{}", "application/json");

        // Act
        var result = await parser.ParseAsync("https://example.com");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported content type");
    }

    [Fact]
    public async Task ParseAsync_WithXhtmlContentType_Succeeds()
    {
        // Arrange
        var html = """
            <html>
            <head><title>XHTML Page</title></head>
            <body></body>
            </html>
            """;
        var parser = CreateParserWithMockResponse("https://example.com", html, "application/xhtml+xml");

        // Act
        var result = await parser.ParseAsync("https://example.com");

        // Assert
        result.Success.Should().BeTrue();
        result.Title.Should().Be("XHTML Page");
    }

    #endregion

    #region Whitespace trimming

    [Fact]
    public async Task ParseAsync_WithWhitespaceInContent_TrimsWhitespace()
    {
        // Arrange
        var html = """
            <html>
            <head>
                <meta property="og:title" content="   Padded Title   " />
                <meta property="og:description" content="
                    Multiline
                    Description
                " />
            </head>
            <body></body>
            </html>
            """;
        var parser = CreateParserWithMockResponse("https://example.com", html);

        // Act
        var result = await parser.ParseAsync("https://example.com");

        // Assert
        result.Success.Should().BeTrue();
        result.Title.Should().Be("Padded Title");
        // Description has inner whitespace which should be preserved
        result.Description.Should().StartWith("Multiline");
    }

    #endregion

    #region Helper methods

    private static OpenGraphParser CreateParserWithMockResponse(
        string url,
        string htmlContent,
        string contentType = "text/html")
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(htmlContent, System.Text.Encoding.UTF8, contentType)
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var logger = Mock.Of<ILogger<OpenGraphParser>>();

        return new OpenGraphParser(httpClient, logger);
    }

    private static OpenGraphParser CreateParserWithErrorResponse(string url, HttpStatusCode statusCode)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                ReasonPhrase = statusCode.ToString()
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var logger = Mock.Of<ILogger<OpenGraphParser>>();

        return new OpenGraphParser(httpClient, logger);
    }

    #endregion
}
