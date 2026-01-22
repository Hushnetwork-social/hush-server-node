using FluentAssertions;
using Xunit;

namespace HushNode.IntegrationTests.Infrastructure;

/// <summary>
/// Verification tests for PlaywrightFixture to ensure browser automation works correctly.
/// </summary>
[Trait("Category", "Integration")]
public class PlaywrightFixtureTests
{
    [Fact]
    public async Task PlaywrightFixture_CanLaunchBrowser_And_NavigateToPage()
    {
        // Arrange
        await using var fixture = new PlaywrightFixture();
        await fixture.InitializeAsync();

        // Act
        var (context, page) = await fixture.CreatePageAsync();

        // Navigate to about:blank (always available)
        await page.GotoAsync("about:blank");

        // Assert
        fixture.IsInitialized.Should().BeTrue();
        page.Url.Should().Be("about:blank");

        // Cleanup
        await context.CloseAsync();
    }

    [Fact]
    public async Task PlaywrightFixture_CreatesIsolatedContexts()
    {
        // Arrange
        await using var fixture = new PlaywrightFixture();
        await fixture.InitializeAsync();

        // Act - Create two separate contexts
        var context1 = await fixture.CreateContextAsync();
        var context2 = await fixture.CreateContextAsync();

        // Assert - Contexts should be different instances
        context1.Should().NotBeSameAs(context2);

        // Cleanup
        await context1.CloseAsync();
        await context2.CloseAsync();
    }

    [Fact]
    public async Task PlaywrightFixture_DisposesCleanly()
    {
        // Arrange
        var fixture = new PlaywrightFixture();
        await fixture.InitializeAsync();
        fixture.IsInitialized.Should().BeTrue();

        // Act
        await fixture.DisposeAsync();

        // Assert - Should not throw when accessing after dispose
        var action = () => fixture.IsInitialized;
        action.Should().NotThrow();
        fixture.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public async Task PlaywrightFixture_ThrowsWhenBrowserNotInitialized()
    {
        // Arrange
        var fixture = new PlaywrightFixture();

        // Act & Assert
        var action = () => fixture.Browser;
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*not launched*");

        await fixture.DisposeAsync();
    }
}
