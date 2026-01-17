using FluentAssertions;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.UrlMetadata.gRPC;
using HushNode.UrlMetadata.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using InternalResult = HushNode.UrlMetadata.Models.UrlMetadataResult;

namespace HushNode.UrlMetadata.Tests;

public class UrlMetadataGrpcServiceTests
{
    private readonly Mock<IOpenGraphParser> _parserMock;
    private readonly Mock<IUrlBlocklist> _blocklistMock;
    private readonly Mock<IUrlMetadataCacheService> _cacheServiceMock;
    private readonly Mock<IImageProcessor> _imageProcessorMock;
    private readonly Mock<ILogger<UrlMetadataGrpcService>> _loggerMock;
    private readonly UrlMetadataGrpcService _service;
    private readonly ServerCallContext _context;

    public UrlMetadataGrpcServiceTests()
    {
        _parserMock = new Mock<IOpenGraphParser>();
        _blocklistMock = new Mock<IUrlBlocklist>();
        _cacheServiceMock = new Mock<IUrlMetadataCacheService>();
        _imageProcessorMock = new Mock<IImageProcessor>();
        _loggerMock = new Mock<ILogger<UrlMetadataGrpcService>>();
        _context = new TestServerCallContext();

        _service = new UrlMetadataGrpcService(
            _parserMock.Object,
            _blocklistMock.Object,
            _cacheServiceMock.Object,
            _imageProcessorMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetUrlMetadata_ValidUrl_ReturnsMetadata()
    {
        // Arrange
        var url = "https://example.com";
        var expectedResult = InternalResult.CreateSuccess(
            url, "example.com", "Example Title", "Example Description",
            "https://example.com/image.jpg", "base64ImageData");

        _blocklistMock.Setup(b => b.IsBlocked(url)).Returns(false);
        _cacheServiceMock
            .Setup(c => c.GetOrFetchAsync(url, It.IsAny<Func<Task<InternalResult>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var request = new GetUrlMetadataRequest { Url = url };

        // Act
        var response = await _service.GetUrlMetadata(request, _context);

        // Assert
        response.Success.Should().BeTrue();
        response.Title.Should().Be("Example Title");
        response.Description.Should().Be("Example Description");
        response.ImageUrl.Should().Be("https://example.com/image.jpg");
        response.ImageBase64.Should().Be("base64ImageData");
        response.Domain.Should().Be("example.com");
        response.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUrlMetadata_BlockedUrl_ReturnsFailure()
    {
        // Arrange
        var url = "https://malware-distribution.example/page";

        _blocklistMock.Setup(b => b.IsBlocked(url)).Returns(true);

        var request = new GetUrlMetadataRequest { Url = url };

        // Act
        var response = await _service.GetUrlMetadata(request, _context);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Be("URL is blocked");
        _cacheServiceMock.Verify(c => c.GetOrFetchAsync(
            It.IsAny<string>(), It.IsAny<Func<Task<InternalResult>>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetUrlMetadata_FetchFailure_ReturnsFailure()
    {
        // Arrange
        var url = "https://example.com";
        var failedResult = InternalResult.CreateFailure(url, "example.com", "HTTP 404: Not Found");

        _blocklistMock.Setup(b => b.IsBlocked(url)).Returns(false);
        _cacheServiceMock
            .Setup(c => c.GetOrFetchAsync(url, It.IsAny<Func<Task<InternalResult>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedResult);

        var request = new GetUrlMetadataRequest { Url = url };

        // Act
        var response = await _service.GetUrlMetadata(request, _context);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Be("HTTP 404: Not Found");
    }

    [Fact]
    public async Task GetUrlMetadataBatch_MultipleUrls_ReturnsResultsInOrder()
    {
        // Arrange
        var urls = new[] { "https://example1.com", "https://example2.com", "https://example3.com" };

        _blocklistMock.Setup(b => b.IsBlocked(It.IsAny<string>())).Returns(false);

        for (int i = 0; i < urls.Length; i++)
        {
            var url = urls[i];
            var result = InternalResult.CreateSuccess(url, $"example{i + 1}.com", $"Title {i + 1}", null, null, null);
            _cacheServiceMock
                .Setup(c => c.GetOrFetchAsync(url, It.IsAny<Func<Task<InternalResult>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);
        }

        var request = new GetUrlMetadataBatchRequest();
        request.Urls.AddRange(urls);

        // Act
        var response = await _service.GetUrlMetadataBatch(request, _context);

        // Assert
        response.Results.Should().HaveCount(3);
        response.Results[0].Url.Should().Be("https://example1.com");
        response.Results[0].Title.Should().Be("Title 1");
        response.Results[1].Url.Should().Be("https://example2.com");
        response.Results[1].Title.Should().Be("Title 2");
        response.Results[2].Url.Should().Be("https://example3.com");
        response.Results[2].Title.Should().Be("Title 3");
    }

    [Fact]
    public async Task GetUrlMetadataBatch_ExceedsLimit_OnlyProcessesFirst10()
    {
        // Arrange
        var urls = Enumerable.Range(1, 15).Select(i => $"https://example{i}.com").ToList();

        _blocklistMock.Setup(b => b.IsBlocked(It.IsAny<string>())).Returns(false);
        _cacheServiceMock
            .Setup(c => c.GetOrFetchAsync(It.IsAny<string>(), It.IsAny<Func<Task<InternalResult>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string url, Func<Task<InternalResult>> fetchFunc, CancellationToken ct) =>
                InternalResult.CreateSuccess(url, "example.com", "Title", null, null, null));

        var request = new GetUrlMetadataBatchRequest();
        request.Urls.AddRange(urls);

        // Act
        var response = await _service.GetUrlMetadataBatch(request, _context);

        // Assert
        response.Results.Should().HaveCount(10);
        response.Results.Last().Url.Should().Be("https://example10.com");
    }

    [Fact]
    public async Task GetUrlMetadataBatch_MixedResults_ReturnsCorrectStatus()
    {
        // Arrange
        var validUrl = "https://valid.com";
        var blockedUrl = "https://malware-distribution.example";
        var failedUrl = "https://notfound.com";

        _blocklistMock.Setup(b => b.IsBlocked(validUrl)).Returns(false);
        _blocklistMock.Setup(b => b.IsBlocked(blockedUrl)).Returns(true);
        _blocklistMock.Setup(b => b.IsBlocked(failedUrl)).Returns(false);

        _cacheServiceMock
            .Setup(c => c.GetOrFetchAsync(validUrl, It.IsAny<Func<Task<InternalResult>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InternalResult.CreateSuccess(validUrl, "valid.com", "Valid Title", null, null, null));

        _cacheServiceMock
            .Setup(c => c.GetOrFetchAsync(failedUrl, It.IsAny<Func<Task<InternalResult>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InternalResult.CreateFailure(failedUrl, "notfound.com", "HTTP 404"));

        var request = new GetUrlMetadataBatchRequest();
        request.Urls.AddRange(new[] { validUrl, blockedUrl, failedUrl });

        // Act
        var response = await _service.GetUrlMetadataBatch(request, _context);

        // Assert
        response.Results.Should().HaveCount(3);

        response.Results[0].Success.Should().BeTrue();
        response.Results[0].Title.Should().Be("Valid Title");

        response.Results[1].Success.Should().BeFalse();
        response.Results[1].ErrorMessage.Should().Be("URL is blocked");

        response.Results[2].Success.Should().BeFalse();
        response.Results[2].ErrorMessage.Should().Be("HTTP 404");
    }

    [Fact]
    public async Task GetUrlMetadata_CacheReturnsNull_ReturnsFailure()
    {
        // Arrange
        var url = "https://example.com";

        _blocklistMock.Setup(b => b.IsBlocked(url)).Returns(false);
        _cacheServiceMock
            .Setup(c => c.GetOrFetchAsync(url, It.IsAny<Func<Task<InternalResult>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InternalResult?)null);

        var request = new GetUrlMetadataRequest { Url = url };

        // Act
        var response = await _service.GetUrlMetadata(request, _context);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Be("Failed to fetch metadata");
    }
}

/// <summary>
/// Minimal implementation of ServerCallContext for testing.
/// </summary>
public class TestServerCallContext : ServerCallContext
{
    protected override string MethodCore => "TestMethod";
    protected override string HostCore => "TestHost";
    protected override string PeerCore => "TestPeer";
    protected override DateTime DeadlineCore => DateTime.MaxValue;
    protected override Metadata RequestHeadersCore => new Metadata();
    protected override CancellationToken CancellationTokenCore => CancellationToken.None;
    protected override Metadata ResponseTrailersCore => new Metadata();
    protected override Status StatusCore { get; set; }
    protected override WriteOptions? WriteOptionsCore { get; set; }
    protected override AuthContext AuthContextCore => new AuthContext(null, new Dictionary<string, List<AuthProperty>>());

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
        => throw new NotImplementedException();

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
        => Task.CompletedTask;
}
