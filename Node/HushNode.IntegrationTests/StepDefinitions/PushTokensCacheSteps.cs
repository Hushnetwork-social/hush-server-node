using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode.Testing;
using StackExchange.Redis;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// FEAT-047: Step definitions for Push Tokens Cache scenarios.
/// Tests write-through caching for device token registration.
/// </summary>
[Binding]
public sealed class PushTokensCacheSteps
{
    private readonly ScenarioContext _scenarioContext;

    public PushTokensCacheSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [When(@"user ""([^""]*)"" registers device token ""([^""]*)"" on platform ""([^""]*)"" via gRPC")]
    public async Task WhenUserRegistersDeviceTokenOnPlatformViaGrpc(string userId, string token, string platform)
    {
        await RegisterDeviceToken(userId, token, platform, "Test Device");
    }

    [When(@"user ""([^""]*)"" registers device token ""([^""]*)"" on platform ""([^""]*)"" with device name ""([^""]*)"" via gRPC")]
    public async Task WhenUserRegistersDeviceTokenWithDeviceNameViaGrpc(string userId, string token, string platform, string deviceName)
    {
        await RegisterDeviceToken(userId, token, platform, deviceName);
    }

    [Given(@"user ""(.*)"" has registered device token ""(.*)"" on platform ""(.*)""")]
    public async Task GivenUserHasRegisteredDeviceTokenOnPlatform(string userId, string token, string platform)
    {
        await RegisterDeviceToken(userId, token, platform, "Test Device");
    }

    [Given(@"the token is in the Redis push token cache")]
    public async Task GivenTheTokenIsInTheRedisPushTokenCache()
    {
        // Token should already be in cache from registration
        var userId = (string)_scenarioContext["LastRegisteredUserId"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:push:v1:user:{userId}";
        var exists = await redisDb.KeyExistsAsync(cacheKey);
        exists.Should().BeTrue("Token should be in cache after registration");
    }

    [Given(@"the token is in the Redis push token cache for ""(.*)""")]
    public async Task GivenTheTokenIsInTheRedisPushTokenCacheForUser(string userId)
    {
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:push:v1:user:{userId}";
        var exists = await redisDb.KeyExistsAsync(cacheKey);
        exists.Should().BeTrue($"Token should be in cache for user {userId}");
    }

    [When(@"user ""(.*)"" unregisters token ""(.*)"" via gRPC")]
    public async Task WhenUserUnregistersTokenViaGrpc(string userId, string token)
    {
        var grpcFactory = GetGrpcFactory();
        var notificationClient = grpcFactory.CreateClient<HushNotification.HushNotificationClient>();

        var response = await notificationClient.UnregisterDeviceTokenAsync(new UnregisterDeviceTokenRequest
        {
            UserId = userId,
            Token = token
        });

        response.Success.Should().BeTrue($"Unregistration should succeed: {response.Message}");
        _scenarioContext["LastUnregisteredToken"] = token;
    }

    [Given(@"user ""(.*)"" has unregistered token ""(.*)""")]
    public async Task GivenUserHasUnregisteredToken(string userId, string token)
    {
        await WhenUserUnregistersTokenViaGrpc(userId, token);
    }

    [When(@"user ""(.*)"" requests active device tokens via gRPC")]
    public async Task WhenUserRequestsActiveDeviceTokensViaGrpc(string userId)
    {
        var grpcFactory = GetGrpcFactory();
        var notificationClient = grpcFactory.CreateClient<HushNotification.HushNotificationClient>();

        var response = await notificationClient.GetActiveDeviceTokensAsync(new GetActiveDeviceTokensRequest
        {
            UserId = userId
        });

        _scenarioContext["LastTokensResponse"] = response;
    }

    [Then(@"the token should be stored in the PostgreSQL DeviceTokens table")]
    public async Task ThenTheTokenShouldBeStoredInThePostgreSQLDeviceTokensTable()
    {
        var userId = (string)_scenarioContext["LastRegisteredUserId"];
        var token = (string)_scenarioContext["LastRegisteredToken"];
        var fixture = GetFixture();

        var query = $"SELECT COUNT(*) FROM \"Notifications\".\"DeviceTokens\" WHERE \"UserId\" = '{userId}' AND \"Token\" = '{token}'";
        var count = await fixture.ExecuteScalarAsync<long>(query);

        count.Should().Be(1, "Token should exist in PostgreSQL DeviceTokens table");
    }

    [Then(@"the token should be in the Redis push token cache for ""(.*)""")]
    public async Task ThenTheTokenShouldBeInTheRedisPushTokenCacheForUser(string userId)
    {
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:push:v1:user:{userId}";
        var cachedTokens = await redisDb.HashGetAllAsync(cacheKey);

        cachedTokens.Should().NotBeEmpty($"Cache should contain tokens for user {userId}");
    }

    [Then(@"the Redis push token cache should contain ""(.*)""")]
    public async Task ThenTheRedisPushTokenCacheShouldContain(string expectedToken)
    {
        var userId = (string)_scenarioContext["LastRegisteredUserId"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:push:v1:user:{userId}";
        var cachedTokens = await redisDb.HashGetAllAsync(cacheKey);

        var tokenValues = cachedTokens.Select(e => e.Value.ToString()).ToList();
        tokenValues.Any(t => t.Contains(expectedToken)).Should().BeTrue($"Cache should contain token {expectedToken}");
    }

    [Then(@"the token should be removed from the Redis push token cache")]
    public async Task ThenTheTokenShouldBeRemovedFromTheRedisPushTokenCache()
    {
        var userId = (string)_scenarioContext["LastRegisteredUserId"];
        var token = (string)_scenarioContext["LastUnregisteredToken"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:push:v1:user:{userId}";
        var cachedTokens = await redisDb.HashGetAllAsync(cacheKey);

        var hasToken = cachedTokens.Any(e => e.Value.ToString().Contains(token));
        hasToken.Should().BeFalse("Token should be removed from cache after unregistration");
    }

    [Then(@"the token should be removed from the Redis push token cache for ""(.*)""")]
    public async Task ThenTheTokenShouldBeRemovedFromTheRedisPushTokenCacheForUser(string userId)
    {
        var token = (string)_scenarioContext["LastRegisteredToken"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:push:v1:user:{userId}";
        var cachedTokens = await redisDb.HashGetAllAsync(cacheKey);

        var hasToken = cachedTokens.Any(e => e.Value.ToString().Contains(token));
        hasToken.Should().BeFalse($"Token should be removed from cache for user {userId}");
    }

    [Then(@"the Redis push token cache TTL should be between (.*) and (.*) days")]
    public async Task ThenTheRedisPushTokenCacheTtlShouldBeBetweenDays(int minDays, int maxDays)
    {
        var userId = (string)_scenarioContext["LastRegisteredUserId"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:push:v1:user:{userId}";
        var ttl = await redisDb.KeyTimeToLiveAsync(cacheKey);

        ttl.Should().NotBeNull("Cache key should have TTL set");
        ttl!.Value.TotalDays.Should().BeGreaterThan(minDays, $"TTL should be greater than {minDays} days");
        ttl.Value.TotalDays.Should().BeLessOrEqualTo(maxDays, $"TTL should not exceed {maxDays} days");
    }

    [Then(@"the response should contain token ""(.*)""")]
    public void ThenTheResponseShouldContainToken(string expectedToken)
    {
        var response = (GetActiveDeviceTokensResponse)_scenarioContext["LastTokensResponse"];
        response.Tokens.Should().Contain(t => t.Token == expectedToken, $"Response should contain token {expectedToken}");
    }

    [Then(@"the response should not contain token ""(.*)""")]
    public void ThenTheResponseShouldNotContainToken(string unexpectedToken)
    {
        var response = (GetActiveDeviceTokensResponse)_scenarioContext["LastTokensResponse"];
        response.Tokens.Should().NotContain(t => t.Token == unexpectedToken, $"Response should not contain token {unexpectedToken}");
    }

    #region Helper Methods

    private async Task RegisterDeviceToken(string userId, string token, string platform, string deviceName)
    {
        var grpcFactory = GetGrpcFactory();
        var notificationClient = grpcFactory.CreateClient<HushNotification.HushNotificationClient>();

        var pushPlatform = platform.ToLowerInvariant() switch
        {
            "android" => PushPlatform.Android,
            "ios" => PushPlatform.Ios,
            "web" => PushPlatform.Web,
            _ => throw new ArgumentException($"Unknown platform: {platform}")
        };

        var response = await notificationClient.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
        {
            UserId = userId,
            Platform = pushPlatform,
            Token = token,
            DeviceName = deviceName
        });

        response.Success.Should().BeTrue($"Token registration should succeed: {response.Message}");

        _scenarioContext["LastRegisteredUserId"] = userId;
        _scenarioContext["LastRegisteredToken"] = token;
    }

    private GrpcClientFactory GetGrpcFactory()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.GrpcFactoryKey, out var factoryObj)
            && factoryObj is GrpcClientFactory grpcFactory)
        {
            return grpcFactory;
        }
        throw new InvalidOperationException("GrpcClientFactory not found in ScenarioContext.");
    }

    private HushTestFixture GetFixture()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.FixtureKey, out var fixtureObj)
            && fixtureObj is HushTestFixture fixture)
        {
            return fixture;
        }
        throw new InvalidOperationException("HushTestFixture not found in ScenarioContext.");
    }

    #endregion
}
