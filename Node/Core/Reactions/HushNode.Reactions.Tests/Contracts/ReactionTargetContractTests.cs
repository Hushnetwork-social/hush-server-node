using FluentAssertions;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;
using Xunit;

namespace HushNode.Reactions.Tests.Contracts;

public class ReactionTargetContractTests
{
    [Fact]
    public void TryMapToMessageId_PostTarget_ShouldMapUsingPostId()
    {
        var postId = Guid.NewGuid();
        var target = ReactionTarget.Post(postId);

        var supported = Feat087ReactionTargetContract.TryMapToMessageId(target, out var messageId);

        supported.Should().BeTrue();
        messageId.Should().Be(new FeedMessageId(postId));
    }

    [Theory]
    [InlineData(ReactionTargetType.Comment)]
    [InlineData(ReactionTargetType.Reply)]
    public void TryMapToMessageId_TargetsNotEnabledInFeat087_ShouldReturnUnsupported(ReactionTargetType targetType)
    {
        var target = new ReactionTarget(targetType, Guid.NewGuid());

        var supported = Feat087ReactionTargetContract.TryMapToMessageId(target, out var messageId);

        supported.Should().BeFalse();
        messageId.Value.Should().Be(Guid.Empty);
    }
}
