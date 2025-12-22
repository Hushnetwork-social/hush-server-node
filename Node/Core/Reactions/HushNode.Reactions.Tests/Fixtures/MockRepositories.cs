using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;
using HushNode.Reactions.Storage;
using Moq;

namespace HushNode.Reactions.Tests.Fixtures;

/// <summary>
/// Factory for creating mock repositories for unit testing.
/// </summary>
public static class MockRepositories
{
    public static Mock<IReactionsRepository> CreateReactionsRepository()
    {
        var mock = new Mock<IReactionsRepository>();

        // Default: no existing tallies or nullifiers
        mock.Setup(x => x.GetTallyAsync(It.IsAny<FeedMessageId>()))
            .ReturnsAsync((MessageReactionTally?)null);

        mock.Setup(x => x.GetTallyForUpdateAsync(It.IsAny<FeedMessageId>()))
            .ReturnsAsync((MessageReactionTally?)null);

        mock.Setup(x => x.GetNullifierAsync(It.IsAny<byte[]>()))
            .ReturnsAsync((ReactionNullifier?)null);

        mock.Setup(x => x.NullifierExistsAsync(It.IsAny<byte[]>()))
            .ReturnsAsync(false);

        mock.Setup(x => x.GetNullifierWithBackupAsync(It.IsAny<byte[]>()))
            .ReturnsAsync((ReactionNullifier?)null);

        mock.Setup(x => x.GetTalliesForMessagesAsync(It.IsAny<IEnumerable<FeedMessageId>>()))
            .ReturnsAsync(Enumerable.Empty<MessageReactionTally>());

        mock.Setup(x => x.GetTransactionsFromBlockAsync(It.IsAny<BlockIndex>()))
            .ReturnsAsync(Enumerable.Empty<ReactionTransaction>());

        return mock;
    }

    public static Mock<IMerkleTreeRepository> CreateMerkleTreeRepository()
    {
        var mock = new Mock<IMerkleTreeRepository>();

        mock.Setup(x => x.GetRecentRootsAsync(It.IsAny<FeedId>(), It.IsAny<int>()))
            .ReturnsAsync(Enumerable.Empty<MerkleRootHistory>());

        mock.Setup(x => x.GetCommitmentsAsync(It.IsAny<FeedId>()))
            .ReturnsAsync(Enumerable.Empty<byte[]>());

        mock.Setup(x => x.GetCommitmentCountAsync(It.IsAny<FeedId>()))
            .ReturnsAsync(0);

        mock.Setup(x => x.IsRootValidAsync(It.IsAny<FeedId>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(false);

        return mock;
    }

    public static Mock<ICommitmentRepository> CreateCommitmentRepository()
    {
        var mock = new Mock<ICommitmentRepository>();

        mock.Setup(x => x.IsCommitmentRegisteredAsync(It.IsAny<FeedId>(), It.IsAny<byte[]>()))
            .ReturnsAsync(false);

        mock.Setup(x => x.GetCommitmentsForFeedAsync(It.IsAny<FeedId>()))
            .ReturnsAsync(Enumerable.Empty<FeedMemberCommitment>());

        mock.Setup(x => x.GetCommitmentIndexAsync(It.IsAny<FeedId>(), It.IsAny<byte[]>()))
            .ReturnsAsync(-1);

        return mock;
    }

    public static void SetupExistingTally(
        this Mock<IReactionsRepository> mock,
        FeedMessageId messageId,
        MessageReactionTally tally)
    {
        mock.Setup(x => x.GetTallyAsync(messageId))
            .ReturnsAsync(tally);

        mock.Setup(x => x.GetTallyForUpdateAsync(messageId))
            .ReturnsAsync(tally);
    }

    public static void SetupExistingNullifier(
        this Mock<IReactionsRepository> mock,
        byte[] nullifier,
        ReactionNullifier record)
    {
        mock.Setup(x => x.GetNullifierAsync(It.Is<byte[]>(n => n.SequenceEqual(nullifier))))
            .ReturnsAsync(record);

        mock.Setup(x => x.NullifierExistsAsync(It.Is<byte[]>(n => n.SequenceEqual(nullifier))))
            .ReturnsAsync(true);

        mock.Setup(x => x.GetNullifierWithBackupAsync(It.Is<byte[]>(n => n.SequenceEqual(nullifier))))
            .ReturnsAsync(record);
    }

    public static void SetupMerkleRoots(
        this Mock<IMerkleTreeRepository> mock,
        FeedId feedId,
        params MerkleRootHistory[] roots)
    {
        mock.Setup(x => x.GetRecentRootsAsync(feedId, It.IsAny<int>()))
            .ReturnsAsync((FeedId _, int count) => roots.Take(count));

        // Setup IsRootValidAsync to return true for any of the roots
        foreach (var root in roots)
        {
            mock.Setup(x => x.IsRootValidAsync(feedId, It.Is<byte[]>(r => r.SequenceEqual(root.MerkleRoot)), It.IsAny<int>()))
                .ReturnsAsync(true);
        }
    }

    public static void SetupCommitments(
        this Mock<ICommitmentRepository> mock,
        FeedId feedId,
        params FeedMemberCommitment[] commitments)
    {
        mock.Setup(x => x.GetCommitmentsForFeedAsync(feedId))
            .ReturnsAsync(commitments);

        for (int i = 0; i < commitments.Length; i++)
        {
            var commitment = commitments[i];
            var index = i;
            mock.Setup(x => x.IsCommitmentRegisteredAsync(
                    feedId,
                    It.Is<byte[]>(c => c.SequenceEqual(commitment.UserCommitment))))
                .ReturnsAsync(true);

            mock.Setup(x => x.GetCommitmentIndexAsync(
                    feedId,
                    It.Is<byte[]>(c => c.SequenceEqual(commitment.UserCommitment))))
                .ReturnsAsync(index);
        }
    }
}
