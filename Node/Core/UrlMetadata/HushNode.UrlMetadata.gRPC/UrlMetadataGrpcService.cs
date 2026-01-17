using Grpc.Core;
using HushNetwork.proto;
using Microsoft.Extensions.Logging;
using InternalResult = HushNode.UrlMetadata.Models.UrlMetadataResult;
using ProtoResult = HushNetwork.proto.UrlMetadataResult;

namespace HushNode.UrlMetadata.gRPC;

/// <summary>
/// gRPC service for URL metadata operations.
/// Handles fetching Open Graph metadata for link previews.
/// </summary>
public class UrlMetadataGrpcService : HushUrlMetadata.HushUrlMetadataBase
{
    private readonly IOpenGraphParser _openGraphParser;
    private readonly IUrlBlocklist _urlBlocklist;
    private readonly IUrlMetadataCacheService _cacheService;
    private readonly IImageProcessor _imageProcessor;
    private readonly ILogger<UrlMetadataGrpcService> _logger;

    /// <summary>
    /// Maximum number of URLs allowed in a batch request.
    /// </summary>
    private const int MaxBatchSize = 10;

    public UrlMetadataGrpcService(
        IOpenGraphParser openGraphParser,
        IUrlBlocklist urlBlocklist,
        IUrlMetadataCacheService cacheService,
        IImageProcessor imageProcessor,
        ILogger<UrlMetadataGrpcService> logger)
    {
        _openGraphParser = openGraphParser;
        _urlBlocklist = urlBlocklist;
        _cacheService = cacheService;
        _imageProcessor = imageProcessor;
        _logger = logger;
    }

    /// <summary>
    /// Gets metadata for a single URL.
    /// </summary>
    public override async Task<GetUrlMetadataResponse> GetUrlMetadata(
        GetUrlMetadataRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation("[UrlMetadata] GetUrlMetadata called for URL: {Url}", request.Url);

        var result = await FetchMetadataAsync(request.Url, context.CancellationToken);

        _logger.LogInformation("[UrlMetadata] Result - Success: {Success}, Title: {Title}",
            result.Success, result.Title);

        return MapToResponse(result);
    }

    /// <summary>
    /// Gets metadata for multiple URLs (batch request).
    /// Maximum 10 URLs per request.
    /// </summary>
    public override async Task<GetUrlMetadataBatchResponse> GetUrlMetadataBatch(
        GetUrlMetadataBatchRequest request,
        ServerCallContext context)
    {
        _logger.LogDebug("GetUrlMetadataBatch called for {Count} URLs", request.Urls.Count);

        var response = new GetUrlMetadataBatchResponse();

        // Limit to MaxBatchSize URLs
        var urlsToProcess = request.Urls.Take(MaxBatchSize).ToList();

        // Process URLs in parallel for better performance
        var tasks = urlsToProcess.Select(url =>
            FetchMetadataAsync(url, context.CancellationToken));

        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
        {
            response.Results.Add(MapToBatchResult(result));
        }

        return response;
    }

    /// <summary>
    /// Fetches metadata for a single URL with caching and blocklist checking.
    /// </summary>
    private async Task<InternalResult> FetchMetadataAsync(string url, CancellationToken cancellationToken)
    {
        var domain = ExtractDomain(url);

        // Check blocklist first
        if (_urlBlocklist.IsBlocked(url))
        {
            _logger.LogDebug("URL blocked by blocklist: {Url}", url);
            return InternalResult.CreateFailure(url, domain, "URL is blocked");
        }

        // Use cache-aside pattern
        var result = await _cacheService.GetOrFetchAsync(
            url,
            async () => await FetchFreshMetadataAsync(url, domain, cancellationToken),
            cancellationToken);

        return result ?? InternalResult.CreateFailure(url, domain, "Failed to fetch metadata");
    }

    /// <summary>
    /// Fetches fresh metadata from the URL (bypassing cache).
    /// </summary>
    private async Task<InternalResult> FetchFreshMetadataAsync(
        string url,
        string domain,
        CancellationToken cancellationToken)
    {
        // Parse Open Graph metadata
        var ogResult = await _openGraphParser.ParseAsync(url, cancellationToken);

        if (!ogResult.Success)
        {
            return InternalResult.CreateFailure(url, domain, ogResult.ErrorMessage ?? "Failed to parse page");
        }

        // Process image if available
        string? imageBase64 = null;
        if (!string.IsNullOrWhiteSpace(ogResult.ImageUrl))
        {
            imageBase64 = await _imageProcessor.FetchAndProcessAsync(ogResult.ImageUrl, cancellationToken);
        }

        return InternalResult.CreateSuccess(
            url,
            domain,
            ogResult.Title,
            ogResult.Description,
            ogResult.ImageUrl,
            imageBase64);
    }

    /// <summary>
    /// Extracts the domain from a URL.
    /// </summary>
    private static string ExtractDomain(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host;
        }
        catch (UriFormatException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Maps internal result to gRPC single response.
    /// </summary>
    private static GetUrlMetadataResponse MapToResponse(InternalResult result)
    {
        return new GetUrlMetadataResponse
        {
            Success = result.Success,
            Title = result.Title ?? string.Empty,
            Description = result.Description ?? string.Empty,
            ImageUrl = result.ImageUrl ?? string.Empty,
            ImageBase64 = result.ImageBase64 ?? string.Empty,
            Domain = result.Domain,
            ErrorMessage = result.ErrorMessage ?? string.Empty
        };
    }

    /// <summary>
    /// Maps internal result to gRPC batch result.
    /// </summary>
    private static ProtoResult MapToBatchResult(InternalResult result)
    {
        return new ProtoResult
        {
            Url = result.Url,
            Success = result.Success,
            Title = result.Title ?? string.Empty,
            Description = result.Description ?? string.Empty,
            ImageUrl = result.ImageUrl ?? string.Empty,
            ImageBase64 = result.ImageBase64 ?? string.Empty,
            Domain = result.Domain,
            ErrorMessage = result.ErrorMessage ?? string.Empty
        };
    }
}
