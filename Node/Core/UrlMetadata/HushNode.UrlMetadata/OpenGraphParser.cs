using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using HushNode.UrlMetadata.Models;
using Microsoft.Extensions.Logging;

namespace HushNode.UrlMetadata;

/// <summary>
/// Parses Open Graph metadata from HTML pages.
/// Fetches the URL and extracts og:title, og:description, og:image tags.
/// Falls back to standard HTML tags when OG tags are missing.
/// </summary>
public interface IOpenGraphParser
{
    /// <summary>
    /// Fetches a URL and parses its Open Graph metadata.
    /// </summary>
    /// <param name="url">The URL to fetch and parse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed metadata result.</returns>
    Task<OpenGraphResult> ParseAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of Open Graph parsing (internal to parser, before processing).
/// </summary>
public class OpenGraphResult
{
    public bool Success { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? ImageUrl { get; init; }
    public string? ErrorMessage { get; init; }

    public static OpenGraphResult CreateSuccess(string? title, string? description, string? imageUrl)
        => new() { Success = true, Title = title, Description = description, ImageUrl = imageUrl };

    public static OpenGraphResult CreateFailure(string errorMessage)
        => new() { Success = false, ErrorMessage = errorMessage };
}

/// <summary>
/// Implementation of Open Graph parser using HtmlAgilityPack.
/// </summary>
public class OpenGraphParser : IOpenGraphParser
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenGraphParser> _logger;

    /// <summary>
    /// HTTP request timeout in seconds.
    /// </summary>
    private const int TimeoutSeconds = 3;

    /// <summary>
    /// Maximum response size to read (1MB).
    /// </summary>
    private const int MaxResponseSizeBytes = 1024 * 1024;

    public OpenGraphParser(HttpClient httpClient, ILogger<OpenGraphParser> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Configure HttpClient timeout
        _httpClient.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);
    }

    /// <inheritdoc />
    public async Task<OpenGraphResult> ParseAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            // Check for YouTube URLs and use oEmbed API
            if (IsYouTubeUrl(url))
            {
                _logger.LogInformation("[YouTube] Detected YouTube URL: {Url}", url);
                var youtubeResult = await ParseYouTubeOEmbedAsync(url, cts.Token);
                if (youtubeResult != null)
                {
                    _logger.LogInformation("[YouTube] oEmbed success - Title: {Title}", youtubeResult.Title);
                    return youtubeResult;
                }
                _logger.LogWarning("[YouTube] oEmbed failed, falling back to HTML parsing");
                // Fall through to standard parsing if oEmbed fails
            }
            else
            {
                _logger.LogDebug("[OpenGraph] Not a YouTube URL: {Url}", url);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "HushNetwork/1.0 (Link Preview Bot)");
            request.Headers.Add("Accept", "text/html,application/xhtml+xml");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return OpenGraphResult.CreateFailure($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            }

            // Check content type
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!IsHtmlContentType(contentType))
            {
                return OpenGraphResult.CreateFailure($"Unsupported content type: {contentType}");
            }

            // Check content length if available
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > MaxResponseSizeBytes)
            {
                return OpenGraphResult.CreateFailure("Response too large");
            }

            // Read response with size limit
            var html = await ReadWithSizeLimitAsync(response.Content, cts.Token);
            if (html == null)
            {
                return OpenGraphResult.CreateFailure("Response too large");
            }

            return ParseHtml(html);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Request timed out for URL: {Url}", url);
            return OpenGraphResult.CreateFailure("Request timed out");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "HTTP request failed for URL: {Url}", url);
            return OpenGraphResult.CreateFailure($"Request failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error parsing URL: {Url}", url);
            return OpenGraphResult.CreateFailure($"Unexpected error: {ex.Message}");
        }
    }

    private static bool IsHtmlContentType(string contentType)
    {
        return contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("application/xhtml+xml", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> ReadWithSizeLimitAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var buffer = new char[MaxResponseSizeBytes];
        var totalRead = 0;

        while (totalRead < MaxResponseSizeBytes)
        {
            var bytesRead = await reader.ReadAsync(buffer.AsMemory(totalRead, MaxResponseSizeBytes - totalRead), cancellationToken);
            if (bytesRead == 0) break;
            totalRead += bytesRead;
        }

        // Check if there's more content (exceeded limit)
        if (!reader.EndOfStream)
        {
            return null;
        }

        return new string(buffer, 0, totalRead);
    }

    private static OpenGraphResult ParseHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Try Open Graph tags first
        var ogTitle = GetMetaContent(doc, "og:title");
        var ogDescription = GetMetaContent(doc, "og:description");
        var ogImage = GetMetaContent(doc, "og:image");

        // Fallback to standard HTML tags
        var title = ogTitle ?? GetHtmlTitle(doc);
        var description = ogDescription ?? GetMetaDescription(doc);

        return OpenGraphResult.CreateSuccess(title, description, ogImage);
    }

    private static string? GetMetaContent(HtmlDocument doc, string property)
    {
        // Try property attribute (Open Graph)
        var node = doc.DocumentNode.SelectSingleNode($"//meta[@property='{property}']");
        if (node != null)
        {
            var content = node.GetAttributeValue("content", null);
            if (!string.IsNullOrWhiteSpace(content))
                return content.Trim();
        }

        // Try name attribute (some sites use name instead of property)
        node = doc.DocumentNode.SelectSingleNode($"//meta[@name='{property}']");
        if (node != null)
        {
            var content = node.GetAttributeValue("content", null);
            if (!string.IsNullOrWhiteSpace(content))
                return content.Trim();
        }

        return null;
    }

    private static string? GetHtmlTitle(HtmlDocument doc)
    {
        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        var title = titleNode?.InnerText?.Trim();
        return string.IsNullOrWhiteSpace(title) ? null : HtmlEntity.DeEntitize(title);
    }

    private static string? GetMetaDescription(HtmlDocument doc)
    {
        var node = doc.DocumentNode.SelectSingleNode("//meta[@name='description']");
        if (node != null)
        {
            var content = node.GetAttributeValue("content", null);
            if (!string.IsNullOrWhiteSpace(content))
                return content.Trim();
        }

        return null;
    }

    #region YouTube oEmbed Support

    /// <summary>
    /// YouTube URL patterns to detect.
    /// </summary>
    private static readonly Regex YouTubeUrlPattern = new(
        @"^https?://(?:www\.)?(?:youtube\.com/watch\?v=|youtu\.be/|youtube\.com/embed/|youtube\.com/shorts/)[\w-]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Checks if a URL is a YouTube video URL.
    /// </summary>
    private static bool IsYouTubeUrl(string url)
    {
        return YouTubeUrlPattern.IsMatch(url);
    }

    /// <summary>
    /// Parses YouTube video metadata using the oEmbed API.
    /// </summary>
    private async Task<OpenGraphResult?> ParseYouTubeOEmbedAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var oEmbedUrl = $"https://www.youtube.com/oembed?url={Uri.EscapeDataString(url)}&format=json";
            _logger.LogInformation("[YouTube] Fetching oEmbed from: {OEmbedUrl}", oEmbedUrl);

            using var request = new HttpRequestMessage(HttpMethod.Get, oEmbedUrl);
            request.Headers.Add("User-Agent", "HushNetwork/1.0 (Link Preview Bot)");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            _logger.LogInformation("[YouTube] oEmbed response status: {StatusCode}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[YouTube] oEmbed returned {StatusCode} for URL: {Url}", response.StatusCode, url);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("[YouTube] oEmbed response: {Json}", json.Length > 200 ? json.Substring(0, 200) + "..." : json);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
            var authorName = root.TryGetProperty("author_name", out var authorProp) ? authorProp.GetString() : null;
            var thumbnailUrl = root.TryGetProperty("thumbnail_url", out var thumbProp) ? thumbProp.GetString() : null;

            // Use author name as description if available
            var description = !string.IsNullOrWhiteSpace(authorName) ? $"Video by {authorName}" : null;

            _logger.LogInformation("[YouTube] Parsed - Title: {Title}, Author: {Author}, Thumbnail: {Thumb}",
                title, authorName, thumbnailUrl);

            return OpenGraphResult.CreateSuccess(title, description, thumbnailUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[YouTube] oEmbed failed for URL: {Url}", url);
            return null;
        }
    }

    #endregion
}
