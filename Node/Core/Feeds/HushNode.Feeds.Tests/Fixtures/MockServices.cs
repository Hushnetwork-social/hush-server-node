using HushNode.Caching;
using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Moq;
using Moq.AutoMock;
using Olimpo;

namespace HushNode.Feeds.Tests.Fixtures;

/// <summary>
/// Factory for configuring mock services with AutoMocker for unit testing.
/// </summary>
public static class MockServices
{
    /// <summary>
    /// Configures the IFeedsStorageService mock in the AutoMocker.
    /// Default: CreateGroupFeed succeeds.
    /// </summary>
    public static void ConfigureFeedsStorageService(AutoMocker mocker)
    {
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateGroupFeed(It.IsAny<GroupFeed>()))
            .Returns(Task.CompletedTask);
    }

    /// <summary>
    /// Configures the IFeedsStorageService mock for admin control operations.
    /// </summary>
    public static void ConfigureFeedsStorageForAdminControls(
        AutoMocker mocker,
        GroupFeed? groupFeed = null,
        GroupFeedParticipantEntity? senderParticipant = null,
        GroupFeedParticipantEntity? targetParticipant = null,
        int adminCount = 1)
    {
        var mock = mocker.GetMock<IFeedsStorageService>();

        // GetGroupFeedAsync
        mock.Setup(x => x.GetGroupFeedAsync(It.IsAny<FeedId>()))
            .ReturnsAsync(groupFeed);

        // GetGroupFeedParticipantAsync for sender
        if (senderParticipant != null)
        {
            mock.Setup(x => x.GetGroupFeedParticipantAsync(It.IsAny<FeedId>(), senderParticipant.ParticipantPublicAddress))
                .ReturnsAsync(senderParticipant);
        }

        // GetGroupFeedParticipantAsync for target
        if (targetParticipant != null)
        {
            mock.Setup(x => x.GetGroupFeedParticipantAsync(It.IsAny<FeedId>(), targetParticipant.ParticipantPublicAddress))
                .ReturnsAsync(targetParticipant);
        }

        // GetAdminCountAsync
        mock.Setup(x => x.GetAdminCountAsync(It.IsAny<FeedId>()))
            .ReturnsAsync(adminCount);

        // IsAdminAsync - based on sender participant type
        if (senderParticipant != null && senderParticipant.ParticipantType == ParticipantType.Admin)
        {
            mock.Setup(x => x.IsAdminAsync(It.IsAny<FeedId>(), senderParticipant.ParticipantPublicAddress))
                .ReturnsAsync(true);
        }

        // Update operations
        mock.Setup(x => x.UpdateGroupFeedTitleAsync(It.IsAny<FeedId>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        mock.Setup(x => x.UpdateGroupFeedDescriptionAsync(It.IsAny<FeedId>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        mock.Setup(x => x.MarkGroupFeedDeletedAsync(It.IsAny<FeedId>()))
            .Returns(Task.CompletedTask);
        mock.Setup(x => x.UpdateParticipantTypeAsync(It.IsAny<FeedId>(), It.IsAny<string>(), It.IsAny<ParticipantType>()))
            .Returns(Task.CompletedTask);
    }

    /// <summary>
    /// Configures the IBlockchainCache mock in the AutoMocker.
    /// </summary>
    public static void ConfigureBlockchainCache(AutoMocker mocker, long currentBlockIndex = 100)
    {
        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(currentBlockIndex));
    }

    /// <summary>
    /// Configures the IEventAggregator mock in the AutoMocker.
    /// Default: PublishAsync succeeds.
    /// </summary>
    public static void ConfigureEventAggregator(AutoMocker mocker)
    {
        mocker.GetMock<IEventAggregator>()
            .Setup(x => x.PublishAsync(It.IsAny<object>()))
            .Returns(Task.CompletedTask);
    }

    /// <summary>
    /// Configures the ICredentialsProvider mock in the AutoMocker.
    /// Returns a test block producer credentials profile.
    /// </summary>
    public static void ConfigureCredentialsProvider(AutoMocker mocker)
    {
        var credentials = new CredentialsProfile
        {
            ProfileName = "TestBlockProducer",
            PublicSigningAddress = "block-producer-address",
            PrivateSigningKey = Convert.ToHexString(new byte[32]),
            PublicEncryptAddress = "block-producer-encrypt-address",
            PrivateEncryptKey = Convert.ToHexString(new byte[32]),
            IsPublic = false
        };

        mocker.GetMock<ICredentialsProvider>()
            .Setup(x => x.GetCredentials())
            .Returns(credentials);
    }
}
