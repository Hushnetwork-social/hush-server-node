using FluentAssertions;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.Feeds.gRPC;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushNode.Identity;
using HushNode.Identity.Storage;
using HushNode.Reactions.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Moq;
using Olimpo;
using Moq.AutoMock;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for FeedsGrpcService to ensure all feed types are properly handled.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class FeedsGrpcServiceTests
{
    #region GetFeedsForAddress Tests

    [Fact]
    public async Task GetFeedsForAddress_WithPersonalFeed_ShouldReturnFeedWithCorrectAlias()
    {
        // Arrange
        var mocker = new AutoMocker();
        var userAddress = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        var personalFeed = CreateFeed(feedId, "Personal", FeedType.Personal, 100);
        personalFeed.Participants = new[]
        {
            CreateFeedParticipant(feedId, userAddress, ParticipantType.Owner, "encryptedKey", personalFeed)
        };

        var mockFeedsStorageService = mocker.GetMock<IFeedsStorageService>();
        mockFeedsStorageService
            .Setup(x => x.RetrieveFeedsForAddress(userAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(new[] { personalFeed });

        var mockIdentityService = mocker.GetMock<IIdentityService>();
        mockIdentityService
            .Setup(x => x.RetrieveIdentityAsync(userAddress))
            .ReturnsAsync(new Profile("TestUser", "TU", userAddress, "encryptKey", true, new BlockIndex(50)));

        // FEAT-051: Mock read position storage to return empty dictionary
        var mockReadPositionService = mocker.GetMock<IFeedReadPositionStorageService>();
        mockReadPositionService
            .Setup(x => x.GetReadPositionsForUserAsync(userAddress))
            .ReturnsAsync(new Dictionary<FeedId, BlockIndex>());

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new GetFeedForAddressRequest { ProfilePublicKey = userAddress, BlockIndex = 0 };

        // Act
        var result = await service.GetFeedsForAddress(request, CreateMockServerCallContext());

        // Assert
        result.Feeds.Should().HaveCount(1);
        result.Feeds[0].FeedType.Should().Be((int)FeedType.Personal);
        result.Feeds[0].FeedTitle.Should().Contain("(YOU)");
    }

    [Fact]
    public async Task GetFeedsForAddress_WithChatFeed_ShouldReturnFeedWithOtherParticipantName()
    {
        // Arrange
        var mocker = new AutoMocker();
        var userAddress = TestDataFactory.CreateAddress();
        var otherUserAddress = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        var chatFeed = CreateFeed(feedId, "Chat", FeedType.Chat, 100);
        chatFeed.Participants = new[]
        {
            CreateFeedParticipant(feedId, userAddress, ParticipantType.Owner, "encryptedKey", chatFeed),
            CreateFeedParticipant(feedId, otherUserAddress, ParticipantType.Member, "encryptedKey2", chatFeed)
        };

        var mockFeedsStorageService = mocker.GetMock<IFeedsStorageService>();
        mockFeedsStorageService
            .Setup(x => x.RetrieveFeedsForAddress(userAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(new[] { chatFeed });

        var mockIdentityService = mocker.GetMock<IIdentityService>();
        mockIdentityService
            .Setup(x => x.RetrieveIdentityAsync(otherUserAddress))
            .ReturnsAsync(new Profile("OtherUser", "OU", otherUserAddress, "encryptKey", true, new BlockIndex(50)));

        // FEAT-051: Mock read position storage to return empty dictionary
        var mockReadPositionService = mocker.GetMock<IFeedReadPositionStorageService>();
        mockReadPositionService
            .Setup(x => x.GetReadPositionsForUserAsync(userAddress))
            .ReturnsAsync(new Dictionary<FeedId, BlockIndex>());

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new GetFeedForAddressRequest { ProfilePublicKey = userAddress, BlockIndex = 0 };

        // Act
        var result = await service.GetFeedsForAddress(request, CreateMockServerCallContext());

        // Assert
        result.Feeds.Should().HaveCount(1);
        result.Feeds[0].FeedType.Should().Be((int)FeedType.Chat);
        result.Feeds[0].FeedTitle.Should().Be("OtherUser");
    }

    #region FEAT-051: LastReadBlockIndex Tests

    [Fact]
    public async Task GetFeedsForAddress_WithReadPositions_ShouldIncludeLastReadBlockIndex()
    {
        // Arrange
        var mocker = new AutoMocker();
        var userAddress = TestDataFactory.CreateAddress();
        var feedIdWithPosition = TestDataFactory.CreateFeedId();
        var feedIdWithoutPosition = TestDataFactory.CreateFeedId();

        var feedWithPosition = CreateFeed(feedIdWithPosition, "Feed With Position", FeedType.Personal, 100);
        feedWithPosition.Participants = new[]
        {
            CreateFeedParticipant(feedIdWithPosition, userAddress, ParticipantType.Owner, "encryptedKey", feedWithPosition)
        };

        var feedWithoutPosition = CreateFeed(feedIdWithoutPosition, "Feed Without Position", FeedType.Personal, 101);
        feedWithoutPosition.Participants = new[]
        {
            CreateFeedParticipant(feedIdWithoutPosition, userAddress, ParticipantType.Owner, "encryptedKey", feedWithoutPosition)
        };

        var mockFeedsStorageService = mocker.GetMock<IFeedsStorageService>();
        mockFeedsStorageService
            .Setup(x => x.RetrieveFeedsForAddress(userAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(new[] { feedWithPosition, feedWithoutPosition });

        // FEAT-051: Mock read position storage to return position for one feed
        var mockReadPositionService = mocker.GetMock<IFeedReadPositionStorageService>();
        mockReadPositionService
            .Setup(x => x.GetReadPositionsForUserAsync(userAddress))
            .ReturnsAsync(new Dictionary<FeedId, BlockIndex>
            {
                { feedIdWithPosition, new BlockIndex(500) }
                // feedIdWithoutPosition not in dictionary - should default to 0
            });

        var mockIdentityService = mocker.GetMock<IIdentityService>();
        mockIdentityService
            .Setup(x => x.RetrieveIdentityAsync(userAddress))
            .ReturnsAsync(new Profile("TestUser", "TU", userAddress, "encryptKey", true, new BlockIndex(50)));

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new GetFeedForAddressRequest { ProfilePublicKey = userAddress, BlockIndex = 0 };

        // Act
        var result = await service.GetFeedsForAddress(request, CreateMockServerCallContext());

        // Assert
        result.Feeds.Should().HaveCount(2);

        var feedWithPositionResult = result.Feeds.First(f => f.FeedId == feedIdWithPosition.ToString());
        feedWithPositionResult.LastReadBlockIndex.Should().Be(500, "Feed with read position should have LastReadBlockIndex = 500");

        var feedWithoutPositionResult = result.Feeds.First(f => f.FeedId == feedIdWithoutPosition.ToString());
        feedWithoutPositionResult.LastReadBlockIndex.Should().Be(0, "Feed without read position should have LastReadBlockIndex = 0");
    }

    [Fact]
    public async Task GetFeedsForAddress_WhenReadPositionServiceFails_ShouldReturnFeedsWithZeroReadPosition()
    {
        // Arrange
        var mocker = new AutoMocker();
        var userAddress = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        var feed = CreateFeed(feedId, "Test Feed", FeedType.Personal, 100);
        feed.Participants = new[]
        {
            CreateFeedParticipant(feedId, userAddress, ParticipantType.Owner, "encryptedKey", feed)
        };

        var mockFeedsStorageService = mocker.GetMock<IFeedsStorageService>();
        mockFeedsStorageService
            .Setup(x => x.RetrieveFeedsForAddress(userAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(new[] { feed });

        // FEAT-051: Mock read position storage to throw exception (simulating Redis/DB failure)
        var mockReadPositionService = mocker.GetMock<IFeedReadPositionStorageService>();
        mockReadPositionService
            .Setup(x => x.GetReadPositionsForUserAsync(userAddress))
            .ThrowsAsync(new Exception("Redis connection failed"));

        var mockIdentityService = mocker.GetMock<IIdentityService>();
        mockIdentityService
            .Setup(x => x.RetrieveIdentityAsync(userAddress))
            .ReturnsAsync(new Profile("TestUser", "TU", userAddress, "encryptKey", true, new BlockIndex(50)));

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new GetFeedForAddressRequest { ProfilePublicKey = userAddress, BlockIndex = 0 };

        // Act
        var result = await service.GetFeedsForAddress(request, CreateMockServerCallContext());

        // Assert - should still return feeds with default LastReadBlockIndex = 0
        result.Feeds.Should().HaveCount(1);
        result.Feeds[0].LastReadBlockIndex.Should().Be(0, "When read position service fails, LastReadBlockIndex should default to 0");
    }

    #endregion

    /// <summary>
    /// This test verifies that Group feeds are correctly returned by GetFeedsForAddress.
    ///
    /// BUG IDENTIFIED: FeedsGrpcService.GetFeedsForAddress throws InvalidOperationException
    /// for FeedType.Group because the switch statement doesn't handle this case.
    ///
    /// Expected: Group feed should be returned with the group title as alias.
    /// Actual: Throws "the FeedTYype Group is not supported" exception.
    /// </summary>
    [Fact]
    public async Task GetFeedsForAddress_WithGroupFeed_ShouldReturnFeedWithGroupTitle()
    {
        // Arrange
        var mocker = new AutoMocker();
        var userAddress = TestDataFactory.CreateAddress();
        var otherMemberAddress = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();
        var groupTitle = "Tech Friends";

        // Create a Group feed with the user as a participant
        var groupFeed = CreateFeed(feedId, groupTitle, FeedType.Group, 100);
        groupFeed.Participants = new[]
        {
            CreateFeedParticipant(feedId, userAddress, ParticipantType.Admin, "encryptedKey", groupFeed),
            CreateFeedParticipant(feedId, otherMemberAddress, ParticipantType.Member, "encryptedKey2", groupFeed)
        };

        // Mock the GroupFeed lookup to return the group's title
        var mockFeedsStorageService = mocker.GetMock<IFeedsStorageService>();
        mockFeedsStorageService
            .Setup(x => x.RetrieveFeedsForAddress(userAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(new[] { groupFeed });

        // Mock GetGroupFeedAsync to return group details with title
        mockFeedsStorageService
            .Setup(x => x.GetGroupFeedAsync(feedId))
            .ReturnsAsync(new GroupFeed(feedId, groupTitle, "Test Description", false, new BlockIndex(100), 0));

        // FEAT-051: Mock read position storage to return empty dictionary
        var mockReadPositionService = mocker.GetMock<IFeedReadPositionStorageService>();
        mockReadPositionService
            .Setup(x => x.GetReadPositionsForUserAsync(userAddress))
            .ReturnsAsync(new Dictionary<FeedId, BlockIndex>());

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new GetFeedForAddressRequest { ProfilePublicKey = userAddress, BlockIndex = 0 };

        // Act
        var result = await service.GetFeedsForAddress(request, CreateMockServerCallContext());

        // Assert
        result.Feeds.Should().HaveCount(1);
        result.Feeds[0].FeedType.Should().Be((int)FeedType.Group);
        result.Feeds[0].FeedTitle.Should().Be(groupTitle);
        result.Feeds[0].FeedId.Should().Be(feedId.ToString());
    }

    [Fact]
    public async Task GetFeedsForAddress_WithMixedFeedTypes_ShouldReturnAllFeeds()
    {
        // Arrange
        var mocker = new AutoMocker();
        var userAddress = TestDataFactory.CreateAddress();
        var otherUserAddress = TestDataFactory.CreateAddress();
        var groupMemberAddress = TestDataFactory.CreateAddress();

        var personalFeedId = TestDataFactory.CreateFeedId();
        var chatFeedId = TestDataFactory.CreateFeedId();
        var groupFeedId = TestDataFactory.CreateFeedId();

        var personalFeed = CreateFeed(personalFeedId, "Personal", FeedType.Personal, 100);
        personalFeed.Participants = new[]
        {
            CreateFeedParticipant(personalFeedId, userAddress, ParticipantType.Owner, "encryptedKey", personalFeed)
        };

        var chatFeed = CreateFeed(chatFeedId, "Chat", FeedType.Chat, 101);
        chatFeed.Participants = new[]
        {
            CreateFeedParticipant(chatFeedId, userAddress, ParticipantType.Owner, "encryptedKey", chatFeed),
            CreateFeedParticipant(chatFeedId, otherUserAddress, ParticipantType.Member, "encryptedKey2", chatFeed)
        };

        var groupFeed = CreateFeed(groupFeedId, "My Group", FeedType.Group, 102);
        groupFeed.Participants = new[]
        {
            CreateFeedParticipant(groupFeedId, userAddress, ParticipantType.Admin, "encryptedKey", groupFeed),
            CreateFeedParticipant(groupFeedId, groupMemberAddress, ParticipantType.Member, "encryptedKey2", groupFeed)
        };

        var mockFeedsStorageService = mocker.GetMock<IFeedsStorageService>();
        mockFeedsStorageService
            .Setup(x => x.RetrieveFeedsForAddress(userAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(new[] { personalFeed, chatFeed, groupFeed });

        mockFeedsStorageService
            .Setup(x => x.GetGroupFeedAsync(groupFeedId))
            .ReturnsAsync(new GroupFeed(groupFeedId, "My Group", "Description", false, new BlockIndex(102), 0));

        var mockIdentityService = mocker.GetMock<IIdentityService>();
        mockIdentityService
            .Setup(x => x.RetrieveIdentityAsync(userAddress))
            .ReturnsAsync(new Profile("TestUser", "TU", userAddress, "encryptKey", true, new BlockIndex(50)));
        mockIdentityService
            .Setup(x => x.RetrieveIdentityAsync(otherUserAddress))
            .ReturnsAsync(new Profile("OtherUser", "OU", otherUserAddress, "encryptKey", true, new BlockIndex(50)));

        // FEAT-051: Mock read position storage to return empty dictionary
        var mockReadPositionService = mocker.GetMock<IFeedReadPositionStorageService>();
        mockReadPositionService
            .Setup(x => x.GetReadPositionsForUserAsync(userAddress))
            .ReturnsAsync(new Dictionary<FeedId, BlockIndex>());

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new GetFeedForAddressRequest { ProfilePublicKey = userAddress, BlockIndex = 0 };

        // Act
        var result = await service.GetFeedsForAddress(request, CreateMockServerCallContext());

        // Assert
        result.Feeds.Should().HaveCount(3);
        result.Feeds.Should().Contain(f => f.FeedType == (int)FeedType.Personal);
        result.Feeds.Should().Contain(f => f.FeedType == (int)FeedType.Chat);
        result.Feeds.Should().Contain(f => f.FeedType == (int)FeedType.Group);
    }

    #endregion

    #region AddMemberToGroupFeed Tests

    [Fact]
    public async Task AddMemberToGroupFeed_WhenSuccessful_ShouldUpdateFeedBlockIndex()
    {
        // Arrange
        var mocker = new AutoMocker();
        var adminAddress = TestDataFactory.CreateAddress();
        var newMemberAddress = TestDataFactory.CreateAddress();
        // Generate valid ECIES keys for testing
        var adminEncryptKeys = new EncryptKeys();
        var newMemberEncryptKeys = new EncryptKeys();
        var feedId = TestDataFactory.CreateFeedId();
        var currentBlockIndex = new BlockIndex(500);

        // Setup mock for blockchain cache to provide current block
        var mockBlockchainCache = mocker.GetMock<HushNode.Caching.IBlockchainCache>();
        mockBlockchainCache.Setup(x => x.LastBlockIndex).Returns(currentBlockIndex);

        // Setup mock for group feed
        var groupFeed = new GroupFeed(feedId, "Test Group", "Description", false, new BlockIndex(100), 1);
        groupFeed.Participants.Add(new GroupFeedParticipantEntity(
            feedId, adminAddress, ParticipantType.Admin, new BlockIndex(100), null, null));

        var mockFeedsStorageService = mocker.GetMock<IFeedsStorageService>();
        mockFeedsStorageService
            .Setup(x => x.GetGroupFeedAsync(feedId))
            .ReturnsAsync(groupFeed);
        mockFeedsStorageService
            .Setup(x => x.IsAdminAsync(feedId, adminAddress))
            .ReturnsAsync(true);
        mockFeedsStorageService
            .Setup(x => x.GetParticipantWithHistoryAsync(feedId, newMemberAddress))
            .ReturnsAsync((GroupFeedParticipantEntity?)null);
        mockFeedsStorageService
            .Setup(x => x.AddParticipantAsync(feedId, It.IsAny<GroupFeedParticipantEntity>()))
            .Returns(Task.CompletedTask);
        mockFeedsStorageService
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(1);
        mockFeedsStorageService
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { adminAddress, newMemberAddress });
        mockFeedsStorageService
            .Setup(x => x.CreateKeyRotationAsync(It.IsAny<GroupFeedKeyGenerationEntity>()))
            .Returns(Task.CompletedTask);
        mockFeedsStorageService
            .Setup(x => x.UpdateFeedBlockIndexAsync(feedId, currentBlockIndex))
            .Returns(Task.CompletedTask);

        // Setup identity storage service for encryption keys - return identity for admin and new member
        var mockIdentityStorageService = mocker.GetMock<IIdentityStorageService>();
        mockIdentityStorageService
            .Setup(x => x.RetrieveIdentityAsync(adminAddress))
            .ReturnsAsync(new Profile("Admin", "A", adminAddress, adminEncryptKeys.PublicKey, true, new BlockIndex(50)));
        mockIdentityStorageService
            .Setup(x => x.RetrieveIdentityAsync(newMemberAddress))
            .ReturnsAsync(new Profile("NewMember", "NM", newMemberAddress, newMemberEncryptKeys.PublicKey, true, new BlockIndex(50)));

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new AddMemberToGroupFeedRequest
        {
            FeedId = feedId.ToString(),
            AdminPublicAddress = adminAddress,
            NewMemberPublicAddress = newMemberAddress,
            NewMemberPublicEncryptKey = newMemberEncryptKeys.PublicKey
        };

        // Act
        var result = await service.AddMemberToGroupFeed(request, CreateMockServerCallContext());

        // Assert
        result.Success.Should().BeTrue(because: result.Message);
        // Verify that UpdateFeedBlockIndexAsync was called with the current block
        mockFeedsStorageService.Verify(
            x => x.UpdateFeedBlockIndexAsync(feedId, currentBlockIndex),
            Times.Once,
            "UpdateFeedBlockIndexAsync should be called to notify other clients of the membership change");
    }

    [Fact]
    public async Task AddMemberToGroupFeed_WhenKeyRotationFails_ShouldNotUpdateFeedBlockIndex()
    {
        // Arrange
        var mocker = new AutoMocker();
        var adminAddress = TestDataFactory.CreateAddress();
        var newMemberAddress = TestDataFactory.CreateAddress();
        var newMemberEncryptKey = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();
        var currentBlockIndex = new BlockIndex(500);

        var mockBlockchainCache = mocker.GetMock<HushNode.Caching.IBlockchainCache>();
        mockBlockchainCache.Setup(x => x.LastBlockIndex).Returns(currentBlockIndex);

        var groupFeed = new GroupFeed(feedId, "Test Group", "Description", false, new BlockIndex(100), 1);
        groupFeed.Participants.Add(new GroupFeedParticipantEntity(
            feedId, adminAddress, ParticipantType.Admin, new BlockIndex(100), null, null));

        var mockFeedsStorageService = mocker.GetMock<IFeedsStorageService>();
        mockFeedsStorageService
            .Setup(x => x.GetGroupFeedAsync(feedId))
            .ReturnsAsync(groupFeed);
        mockFeedsStorageService
            .Setup(x => x.IsAdminAsync(feedId, adminAddress))
            .ReturnsAsync(true);
        mockFeedsStorageService
            .Setup(x => x.GetParticipantWithHistoryAsync(feedId, newMemberAddress))
            .ReturnsAsync((GroupFeedParticipantEntity?)null);
        mockFeedsStorageService
            .Setup(x => x.AddParticipantAsync(feedId, It.IsAny<GroupFeedParticipantEntity>()))
            .Returns(Task.CompletedTask);
        mockFeedsStorageService
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(1);
        // Return empty list to cause key rotation to fail (no active members)
        mockFeedsStorageService
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string>());

        var mockIdentityService = mocker.GetMock<IIdentityService>();
        mockIdentityService
            .Setup(x => x.RetrieveIdentityAsync(newMemberAddress))
            .ReturnsAsync(new Profile("NewMember", "NM", newMemberAddress, newMemberEncryptKey, true, new BlockIndex(50)));

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new AddMemberToGroupFeedRequest
        {
            FeedId = feedId.ToString(),
            AdminPublicAddress = adminAddress,
            NewMemberPublicAddress = newMemberAddress,
            NewMemberPublicEncryptKey = newMemberEncryptKey
        };

        // Act
        var result = await service.AddMemberToGroupFeed(request, CreateMockServerCallContext());

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("key distribution failed");
        // Verify that UpdateFeedBlockIndexAsync was NOT called when key rotation fails
        mockFeedsStorageService.Verify(
            x => x.UpdateFeedBlockIndexAsync(It.IsAny<FeedId>(), It.IsAny<BlockIndex>()),
            Times.Never,
            "UpdateFeedBlockIndexAsync should NOT be called when key rotation fails");
    }

    [Fact]
    public async Task AddMemberToGroupFeed_WhenNonAdmin_ShouldNotUpdateFeedBlockIndex()
    {
        // Arrange
        var mocker = new AutoMocker();
        var nonAdminAddress = TestDataFactory.CreateAddress();
        var newMemberAddress = TestDataFactory.CreateAddress();
        var newMemberEncryptKey = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        var groupFeed = new GroupFeed(feedId, "Test Group", "Description", false, new BlockIndex(100), 1);
        groupFeed.Participants.Add(new GroupFeedParticipantEntity(
            feedId, nonAdminAddress, ParticipantType.Member, new BlockIndex(100), null, null));

        var mockFeedsStorageService = mocker.GetMock<IFeedsStorageService>();
        mockFeedsStorageService
            .Setup(x => x.GetGroupFeedAsync(feedId))
            .ReturnsAsync(groupFeed);
        mockFeedsStorageService
            .Setup(x => x.IsAdminAsync(feedId, nonAdminAddress))
            .ReturnsAsync(false);

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var request = new AddMemberToGroupFeedRequest
        {
            FeedId = feedId.ToString(),
            AdminPublicAddress = nonAdminAddress,
            NewMemberPublicAddress = newMemberAddress,
            NewMemberPublicEncryptKey = newMemberEncryptKey
        };

        // Act
        var result = await service.AddMemberToGroupFeed(request, CreateMockServerCallContext());

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("administrator");
        // Verify that UpdateFeedBlockIndexAsync was NOT called for non-admin
        mockFeedsStorageService.Verify(
            x => x.UpdateFeedBlockIndexAsync(It.IsAny<FeedId>(), It.IsAny<BlockIndex>()),
            Times.Never,
            "UpdateFeedBlockIndexAsync should NOT be called when requester is not an admin");
    }

    #endregion

    #region Helper Methods

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
