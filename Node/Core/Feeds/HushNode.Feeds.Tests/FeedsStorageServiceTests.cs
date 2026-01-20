using FluentAssertions;
using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.AutoMock;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for FeedsStorageService storage methods.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class FeedsStorageServiceTests
{
    #region RetrieveFeedsForAddress Tests

    /// <summary>
    /// BUG: RetrieveFeedsForAddress currently only queries the Feeds table.
    /// Group feeds are stored in GroupFeeds/GroupFeedParticipants tables and are NOT returned.
    ///
    /// This test documents the bug: Group feeds should be included in the results.
    /// </summary>
    [Fact]
    public async Task RetrieveFeedsForAddress_WithGroupFeed_ShouldIncludeGroupFeedsFromGroupFeedParticipants()
    {
        // Arrange
        var mocker = new AutoMocker();
        var userAddress = TestDataFactory.CreateAddress();
        var personalFeedId = TestDataFactory.CreateFeedId();
        var groupFeedId = TestDataFactory.CreateFeedId();

        // Create a personal feed (stored in Feeds table)
        var personalFeed = new Feed(personalFeedId, "Personal", FeedType.Personal, new BlockIndex(100));

        // Create a group feed participant (stored in GroupFeedParticipants table)
        var groupFeed = new GroupFeed(groupFeedId, "Tech Friends", "Description", false, new BlockIndex(100), 0);
        groupFeed.Participants.Add(new GroupFeedParticipantEntity(
            groupFeedId, userAddress, ParticipantType.Admin, new BlockIndex(100), null, null));

        var mockRepository = new Mock<IFeedsRepository>();

        // Current behavior: RetrieveFeedsForAddress only returns feeds from Feeds table
        mockRepository
            .Setup(x => x.RetrieveFeedsForAddress(userAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(new[] { personalFeed });

        // New method needed: RetrieveGroupFeedsForAddress to query GroupFeedParticipants
        mockRepository
            .Setup(x => x.RetrieveGroupFeedsForAddress(userAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(new[] { groupFeed });

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.RetrieveFeedsForAddress(userAddress, new BlockIndex(0));

        // Assert - Should include BOTH personal AND group feeds
        result.Should().HaveCount(2);
        result.Should().Contain(f => f.FeedType == FeedType.Personal);
        result.Should().Contain(f => f.FeedType == FeedType.Group);
    }

    [Fact]
    public async Task RetrieveFeedsForAddress_WithOnlyGroupFeeds_ShouldReturnGroupFeeds()
    {
        // Arrange
        var mocker = new AutoMocker();
        var userAddress = TestDataFactory.CreateAddress();
        var groupFeedId = TestDataFactory.CreateFeedId();

        // No personal/chat feeds in Feeds table
        var groupFeed = new GroupFeed(groupFeedId, "Friends Group", "A test group", false, new BlockIndex(100), 0);
        groupFeed.Participants.Add(new GroupFeedParticipantEntity(
            groupFeedId, userAddress, ParticipantType.Member, new BlockIndex(100), null, null));

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.RetrieveFeedsForAddress(userAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(Array.Empty<Feed>());
        mockRepository
            .Setup(x => x.RetrieveGroupFeedsForAddress(userAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(new[] { groupFeed });

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.RetrieveFeedsForAddress(userAddress, new BlockIndex(0));

        // Assert
        result.Should().HaveCount(1);
        result.First().FeedType.Should().Be(FeedType.Group);
        result.First().Alias.Should().Be("Friends Group");
    }

    [Fact]
    public async Task RetrieveFeedsForAddress_WithLeftGroupMember_ShouldExcludeLeftGroups()
    {
        // Arrange
        var mocker = new AutoMocker();
        var userAddress = TestDataFactory.CreateAddress();
        var groupFeedId = TestDataFactory.CreateFeedId();

        // User has left this group (LeftAtBlock is set)
        var groupFeed = new GroupFeed(groupFeedId, "Old Group", "Left this group", false, new BlockIndex(100), 0);
        groupFeed.Participants.Add(new GroupFeedParticipantEntity(
            groupFeedId, userAddress, ParticipantType.Member, new BlockIndex(100),
            LeftAtBlock: new BlockIndex(150), LastLeaveBlock: new BlockIndex(150)));

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.RetrieveFeedsForAddress(userAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(Array.Empty<Feed>());
        // Should NOT return groups where user has left
        mockRepository
            .Setup(x => x.RetrieveGroupFeedsForAddress(userAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(Array.Empty<GroupFeed>());

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.RetrieveFeedsForAddress(userAddress, new BlockIndex(0));

        // Assert - No feeds returned (user left the only group)
        result.Should().BeEmpty();
    }

    #endregion

    #region RetrieveGroupFeedsForAddress Query Logic Tests

    /// <summary>
    /// FEAT-017 BUG: Participant "Paulo Tauri 5" cannot see "Tech Friends" group.
    /// This test verifies that when a user is an ACTIVE participant (Admin/Member),
    /// the group feed is returned by RetrieveFeedsForAddress.
    ///
    /// Expected: Query should match participants where:
    /// - ParticipantPublicAddress matches
    /// - LeftAtBlock is null (active)
    /// - ParticipantType is not Banned
    /// </summary>
    [Fact]
    public async Task RetrieveFeedsForAddress_ParticipantWithAdminRole_ShouldReturnGroupFeed()
    {
        // Arrange
        var mocker = new AutoMocker();
        var participantAddress = TestDataFactory.CreateAddress();
        var groupFeedId = TestDataFactory.CreateFeedId();

        // Create group feed with the participant as Admin (not Owner!)
        var groupFeed = new GroupFeed(groupFeedId, "Tech Friends", "A tech discussion group", false, new BlockIndex(100), 0);
        groupFeed.Participants.Add(new GroupFeedParticipantEntity(
            groupFeedId, participantAddress, ParticipantType.Admin, new BlockIndex(105), null, null));

        var mockRepository = new Mock<IFeedsRepository>();

        // Setup: No personal/chat feeds
        mockRepository
            .Setup(x => x.RetrieveFeedsForAddress(participantAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(Array.Empty<Feed>());

        // Setup: Group feed query should return the group
        mockRepository
            .Setup(x => x.RetrieveGroupFeedsForAddress(participantAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(new[] { groupFeed });

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.RetrieveFeedsForAddress(participantAddress, new BlockIndex(0));

        // Assert - User should see the group feed
        result.Should().HaveCount(1);
        result.First().FeedType.Should().Be(FeedType.Group);
        result.First().FeedId.Should().Be(groupFeedId);
    }

    /// <summary>
    /// FEAT-017 BUG: Verifies the conversion from GroupFeed to Feed includes participants correctly.
    /// The participant should be visible in the Feed.Participants collection.
    /// </summary>
    [Fact]
    public async Task RetrieveFeedsForAddress_GroupFeedConversion_ShouldIncludeActiveParticipants()
    {
        // Arrange
        var mocker = new AutoMocker();
        var ownerAddress = TestDataFactory.CreateAddress();
        var memberAddress = TestDataFactory.CreateAddress();
        var groupFeedId = TestDataFactory.CreateFeedId();

        // Create group feed with Owner and a Member
        var groupFeed = new GroupFeed(groupFeedId, "Tech Friends", "Description", false, new BlockIndex(100), 0);
        groupFeed.Participants.Add(new GroupFeedParticipantEntity(
            groupFeedId, ownerAddress, ParticipantType.Admin, new BlockIndex(100), null, null));
        groupFeed.Participants.Add(new GroupFeedParticipantEntity(
            groupFeedId, memberAddress, ParticipantType.Member, new BlockIndex(105), null, null));

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.RetrieveFeedsForAddress(memberAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(Array.Empty<Feed>());
        mockRepository
            .Setup(x => x.RetrieveGroupFeedsForAddress(memberAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(new[] { groupFeed });

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.RetrieveFeedsForAddress(memberAddress, new BlockIndex(0));

        // Assert - Should return the group with all active participants
        result.Should().HaveCount(1);
        var convertedFeed = result.First();
        convertedFeed.FeedType.Should().Be(FeedType.Group);
        convertedFeed.Participants.Should().HaveCount(2);
        convertedFeed.Participants.Should().Contain(p => p.ParticipantPublicAddress == ownerAddress);
        convertedFeed.Participants.Should().Contain(p => p.ParticipantPublicAddress == memberAddress);
    }

    /// <summary>
    /// FEAT-017: Verifies that left participants (LeftAtBlock != null) are excluded from results.
    /// </summary>
    [Fact]
    public async Task RetrieveFeedsForAddress_ParticipantWithLeftAtBlock_ShouldNotReturnGroupFeed()
    {
        // Arrange
        var mocker = new AutoMocker();
        var participantAddress = TestDataFactory.CreateAddress();
        var groupFeedId = TestDataFactory.CreateFeedId();

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.RetrieveFeedsForAddress(participantAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(Array.Empty<Feed>());
        // When user has left the group, RetrieveGroupFeedsForAddress should return empty
        mockRepository
            .Setup(x => x.RetrieveGroupFeedsForAddress(participantAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(Array.Empty<GroupFeed>());

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.RetrieveFeedsForAddress(participantAddress, new BlockIndex(0));

        // Assert - No feeds returned (user left the group)
        result.Should().BeEmpty();
    }

    /// <summary>
    /// FEAT-017: Verifies that banned participants are excluded from group feed results.
    /// </summary>
    [Fact]
    public async Task RetrieveFeedsForAddress_BannedParticipant_ShouldNotReturnGroupFeed()
    {
        // Arrange
        var mocker = new AutoMocker();
        var bannedUserAddress = TestDataFactory.CreateAddress();
        var groupFeedId = TestDataFactory.CreateFeedId();

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.RetrieveFeedsForAddress(bannedUserAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(Array.Empty<Feed>());
        // Banned users should not see the group
        mockRepository
            .Setup(x => x.RetrieveGroupFeedsForAddress(bannedUserAddress, It.IsAny<BlockIndex>()))
            .ReturnsAsync(Array.Empty<GroupFeed>());

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.RetrieveFeedsForAddress(bannedUserAddress, new BlockIndex(0));

        // Assert - No feeds returned (user is banned)
        result.Should().BeEmpty();
    }

    #endregion

    #region GetMaxKeyGenerationAsync Tests

    [Fact]
    public async Task GetMaxKeyGenerationAsync_WithMultipleKeyGenerations_ShouldReturnMax()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var expectedMax = 5;

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(expectedMax);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.GetMaxKeyGenerationAsync(feedId);

        // Assert
        result.Should().Be(expectedMax);
    }

    [Fact]
    public async Task GetMaxKeyGenerationAsync_WithSingleKeyGeneration_ShouldReturnZero()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.GetMaxKeyGenerationAsync(feedId);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task GetMaxKeyGenerationAsync_WithNonExistentGroup_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync((int?)null);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.GetMaxKeyGenerationAsync(feedId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetMaxKeyGenerationAsync_ShouldUseReadOnlyUnitOfWork()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(2);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        var mockProvider = mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>();
        mockProvider
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        await service.GetMaxKeyGenerationAsync(feedId);

        // Assert
        mockProvider.Verify(x => x.CreateReadOnly(), Times.Once);
        mockRepository.Verify(x => x.GetMaxKeyGenerationAsync(feedId), Times.Once);
    }

    #endregion

    #region GetActiveGroupMemberAddressesAsync Tests

    [Fact]
    public async Task GetActiveGroupMemberAddressesAsync_WithActiveMembers_ShouldReturnAddresses()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var expectedAddresses = new List<string>
        {
            TestDataFactory.CreateAddress(),
            TestDataFactory.CreateAddress(),
            TestDataFactory.CreateAddress()
        };

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(expectedAddresses);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.GetActiveGroupMemberAddressesAsync(feedId);

        // Assert
        result.Should().BeEquivalentTo(expectedAddresses);
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetActiveGroupMemberAddressesAsync_WithNoMembers_ShouldReturnEmptyList()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string>());

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.GetActiveGroupMemberAddressesAsync(feedId);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveGroupMemberAddressesAsync_ShouldUseReadOnlyUnitOfWork()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { TestDataFactory.CreateAddress() });

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        var mockProvider = mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>();
        mockProvider
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        await service.GetActiveGroupMemberAddressesAsync(feedId);

        // Assert
        mockProvider.Verify(x => x.CreateReadOnly(), Times.Once);
        mockRepository.Verify(x => x.GetActiveGroupMemberAddressesAsync(feedId), Times.Once);
    }

    #endregion

    #region CreateKeyRotationAsync Tests

    [Fact]
    public async Task CreateKeyRotationAsync_WithValidKeyGeneration_ShouldCreateAndUpdateGroup()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var keyGeneration = new GroupFeedKeyGenerationEntity(
            feedId,
            KeyGeneration: 3,
            ValidFromBlock: new BlockIndex(100),
            RotationTrigger: RotationTrigger.Join);

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.CreateKeyRotationAsync(keyGeneration))
            .Returns(Task.CompletedTask);
        mockRepository
            .Setup(x => x.UpdateCurrentKeyGenerationAsync(feedId, 3))
            .Returns(Task.CompletedTask);

        var mockUnitOfWork = new Mock<IWritableUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);
        mockUnitOfWork
            .Setup(x => x.CommitAsync())
            .Returns(Task.CompletedTask);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateWritable())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        await service.CreateKeyRotationAsync(keyGeneration);

        // Assert
        mockRepository.Verify(x => x.CreateKeyRotationAsync(keyGeneration), Times.Once);
        mockRepository.Verify(x => x.UpdateCurrentKeyGenerationAsync(feedId, 3), Times.Once);
        mockUnitOfWork.Verify(x => x.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateKeyRotationAsync_ShouldUseWritableUnitOfWork()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var keyGeneration = new GroupFeedKeyGenerationEntity(
            feedId,
            KeyGeneration: 1,
            ValidFromBlock: new BlockIndex(50),
            RotationTrigger: RotationTrigger.Leave);

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.CreateKeyRotationAsync(keyGeneration))
            .Returns(Task.CompletedTask);
        mockRepository
            .Setup(x => x.UpdateCurrentKeyGenerationAsync(feedId, 1))
            .Returns(Task.CompletedTask);

        var mockUnitOfWork = new Mock<IWritableUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);
        mockUnitOfWork
            .Setup(x => x.CommitAsync())
            .Returns(Task.CompletedTask);

        var mockProvider = mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>();
        mockProvider
            .Setup(x => x.CreateWritable())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        await service.CreateKeyRotationAsync(keyGeneration);

        // Assert
        mockProvider.Verify(x => x.CreateWritable(), Times.Once);
    }

    [Fact]
    public async Task CreateKeyRotationAsync_ShouldCommitTransaction()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var keyGeneration = new GroupFeedKeyGenerationEntity(
            feedId,
            KeyGeneration: 5,
            ValidFromBlock: new BlockIndex(200),
            RotationTrigger: RotationTrigger.Ban);

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.CreateKeyRotationAsync(keyGeneration))
            .Returns(Task.CompletedTask);
        mockRepository
            .Setup(x => x.UpdateCurrentKeyGenerationAsync(feedId, 5))
            .Returns(Task.CompletedTask);

        var mockUnitOfWork = new Mock<IWritableUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);
        mockUnitOfWork
            .Setup(x => x.CommitAsync())
            .Returns(Task.CompletedTask);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateWritable())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        await service.CreateKeyRotationAsync(keyGeneration);

        // Assert
        mockUnitOfWork.Verify(x => x.CommitAsync(), Times.Once);
    }

    #endregion

    #region GetFeedIdsForUserAsync Cache Integration Tests (FEAT-049)

    /// <summary>
    /// FEAT-049: Verifies that cached feed IDs are returned on cache hit.
    /// The repository should NOT be called when cache has data.
    /// </summary>
    [Fact]
    public async Task GetFeedIdsForUserAsync_CacheHit_ShouldReturnCachedDataAndNotQueryRepository()
    {
        // Arrange
        var mocker = new AutoMocker();
        var userAddress = TestDataFactory.CreateAddress();
        var cachedFeedIds = new List<FeedId>
        {
            TestDataFactory.CreateFeedId(),
            TestDataFactory.CreateFeedId()
        };

        // Setup cache to return cached data (cache hit)
        mocker.GetMock<IUserFeedsCacheService>()
            .Setup(x => x.GetUserFeedsAsync(userAddress))
            .ReturnsAsync(cachedFeedIds);

        // Setup repository (should NOT be called)
        var mockRepository = new Mock<IFeedsRepository>();
        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.GetFeedIdsForUserAsync(userAddress);

        // Assert
        result.Should().BeEquivalentTo(cachedFeedIds);
        result.Should().HaveCount(2);

        // Verify repository was NOT called (cache hit = no DB query)
        mockRepository.Verify(x => x.GetFeedIdsForUserAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// FEAT-049: Verifies that on cache miss, the repository is queried and cache is populated.
    /// </summary>
    [Fact]
    public async Task GetFeedIdsForUserAsync_CacheMiss_ShouldQueryRepositoryAndPopulateCache()
    {
        // Arrange
        var mocker = new AutoMocker();
        var userAddress = TestDataFactory.CreateAddress();
        var dbFeedIds = new List<FeedId>
        {
            TestDataFactory.CreateFeedId(),
            TestDataFactory.CreateFeedId(),
            TestDataFactory.CreateFeedId()
        };

        // Setup cache to return null (cache miss)
        mocker.GetMock<IUserFeedsCacheService>()
            .Setup(x => x.GetUserFeedsAsync(userAddress))
            .ReturnsAsync((IReadOnlyList<FeedId>?)null);

        mocker.GetMock<IUserFeedsCacheService>()
            .Setup(x => x.SetUserFeedsAsync(userAddress, It.IsAny<IEnumerable<FeedId>>()))
            .Returns(Task.CompletedTask);

        // Setup repository to return data
        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.GetFeedIdsForUserAsync(userAddress))
            .ReturnsAsync(dbFeedIds);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.GetFeedIdsForUserAsync(userAddress);

        // Assert
        result.Should().BeEquivalentTo(dbFeedIds);
        result.Should().HaveCount(3);

        // Verify repository was called (cache miss = DB query)
        mockRepository.Verify(x => x.GetFeedIdsForUserAsync(userAddress), Times.Once);

        // Verify cache was populated
        mocker.GetMock<IUserFeedsCacheService>()
            .Verify(x => x.SetUserFeedsAsync(userAddress, It.Is<IEnumerable<FeedId>>(ids => ids.Count() == 3)), Times.Once);
    }

    /// <summary>
    /// FEAT-049: Verifies that empty results are NOT cached.
    /// This prevents caching an empty state that may change when the user joins feeds.
    /// </summary>
    [Fact]
    public async Task GetFeedIdsForUserAsync_EmptyResult_ShouldNotCacheEmptyList()
    {
        // Arrange
        var mocker = new AutoMocker();
        var userAddress = TestDataFactory.CreateAddress();
        var emptyFeedIds = new List<FeedId>();

        // Setup cache to return null (cache miss)
        mocker.GetMock<IUserFeedsCacheService>()
            .Setup(x => x.GetUserFeedsAsync(userAddress))
            .ReturnsAsync((IReadOnlyList<FeedId>?)null);

        // Setup repository to return empty list
        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.GetFeedIdsForUserAsync(userAddress))
            .ReturnsAsync(emptyFeedIds);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.GetFeedIdsForUserAsync(userAddress);

        // Assert
        result.Should().BeEmpty();

        // Verify cache was NOT populated (empty result = no cache)
        mocker.GetMock<IUserFeedsCacheService>()
            .Verify(x => x.SetUserFeedsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<FeedId>>()), Times.Never);
    }

    /// <summary>
    /// FEAT-049: Verifies that cache service exceptions are handled gracefully.
    /// On Redis failure, the method should fall back to PostgreSQL.
    /// </summary>
    [Fact]
    public async Task GetFeedIdsForUserAsync_CacheException_ShouldFallbackToRepository()
    {
        // Arrange
        var mocker = new AutoMocker();
        var userAddress = TestDataFactory.CreateAddress();
        var dbFeedIds = new List<FeedId>
        {
            TestDataFactory.CreateFeedId()
        };

        // Setup cache to throw exception (simulating Redis failure)
        mocker.GetMock<IUserFeedsCacheService>()
            .Setup(x => x.GetUserFeedsAsync(userAddress))
            .ThrowsAsync(new Exception("Redis connection failed"));

        // Setup repository to return data
        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.GetFeedIdsForUserAsync(userAddress))
            .ReturnsAsync(dbFeedIds);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.GetFeedIdsForUserAsync(userAddress);

        // Assert - Should return DB result despite cache failure
        result.Should().BeEquivalentTo(dbFeedIds);
        result.Should().HaveCount(1);

        // Verify repository was called (fallback to DB)
        mockRepository.Verify(x => x.GetFeedIdsForUserAsync(userAddress), Times.Once);
    }

    /// <summary>
    /// FEAT-049: Verifies that cache population failure does not affect the return value.
    /// If SetUserFeedsAsync throws, the DB result should still be returned.
    /// </summary>
    [Fact]
    public async Task GetFeedIdsForUserAsync_CachePopulationFailure_ShouldStillReturnDbResult()
    {
        // Arrange
        var mocker = new AutoMocker();
        var userAddress = TestDataFactory.CreateAddress();
        var dbFeedIds = new List<FeedId>
        {
            TestDataFactory.CreateFeedId(),
            TestDataFactory.CreateFeedId()
        };

        // Setup cache to return null (cache miss)
        mocker.GetMock<IUserFeedsCacheService>()
            .Setup(x => x.GetUserFeedsAsync(userAddress))
            .ReturnsAsync((IReadOnlyList<FeedId>?)null);

        // Setup cache population to throw (simulating Redis write failure)
        mocker.GetMock<IUserFeedsCacheService>()
            .Setup(x => x.SetUserFeedsAsync(userAddress, It.IsAny<IEnumerable<FeedId>>()))
            .ThrowsAsync(new Exception("Redis write failed"));

        // Setup repository to return data
        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.GetFeedIdsForUserAsync(userAddress))
            .ReturnsAsync(dbFeedIds);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.GetFeedIdsForUserAsync(userAddress);

        // Assert - Should return DB result despite cache population failure
        result.Should().BeEquivalentTo(dbFeedIds);
        result.Should().HaveCount(2);
    }

    #endregion
}
