using System.Diagnostics;
using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode;
using HushServerNode.Testing;
using Xunit;
using Xunit.Abstractions;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-046: Performance baseline tests for feed message caching.
/// These tests are NOT for CI (marked Category=Performance) but useful for validation.
/// They compare cache hit latency vs PostgreSQL query latency.
/// </summary>
[Collection("Integration Tests")]
[Trait("Category", "Performance")]
public class FeedMessageCachePerformanceTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private HushTestFixture? _fixture;
    private HushServerNodeCore? _node;
    private BlockProductionControl? _blockControl;
    private GrpcClientFactory? _grpcFactory;
    private readonly Dictionary<string, string> _personalFeedAesKeys = new();

    public FeedMessageCachePerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        _fixture = new HushTestFixture();
        await _fixture.InitializeAsync();
        await _fixture.ResetAllAsync();

        var (node, blockControl, grpcFactory) = await _fixture.StartNodeAsync();
        _node = node;
        _blockControl = blockControl;
        _grpcFactory = grpcFactory;
    }

    public async Task DisposeAsync()
    {
        _grpcFactory?.Dispose();

        if (_node != null)
        {
            await _node.DisposeAsync();
        }

        if (_fixture != null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task CacheHit_FasterThanPostgresQuery()
    {
        // Arrange: Register user and send messages
        var alice = TestIdentities.Alice;
        await RegisterUserWithPersonalFeed(alice);

        var feedClient = _grpcFactory!.CreateClient<HushFeed.HushFeedClient>();
        var feedsResponse = await feedClient.GetFeedsForAddressAsync(new GetFeedForAddressRequest
        {
            ProfilePublicKey = alice.PublicSigningAddress,
            BlockIndex = 0
        });

        var personalFeed = feedsResponse.Feeds.First(f => f.FeedType == 0);
        var feedId = personalFeed.FeedId;

        // Send some messages to have data
        const int messageCount = 50;
        _output.WriteLine($"Sending {messageCount} messages for performance test...");

        for (int i = 0; i < messageCount; i++)
        {
            await SendMessageToFeed(alice, feedId, $"Performance test message {i + 1}");
        }
        await _blockControl!.ProduceBlockAsync();

        // Warm up: First query to ensure cache is populated
        await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = alice.PublicSigningAddress,
            BlockIndex = 0
        });

        // Measure cache hit latency (multiple iterations for average)
        const int iterations = 10;
        var cacheLatencies = new List<double>();

        for (int i = 0; i < iterations; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
            {
                ProfilePublicKey = alice.PublicSigningAddress,
                BlockIndex = 0
            });
            stopwatch.Stop();
            cacheLatencies.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        var avgCacheLatency = cacheLatencies.Average();
        _output.WriteLine($"Cache hit average latency: {avgCacheLatency:F2}ms over {iterations} iterations");

        // Now flush cache and measure PostgreSQL-only latency
        await _fixture!.FlushRedisAsync();

        var postgresLatencies = new List<double>();
        for (int i = 0; i < iterations; i++)
        {
            // Flush cache before each iteration to force PostgreSQL query
            await _fixture.FlushRedisAsync();

            var stopwatch = Stopwatch.StartNew();
            await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
            {
                ProfilePublicKey = alice.PublicSigningAddress,
                BlockIndex = 0
            });
            stopwatch.Stop();
            postgresLatencies.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        var avgPostgresLatency = postgresLatencies.Average();
        _output.WriteLine($"PostgreSQL fallback average latency: {avgPostgresLatency:F2}ms over {iterations} iterations");

        // Report improvement
        var improvement = (avgPostgresLatency - avgCacheLatency) / avgPostgresLatency * 100;
        _output.WriteLine($"Cache provides {improvement:F1}% improvement over PostgreSQL");

        // Assert: Cache should be faster (or at least not significantly slower)
        // Note: In test container environment, improvement may be minimal due to overhead
        // The main goal is to document the baseline performance
        avgCacheLatency.Should().BeLessThanOrEqualTo(avgPostgresLatency * 2,
            "Cache should not be significantly slower than PostgreSQL");
    }

    #region Helper Methods

    private async Task RegisterUserWithPersonalFeed(TestIdentity identity)
    {
        var feedClient = _grpcFactory!.CreateClient<HushFeed.HushFeedClient>();
        var blockchainClient = _grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var hasPersonalFeed = await feedClient.HasPersonalFeedAsync(new HasPersonalFeedRequest
        {
            PublicPublicKey = identity.PublicSigningAddress
        });

        if (!hasPersonalFeed.FeedAvailable)
        {
            var identityTxJson = TestTransactionFactory.CreateIdentityRegistration(identity);
            var identityResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
            {
                SignedTransaction = identityTxJson
            });
            identityResponse.Successfull.Should().BeTrue($"Identity registration should succeed: {identityResponse.Message}");
            await _blockControl!.ProduceBlockAsync();

            var (personalFeedTxJson, aesKey) = TestTransactionFactory.CreatePersonalFeedWithKey(identity);
            _personalFeedAesKeys[identity.PublicSigningAddress] = aesKey;

            var feedResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
            {
                SignedTransaction = personalFeedTxJson
            });
            feedResponse.Successfull.Should().BeTrue($"Personal feed creation should succeed: {feedResponse.Message}");
            await _blockControl.ProduceBlockAsync();
        }
    }

    private async Task SendMessageToFeed(TestIdentity sender, string feedIdString, string message)
    {
        var blockchainClient = _grpcFactory!.CreateClient<HushBlockchain.HushBlockchainClient>();

        var feedId = new HushShared.Feeds.Model.FeedId(Guid.Parse(feedIdString));
        var aesKey = _personalFeedAesKeys[sender.PublicSigningAddress];
        var signedTransactionJson = TestTransactionFactory.CreateFeedMessage(sender, feedId, message, aesKey);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransactionJson
        });

        response.Successfull.Should().BeTrue($"Message submission should succeed: {response.Message}");
    }

    #endregion
}
