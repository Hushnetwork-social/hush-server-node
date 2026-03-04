using FluentAssertions;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Moq;
using Moq.AutoMock;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushNode.Feeds.Tests;

public class FeedsStorageServiceCustomCircleTests
{
    [Fact]
    public async Task GetCustomCircleCountByOwnerAsync_ShouldUseRepository()
    {
        // Arrange
        var mocker = new AutoMocker();
        var owner = TestDataFactory.CreateAddress();
        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository.Setup(x => x.GetCustomCircleCountByOwnerAsync(owner)).ReturnsAsync(2);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork.Setup(x => x.GetRepository<IFeedsRepository>()).Returns(mockRepository.Object);
        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.GetCustomCircleCountByOwnerAsync(owner);

        // Assert
        result.Should().Be(2);
    }

    [Fact]
    public async Task OwnerHasCustomCircleNamedAsync_ShouldUseRepository()
    {
        // Arrange
        var mocker = new AutoMocker();
        var owner = TestDataFactory.CreateAddress();
        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.OwnerHasCustomCircleNamedAsync(owner, "friends"))
            .ReturnsAsync(true);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork.Setup(x => x.GetRepository<IFeedsRepository>()).Returns(mockRepository.Object);
        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.OwnerHasCustomCircleNamedAsync(owner, "friends");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetCirclesForOwnerAsync_ShouldReturnProjectedCircles()
    {
        // Arrange
        var mocker = new AutoMocker();
        var owner = TestDataFactory.CreateAddress();
        var circles = new List<CustomCircleSummary>
        {
            new(TestDataFactory.CreateFeedId(), "Inner Circle", true, 4, 1, new BlockIndex(10), new BlockIndex(11)),
            new(TestDataFactory.CreateFeedId(), "Friends", false, 2, 0, new BlockIndex(12), null)
        };

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.GetCirclesForOwnerAsync(owner))
            .ReturnsAsync(circles);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork.Setup(x => x.GetRepository<IFeedsRepository>()).Returns(mockRepository.Object);
        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.GetCirclesForOwnerAsync(owner);

        // Assert
        result.Should().HaveCount(2);
        result[0].IsInnerCircle.Should().BeTrue();
    }
}
