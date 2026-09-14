// EPIC-001 -> FEAT-008 AC-008-076 / FEAT-009 AC-009-081.
// FEAT-011 Phase 3 Tasks 3.5/3.6 and 3.7/3.8; shared FEAT-048 cache contract.
// Real Redis/RPC/indexing FEAT evidence, not browser/EPIC acceptance.
using System.Text.Json;
using FluentAssertions;
using HushNetwork.proto;
using HushNode.Caching;
using HushNode.Identity.Storage;
using HushNode.MemPool;
using HushNode.Notifications.Models;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Olimpo.KeyDerivation;
using StackExchange.Redis;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.ServerTwins;

[Binding]
[Scope(Tag = "HV-SERVER-TWIN")]
internal sealed class IdentityCacheTwinSteps(HushVotingScenario scenario)
{
    private const string Alias = "Cache Alice";
    private DerivedKeys _keys = null!;
    private string _signed = "";
    private RedisKey _cacheKey;
    private IDatabase _redis = null!;
    private IdentityCacheService _cache = null!;
    private string _fault = "";
    private long _misses;
    private long _errors;

    [Given("the owned real node has an unregistered identity and its actual Redis cache")]
    public async Task PrepareAsync()
    {
        (scenario.Page is null && scenario.Context is null).Should().BeTrue();
        scenario.BaseUrl.Should().BeEmpty();
        await scenario.Blocks.ProduceBlockAsync();
        var words = MnemonicGenerator.GenerateMnemonic();
        _keys = HushVotingTestIdentity.DeriveP01(words);
        await HushVotingArtifactClient.RegisterAsync(words, _keys.SigningPrivateKey, _keys.EncryptPrivateKey,
            _keys.SigningPublicKey, _keys.EncryptPublicKey, Alias);
        _signed = HushVotingServerIdentity.Sign(_keys, Alias, false);
        await HushVotingArtifactClient.RegisterAsync(_signed);
        var services = scenario.Node.Services;
        _redis = services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        var prefix = services.GetRequiredService<IOptions<RedisSettings>>().Value.InstanceName;
        _cacheKey = prefix + IdentityCacheConstants.GetIdentityKey(_keys.SigningPublicKey);
        _cache = services.GetRequiredService<IIdentityCacheService>().Should().BeOfType<IdentityCacheService>().Subject;
        (await _redis.KeyExistsAsync(_cacheKey)).Should().BeFalse();
        await EffectsAsync(0, 0);
    }

    [When("repeated exact absence lookups precede real admission and indexing")]
    public async Task AbsenceThenIndexAsync()
    {
        for (var i = 0; i < 3; i++) await AbsentWithoutCacheAsync();
        using (var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(20)))
        {
            AssertReply(await SubmitAsync(), TransactionStatus.Accepted);
            await received.WaitAsync();
        }
        AssertReply(await SubmitAsync(), TransactionStatus.Pending);
        await EffectsAsync(0, 1);
        await AbsentWithoutCacheAsync();
        await scenario.Blocks.ProduceBlockAsync();
        await ExactLookupAsync();
    }

    [Given("the identity is genuinely indexed and its exact profile has populated Redis")]
    public async Task IndexedAsync()
    {
        await AbsenceThenIndexAsync();
        await CachedExactAsync();
    }

    [When("the owned identity cache entry encounters (invalidation|malformed-json|wrong-redis-type|null-alias|wrong-signing-address)")]
    public async Task FaultAsync(string fault)
    {
        _fault = fault;
        _misses = _cache.CacheMisses;
        _errors = _cache.ReadErrors;
        await _redis.KeyDeleteAsync(_cacheKey);
        switch (fault)
        {
            case "invalidation":
                (await _redis.KeyExistsAsync(_cacheKey)).Should().BeFalse();
                break;
            case "malformed-json":
                await _redis.StringSetAsync(_cacheKey, "{ invalid profile json");
                (await _redis.KeyTypeAsync(_cacheKey)).Should().Be(RedisType.String);
                break;
            case "wrong-redis-type":
                await _redis.ListRightPushAsync(_cacheKey, "invalid profile type");
                (await _redis.KeyTypeAsync(_cacheKey)).Should().Be(RedisType.List);
                break;
            case "null-alias":
            case "wrong-signing-address":
                // Parseable but unusable cache metadata must never override database truth.
                await _redis.StringSetAsync(_cacheKey, JsonSerializer.Serialize(new
                {
                    Alias = fault == "null-alias" ? null : Alias,
                    ShortAlias = "CA",
                    PublicSigningAddress = fault == "wrong-signing-address" ? _keys.EncryptPublicKey : _keys.SigningPublicKey,
                    PublicEncryptAddress = _keys.EncryptPublicKey,
                    IsPublic = false,
                    BlockIndex = new HushShared.Blockchain.BlockModel.BlockIndex(1)
                }));
                (await _redis.KeyTypeAsync(_cacheKey)).Should().Be(RedisType.String);
                break;
            default: throw new InvalidOperationException("Unknown closed cache test case.");
        }
    }

    [Then("normal identity RPC falls back to PostgreSQL and repairs the cache without admission")]
    public async Task FallbackAsync()
    {
        await ExactLookupAsync();
        if (_fault == "wrong-redis-type") _cache.ReadErrors.Should().BeGreaterThan(_errors);
        else _cache.CacheMisses.Should().BeGreaterThan(_misses);
        await CachedExactAsync();
    }

    [When("the run-owned Redis process is stopped during exact identity lookup")]
    public async Task OutageAsync()
    {
        var reads = _cache.ReadErrors;
        var writes = _cache.WriteErrors;
        await scenario.WithRedisStoppedAsync(async () =>
        {
            // Real connection failure, never a fabricated cache or RPC response.
            var unavailable = false;
            try { await _redis.PingAsync(); }
            catch (RedisException) { unavailable = true; }
            unavailable.Should().BeTrue("the owned Redis process must actually be unavailable");
            await ExactLookupAsync(timeoutSeconds: 30);
            _cache.ReadErrors.Should().BeGreaterThan(reads);
            _cache.WriteErrors.Should().BeGreaterThan(writes);
            await EffectsAsync(1, 0);
        });
    }

    [When("real Redis rejects writes during an identity cache (miss|hit)")]
    public async Task RejectedWriteAsync(string state)
    {
        if (state == "miss") await _redis.KeyDeleteAsync(_cacheKey);
        var reads = _cache.ReadErrors;
        var writes = _cache.WriteErrors;
        await scenario.WithRedisWritesRejectedAsync(async () =>
        {
            // The standalone Redis has no replicas, so a required replica rejects writes.
            var rejected = false;
            try { await _redis.StringSetAsync("hushvoting-public-write-probe", "public fixture"); }
            catch (RedisServerException error) when (error.Message.StartsWith("NOREPLICAS", StringComparison.Ordinal)) { rejected = true; }
            rejected.Should().BeTrue("the real Redis server must reject the control write");
            await ExactLookupAsync();
            _cache.WriteErrors.Should().BeGreaterThan(writes);
            if (state == "hit") _cache.ReadErrors.Should().BeGreaterThan(reads, "TTL refresh is a real failed write on the cache-hit path");
            else _cache.ReadErrors.Should().Be(reads, "a readable cache miss is not a Redis read failure");
            await EffectsAsync(1, 0);
        });
    }

    [Then("restored Redis resumes exact cached lookup with one unchanged indexed identity")]
    public async Task RestoredAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            try { await _redis.PingAsync().WaitAsync(deadline.Token); break; }
            catch (RedisException) { await Task.Delay(100, deadline.Token); }
        }
        await ExactLookupAsync();
        await CachedExactAsync();
    }

    [Then("the exact indexed profile is cached and retry creates no duplicate state")]
    public async Task CachedExactAsync()
    {
        (await _redis.KeyTypeAsync(_cacheKey)).Should().Be(RedisType.String);
        var serialized = await _redis.StringGetAsync(_cacheKey);
        using (var json = JsonDocument.Parse(serialized.ToString()))
        {
            var profile = json.RootElement;
            (profile.GetProperty("PublicSigningAddress").GetString() == _keys.SigningPublicKey
                && profile.GetProperty("PublicEncryptAddress").GetString() == _keys.EncryptPublicKey
                && profile.GetProperty("Alias").GetString() == Alias && !profile.GetProperty("IsPublic").GetBoolean()).Should().BeTrue();
        }
        var hits = _cache.CacheHits;
        await ExactLookupAsync();
        _cache.CacheHits.Should().BeGreaterThan(hits);
        var ttl = await _redis.KeyTimeToLiveAsync(_cacheKey);
        (ttl is not null && ttl > TimeSpan.FromDays(6) && ttl <= IdentityCacheConstants.CacheTtl).Should().BeTrue();
        AssertReply(await SubmitAsync(), TransactionStatus.AlreadyExists);
        await EffectsAsync(1, 0);
    }

    private async Task AbsentWithoutCacheAsync()
    {
        var reply = await LookupAsync();
        (!reply.Successfull && string.IsNullOrEmpty(reply.PublicSigningAddress) && string.IsNullOrEmpty(reply.PublicEncryptAddress)).Should().BeTrue();
        (await _redis.KeyExistsAsync(_cacheKey)).Should().BeFalse("authoritative absence must not poison the cache before indexing");
    }

    private async Task ExactLookupAsync(int timeoutSeconds = 10)
    {
        var reply = await LookupAsync(timeoutSeconds);
        (reply.Successfull && reply.PublicSigningAddress == _keys.SigningPublicKey && reply.PublicEncryptAddress == _keys.EncryptPublicKey
            && reply.ProfileName == Alias && !reply.IsPublic).Should().BeTrue();
    }

    private Task<GetIdentityReply> LookupAsync(int timeoutSeconds = 10) => scenario.Identities.GetIdentityAsync(
        new() { PublicSigningAddress = _keys.SigningPublicKey }, deadline: DateTime.UtcNow.AddSeconds(timeoutSeconds)).ResponseAsync;
    private Task<SubmitSignedTransactionReply> SubmitAsync() => scenario.Blockchain.SubmitSignedTransactionAsync(
        new() { SignedTransaction = _signed }, deadline: DateTime.UtcNow.AddSeconds(10)).ResponseAsync;
    private static void AssertReply(SubmitSignedTransactionReply reply, TransactionStatus expected)
    {
        reply.Successfull.Should().BeTrue(); reply.Status.Should().Be(expected); reply.ValidationCode.Should().BeEmpty();
    }
    private async Task EffectsAsync(int profiles, int pending)
    {
        using var scope = scenario.Node.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Profiles.CountAsync(p => p.PublicSigningAddress == _keys.SigningPublicKey)).Should().Be(profiles);
        scenario.Node.Services.GetRequiredService<IMemPoolService>().PeekPendingValidatedTransactions().Count().Should().Be(pending);
    }
}
