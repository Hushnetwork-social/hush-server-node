using FluentAssertions;
using HushNode.IntegrationTests.Infrastructure;
using Xunit;

namespace HushNode.IntegrationTests.Tests;

/// <summary>
/// Unit tests for GrpcClientFactory to verify client creation logic.
/// These tests verify factory construction and basic behavior without requiring gRPC client stubs.
/// Full gRPC client creation tests are in integration tests where the server is running.
/// </summary>
public class GrpcClientFactoryTests
{
    [Fact]
    public void Constructor_WithValidHostAndPort_ShouldCreateFactory()
    {
        // Arrange & Act
        using var factory = new GrpcClientFactory("localhost", 12345);

        // Assert
        factory.Should().NotBeNull();
        factory.Channel.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithEmptyHost_ShouldThrowArgumentException()
    {
        // Arrange & Act
        var act = () => new GrpcClientFactory("", 12345);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithZeroPort_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange & Act
        var act = () => new GrpcClientFactory("localhost", 0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_WithNegativePort_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange & Act
        var act = () => new GrpcClientFactory("localhost", -1);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Channel_ShouldHaveCorrectTarget()
    {
        // Arrange
        using var factory = new GrpcClientFactory("localhost", 12345);

        // Act
        var target = factory.Channel.Target;

        // Assert
        target.Should().Be("localhost:12345");
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var factory = new GrpcClientFactory("localhost", 12345);

        // Act
        var act = () =>
        {
            factory.Dispose();
            factory.Dispose();
            factory.Dispose();
        };

        // Assert
        act.Should().NotThrow();
    }
}
