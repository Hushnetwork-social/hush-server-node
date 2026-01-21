using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode.Testing;
using StackExchange.Redis;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// FEAT-048: Step definitions for Identity Lookups Cache scenarios.
/// Tests cache-aside pattern for identity lookups.
/// </summary>
[Binding]
public sealed class IdentityLookupsCacheSteps
{
    private readonly ScenarioContext _scenarioContext;

    // Test public encrypt address (130 chars hex format)
    private const string TestPublicEncryptAddress = "04encrypt1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef12345678";

    public IdentityLookupsCacheSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"a user profile exists in the database with address ""(.*)"" and alias ""(.*)""")]
    public async Task GivenAUserProfileExistsInTheDatabaseWithAddressAndAlias(string address, string alias)
    {
        var fixture = GetFixture();

        // Generate a valid 130-character public signing address from the test address
        var fullAddress = GenerateFullAddress(address);
        var shortAlias = alias.Length > 2 ? alias.Substring(0, 2).ToUpper() : alias.ToUpper();

        var insertSql = $"""
            INSERT INTO "Identity"."Profile"
            ("PublicSigningAddress", "Alias", "ShortAlias", "PublicEncryptAddress", "IsPublic", "BlockIndex")
            VALUES ('{fullAddress}', '{alias}', '{shortAlias}', '{TestPublicEncryptAddress}', true, 1)
            ON CONFLICT ("PublicSigningAddress") DO UPDATE SET
            "Alias" = EXCLUDED."Alias",
            "ShortAlias" = EXCLUDED."ShortAlias"
            """;

        await fixture.ExecuteScalarAsync<object>(insertSql);

        // Store for later lookup
        _scenarioContext[$"FullAddress_{address}"] = fullAddress;
        _scenarioContext[$"Alias_{address}"] = alias;
    }

    [Given(@"the Redis identity cache has no entry for ""(.*)""")]
    public async Task GivenTheRedisIdentityCacheHasNoEntryFor(string address)
    {
        var fullAddress = GetFullAddress(address);
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:identity:{fullAddress}";
        await redisDb.KeyDeleteAsync(cacheKey);

        var exists = await redisDb.KeyExistsAsync(cacheKey);
        exists.Should().BeFalse("Identity cache should be empty");
    }

    [Given(@"no user profile exists for address ""(.*)""")]
    public void GivenNoUserProfileExistsForAddress(string address)
    {
        // Generate full address but don't insert anything
        var fullAddress = GenerateFullAddress(address);
        _scenarioContext[$"FullAddress_{address}"] = fullAddress;
    }

    [Given(@"the identity for ""(.*)"" has been looked up once to populate the cache")]
    public async Task GivenTheIdentityHasBeenLookedUpOnceToPopulateTheCache(string address)
    {
        await WhenTheIdentityIsLookedUpViaGrpc(address);
    }

    [Given(@"the identity for ""(.*)"" is in the Redis cache")]
    public async Task GivenTheIdentityIsInTheRedisCache(string address)
    {
        // First lookup to populate cache
        await WhenTheIdentityIsLookedUpViaGrpc(address);

        // Verify it's in cache
        var fullAddress = GetFullAddress(address);
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:identity:{fullAddress}";
        var exists = await redisDb.KeyExistsAsync(cacheKey);
        exists.Should().BeTrue("Identity should be in cache after lookup");
    }

    [When(@"the identity for ""(.*)"" is looked up via gRPC")]
    public async Task WhenTheIdentityIsLookedUpViaGrpc(string address)
    {
        var fullAddress = GetFullAddress(address);
        var grpcFactory = GetGrpcFactory();
        var identityClient = grpcFactory.CreateClient<HushIdentity.HushIdentityClient>();

        var response = await identityClient.GetIdentityAsync(new GetIdentityRequest
        {
            PublicSigningAddress = fullAddress
        });

        _scenarioContext["LastIdentityResponse"] = response;
        _scenarioContext["LastLookedUpAddress"] = address;
    }

    [When(@"the Redis identity cache key for ""(.*)"" is deleted")]
    public async Task WhenTheRedisIdentityCacheKeyIsDeleted(string address)
    {
        var fullAddress = GetFullAddress(address);
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:identity:{fullAddress}";
        await redisDb.KeyDeleteAsync(cacheKey);
    }

    [Then(@"the response should contain display name ""(.*)""")]
    public void ThenTheResponseShouldContainDisplayName(string expectedAlias)
    {
        var response = (GetIdentityReply)_scenarioContext["LastIdentityResponse"];
        response.Successfull.Should().BeTrue("Identity lookup should succeed");
        response.ProfileName.Should().Be(expectedAlias);
    }

    [Then(@"the identity should be in the Redis cache for ""(.*)""")]
    public async Task ThenTheIdentityShouldBeInTheRedisCacheFor(string address)
    {
        var fullAddress = GetFullAddress(address);
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:identity:{fullAddress}";
        var cachedValue = await redisDb.StringGetAsync(cacheKey);

        cachedValue.IsNullOrEmpty.Should().BeFalse($"Identity for {address} should be in cache");
    }

    [Then(@"the identity should still be in the Redis cache for ""(.*)""")]
    public async Task ThenTheIdentityShouldStillBeInTheRedisCacheFor(string address)
    {
        await ThenTheIdentityShouldBeInTheRedisCacheFor(address);
    }

    [Then(@"the lookup should return not found")]
    public void ThenTheLookupShouldReturnNotFound()
    {
        var response = (GetIdentityReply)_scenarioContext["LastIdentityResponse"];
        response.Successfull.Should().BeFalse("Lookup for non-existing identity should return not found");
    }

    [Then(@"no cache entry should exist for ""(.*)""")]
    public async Task ThenNoCacheEntryShouldExistFor(string address)
    {
        var fullAddress = GetFullAddress(address);
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:identity:{fullAddress}";
        var exists = await redisDb.KeyExistsAsync(cacheKey);

        exists.Should().BeFalse("Non-existing identities should not be cached (no negative caching)");
    }

    [Then(@"the Redis identity cache TTL should be between (.*) and (.*) days")]
    public async Task ThenTheRedisIdentityCacheTtlShouldBeBetweenDays(int minDays, int maxDays)
    {
        var address = (string)_scenarioContext["LastLookedUpAddress"];
        var fullAddress = GetFullAddress(address);
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:identity:{fullAddress}";
        var ttl = await redisDb.KeyTimeToLiveAsync(cacheKey);

        ttl.Should().NotBeNull("Cache key should have TTL set");
        ttl!.Value.TotalDays.Should().BeGreaterThan(minDays, $"TTL should be greater than {minDays} days");
        ttl.Value.TotalDays.Should().BeLessOrEqualTo(maxDays, $"TTL should not exceed {maxDays} days");
    }

    [Then(@"the PostgreSQL table ""(.*)""\.""(.*)"" should exist")]
    public async Task ThenThePostgreSQLTableShouldExist(string schema, string table)
    {
        var fixture = GetFixture();
        var query = $"""
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = '{schema}'
            AND table_name = '{table}'
            """;

        var results = await fixture.ExecuteQueryAsync(query);
        results.Should().NotBeEmpty($"Table {schema}.{table} should exist");
    }

    #region Helper Methods

    private string GenerateFullAddress(string shortAddress)
    {
        // Generate a valid 130-character public signing address
        // Format: 04 + 128 hex chars
        var baseHex = shortAddress.Replace("-", "").PadRight(128, '0');
        if (baseHex.Length > 128) baseHex = baseHex.Substring(0, 128);
        return "04" + baseHex;
    }

    private string GetFullAddress(string shortAddress)
    {
        var key = $"FullAddress_{shortAddress}";
        if (_scenarioContext.TryGetValue(key, out var fullAddr) && fullAddr is string address)
        {
            return address;
        }
        // Generate if not stored
        return GenerateFullAddress(shortAddress);
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
