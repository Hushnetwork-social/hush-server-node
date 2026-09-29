using FluentAssertions;
using HushNode.Blockchain.BlockModel.States;
using HushNode.Blockchain.Storage.Model;
using HushServerNode.HushVotingLicensingIntegration;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using Xunit;

namespace HushServerNode.Tests;

public sealed class ElectionRetainedHistoryTests
{
    [Fact]
    public void RetainedUtcTimestamp_StaysUtcAfterLegacyConverterRoundTrip()
    {
        var time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var block = UnsignedBlockHandler.CreateNew(BlockId.GenesisBlockId, new BlockIndex(1),
            new Timestamp(time), BlockId.Empty, BlockId.NewBlockId);
        var row = new BlockchainBlock(block.BlockId, block.BlockIndex, block.PreviousBlockId, block.NextBlockId, "retained", block.ToJson());
        var parsed = ElectionEntitlementRolloutReadiness.ReadCanonicalBlock(row);
        parsed.CreationTimeStamp.Value.Should().Be(time);
        parsed.CreationTimeStamp.Value.Kind.Should().Be(DateTimeKind.Utc);
        var changed = () => ElectionEntitlementRolloutReadiness.ReadCanonicalBlock(row with { BlockId = BlockId.NewBlockId });
        changed.Should().Throw<InvalidDataException>();
    }
}
