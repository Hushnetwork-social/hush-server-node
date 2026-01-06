using FluentAssertions;
using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Moq;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for the "rejoin after leave" scenario.
///
/// Expected behavior:
/// 1. Admin can add a member back who previously left
/// 2. The rejoining member gets a new JoinedAtBlock
/// 3. The rejoining member can only decrypt messages from:
///    - Their original membership period (before leaving)
///    - After they rejoin
/// 4. Messages sent while they were gone remain inaccessible
/// </summary>
public class RejoinAfterLeaveTests
{
    #region Admin Re-Adding Member Who Left

    [Fact]
    public void ContentHandler_AdminAddingFormerMember_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var formerMemberAddress = TestDataFactory.CreateAddress();
        var encryptedKey = TestDataFactory.CreateEncryptedKey();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        // Former member who left at block 200
        var formerMember = TestDataFactory.CreateParticipantEntityWithHistory(
            feedId,
            formerMemberAddress,
            ParticipantType.Member,
            leftAtBlock: new BlockIndex(200),
            lastLeaveBlock: new BlockIndex(200));

        MockServices.ConfigureFeedsStorageForAddMember(mocker, groupFeed, adminParticipant, formerMember);

        var handler = mocker.CreateInstance<AddMemberToGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateAddMemberToGroupFeedPayload(feedId, adminAddress, formerMemberAddress, encryptedKey);
        var transaction = TestDataFactory.CreateAddMemberToGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert - Admin CAN add back a member who left
        result.Should().NotBeNull("admin should be able to add back a member who left");
    }

    [Fact]
    public async Task TransactionHandler_ReaddFormerMember_ShouldCallUpdateParticipantRejoin()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var formerMemberAddress = TestDataFactory.CreateAddress();
        var encryptedKey = TestDataFactory.CreateEncryptedKey();
        var currentBlock = new BlockIndex(500);

        // Former member who left at block 200
        var formerMember = TestDataFactory.CreateParticipantEntityWithHistory(
            feedId,
            formerMemberAddress,
            ParticipantType.Member,
            leftAtBlock: new BlockIndex(200),
            lastLeaveBlock: new BlockIndex(200));

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(currentBlock);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetParticipantWithHistoryAsync(feedId, formerMemberAddress))
            .ReturnsAsync(formerMember);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.UpdateParticipantRejoinAsync(
                It.IsAny<FeedId>(),
                It.IsAny<string>(),
                It.IsAny<BlockIndex>(),
                It.IsAny<ParticipantType>()))
            .Returns(Task.CompletedTask);

        var handler = mocker.CreateInstance<AddMemberToGroupFeedTransactionHandler>();
        var payload = TestDataFactory.CreateAddMemberToGroupFeedPayload(feedId, adminAddress, formerMemberAddress, encryptedKey);
        var transaction = TestDataFactory.CreateAddMemberToGroupFeedValidatedTransaction(payload, adminAddress);

        // Act
        await handler.HandleAddMemberToGroupFeedTransactionAsync(transaction);

        // Assert - Should call UpdateParticipantRejoinAsync, NOT AddParticipantAsync
        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.UpdateParticipantRejoinAsync(
                feedId,
                formerMemberAddress,
                currentBlock,
                ParticipantType.Member),
                Times.Once);

        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.AddParticipantAsync(
                It.IsAny<FeedId>(),
                It.IsAny<GroupFeedParticipantEntity>()),
                Times.Never);
    }

    [Fact]
    public async Task TransactionHandler_AddNewMember_ShouldCallAddParticipant()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var newMemberAddress = TestDataFactory.CreateAddress();
        var encryptedKey = TestDataFactory.CreateEncryptedKey();
        var currentBlock = new BlockIndex(500);

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(currentBlock);

        // No existing participant - new member
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetParticipantWithHistoryAsync(feedId, newMemberAddress))
            .ReturnsAsync((GroupFeedParticipantEntity?)null);

        GroupFeedParticipantEntity? capturedParticipant = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.AddParticipantAsync(It.IsAny<FeedId>(), It.IsAny<GroupFeedParticipantEntity>()))
            .Callback<FeedId, GroupFeedParticipantEntity>((_, p) => capturedParticipant = p)
            .Returns(Task.CompletedTask);

        var handler = mocker.CreateInstance<AddMemberToGroupFeedTransactionHandler>();
        var payload = TestDataFactory.CreateAddMemberToGroupFeedPayload(feedId, adminAddress, newMemberAddress, encryptedKey);
        var transaction = TestDataFactory.CreateAddMemberToGroupFeedValidatedTransaction(payload, adminAddress);

        // Act
        await handler.HandleAddMemberToGroupFeedTransactionAsync(transaction);

        // Assert - Should call AddParticipantAsync for new members
        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.AddParticipantAsync(feedId, It.IsAny<GroupFeedParticipantEntity>()), Times.Once);

        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.UpdateParticipantRejoinAsync(
                It.IsAny<FeedId>(),
                It.IsAny<string>(),
                It.IsAny<BlockIndex>(),
                It.IsAny<ParticipantType>()),
                Times.Never);

        capturedParticipant.Should().NotBeNull();
        capturedParticipant!.ParticipantPublicAddress.Should().Be(newMemberAddress);
        capturedParticipant.ParticipantType.Should().Be(ParticipantType.Member);
        capturedParticipant.JoinedAtBlock.Should().Be(currentBlock);
        capturedParticipant.LeftAtBlock.Should().BeNull();
    }

    #endregion

    #region Self-Rejoin After Cooldown

    [Fact]
    public void ContentHandler_UserRejoinAfterCooldown_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();
        var publicGroup = TestDataFactory.CreatePublicGroupFeed(feedId);

        // User left at block 300, current block is 500 (200 blocks > 100 cooldown)
        var formerMember = TestDataFactory.CreateParticipantEntityWithHistory(
            feedId,
            userAddress,
            ParticipantType.Member,
            leftAtBlock: new BlockIndex(300),
            lastLeaveBlock: new BlockIndex(300));

        MockServices.ConfigureFeedsStorageForJoinGroup(mocker, publicGroup, formerMember, currentBlock: 500);

        var handler = mocker.CreateInstance<JoinGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateJoinGroupFeedPayload(feedId, userAddress);
        var transaction = TestDataFactory.CreateJoinGroupFeedSignedTransaction(payload, userAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull("user should be able to self-rejoin after cooldown period");
    }

    [Fact]
    public void ContentHandler_UserRejoinBeforeCooldown_ShouldFail()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();
        var publicGroup = TestDataFactory.CreatePublicGroupFeed(feedId);

        // User left at block 450, current block is 500 (only 50 blocks < 100 cooldown)
        var formerMember = TestDataFactory.CreateParticipantEntityWithHistory(
            feedId,
            userAddress,
            ParticipantType.Member,
            leftAtBlock: new BlockIndex(450),
            lastLeaveBlock: new BlockIndex(450));

        MockServices.ConfigureFeedsStorageForJoinGroup(mocker, publicGroup, formerMember, currentBlock: 500);

        var handler = mocker.CreateInstance<JoinGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateJoinGroupFeedPayload(feedId, userAddress);
        var transaction = TestDataFactory.CreateJoinGroupFeedSignedTransaction(payload, userAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull("user should not be able to rejoin during cooldown period");
    }

    [Fact]
    public void ContentHandler_UserRejoinExactlyAtCooldownBoundary_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();
        var publicGroup = TestDataFactory.CreatePublicGroupFeed(feedId);

        // User left at block 400, current block is 500 (exactly 100 blocks = cooldown)
        var formerMember = TestDataFactory.CreateParticipantEntityWithHistory(
            feedId,
            userAddress,
            ParticipantType.Member,
            leftAtBlock: new BlockIndex(400),
            lastLeaveBlock: new BlockIndex(400));

        MockServices.ConfigureFeedsStorageForJoinGroup(mocker, publicGroup, formerMember, currentBlock: 500);

        var handler = mocker.CreateInstance<JoinGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateJoinGroupFeedPayload(feedId, userAddress);
        var transaction = TestDataFactory.CreateJoinGroupFeedSignedTransaction(payload, userAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert - At exactly 100 blocks, rejoin should succeed
        result.Should().NotBeNull("user should be able to rejoin at exactly the cooldown boundary (100 blocks)");
    }

    #endregion

    #region Admin Bypass Cooldown

    [Fact]
    public void ContentHandler_AdminCanAddMemberBeforeCooldown_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var formerMemberAddress = TestDataFactory.CreateAddress();
        var encryptedKey = TestDataFactory.CreateEncryptedKey();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        // Former member left only 10 blocks ago (within cooldown)
        // But admin should be able to add them back regardless
        var formerMember = TestDataFactory.CreateParticipantEntityWithHistory(
            feedId,
            formerMemberAddress,
            ParticipantType.Member,
            leftAtBlock: new BlockIndex(490),
            lastLeaveBlock: new BlockIndex(490));

        MockServices.ConfigureFeedsStorageForAddMember(mocker, groupFeed, adminParticipant, formerMember);

        var handler = mocker.CreateInstance<AddMemberToGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateAddMemberToGroupFeedPayload(feedId, adminAddress, formerMemberAddress, encryptedKey);
        var transaction = TestDataFactory.CreateAddMemberToGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert - Admin can bypass cooldown and add member back immediately
        result.Should().NotBeNull("admin should be able to add back a member even during cooldown period");
    }

    #endregion

    #region Multiple Leave/Rejoin Cycles

    [Fact]
    public async Task TransactionHandler_MultipleRejoinCycles_ShouldUpdateJoinedAtBlock()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var memberAddress = TestDataFactory.CreateAddress();
        var encryptedKey = TestDataFactory.CreateEncryptedKey();

        // Member originally joined at 100, left at 200, rejoined at 400, left at 500
        // Now being added back at block 700
        var currentBlock = new BlockIndex(700);
        var formerMember = new GroupFeedParticipantEntity(
            feedId,
            memberAddress,
            ParticipantType.Member,
            new BlockIndex(400)) // Last JoinedAtBlock
        {
            LeftAtBlock = new BlockIndex(500),
            LastLeaveBlock = new BlockIndex(500)
        };

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(currentBlock);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetParticipantWithHistoryAsync(feedId, memberAddress))
            .ReturnsAsync(formerMember);

        BlockIndex? capturedJoinedAtBlock = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.UpdateParticipantRejoinAsync(
                It.IsAny<FeedId>(),
                It.IsAny<string>(),
                It.IsAny<BlockIndex>(),
                It.IsAny<ParticipantType>()))
            .Callback<FeedId, string, BlockIndex, ParticipantType>((_, _, block, _) => capturedJoinedAtBlock = block)
            .Returns(Task.CompletedTask);

        var handler = mocker.CreateInstance<AddMemberToGroupFeedTransactionHandler>();
        var payload = TestDataFactory.CreateAddMemberToGroupFeedPayload(feedId, adminAddress, memberAddress, encryptedKey);
        var transaction = TestDataFactory.CreateAddMemberToGroupFeedValidatedTransaction(payload, adminAddress);

        // Act
        await handler.HandleAddMemberToGroupFeedTransactionAsync(transaction);

        // Assert - JoinedAtBlock should be updated to current block (700)
        capturedJoinedAtBlock.Should().NotBeNull();
        capturedJoinedAtBlock!.Value.Should().Be(700, "JoinedAtBlock should be updated to current block on rejoin");
    }

    #endregion

    #region Banned Users Cannot Rejoin

    [Fact]
    public void ContentHandler_BannedUserCannotSelfRejoin_ShouldFail()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();
        var publicGroup = TestDataFactory.CreatePublicGroupFeed(feedId);

        // User is banned (not just left)
        var bannedMember = TestDataFactory.CreateParticipantEntity(feedId, userAddress, ParticipantType.Banned);

        MockServices.ConfigureFeedsStorageForJoinGroup(mocker, publicGroup, bannedMember, currentBlock: 500);

        var handler = mocker.CreateInstance<JoinGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateJoinGroupFeedPayload(feedId, userAddress);
        var transaction = TestDataFactory.CreateJoinGroupFeedSignedTransaction(payload, userAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull("banned users should not be able to self-rejoin");
    }

    [Fact]
    public void ContentHandler_AdminCannotAddBannedUser_ShouldFail()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var bannedUserAddress = TestDataFactory.CreateAddress();
        var encryptedKey = TestDataFactory.CreateEncryptedKey();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        // User is banned (must be unbanned first, not just added back)
        var bannedUser = TestDataFactory.CreateParticipantEntity(feedId, bannedUserAddress, ParticipantType.Banned);

        MockServices.ConfigureFeedsStorageForAddMember(mocker, groupFeed, adminParticipant, bannedUser);

        var handler = mocker.CreateInstance<AddMemberToGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateAddMemberToGroupFeedPayload(feedId, adminAddress, bannedUserAddress, encryptedKey);
        var transaction = TestDataFactory.CreateAddMemberToGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert - Admin cannot add banned users (they must be unbanned first)
        result.Should().BeNull("admin should not be able to add banned users - they must be unbanned first");
    }

    #endregion
}
