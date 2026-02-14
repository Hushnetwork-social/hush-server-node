using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HushNode.PushNotifications.Exceptions;
using HushNode.PushNotifications.Models;
using HushNode.PushNotifications.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace HushNode.PushNotifications.Tests;

/// <summary>
/// Tests for ApnsProvider.
/// Each test follows AAA pattern with isolated setup.
/// Uses MockHttpMessageHandler for HTTP interaction testing.
/// </summary>
public class ApnsProviderTests
{
    private const string TestKeyId = "TESTKEY123";
    private const string TestTeamId = "TESTTEAM99";
    private const string TestBundleId = "social.hushnetwork.feeds";
    private const string TestToken = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    #region Constructor Tests (Task 3.2)

    [Fact]
    public void Constructor_WithDisabledConfig_CreatesDisabledProvider()
    {
        // Arrange
        var (provider, logger) = CreateDisabledProvider();

        // Assert
        provider.Should().NotBeNull();
        VerifyLogContains(logger, LogLevel.Information, "Enabled: False");
    }

    [Fact]
    public void Constructor_WithDisabledConfig_DoesNotLogWarning()
    {
        // Arrange
        var (_, logger) = CreateDisabledProvider();

        // Assert
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void Constructor_WithMissingKeyId_DisablesWithWarning()
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.KeyId = null;
        var logger = new Mock<ILogger<ApnsProvider>>();

        // Act
        var provider = CreateProviderWithSettings(settings, logger);

        // Assert
        VerifyLogContains(logger, LogLevel.Warning, "KeyId");
        VerifyLogContains(logger, LogLevel.Information, "Enabled: False");
    }

    [Fact]
    public void Constructor_WithEmptyKeyId_DisablesWithWarning()
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.KeyId = "";
        var logger = new Mock<ILogger<ApnsProvider>>();

        // Act
        var provider = CreateProviderWithSettings(settings, logger);

        // Assert
        VerifyLogContains(logger, LogLevel.Warning, "KeyId");
    }

    [Fact]
    public void Constructor_WithMissingTeamId_DisablesWithWarning()
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.TeamId = null;
        var logger = new Mock<ILogger<ApnsProvider>>();

        // Act
        var provider = CreateProviderWithSettings(settings, logger);

        // Assert
        VerifyLogContains(logger, LogLevel.Warning, "TeamId");
    }

    [Fact]
    public void Constructor_WithMissingBundleId_DisablesWithWarning()
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.BundleId = null;
        var logger = new Mock<ILogger<ApnsProvider>>();

        // Act
        var provider = CreateProviderWithSettings(settings, logger);

        // Assert
        VerifyLogContains(logger, LogLevel.Warning, "BundleId");
    }

    [Fact]
    public void Constructor_WithMissingPrivateKeyPath_DisablesWithWarning()
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.PrivateKeyPath = null;
        var logger = new Mock<ILogger<ApnsProvider>>();

        // Act
        var provider = CreateProviderWithSettings(settings, logger);

        // Assert
        VerifyLogContains(logger, LogLevel.Warning, "PrivateKeyPath");
    }

    [Fact]
    public void Constructor_WithNonExistentKeyFile_DisablesWithWarning()
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.PrivateKeyPath = "/nonexistent/key.p8";
        var logger = new Mock<ILogger<ApnsProvider>>();

        // Act
        var provider = new ApnsProvider(
            Options.Create(settings),
            new Mock<IHttpClientFactory>().Object,
            logger.Object);

        // Assert
        VerifyLogContains(logger, LogLevel.Warning, "/nonexistent/key.p8");
        VerifyLogContains(logger, LogLevel.Information, "Enabled: False");
    }

    [Fact]
    public async Task SendAsync_WhenDisabled_ReturnsImmediately()
    {
        // Arrange
        var (provider, logger) = CreateDisabledProvider();
        var payload = new PushPayload("Test", "Body");

        // Act
        await provider.SendAsync(TestToken, payload);

        // Assert
        VerifyLogContains(logger, LogLevel.Debug, "disabled");
    }

    [Fact]
    public void Constructor_WithValidConfig_CreatesEnabledProvider()
    {
        // Arrange
        var (provider, logger, _) = CreateEnabledProvider();

        // Assert
        provider.Should().NotBeNull();
        VerifyLogContains(logger, LogLevel.Information, "Enabled: True");
    }

    #endregion

    #region JWT Token Generation Tests (Task 3.4)

    [Fact]
    public async Task GenerateJwt_ReturnsTokenWithThreeParts()
    {
        // Arrange
        var (provider, _, _) = CreateEnabledProvider();

        // Act
        var jwt = await provider.GetOrGenerateJwtAsync();

        // Assert
        jwt.Split('.').Should().HaveCount(3);
    }

    [Fact]
    public async Task GenerateJwt_HeaderContainsES256AndKid()
    {
        // Arrange
        var (provider, _, _) = CreateEnabledProvider();

        // Act
        var jwt = await provider.GetOrGenerateJwtAsync();

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);
        token.Header.Alg.Should().Be("ES256");
        token.Header.Kid.Should().Be(TestKeyId);
    }

    [Fact]
    public async Task GenerateJwt_PayloadContainsIssAndIat()
    {
        // Arrange
        var (provider, _, _) = CreateEnabledProvider();

        // Act
        var jwt = await provider.GetOrGenerateJwtAsync();

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);
        token.Payload["iss"].Should().Be(TestTeamId);
        token.Payload.ContainsKey("iat").Should().BeTrue();
        var iat = Convert.ToInt64(token.Payload["iat"]);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        iat.Should().BeCloseTo(now, 5);
    }

    [Fact]
    public async Task GenerateJwt_ReturnsCachedTokenWithin50Minutes()
    {
        // Arrange
        var (provider, _, _) = CreateEnabledProvider();

        // Act
        var jwt1 = await provider.GetOrGenerateJwtAsync();
        var jwt2 = await provider.GetOrGenerateJwtAsync();

        // Assert
        jwt2.Should().Be(jwt1);
    }

    [Fact]
    public async Task GenerateJwt_ConcurrentRequests_ReturnSameToken()
    {
        // Arrange
        var (provider, _, _) = CreateEnabledProvider();

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => provider.GetOrGenerateJwtAsync())
            .ToArray();
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Distinct().Should().HaveCount(1);
    }

    #endregion

    #region SendAsync Success Path Tests (Task 3.6)

    [Fact]
    public async Task SendAsync_SendsPostToCorrectSandboxUrl()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var (provider, _, _) = CreateEnabledProvider(handler, useSandbox: true);
        var payload = new PushPayload("Alice", "sent a message");

        // Act
        await provider.SendAsync(TestToken, payload);

        // Assert
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString()
            .Should().Be($"https://api.development.push.apple.com/3/device/{TestToken}");
    }

    [Fact]
    public async Task SendAsync_SendsPostToCorrectProductionUrl()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var (provider, _, _) = CreateEnabledProvider(handler, useSandbox: false);
        var payload = new PushPayload("Alice", "sent a message");

        // Act
        await provider.SendAsync(TestToken, payload);

        // Assert
        handler.LastRequest!.RequestUri!.Host.Should().Be("api.push.apple.com");
    }

    [Fact]
    public async Task SendAsync_IncludesBearerJwtInAuthHeader()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var (provider, _, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Alice", "sent a message");

        // Act
        await provider.SendAsync(TestToken, payload);

        // Assert
        handler.LastRequest!.Headers.Authorization.Should().NotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("bearer");
        handler.LastRequest.Headers.Authorization.Parameter!.Split('.').Should().HaveCount(3);
    }

    [Fact]
    public async Task SendAsync_IncludesApnsHeaders()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var (provider, _, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Alice", "sent a message");

        // Act
        await provider.SendAsync(TestToken, payload);

        // Assert
        handler.LastRequest!.Headers.GetValues("apns-topic").First().Should().Be(TestBundleId);
        handler.LastRequest.Headers.GetValues("apns-push-type").First().Should().Be("alert");
        handler.LastRequest.Headers.GetValues("apns-priority").First().Should().Be("10");
    }

    [Fact]
    public async Task SendAsync_PayloadHasCorrectJsonStructure()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var (provider, _, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Alice", "sent a new message", "feed-123");

        // Act
        await provider.SendAsync(TestToken, payload);

        // Assert
        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("aps").GetProperty("alert").GetProperty("title").GetString().Should().Be("Alice");
        root.GetProperty("aps").GetProperty("alert").GetProperty("body").GetString().Should().Be("sent a new message");
        root.GetProperty("aps").GetProperty("sound").GetString().Should().Be("default");
        root.GetProperty("aps").GetProperty("badge").GetInt32().Should().Be(1);
        root.GetProperty("feedId").GetString().Should().Be("feed-123");
    }

    [Fact]
    public async Task SendAsync_WithoutFeedId_OmitsFeedIdFromPayload()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var (provider, _, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Alice", "sent a message");

        // Act
        await provider.SendAsync(TestToken, payload);

        // Assert
        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("feedId", out _).Should().BeFalse();
    }

    #endregion

    #region Error Handling Tests (Task 3.8)

    [Fact]
    public async Task SendAsync_400BadDeviceToken_ThrowsInvalidTokenException()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.BadRequest,
            "{\"reason\": \"BadDeviceToken\"}");
        var (provider, _, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Test", "Body");

        // Act
        var act = async () => await provider.SendAsync(TestToken, payload);

        // Assert
        await act.Should().ThrowAsync<InvalidTokenException>();
    }

    [Fact]
    public async Task SendAsync_403ThenSuccess_RetriesWithNewJwt()
    {
        // Arrange
        var handler = new SequentialMockHttpMessageHandler(
            (HttpStatusCode.Forbidden, "{\"reason\": \"ExpiredProviderToken\"}"),
            (HttpStatusCode.OK, ""));
        var (provider, _, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Test", "Body");

        // Act
        await provider.SendAsync(TestToken, payload);

        // Assert
        handler.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_403ThenSuccess_UseDifferentJwtOnRetry()
    {
        // Arrange
        var handler = new SequentialMockHttpMessageHandler(
            (HttpStatusCode.Forbidden, "{\"reason\": \"ExpiredProviderToken\"}"),
            (HttpStatusCode.OK, ""));
        var (provider, _, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Test", "Body");

        // Act
        await provider.SendAsync(TestToken, payload);

        // Assert
        handler.Requests.Should().HaveCount(2);
        var jwt1 = handler.Requests[0].Headers.Authorization!.Parameter;
        var jwt2 = handler.Requests[1].Headers.Authorization!.Parameter;
        jwt2.Should().NotBe(jwt1);
    }

    [Fact]
    public async Task SendAsync_Double403_DoesNotThrowInvalidTokenException()
    {
        // Arrange
        var handler = new SequentialMockHttpMessageHandler(
            (HttpStatusCode.Forbidden, "{\"reason\": \"ExpiredProviderToken\"}"),
            (HttpStatusCode.Forbidden, "{\"reason\": \"ExpiredProviderToken\"}"));
        var (provider, logger, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Test", "Body");

        // Act
        var act = async () => await provider.SendAsync(TestToken, payload);

        // Assert
        await act.Should().NotThrowAsync<InvalidTokenException>();
        handler.RequestCount.Should().Be(2);
        VerifyLogContains(logger, LogLevel.Error, "configuration issue");
    }

    [Fact]
    public async Task SendAsync_410Unregistered_ThrowsInvalidTokenException()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.Gone,
            "{\"reason\": \"Unregistered\", \"timestamp\": 1707753600}");
        var (provider, _, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Test", "Body");

        // Act
        var act = async () => await provider.SendAsync(TestToken, payload);

        // Assert
        await act.Should().ThrowAsync<InvalidTokenException>();
    }

    [Fact]
    public async Task SendAsync_410WithTimestamp_LogsTimestamp()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.Gone,
            "{\"reason\": \"Unregistered\", \"timestamp\": 1707753600}");
        var (provider, logger, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Test", "Body");

        // Act
        try { await provider.SendAsync(TestToken, payload); } catch (InvalidTokenException) { }

        // Assert
        VerifyLogContains(logger, LogLevel.Information, "1707753600");
    }

    [Fact]
    public async Task SendAsync_500ServerError_DoesNotThrowInvalidTokenException()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.InternalServerError,
            "{\"reason\": \"InternalServerError\"}");
        var (provider, logger, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Test", "Body");

        // Act
        var act = async () => await provider.SendAsync(TestToken, payload);

        // Assert
        await act.Should().NotThrowAsync<InvalidTokenException>();
        VerifyLogContains(logger, LogLevel.Error, "500");
    }

    [Fact]
    public async Task SendAsync_503ServiceUnavailable_DoesNotThrowInvalidTokenException()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.ServiceUnavailable,
            "{\"reason\": \"ServiceUnavailable\"}");
        var (provider, _, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Test", "Body");

        // Act
        var act = async () => await provider.SendAsync(TestToken, payload);

        // Assert
        await act.Should().NotThrowAsync<InvalidTokenException>();
    }

    [Fact]
    public async Task SendAsync_MalformedJsonErrorBody_HandledGracefully()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.BadRequest,
            "not valid json");
        var (provider, logger, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Test", "Body");

        // Act
        var act = async () => await provider.SendAsync(TestToken, payload);

        // Assert - should not crash (reason will be null, logged as error)
        await act.Should().NotThrowAsync();
        VerifyLogContains(logger, LogLevel.Error, "400");
    }

    #endregion

    #region Edge Case Tests (Task 5.1 - FEAT-064 Phase 5)

    [Fact]
    public async Task SendAsync_WithEmptyToken_ApnsRejects_ThrowsInvalidTokenException()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.BadRequest,
            "{\"reason\": \"BadDeviceToken\"}");
        var (provider, _, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Test", "Body");

        // Act
        var act = async () => await provider.SendAsync("", payload);

        // Assert
        await act.Should().ThrowAsync<InvalidTokenException>();
        handler.LastRequest!.RequestUri!.ToString()
            .Should().EndWith("/3/device/");
    }

    [Fact]
    public async Task SendAsync_WithLongToken_TokenInUrlPath()
    {
        // Arrange
        var longToken = new string('a', 200);
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var (provider, _, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Test", "Body");

        // Act
        await provider.SendAsync(longToken, payload);

        // Assert
        handler.LastRequest!.RequestUri!.ToString()
            .Should().Contain($"/3/device/{longToken}");
    }

    [Fact]
    public void BuildApnsPayload_WithEmojiAndUnicode_CorrectlyEncoded()
    {
        // Arrange
        var payload = new PushPayload("Alice \ud83d\udc4b", "Ol\u00e1 mundo! \ud83c\udf0d");

        // Act
        var json = ApnsProvider.BuildApnsPayload(payload);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var alert = doc.RootElement.GetProperty("aps").GetProperty("alert");
        alert.GetProperty("title").GetString().Should().Be("Alice \ud83d\udc4b");
        alert.GetProperty("body").GetString().Should().Be("Ol\u00e1 mundo! \ud83c\udf0d");
    }

    [Fact]
    public async Task SendAsync_ConcurrentCalls_ShareJwtButMakeSeparateRequests()
    {
        // Arrange
        var handler = new CountingMockHttpMessageHandler(HttpStatusCode.OK);
        var (provider, _, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Test", "Body");

        // Act
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => provider.SendAsync(TestToken, payload))
            .ToArray();
        await Task.WhenAll(tasks);

        // Assert - 5 HTTP requests sent, all using same JWT
        handler.RequestCount.Should().Be(5);
        handler.AuthTokens.Distinct().Should().HaveCount(1);
    }

    [Fact]
    public async Task SendAsync_400BadDeviceToken_ExceptionMessageContainsBadDeviceToken()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.BadRequest,
            "{\"reason\": \"BadDeviceToken\"}");
        var (provider, _, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Test", "Body");

        // Act
        var act = async () => await provider.SendAsync(TestToken, payload);

        // Assert
        var ex = await act.Should().ThrowAsync<InvalidTokenException>();
        ex.Which.Message.Should().Contain("BadDeviceToken");
    }

    [Fact]
    public async Task SendAsync_410Unregistered_ExceptionMessageContainsUnregistered()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.Gone,
            "{\"reason\": \"Unregistered\"}");
        var (provider, _, _) = CreateEnabledProvider(handler);
        var payload = new PushPayload("Test", "Body");

        // Act
        var act = async () => await provider.SendAsync(TestToken, payload);

        // Assert
        var ex = await act.Should().ThrowAsync<InvalidTokenException>();
        ex.Which.Message.Should().Contain("unregistered", Exactly.Once(), "message should mention unregistered");
    }

    #endregion

    #region Payload Construction Tests

    [Fact]
    public void BuildApnsPayload_WithFeedId_IncludesFeedIdAtRoot()
    {
        // Arrange
        var payload = new PushPayload("Alice", "sent a message", "feed-123");

        // Act
        var json = ApnsProvider.BuildApnsPayload(payload);

        // Assert
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("feedId").GetString().Should().Be("feed-123");
    }

    [Fact]
    public void BuildApnsPayload_WithoutFeedId_OmitsFeedId()
    {
        // Arrange
        var payload = new PushPayload("Alice", "sent a message");

        // Act
        var json = ApnsProvider.BuildApnsPayload(payload);

        // Assert
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("feedId", out _).Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private static (ApnsProvider provider, Mock<ILogger<ApnsProvider>> logger) CreateDisabledProvider()
    {
        var settings = new ApnsSettings { Enabled = false };
        var logger = new Mock<ILogger<ApnsProvider>>();
        var httpFactory = new Mock<IHttpClientFactory>();

        var provider = new ApnsProvider(
            Options.Create(settings),
            httpFactory.Object,
            logger.Object);

        return (provider, logger);
    }

    private static ApnsProvider CreateProviderWithSettings(ApnsSettings settings, Mock<ILogger<ApnsProvider>> logger)
    {
        return new ApnsProvider(
            Options.Create(settings),
            new Mock<IHttpClientFactory>().Object,
            logger.Object);
    }

    private static (ApnsProvider provider, Mock<ILogger<ApnsProvider>> logger, ECDsa key) CreateEnabledProvider(
        HttpMessageHandler? handler = null,
        bool useSandbox = true)
    {
        var ecdsaKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var logger = new Mock<ILogger<ApnsProvider>>();

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory
            .Setup(f => f.CreateClient("ApnsClient"))
            .Returns(new HttpClient(handler ?? new MockHttpMessageHandler(HttpStatusCode.OK)));

        var settings = new ApnsSettings
        {
            Enabled = true,
            KeyId = TestKeyId,
            TeamId = TestTeamId,
            BundleId = TestBundleId,
            PrivateKeyPath = "",
            UseSandbox = useSandbox
        };

        var provider = new ApnsProvider(settings, ecdsaKey, httpFactory.Object, logger.Object);
        return (provider, logger, ecdsaKey);
    }

    private static ApnsSettings CreateValidSettings()
    {
        return new ApnsSettings
        {
            Enabled = true,
            KeyId = TestKeyId,
            TeamId = TestTeamId,
            BundleId = TestBundleId,
            PrivateKeyPath = "/some/path/key.p8",
            UseSandbox = true
        };
    }

    private static void VerifyLogContains(Mock<ILogger<ApnsProvider>> logger, LogLevel level, string message)
    {
        logger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains(message)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Mock HTTP Handlers

    /// <summary>
    /// Simple mock HTTP handler that returns a fixed response.
    /// </summary>
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public HttpRequestMessage? LastRequest { get; private set; }

        public MockHttpMessageHandler(HttpStatusCode statusCode, string responseBody = "")
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>
    /// Mock HTTP handler that returns different responses for sequential calls.
    /// Used for testing 403 retry logic.
    /// </summary>
    private class SequentialMockHttpMessageHandler : HttpMessageHandler
    {
        private readonly List<(HttpStatusCode statusCode, string body)> _responses;
        private int _callIndex;

        public List<HttpRequestMessage> Requests { get; } = new();
        public int RequestCount => Requests.Count;

        public SequentialMockHttpMessageHandler(params (HttpStatusCode, string)[] responses)
        {
            _responses = responses.ToList();
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Clone the request to preserve headers (original gets disposed)
            var cloned = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                cloned.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            Requests.Add(cloned);

            var index = Math.Min(_callIndex, _responses.Count - 1);
            var (statusCode, body) = _responses[index];
            _callIndex++;

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>
    /// Thread-safe mock HTTP handler that counts requests and captures JWT tokens.
    /// Used for testing concurrent SendAsync calls.
    /// </summary>
    private class CountingMockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private int _requestCount;

        public List<string?> AuthTokens { get; } = new();
        public int RequestCount => _requestCount;

        public CountingMockHttpMessageHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            lock (AuthTokens)
            {
                AuthTokens.Add(request.Headers.Authorization?.Parameter);
            }

            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent("", Encoding.UTF8, "application/json")
            });
        }
    }

    #endregion
}
