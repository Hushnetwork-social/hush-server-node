using FluentAssertions;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.Caching;
using HushNode.Feeds.gRPC;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushNode.Identity.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// FEAT-046: Tests for FeedsGrpcService cache integration.
/// Verifies cache-first pattern, cache-aside pattern, and fallback behavior.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class FeedsGrpcServiceCacheTests
{
    #region Cache Hit Tests

    [Fact]
    public async Task GetFeedMessagesForAddress_CacheHit_ReturnsFromCache()
    {
        // Arrange
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);
        var userAddress = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        var feed = CreateFeed(feedId, "Test Feed", FeedType.Personal, 50);
        feed.Participants = new[]
        {
            CreateFeedParticipant(feedId, userAddress, ParticipantType.Owner, "encryptedKey", feed)
        };

        // Cached messages start at block 90, and we request since block 90
        // This means sinceBlockIndex (90) >= oldestCachedBlock (90), so NO gap detected
        var cachedMessages = new[]
        {
            CreateFeedMessage(feedId, userAddress, "Cached message 1", new BlockIndex(90)),
            CreateFeedMessage(feedId, userAddress, "Cached message 2", new BlockIndex(95))
        };

        // Mock feeds storage to return the feed
        var mockFeedsStorageService = mocker.GetMock<IFeedsStorageService>();
        mockFeedsStorageService
            .Setup(x => x.RetrieveFeedsForAddress(userAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(new[] { feed });

        // Mock cache to return cached messages (CACHE HIT)
        var mockCacheService = mocker.GetMock<IFeedMessageCacheService>();
        mockCacheService
            .Setup(x => x.GetMessagesAsync(feedId, It.IsAny<BlockIndex>()))
            .ReturnsAsync(cachedMessages);

        // Mock identity service for display name
        var mockIdentityService = mocker.GetMock<IIdentityService>();
        mockIdentityService
            .Setup(x => x.RetrieveIdentityAsync(userAddress))
            .ReturnsAsync(new Profile("TestUser", "TU", userAddress, "encryptKey", true, new BlockIndex(50)));

        // FEAT-052: Mock pagination method (used to check for older messages in cache scenario)
        var mockMessageStorageService = mocker.GetMock<IFeedMessageStorageService>();
        mockMessageStorageService
            .Setup(x => x.GetPaginatedMessagesAsync(feedId, It.IsAny<BlockIndex>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(new PaginatedMessagesResult(new List<FeedMessage>(), false, new BlockIndex(0)));

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = userAddress,
            BlockIndex = 90,  // Request since block 90, oldest cached is 90 -> no gap
            FetchLatest = true  // FEAT-052: Use fetch_latest to hit cache path
        };

        // Act
        var result = await service.GetFeedMessagesForAddress(request, CreateMockServerCallContext());

        // Assert
        result.Messages.Should().HaveCount(2);
        result.Messages[0].MessageContent.Should().Be("Cached message 1");
        result.Messages[1].MessageContent.Should().Be("Cached message 2");
    }

    #endregion

    #region Cache Miss Tests

    [Fact]
    public async Task GetFeedMessagesForAddress_CacheMiss_QueriesPostgresAndPopulatesCache()
    {
        // Arrange
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);
        var userAddress = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        var feed = CreateFeed(feedId, "Test Feed", FeedType.Personal, 50);
        feed.Participants = new[]
        {
            CreateFeedParticipant(feedId, userAddress, ParticipantType.Owner, "encryptedKey", feed)
        };

        var dbMessages = new[]
        {
            CreateFeedMessage(feedId, userAddress, "DB message 1", new BlockIndex(90)),
            CreateFeedMessage(feedId, userAddress, "DB message 2", new BlockIndex(95))
        };

        // Mock feeds storage to return the feed
        var mockFeedsStorageService = mocker.GetMock<IFeedsStorageService>();
        mockFeedsStorageService
            .Setup(x => x.RetrieveFeedsForAddress(userAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(new[] { feed });

        // Mock cache to return null (CACHE MISS)
        var mockCacheService = mocker.GetMock<IFeedMessageCacheService>();
        mockCacheService
            .Setup(x => x.GetMessagesAsync(feedId, It.IsAny<BlockIndex>()))
            .ReturnsAsync((IEnumerable<FeedMessage>?)null);
        mockCacheService
            .Setup(x => x.PopulateCacheAsync(feedId, It.IsAny<IEnumerable<FeedMessage>>()))
            .Returns(Task.CompletedTask);

        // FEAT-052: Mock pagination method to return messages
        var mockMessageStorageService = mocker.GetMock<IFeedMessageStorageService>();
        mockMessageStorageService
            .Setup(x => x.GetPaginatedMessagesAsync(feedId, It.IsAny<BlockIndex>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(new PaginatedMessagesResult(dbMessages.ToList(), false, new BlockIndex(90)));

        // Mock identity service for display name
        var mockIdentityService = mocker.GetMock<IIdentityService>();
        mockIdentityService
            .Setup(x => x.RetrieveIdentityAsync(userAddress))
            .ReturnsAsync(new Profile("TestUser", "TU", userAddress, "encryptKey", true, new BlockIndex(50)));

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = userAddress,
            BlockIndex = 80,
            FetchLatest = true  // FEAT-052: Use fetch_latest for cache fallback testing
        };

        // Act
        var result = await service.GetFeedMessagesForAddress(request, CreateMockServerCallContext());

        // Assert
        result.Messages.Should().HaveCount(2);
        result.Messages[0].MessageContent.Should().Be("DB message 1");
        result.Messages[1].MessageContent.Should().Be("DB message 2");

        // FEAT-052: Verify pagination method was called (cache miss)
        mockMessageStorageService.Verify(
            x => x.GetPaginatedMessagesAsync(feedId, It.IsAny<BlockIndex>(), It.IsAny<int>(), It.IsAny<bool>()),
            Times.Once,
            "Pagination method should be queried on cache miss");

        // Verify cache was populated (cache-aside pattern)
        mockCacheService.Verify(
            x => x.PopulateCacheAsync(feedId, It.IsAny<IEnumerable<FeedMessage>>()),
            Times.Once,
            "Cache should be populated after cache miss");
    }

    #endregion

    #region Cache Gap Tests

    [Fact]
    public async Task GetFeedMessagesForAddress_CacheGap_FallsBackToPostgres()
    {
        // Arrange
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);
        var userAddress = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        var feed = CreateFeed(feedId, "Test Feed", FeedType.Personal, 50);
        feed.Participants = new[]
        {
            CreateFeedParticipant(feedId, userAddress, ParticipantType.Owner, "encryptedKey", feed)
        };

        // Cache contains messages from block 90+, but client asks for messages since block 50
        // This creates a gap: cache doesn't have blocks 51-89
        var cachedMessages = new[]
        {
            CreateFeedMessage(feedId, userAddress, "Cached message 1", new BlockIndex(90)),
            CreateFeedMessage(feedId, userAddress, "Cached message 2", new BlockIndex(95))
        };

        var dbMessages = new[]
        {
            CreateFeedMessage(feedId, userAddress, "DB message from block 60", new BlockIndex(60)),
            CreateFeedMessage(feedId, userAddress, "DB message from block 70", new BlockIndex(70)),
            CreateFeedMessage(feedId, userAddress, "DB message from block 90", new BlockIndex(90)),
            CreateFeedMessage(feedId, userAddress, "DB message from block 95", new BlockIndex(95))
        };

        // Mock feeds storage to return the feed
        var mockFeedsStorageService = mocker.GetMock<IFeedsStorageService>();
        mockFeedsStorageService
            .Setup(x => x.RetrieveFeedsForAddress(userAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(new[] { feed });

        // Mock cache - for regular pagination (not fetch_latest), cache is not checked
        var mockCacheService = mocker.GetMock<IFeedMessageCacheService>();
        mockCacheService
            .Setup(x => x.GetMessagesAsync(feedId, It.IsAny<BlockIndex>()))
            .ReturnsAsync(cachedMessages);

        // FEAT-052: Mock pagination method to return complete messages
        var mockMessageStorageService = mocker.GetMock<IFeedMessageStorageService>();
        mockMessageStorageService
            .Setup(x => x.GetPaginatedMessagesAsync(feedId, It.IsAny<BlockIndex>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(new PaginatedMessagesResult(dbMessages.ToList(), false, new BlockIndex(60)));

        // Mock identity service for display name
        var mockIdentityService = mocker.GetMock<IIdentityService>();
        mockIdentityService
            .Setup(x => x.RetrieveIdentityAsync(userAddress))
            .ReturnsAsync(new Profile("TestUser", "TU", userAddress, "encryptKey", true, new BlockIndex(50)));

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = userAddress,
            BlockIndex = 50  // FEAT-052: Regular pagination (not fetch_latest) - directly queries DB
        };

        // Act
        var result = await service.GetFeedMessagesForAddress(request, CreateMockServerCallContext());

        // Assert
        result.Messages.Should().HaveCount(4, "Should return all messages from PostgreSQL");

        // FEAT-052: Verify pagination method was queried (regular pagination goes to DB)
        mockMessageStorageService.Verify(
            x => x.GetPaginatedMessagesAsync(feedId, It.IsAny<BlockIndex>(), It.IsAny<int>(), false),
            Times.Once,
            "Pagination method should be queried for regular pagination");
    }

    #endregion

    #region Redis Unavailable Tests

    [Fact]
    public async Task GetFeedMessagesForAddress_RedisUnavailable_FallsBackToPostgres()
    {
        // Arrange
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);
        var userAddress = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        var feed = CreateFeed(feedId, "Test Feed", FeedType.Personal, 50);
        feed.Participants = new[]
        {
            CreateFeedParticipant(feedId, userAddress, ParticipantType.Owner, "encryptedKey", feed)
        };

        var dbMessages = new[]
        {
            CreateFeedMessage(feedId, userAddress, "DB message 1", new BlockIndex(90)),
            CreateFeedMessage(feedId, userAddress, "DB message 2", new BlockIndex(95))
        };

        // Mock feeds storage to return the feed
        var mockFeedsStorageService = mocker.GetMock<IFeedsStorageService>();
        mockFeedsStorageService
            .Setup(x => x.RetrieveFeedsForAddress(userAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(new[] { feed });

        // Mock cache to throw exception (Redis unavailable)
        var mockCacheService = mocker.GetMock<IFeedMessageCacheService>();
        mockCacheService
            .Setup(x => x.GetMessagesAsync(feedId, It.IsAny<BlockIndex>()))
            .ThrowsAsync(new Exception("Redis connection failed"));

        // FEAT-052: Mock pagination method to return messages (used as fallback)
        var mockMessageStorageService = mocker.GetMock<IFeedMessageStorageService>();
        mockMessageStorageService
            .Setup(x => x.GetPaginatedMessagesAsync(feedId, It.IsAny<BlockIndex>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(new PaginatedMessagesResult(dbMessages.ToList(), false, new BlockIndex(90)));

        // Mock identity service for display name
        var mockIdentityService = mocker.GetMock<IIdentityService>();
        mockIdentityService
            .Setup(x => x.RetrieveIdentityAsync(userAddress))
            .ReturnsAsync(new Profile("TestUser", "TU", userAddress, "encryptKey", true, new BlockIndex(50)));

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = userAddress,
            BlockIndex = 80,
            FetchLatest = true  // FEAT-052: Use fetch_latest to test cache fallback
        };

        // Act - should NOT throw, should fall back gracefully
        var result = await service.GetFeedMessagesForAddress(request, CreateMockServerCallContext());

        // Assert
        result.Messages.Should().HaveCount(2);
        result.Messages[0].MessageContent.Should().Be("DB message 1");
        result.Messages[1].MessageContent.Should().Be("DB message 2");

        // FEAT-052: Verify pagination method was queried as fallback
        mockMessageStorageService.Verify(
            x => x.GetPaginatedMessagesAsync(feedId, It.IsAny<BlockIndex>(), It.IsAny<int>(), It.IsAny<bool>()),
            Times.Once,
            "Pagination method should be queried when Redis is unavailable");
    }

    #endregion

    #region Filtering Tests

    [Fact]
    public async Task GetFeedMessagesForAddress_FiltersByBlockIndex()
    {
        // Arrange
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);
        var userAddress = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        var feed = CreateFeed(feedId, "Test Feed", FeedType.Personal, 50);
        feed.Participants = new[]
        {
            CreateFeedParticipant(feedId, userAddress, ParticipantType.Owner, "encryptedKey", feed)
        };

        // Cache returns messages after the requested block index
        // Simulating the filtering that happens in FeedMessageCacheService.GetMessagesAsync
        // The cache service filters to only return messages with BlockIndex > sinceBlockIndex
        // Oldest cached message is at 95, we request since 90 -> gap detected (90 < 95)
        // So we set up sinceBlockIndex >= oldestCachedBlock to avoid gap detection
        var cachedMessagesAfterFilter = new[]
        {
            CreateFeedMessage(feedId, userAddress, "Message at block 95", new BlockIndex(95)),
            CreateFeedMessage(feedId, userAddress, "Message at block 100", new BlockIndex(100))
        };

        // Mock feeds storage to return the feed
        var mockFeedsStorageService = mocker.GetMock<IFeedsStorageService>();
        mockFeedsStorageService
            .Setup(x => x.RetrieveFeedsForAddress(userAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(new[] { feed });

        // Mock cache to return filtered messages
        var mockCacheService = mocker.GetMock<IFeedMessageCacheService>();
        mockCacheService
            .Setup(x => x.GetMessagesAsync(feedId, It.IsAny<BlockIndex>()))
            .ReturnsAsync(cachedMessagesAfterFilter);

        // FEAT-052: Mock pagination method for checking older messages
        var mockMessageStorageService = mocker.GetMock<IFeedMessageStorageService>();
        mockMessageStorageService
            .Setup(x => x.GetPaginatedMessagesAsync(feedId, It.IsAny<BlockIndex>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(new PaginatedMessagesResult(new List<FeedMessage>(), false, new BlockIndex(0)));

        // Mock identity service for display name
        var mockIdentityService = mocker.GetMock<IIdentityService>();
        mockIdentityService
            .Setup(x => x.RetrieveIdentityAsync(userAddress))
            .ReturnsAsync(new Profile("TestUser", "TU", userAddress, "encryptKey", true, new BlockIndex(50)));

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = userAddress,
            BlockIndex = 95,  // Request messages since block 95
            FetchLatest = true  // FEAT-052: Use fetch_latest to test cache path
        };

        // Act
        var result = await service.GetFeedMessagesForAddress(request, CreateMockServerCallContext());

        // Assert
        result.Messages.Should().HaveCount(2);
        result.Messages.Should().OnlyContain(m => m.BlockIndex >= 95,
            "Only messages with BlockIndex >= 95 should be returned");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Sets up the IConfiguration mock to return proper values for Feeds configuration.
    /// FEAT-052: Required for pagination configuration.
    /// </summary>
    private static void SetupConfigurationMock(AutoMocker mocker, int maxMessagesPerResponse = 100)
    {
        var mockConfigSection = new Mock<IConfigurationSection>();
        mockConfigSection.Setup(s => s.Value).Returns(maxMessagesPerResponse.ToString());

        var mockConfiguration = mocker.GetMock<IConfiguration>();
        mockConfiguration
            .Setup(c => c.GetSection("Feeds:MaxMessagesPerResponse"))
            .Returns(mockConfigSection.Object);
    }

    private static Feed CreateFeed(FeedId feedId, string alias, FeedType feedType, long blockIndex)
    {
        return new Feed(feedId, alias, feedType, new BlockIndex(blockIndex));
    }

    private static FeedParticipant CreateFeedParticipant(
        FeedId feedId,
        string participantPublicAddress,
        ParticipantType participantType,
        string encryptedFeedKey,
        Feed feed)
    {
        return new FeedParticipant(feedId, participantPublicAddress, participantType, encryptedFeedKey)
        {
            Feed = feed
        };
    }

    private static FeedMessage CreateFeedMessage(
        FeedId feedId,
        string issuerAddress,
        string messageContent,
        BlockIndex blockIndex)
    {
        return new FeedMessage(
            FeedMessageId: new FeedMessageId(Guid.NewGuid()),
            FeedId: feedId,
            MessageContent: messageContent,
            IssuerPublicAddress: issuerAddress,
            Timestamp: new Timestamp(DateTime.UtcNow),
            BlockIndex: blockIndex);
    }

    private static ServerCallContext CreateMockServerCallContext()
    {
        return new MockServerCallContext();
    }

    /// <summary>
    /// Minimal mock implementation of ServerCallContext for testing.
    /// </summary>
    private class MockServerCallContext : ServerCallContext
    {
        protected override string MethodCore => "TestMethod";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test-peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore => new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new("test", new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
        {
            throw new NotImplementedException();
        }

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
        {
            return Task.CompletedTask;
        }
    }

    #endregion
}

/// <summary>
/// FEAT-050: Tests for GetKeyGenerations cache integration.
/// Verifies cache-first pattern for group feed key generations.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class FeedsGrpcServiceKeyGenerationsCacheTests
{
    #region Cache Hit Tests

    [Fact]
    public async Task GetKeyGenerations_CacheHit_ReturnsFromCache()
    {
        // Arrange
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);
        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = "user-address";

        // Create cached key generations
        var cachedKeyGens = new CachedKeyGenerations
        {
            KeyGenerations = new List<KeyGenerationCacheDto>
            {
                new()
                {
                    Version = 0,
                    ValidFromBlock = 100,
                    ValidToBlock = 200,
                    EncryptedKeysByMember = new Dictionary<string, string>
                    {
                        [userAddress] = "encrypted-key-v0"
                    }
                },
                new()
                {
                    Version = 1,
                    ValidFromBlock = 200,
                    ValidToBlock = null,
                    EncryptedKeysByMember = new Dictionary<string, string>
                    {
                        [userAddress] = "encrypted-key-v1"
                    }
                }
            }
        };

        // Mock cache to return key generations (CACHE HIT)
        mocker.GetMock<IFeedParticipantsCacheService>()
            .Setup(x => x.GetKeyGenerationsAsync(feedId))
            .ReturnsAsync(cachedKeyGens);

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new GetKeyGenerationsRequest
        {
            FeedId = feedId.ToString(),
            UserPublicAddress = userAddress
        };

        // Act
        var result = await service.GetKeyGenerations(request, CreateMockServerCallContext());

        // Assert
        result.KeyGenerations.Should().HaveCount(2);
        result.KeyGenerations[0].KeyGeneration.Should().Be(0);
        result.KeyGenerations[0].EncryptedKey.Should().Be("encrypted-key-v0");
        result.KeyGenerations[1].KeyGeneration.Should().Be(1);
        result.KeyGenerations[1].EncryptedKey.Should().Be("encrypted-key-v1");

        // Verify database was NOT queried
        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.GetAllKeyGenerationsAsync(It.IsAny<FeedId>()),
                Times.Never,
                "Database should NOT be queried on cache hit");
    }

    [Fact]
    public async Task GetKeyGenerations_CacheHit_FiltersKeysByUser()
    {
        // Arrange
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);
        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = "user-address";
        var otherUserAddress = "other-user-address";

        // Create cached key generations with mixed user keys
        var cachedKeyGens = new CachedKeyGenerations
        {
            KeyGenerations = new List<KeyGenerationCacheDto>
            {
                new()
                {
                    Version = 0,
                    ValidFromBlock = 100,
                    ValidToBlock = null,
                    EncryptedKeysByMember = new Dictionary<string, string>
                    {
                        [userAddress] = "user-encrypted-key",
                        [otherUserAddress] = "other-user-encrypted-key"
                    }
                },
                new()
                {
                    Version = 1,
                    ValidFromBlock = 200,
                    ValidToBlock = null,
                    // User doesn't have key for this generation (joined later)
                    EncryptedKeysByMember = new Dictionary<string, string>
                    {
                        [otherUserAddress] = "other-user-encrypted-key-v1"
                    }
                }
            }
        };

        mocker.GetMock<IFeedParticipantsCacheService>()
            .Setup(x => x.GetKeyGenerationsAsync(feedId))
            .ReturnsAsync(cachedKeyGens);

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new GetKeyGenerationsRequest
        {
            FeedId = feedId.ToString(),
            UserPublicAddress = userAddress
        };

        // Act
        var result = await service.GetKeyGenerations(request, CreateMockServerCallContext());

        // Assert - Only returns generations where user has a key
        result.KeyGenerations.Should().HaveCount(1);
        result.KeyGenerations[0].KeyGeneration.Should().Be(0);
        result.KeyGenerations[0].EncryptedKey.Should().Be("user-encrypted-key");
    }

    #endregion

    #region Cache Miss Tests

    [Fact]
    public async Task GetKeyGenerations_CacheMiss_QueriesDatabaseAndPopulatesCache()
    {
        // Arrange
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);
        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = "user-address";

        // Mock cache to return null (CACHE MISS)
        mocker.GetMock<IFeedParticipantsCacheService>()
            .Setup(x => x.GetKeyGenerationsAsync(feedId))
            .ReturnsAsync((CachedKeyGenerations?)null);

        mocker.GetMock<IFeedParticipantsCacheService>()
            .Setup(x => x.SetKeyGenerationsAsync(feedId, It.IsAny<CachedKeyGenerations>()))
            .Returns(Task.CompletedTask);

        // Mock database to return key generations
        var dbKeyGenerations = new List<GroupFeedKeyGenerationEntity>
        {
            new(feedId, 0, new BlockIndex(100), RotationTrigger.Join)
            {
                EncryptedKeys = new List<GroupFeedEncryptedKeyEntity>
                {
                    new(feedId, 0, userAddress, "db-encrypted-key-v0")
                }
            }
        };

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetAllKeyGenerationsAsync(feedId))
            .ReturnsAsync(dbKeyGenerations);

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new GetKeyGenerationsRequest
        {
            FeedId = feedId.ToString(),
            UserPublicAddress = userAddress
        };

        // Act
        var result = await service.GetKeyGenerations(request, CreateMockServerCallContext());

        // Assert
        result.KeyGenerations.Should().HaveCount(1);
        result.KeyGenerations[0].KeyGeneration.Should().Be(0);
        result.KeyGenerations[0].EncryptedKey.Should().Be("db-encrypted-key-v0");

        // Verify database was queried
        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.GetAllKeyGenerationsAsync(feedId),
                Times.Once,
                "Database should be queried on cache miss");

        // Verify cache was populated
        mocker.GetMock<IFeedParticipantsCacheService>()
            .Verify(x => x.SetKeyGenerationsAsync(feedId, It.IsAny<CachedKeyGenerations>()),
                Times.Once,
                "Cache should be populated after database query");
    }

    [Fact]
    public async Task GetKeyGenerations_CacheMiss_EmptyResult_DoesNotPopulateCache()
    {
        // Arrange
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);
        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = "user-address";

        // Mock cache to return null (CACHE MISS)
        mocker.GetMock<IFeedParticipantsCacheService>()
            .Setup(x => x.GetKeyGenerationsAsync(feedId))
            .ReturnsAsync((CachedKeyGenerations?)null);

        // Mock database to return empty list
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetAllKeyGenerationsAsync(feedId))
            .ReturnsAsync(new List<GroupFeedKeyGenerationEntity>());

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new GetKeyGenerationsRequest
        {
            FeedId = feedId.ToString(),
            UserPublicAddress = userAddress
        };

        // Act
        var result = await service.GetKeyGenerations(request, CreateMockServerCallContext());

        // Assert
        result.KeyGenerations.Should().BeEmpty();

        // Verify cache was NOT populated (no point caching empty result)
        mocker.GetMock<IFeedParticipantsCacheService>()
            .Verify(x => x.SetKeyGenerationsAsync(feedId, It.IsAny<CachedKeyGenerations>()),
                Times.Never,
                "Cache should NOT be populated when no key generations exist");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Sets up the IConfiguration mock to return proper values for Feeds configuration.
    /// FEAT-052: Required for pagination configuration.
    /// </summary>
    private static void SetupConfigurationMock(AutoMocker mocker, int maxMessagesPerResponse = 100)
    {
        var mockConfigSection = new Mock<IConfigurationSection>();
        mockConfigSection.Setup(s => s.Value).Returns(maxMessagesPerResponse.ToString());

        var mockConfiguration = mocker.GetMock<IConfiguration>();
        mockConfiguration
            .Setup(c => c.GetSection("Feeds:MaxMessagesPerResponse"))
            .Returns(mockConfigSection.Object);
    }

    private static ServerCallContext CreateMockServerCallContext()
    {
        return new MockServerCallContext();
    }

    /// <summary>
    /// Minimal mock implementation of ServerCallContext for testing.
    /// </summary>
    private class MockServerCallContext : ServerCallContext
    {
        protected override string MethodCore => "TestMethod";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test-peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore => new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new("test", new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
        {
            throw new NotImplementedException();
        }

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
        {
            return Task.CompletedTask;
        }
    }

    #endregion
}
