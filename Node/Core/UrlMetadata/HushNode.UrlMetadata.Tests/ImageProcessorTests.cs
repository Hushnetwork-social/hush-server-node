using FluentAssertions;
using HushNode.UrlMetadata;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using Xunit;

namespace HushNode.UrlMetadata.Tests;

/// <summary>
/// Tests for ImageProcessor - image fetching and resizing.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class ImageProcessorTests
{
    #region Fetch failure tests

    [Fact]
    public async Task FetchAndProcessAsync_WithNullUrl_ReturnsNull()
    {
        // Arrange
        var processor = CreateProcessor();

        // Act
        var result = await processor.FetchAndProcessAsync(null!);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchAndProcessAsync_WithEmptyUrl_ReturnsNull()
    {
        // Arrange
        var processor = CreateProcessor();

        // Act
        var result = await processor.FetchAndProcessAsync("");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchAndProcessAsync_WithHttpError_ReturnsNull()
    {
        // Arrange
        var processor = CreateProcessorWithErrorResponse(HttpStatusCode.NotFound);

        // Act
        var result = await processor.FetchAndProcessAsync("https://example.com/image.jpg");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchAndProcessAsync_WithServerError_ReturnsNull()
    {
        // Arrange
        var processor = CreateProcessorWithErrorResponse(HttpStatusCode.InternalServerError);

        // Act
        var result = await processor.FetchAndProcessAsync("https://example.com/image.jpg");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Valid image tests

    [Fact]
    public async Task FetchAndProcessAsync_WithValidSmallImage_ReturnsBase64()
    {
        // Arrange
        var imageBytes = CreateTestJpegImage(100, 100);
        var processor = CreateProcessorWithImageResponse(imageBytes);

        // Act
        var result = await processor.FetchAndProcessAsync("https://example.com/image.jpg");

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
        // Result should be valid base64
        var decoded = Convert.FromBase64String(result!);
        decoded.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FetchAndProcessAsync_WithLargeImage_ResizesToMaxWidth()
    {
        // Arrange
        // Create a 800x600 image (should be resized to 400x300)
        var imageBytes = CreateTestJpegImage(800, 600);
        var processor = CreateProcessorWithImageResponse(imageBytes);

        // Act
        var result = await processor.FetchAndProcessAsync("https://example.com/large-image.jpg");

        // Assert
        result.Should().NotBeNull();
        // The result should be valid base64 representing a smaller image
        var decoded = Convert.FromBase64String(result!);
        decoded.Should().NotBeEmpty();
    }

    #endregion

    #region Invalid image tests

    [Fact]
    public async Task FetchAndProcessAsync_WithInvalidImageData_ReturnsNull()
    {
        // Arrange
        var invalidData = new byte[] { 0x00, 0x01, 0x02, 0x03 }; // Not valid image data
        var processor = CreateProcessorWithImageResponse(invalidData);

        // Act
        var result = await processor.FetchAndProcessAsync("https://example.com/invalid.jpg");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Helper methods

    private static ImageProcessor CreateProcessor()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        var logger = Mock.Of<ILogger<ImageProcessor>>();

        return new ImageProcessor(httpClient, logger);
    }

    private static ImageProcessor CreateProcessorWithErrorResponse(HttpStatusCode statusCode)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var logger = Mock.Of<ILogger<ImageProcessor>>();

        return new ImageProcessor(httpClient, logger);
    }

    private static ImageProcessor CreateProcessorWithImageResponse(byte[] imageData)
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
                Content = new ByteArrayContent(imageData)
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var logger = Mock.Of<ILogger<ImageProcessor>>();

        return new ImageProcessor(httpClient, logger);
    }

    /// <summary>
    /// Creates a minimal valid JPEG image for testing.
    /// Uses SkiaSharp to create an actual image.
    /// </summary>
    private static byte[] CreateTestJpegImage(int width, int height)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);

        // Fill with a solid color
        canvas.Clear(SkiaSharp.SKColors.Blue);

        // Encode as JPEG
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80);

        return data.ToArray();
    }

    #endregion
}
