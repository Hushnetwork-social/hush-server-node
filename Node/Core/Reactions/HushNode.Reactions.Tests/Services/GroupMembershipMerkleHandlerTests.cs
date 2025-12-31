using FluentAssertions;
using HushNode.Feeds.Events;
using HushNode.Feeds.Storage;
using HushNode.Reactions.Storage;
using HushNode.Reactions.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using Microsoft.Extensions.Logging;
using Moq;
using Olimpo;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushNode.Reactions.Tests.Services;

/// <summary>
/// Tests for GroupMembershipMerkleHandler - event handling for Group Feed membership changes.
/// </summary>
public class GroupMembershipMerkleHandlerTests
{
    [Fact]
    public async Task MemberJoined_AddsCommitmentToTree()
    {
        // Arrange
        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = "04abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
        var commitment = TestDataFactory.CreateCommitment();
        var keyGeneration = 1;
        var blockIndex = new BlockIndex(100);

        var membershipServiceMock = new Mock<IMembershipService>();
        membershipServiceMock
            .Setup(x => x.RegisterCommitmentAsync(feedId, It.IsAny<byte[]>()))
            .ReturnsAsync(RegisterCommitmentResult.Ok(TestDataFactory.CreateCommitment(), 0));

        var userCommitmentServiceMock = new Mock<IUserCommitmentService>();
        userCommitmentServiceMock
            .Setup(x => x.DeriveCommitmentFromAddress(memberAddress))
            .Returns(commitment);

        var commitmentRepoMock = new Mock<IGroupFeedMemberCommitmentRepository>();

        var unitOfWorkMock = new Mock<IWritableUnitOfWork<FeedsDbContext>>();
        unitOfWorkMock
            .Setup(x => x.GetRepository<IGroupFeedMemberCommitmentRepository>())
            .Returns(commitmentRepoMock.Object);

        var unitOfWorkProviderMock = new Mock<IUnitOfWorkProvider<FeedsDbContext>>();
        unitOfWorkProviderMock
            .Setup(x => x.CreateWritable())
            .Returns(unitOfWorkMock.Object);

        var eventAggregatorLoggerMock = new Mock<ILogger<EventAggregator>>();
        var eventAggregator = new EventAggregator(eventAggregatorLoggerMock.Object);
        var handlerLoggerMock = new Mock<ILogger<GroupMembershipMerkleHandler>>();

        var handler = new GroupMembershipMerkleHandler(
            unitOfWorkProviderMock.Object,
            membershipServiceMock.Object,
            userCommitmentServiceMock.Object,
            eventAggregator,
            handlerLoggerMock.Object);

        var @event = new MemberJoinedGroupFeedEvent(feedId, memberAddress, keyGeneration, blockIndex);

        // Act
        handler.Handle(@event);

        // Give async handler time to complete
        await Task.Delay(100);

        // Assert
        userCommitmentServiceMock.Verify(
            x => x.DeriveCommitmentFromAddress(memberAddress),
            Times.Once);

        commitmentRepoMock.Verify(
            x => x.RegisterCommitmentAsync(feedId, commitment, keyGeneration, blockIndex),
            Times.Once);

        membershipServiceMock.Verify(
            x => x.RegisterCommitmentAsync(feedId, commitment),
            Times.Once);
    }

    [Fact]
    public async Task MemberLeft_RevokesCommitment()
    {
        // Arrange
        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = "04abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
        var commitment = TestDataFactory.CreateCommitment();
        var blockIndex = new BlockIndex(100);

        var membershipServiceMock = new Mock<IMembershipService>();
        membershipServiceMock
            .Setup(x => x.UpdateMerkleRootAsync(feedId, blockIndex.Value))
            .ReturnsAsync(TestDataFactory.CreateCommitment());

        var userCommitmentServiceMock = new Mock<IUserCommitmentService>();
        userCommitmentServiceMock
            .Setup(x => x.DeriveCommitmentFromAddress(memberAddress))
            .Returns(commitment);

        var commitmentRepoMock = new Mock<IGroupFeedMemberCommitmentRepository>();

        var unitOfWorkMock = new Mock<IWritableUnitOfWork<FeedsDbContext>>();
        unitOfWorkMock
            .Setup(x => x.GetRepository<IGroupFeedMemberCommitmentRepository>())
            .Returns(commitmentRepoMock.Object);

        var unitOfWorkProviderMock = new Mock<IUnitOfWorkProvider<FeedsDbContext>>();
        unitOfWorkProviderMock
            .Setup(x => x.CreateWritable())
            .Returns(unitOfWorkMock.Object);

        var eventAggregatorLoggerMock = new Mock<ILogger<EventAggregator>>();
        var eventAggregator = new EventAggregator(eventAggregatorLoggerMock.Object);
        var handlerLoggerMock = new Mock<ILogger<GroupMembershipMerkleHandler>>();

        var handler = new GroupMembershipMerkleHandler(
            unitOfWorkProviderMock.Object,
            membershipServiceMock.Object,
            userCommitmentServiceMock.Object,
            eventAggregator,
            handlerLoggerMock.Object);

        var @event = new MemberLeftGroupFeedEvent(feedId, memberAddress, blockIndex);

        // Act
        handler.Handle(@event);

        // Give async handler time to complete
        await Task.Delay(100);

        // Assert
        userCommitmentServiceMock.Verify(
            x => x.DeriveCommitmentFromAddress(memberAddress),
            Times.Once);

        commitmentRepoMock.Verify(
            x => x.RevokeCommitmentAsync(feedId, commitment, blockIndex),
            Times.Once);

        membershipServiceMock.Verify(
            x => x.UpdateMerkleRootAsync(feedId, blockIndex.Value),
            Times.Once);
    }

    [Fact]
    public async Task MemberBanned_RevokesCommitment()
    {
        // Arrange
        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = "04abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
        var commitment = TestDataFactory.CreateCommitment();
        var blockIndex = new BlockIndex(100);

        var membershipServiceMock = new Mock<IMembershipService>();
        membershipServiceMock
            .Setup(x => x.UpdateMerkleRootAsync(feedId, blockIndex.Value))
            .ReturnsAsync(TestDataFactory.CreateCommitment());

        var userCommitmentServiceMock = new Mock<IUserCommitmentService>();
        userCommitmentServiceMock
            .Setup(x => x.DeriveCommitmentFromAddress(memberAddress))
            .Returns(commitment);

        var commitmentRepoMock = new Mock<IGroupFeedMemberCommitmentRepository>();

        var unitOfWorkMock = new Mock<IWritableUnitOfWork<FeedsDbContext>>();
        unitOfWorkMock
            .Setup(x => x.GetRepository<IGroupFeedMemberCommitmentRepository>())
            .Returns(commitmentRepoMock.Object);

        var unitOfWorkProviderMock = new Mock<IUnitOfWorkProvider<FeedsDbContext>>();
        unitOfWorkProviderMock
            .Setup(x => x.CreateWritable())
            .Returns(unitOfWorkMock.Object);

        var eventAggregatorLoggerMock = new Mock<ILogger<EventAggregator>>();
        var eventAggregator = new EventAggregator(eventAggregatorLoggerMock.Object);
        var handlerLoggerMock = new Mock<ILogger<GroupMembershipMerkleHandler>>();

        var handler = new GroupMembershipMerkleHandler(
            unitOfWorkProviderMock.Object,
            membershipServiceMock.Object,
            userCommitmentServiceMock.Object,
            eventAggregator,
            handlerLoggerMock.Object);

        var @event = new MemberBannedFromGroupFeedEvent(feedId, memberAddress, blockIndex);

        // Act
        handler.Handle(@event);

        // Give async handler time to complete
        await Task.Delay(100);

        // Assert
        userCommitmentServiceMock.Verify(
            x => x.DeriveCommitmentFromAddress(memberAddress),
            Times.Once);

        commitmentRepoMock.Verify(
            x => x.RevokeCommitmentAsync(feedId, commitment, blockIndex),
            Times.Once);

        membershipServiceMock.Verify(
            x => x.UpdateMerkleRootAsync(feedId, blockIndex.Value),
            Times.Once);
    }

    [Fact]
    public async Task MemberUnbanned_AddsNewCommitment()
    {
        // Arrange
        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = "04abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
        var commitment = TestDataFactory.CreateCommitment();
        var keyGeneration = 2; // New key generation after unban
        var blockIndex = new BlockIndex(100);

        var membershipServiceMock = new Mock<IMembershipService>();
        membershipServiceMock
            .Setup(x => x.RegisterCommitmentAsync(feedId, It.IsAny<byte[]>()))
            .ReturnsAsync(RegisterCommitmentResult.Ok(TestDataFactory.CreateCommitment(), 0));

        var userCommitmentServiceMock = new Mock<IUserCommitmentService>();
        userCommitmentServiceMock
            .Setup(x => x.DeriveCommitmentFromAddress(memberAddress))
            .Returns(commitment);

        var commitmentRepoMock = new Mock<IGroupFeedMemberCommitmentRepository>();

        var unitOfWorkMock = new Mock<IWritableUnitOfWork<FeedsDbContext>>();
        unitOfWorkMock
            .Setup(x => x.GetRepository<IGroupFeedMemberCommitmentRepository>())
            .Returns(commitmentRepoMock.Object);

        var unitOfWorkProviderMock = new Mock<IUnitOfWorkProvider<FeedsDbContext>>();
        unitOfWorkProviderMock
            .Setup(x => x.CreateWritable())
            .Returns(unitOfWorkMock.Object);

        var eventAggregatorLoggerMock = new Mock<ILogger<EventAggregator>>();
        var eventAggregator = new EventAggregator(eventAggregatorLoggerMock.Object);
        var handlerLoggerMock = new Mock<ILogger<GroupMembershipMerkleHandler>>();

        var handler = new GroupMembershipMerkleHandler(
            unitOfWorkProviderMock.Object,
            membershipServiceMock.Object,
            userCommitmentServiceMock.Object,
            eventAggregator,
            handlerLoggerMock.Object);

        var @event = new MemberUnbannedFromGroupFeedEvent(feedId, memberAddress, keyGeneration, blockIndex);

        // Act
        handler.Handle(@event);

        // Give async handler time to complete
        await Task.Delay(100);

        // Assert
        userCommitmentServiceMock.Verify(
            x => x.DeriveCommitmentFromAddress(memberAddress),
            Times.Once);

        commitmentRepoMock.Verify(
            x => x.RegisterCommitmentAsync(feedId, commitment, keyGeneration, blockIndex),
            Times.Once);

        membershipServiceMock.Verify(
            x => x.RegisterCommitmentAsync(feedId, commitment),
            Times.Once);
    }
}
