using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HushNode.UrlMetadata;

/// <summary>
/// Processes images for link previews - resizing and base64 encoding.
/// </summary>
public interface IImageProcessor
{
    /// <summary>
    /// Fetches an image, resizes it if necessary, and returns as base64.
    /// </summary>
    /// <param name="imageUrl">The URL of the image to fetch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Base64-encoded image, or null if processing failed.</returns>
    Task<string?> FetchAndProcessAsync(string imageUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of image processor using SkiaSharp.
/// </summary>
public class ImageProcessor : IImageProcessor
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ImageProcessor> _logger;

    /// <summary>
    /// Maximum image size to fetch (1MB).
    /// </summary>
    private const int MaxFetchSizeBytes = 1024 * 1024;

    /// <summary>
    /// Maximum width for resized images.
    /// </summary>
    private const int MaxWidth = 400;

    /// <summary>
    /// HTTP timeout for image fetching (5 seconds).
    /// </summary>
    private const int TimeoutSeconds = 5;

    /// <summary>
    /// JPEG quality for output (0-100).
    /// </summary>
    private const int JpegQuality = 80;

    public ImageProcessor(HttpClient httpClient, ILogger<ImageProcessor> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);
    }

    /// <inheritdoc />
    public async Task<string?> FetchAndProcessAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return null;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            // Fetch image with size limit
            var imageData = await FetchImageAsync(imageUrl, cts.Token);
            if (imageData == null)
                return null;

            // Resize and encode
            return ProcessImage(imageData);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Image fetch timed out for URL: {Url}", imageUrl);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "Failed to fetch image: {Url}", imageUrl);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error processing image: {Url}", imageUrl);
            return null;
        }
    }

    private async Task<byte[]?> FetchImageAsync(string imageUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
        request.Headers.Add("User-Agent", "HushNetwork/1.0 (Link Preview Bot)");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("Image fetch returned {StatusCode} for {Url}", response.StatusCode, imageUrl);
            return null;
        }

        // Check content length if available
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > MaxFetchSizeBytes)
        {
            _logger.LogDebug("Image too large ({Size} bytes) for {Url}", contentLength.Value, imageUrl);
            return null;
        }

        // Read with size limit
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memoryStream = new MemoryStream();

        var buffer = new byte[8192];
        var totalRead = 0;

        while (totalRead < MaxFetchSizeBytes)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (bytesRead == 0) break;

            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;
        }

        // Check if there's more content (exceeded limit)
        if (await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) > 0)
        {
            _logger.LogDebug("Image exceeded size limit for {Url}", imageUrl);
            return null;
        }

        return memoryStream.ToArray();
    }

    private string? ProcessImage(byte[] imageData)
    {
        using var inputBitmap = SKBitmap.Decode(imageData);
        if (inputBitmap == null)
        {
            _logger.LogDebug("Failed to decode image data");
            return null;
        }

        var outputBitmap = inputBitmap;
        var shouldDispose = false;

        try
        {
            // Resize if width exceeds maximum
            if (inputBitmap.Width > MaxWidth)
            {
                var aspectRatio = (float)inputBitmap.Height / inputBitmap.Width;
                var newHeight = (int)(MaxWidth * aspectRatio);

                outputBitmap = inputBitmap.Resize(new SKImageInfo(MaxWidth, newHeight), SKSamplingOptions.Default);
                shouldDispose = true;

                if (outputBitmap == null)
                {
                    _logger.LogDebug("Failed to resize image");
                    return null;
                }

                _logger.LogDebug("Resized image from {OldWidth}x{OldHeight} to {NewWidth}x{NewHeight}",
                    inputBitmap.Width, inputBitmap.Height, MaxWidth, newHeight);
            }

            // Encode as JPEG
            using var image = SKImage.FromBitmap(outputBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);

            return Convert.ToBase64String(data.ToArray());
        }
        finally
        {
            if (shouldDispose && outputBitmap != null)
            {
                outputBitmap.Dispose();
            }
        }
    }
}
