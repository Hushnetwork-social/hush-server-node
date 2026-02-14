using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HushNode.PushNotifications.Exceptions;
using HushNode.PushNotifications.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HushNode.PushNotifications.Providers;

/// <summary>
/// Apple Push Notification service (APNs) provider for sending push notifications to iOS devices.
/// Uses JWT token-based authentication with ECDSA P-256 (ES256) signing.
/// Registered as Singleton - holds ECDsa key and JWT cache for the application lifetime.
/// </summary>
public class ApnsProvider : IApnsProvider
{
    private readonly ILogger<ApnsProvider> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly bool _isEnabled;
    private readonly string _keyId;
    private readonly string _teamId;
    private readonly string _bundleId;
    private readonly bool _useSandbox;
    private readonly ECDsa? _ecdsaKey;

    private const string SandboxEndpoint = "https://api.development.push.apple.com";
    private const string ProductionEndpoint = "https://api.push.apple.com";
    private const string HttpClientName = "ApnsClient";
    private static readonly TimeSpan JwtValidityDuration = TimeSpan.FromMinutes(50);

    private readonly SemaphoreSlim _jwtSemaphore = new(1, 1);
    private string? _cachedJwt;
    private DateTime _cachedJwtGeneratedAt;

    /// <summary>
    /// Initializes a new instance of <see cref="ApnsProvider"/>.
    /// Validates configuration and loads the ECDsa private key from the .p8 file.
    /// </summary>
    public ApnsProvider(
        IOptions<ApnsSettings> options,
        IHttpClientFactory httpClientFactory,
        ILogger<ApnsProvider> logger)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;

        var settings = options.Value;
        _keyId = settings.KeyId ?? string.Empty;
        _teamId = settings.TeamId ?? string.Empty;
        _bundleId = settings.BundleId ?? string.Empty;
        _useSandbox = settings.UseSandbox;

        if (!settings.Enabled)
        {
            _isEnabled = false;
            _logger.LogInformation("ApnsProvider created. Enabled: {IsEnabled}", _isEnabled);
            return;
        }

        // Validate required fields
        if (string.IsNullOrWhiteSpace(settings.KeyId))
        {
            _logger.LogWarning("APNs enabled but KeyId is not configured. APNs will be disabled.");
            _isEnabled = false;
            _logger.LogInformation("ApnsProvider created. Enabled: {IsEnabled}", _isEnabled);
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.TeamId))
        {
            _logger.LogWarning("APNs enabled but TeamId is not configured. APNs will be disabled.");
            _isEnabled = false;
            _logger.LogInformation("ApnsProvider created. Enabled: {IsEnabled}", _isEnabled);
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.BundleId))
        {
            _logger.LogWarning("APNs enabled but BundleId is not configured. APNs will be disabled.");
            _isEnabled = false;
            _logger.LogInformation("ApnsProvider created. Enabled: {IsEnabled}", _isEnabled);
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.PrivateKeyPath))
        {
            _logger.LogWarning("APNs enabled but PrivateKeyPath is not configured. APNs will be disabled.");
            _isEnabled = false;
            _logger.LogInformation("ApnsProvider created. Enabled: {IsEnabled}", _isEnabled);
            return;
        }

        // Load ECDsa key from .p8 file
        try
        {
            if (!File.Exists(settings.PrivateKeyPath))
            {
                _logger.LogWarning("APNs private key file not found: {Path}. APNs will be disabled.", settings.PrivateKeyPath);
                _isEnabled = false;
                _logger.LogInformation("ApnsProvider created. Enabled: {IsEnabled}", _isEnabled);
                return;
            }

            var keyContent = File.ReadAllText(settings.PrivateKeyPath);
            _ecdsaKey = LoadEcdsaKey(keyContent);
            _isEnabled = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load APNs private key from {Path}. APNs will be disabled.", settings.PrivateKeyPath);
            _isEnabled = false;
        }

        _logger.LogInformation("ApnsProvider created. Enabled: {IsEnabled}", _isEnabled);
    }

    /// <summary>
    /// Internal constructor for testing - allows injecting an ECDsa key directly.
    /// </summary>
    internal ApnsProvider(
        ApnsSettings settings,
        ECDsa ecdsaKey,
        IHttpClientFactory httpClientFactory,
        ILogger<ApnsProvider> logger)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _keyId = settings.KeyId ?? string.Empty;
        _teamId = settings.TeamId ?? string.Empty;
        _bundleId = settings.BundleId ?? string.Empty;
        _useSandbox = settings.UseSandbox;
        _ecdsaKey = ecdsaKey;
        _isEnabled = settings.Enabled;

        _logger.LogInformation("ApnsProvider created. Enabled: {IsEnabled}", _isEnabled);
    }

    /// <inheritdoc />
    public async Task SendAsync(string apnsToken, PushPayload payload)
    {
        if (!_isEnabled)
        {
            _logger.LogDebug("APNs is disabled, skipping push notification");
            return;
        }

        var jwt = await GetOrGenerateJwtAsync();
        var baseUrl = _useSandbox ? SandboxEndpoint : ProductionEndpoint;
        var url = $"{baseUrl}/3/device/{apnsToken}";

        var jsonPayload = BuildApnsPayload(payload);

        var response = await SendRequestAsync(url, jwt, jsonPayload);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogDebug(
                "APNs push sent successfully. Token: {Token}",
                TruncateToken(apnsToken));
            return;
        }

        await HandleErrorResponseAsync(response, apnsToken, payload);
    }

    private async Task<HttpResponseMessage> SendRequestAsync(string url, string jwt, string jsonPayload)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", jwt);
        request.Headers.TryAddWithoutValidation("apns-topic", _bundleId);
        request.Headers.TryAddWithoutValidation("apns-push-type", "alert");
        request.Headers.TryAddWithoutValidation("apns-priority", "10");
        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        return await client.SendAsync(request);
    }

    private async Task HandleErrorResponseAsync(HttpResponseMessage response, string apnsToken, PushPayload payload)
    {
        var statusCode = (int)response.StatusCode;
        var responseBody = await response.Content.ReadAsStringAsync();
        var errorReason = ParseErrorReason(responseBody);

        switch (statusCode)
        {
            case 400 when errorReason == "BadDeviceToken":
                _logger.LogWarning(
                    "APNs rejected token: BadDeviceToken. Token: {Token}",
                    TruncateToken(apnsToken));
                throw new InvalidTokenException($"APNs device token is invalid (BadDeviceToken)");

            case 403:
                await Handle403RetryAsync(apnsToken, payload, errorReason);
                break;

            case 410:
                var timestamp = ParseErrorTimestamp(responseBody);
                _logger.LogInformation(
                    "APNs device unregistered. Token: {Token}, Timestamp: {Timestamp}",
                    TruncateToken(apnsToken),
                    timestamp);
                throw new InvalidTokenException($"APNs device token is unregistered (410)");

            default:
                _logger.LogError(
                    "APNs error. StatusCode: {StatusCode}, Reason: {Reason}, Token: {Token}",
                    statusCode,
                    errorReason ?? "unknown",
                    TruncateToken(apnsToken));
                break;
        }
    }

    private async Task Handle403RetryAsync(string apnsToken, PushPayload payload, string? errorReason)
    {
        _logger.LogWarning(
            "APNs 403 received (Reason: {Reason}). Refreshing JWT and retrying.",
            errorReason ?? "unknown");

        // Invalidate cached JWT
        await _jwtSemaphore.WaitAsync();
        try
        {
            _cachedJwt = null;
        }
        finally
        {
            _jwtSemaphore.Release();
        }

        // Retry with new JWT
        var newJwt = await GetOrGenerateJwtAsync();
        var baseUrl = _useSandbox ? SandboxEndpoint : ProductionEndpoint;
        var url = $"{baseUrl}/3/device/{apnsToken}";
        var jsonPayload = BuildApnsPayload(payload);

        var retryResponse = await SendRequestAsync(url, newJwt, jsonPayload);

        if (retryResponse.IsSuccessStatusCode)
        {
            _logger.LogInformation("APNs JWT refreshed successfully, retry succeeded.");
            return;
        }

        var retryStatusCode = (int)retryResponse.StatusCode;
        var retryBody = await retryResponse.Content.ReadAsStringAsync();
        var retryReason = ParseErrorReason(retryBody);

        if (retryStatusCode == 403)
        {
            _logger.LogError(
                "APNs JWT rejected after refresh, possible configuration issue. Reason: {Reason}",
                retryReason ?? "unknown");
            return;
        }

        // Handle other error codes on retry
        if (retryStatusCode == 400 && retryReason == "BadDeviceToken")
        {
            throw new InvalidTokenException("APNs device token is invalid (BadDeviceToken)");
        }

        if (retryStatusCode == 410)
        {
            throw new InvalidTokenException("APNs device token is unregistered (410)");
        }

        _logger.LogError(
            "APNs error on retry. StatusCode: {StatusCode}, Reason: {Reason}",
            retryStatusCode,
            retryReason ?? "unknown");
    }

    internal async Task<string> GetOrGenerateJwtAsync()
    {
        // Fast path: cached token is still valid
        if (_cachedJwt != null && DateTime.UtcNow - _cachedJwtGeneratedAt < JwtValidityDuration)
        {
            return _cachedJwt;
        }

        await _jwtSemaphore.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_cachedJwt != null && DateTime.UtcNow - _cachedJwtGeneratedAt < JwtValidityDuration)
            {
                return _cachedJwt;
            }

            _cachedJwt = GenerateJwt();
            _cachedJwtGeneratedAt = DateTime.UtcNow;
            return _cachedJwt;
        }
        finally
        {
            _jwtSemaphore.Release();
        }
    }

    private string GenerateJwt()
    {
        var securityKey = new ECDsaSecurityKey(_ecdsaKey);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);

        var header = new JwtHeader(credentials);
        header["kid"] = _keyId;

        var now = DateTimeOffset.UtcNow;
        var payload = new JwtPayload
        {
            { "iss", _teamId },
            { "iat", now.ToUnixTimeSeconds() }
        };

        var token = new JwtSecurityToken(header, payload);
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(token);
    }

    internal static string BuildApnsPayload(PushPayload payload)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();

        // aps object
        writer.WriteStartObject("aps");
        writer.WriteStartObject("alert");
        writer.WriteString("title", payload.Title);
        writer.WriteString("body", payload.Body);
        writer.WriteEndObject(); // alert
        writer.WriteString("sound", "default");
        writer.WriteNumber("badge", 1);
        writer.WriteEndObject(); // aps

        // feedId at root level (for deep-linking)
        if (!string.IsNullOrEmpty(payload.FeedId))
        {
            writer.WriteString("feedId", payload.FeedId);
        }

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static ECDsa LoadEcdsaKey(string pemContent)
    {
        // Strip PEM headers if present
        var base64 = pemContent
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\r", "")
            .Replace("\n", "")
            .Trim();

        var keyBytes = Convert.FromBase64String(base64);
        var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(keyBytes, out _);
        return ecdsa;
    }

    private static string? ParseErrorReason(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("reason", out var reason))
            {
                return reason.GetString();
            }
        }
        catch (JsonException)
        {
            // Malformed JSON - return null
        }

        return null;
    }

    private static long? ParseErrorTimestamp(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("timestamp", out var timestamp))
            {
                return timestamp.GetInt64();
            }
        }
        catch (JsonException)
        {
            // Malformed JSON - return null
        }

        return null;
    }

    private static string TruncateToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length <= 16)
            return "[hidden]";

        return $"{token[..8]}...{token[^8..]}";
    }
}
