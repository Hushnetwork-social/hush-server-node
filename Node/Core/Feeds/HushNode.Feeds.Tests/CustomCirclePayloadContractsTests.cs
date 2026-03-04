using FluentAssertions;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Xunit;

namespace HushNode.Feeds.Tests;

public class CustomCirclePayloadContractsTests
{
    [Fact]
    public void CreateCustomCirclePayload_ShouldPreserveContractFields()
    {
        // Arrange
        var feedId = TestDataFactory.CreateFeedId();
        var owner = TestDataFactory.CreateAddress();
        const string circleName = "Close Friends";

        // Act
        var payload = new CreateCustomCirclePayload(feedId, owner, circleName);

        // Assert
        payload.FeedId.Should().Be(feedId);
        payload.OwnerPublicAddress.Should().Be(owner);
        payload.CircleName.Should().Be(circleName);
    }

    [Fact]
    public void AddMembersToCustomCirclePayload_ShouldSupportBatchSemantics()
    {
        // Arrange
        var feedId = TestDataFactory.CreateFeedId();
        var owner = TestDataFactory.CreateAddress();
        var members = new[]
        {
            new CustomCircleMember(TestDataFactory.CreateAddress(), "enc-a"),
            new CustomCircleMember(TestDataFactory.CreateAddress(), "enc-b")
        };

        // Act
        var payload = new AddMembersToCustomCirclePayload(feedId, owner, members);

        // Assert
        payload.FeedId.Should().Be(feedId);
        payload.OwnerPublicAddress.Should().Be(owner);
        payload.Members.Should().HaveCount(2);
    }

    [Fact]
    public void CustomCirclePayloadKinds_ShouldBeUniqueAndStable()
    {
        CreateCustomCirclePayloadHandler.CreateCustomCirclePayloadKind
            .Should()
            .NotBe(AddMembersToCustomCirclePayloadHandler.AddMembersToCustomCirclePayloadKind);
    }
}
