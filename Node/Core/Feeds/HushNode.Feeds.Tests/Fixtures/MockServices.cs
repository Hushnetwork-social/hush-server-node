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
