namespace HushNode.UrlMetadata.Models;

/// <summary>
/// Represents the result of fetching metadata for a URL.
/// </summary>
public class UrlMetadataResult
{
    /// <summary>
    /// Maximum length for the title field.
    /// </summary>
    public const int MaxTitleLength = 200;

    /// <summary>
    /// Maximum length for the description field.
    /// </summary>
    public const int MaxDescriptionLength = 500;

    /// <summary>
    /// The original URL that was requested.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Whether metadata was successfully fetched.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The page title from Open Graph (og:title) or HTML title tag.
    /// Truncated to 200 characters max.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The page description from Open Graph (og:description) or meta description.
    /// Truncated to 500 characters max.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The original image URL from Open Graph (og:image).
    /// </summary>
    public string? ImageUrl { get; init; }

    /// <summary>
    /// The processed image as base64 string (resized to max 400px width).
    /// </summary>
    public string? ImageBase64 { get; init; }

    /// <summary>
    /// The domain extracted from the URL (e.g., "www.example.com").
    /// </summary>
    public required string Domain { get; init; }

    /// <summary>
    /// When the metadata was fetched.
    /// </summary>
    public DateTime FetchedAt { get; init; }

    /// <summary>
    /// Error message if Success is false.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful result with metadata.
    /// </summary>
    public static UrlMetadataResult CreateSuccess(
        string url,
        string domain,
        string? title,
        string? description,
        string? imageUrl,
        string? imageBase64)
    {
        return new UrlMetadataResult
        {
            Url = url,
            Success = true,
            Domain = domain,
            Title = TruncateString(title, MaxTitleLength),
            Description = TruncateString(description, MaxDescriptionLength),
            ImageUrl = imageUrl,
            ImageBase64 = imageBase64,
            FetchedAt = DateTime.UtcNow,
            ErrorMessage = null
        };
    }

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    public static UrlMetadataResult CreateFailure(string url, string domain, string errorMessage)
    {
        return new UrlMetadataResult
        {
            Url = url,
            Success = false,
            Domain = domain,
            Title = null,
            Description = null,
            ImageUrl = null,
            ImageBase64 = null,
            FetchedAt = DateTime.UtcNow,
            ErrorMessage = errorMessage
        };
    }

    private static string? TruncateString(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }
}
