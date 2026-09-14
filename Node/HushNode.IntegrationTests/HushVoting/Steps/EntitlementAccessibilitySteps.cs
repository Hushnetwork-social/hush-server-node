using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class EntitlementAccessibilitySteps(HushVotingScenario scenario, HushVotingIdentityJourney identity)
{
    private IPage Page => scenario.Page;
    private ILocator Gate => Page.GetByTestId("entitlement-gate");

    [Given("Alice uses keyboard screen-reader reduced-motion and enlarged text")]
    public async Task AccessibleSessionAsync()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Page.SetViewportSizeAsync(320, 740);
        await identity.AuthenticateUntilEntitlementAsync();
        // Emulate the browser's text-size preference; no application state or response is replaced.
        await Page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await Expect(Gate.GetByRole(AriaRole.Status)).ToHaveAttributeAsync("aria-live", "polite");
        await Expect(Page.GetByTestId("entitlement-gate-heading")).ToHaveTextAsync("Checking your HushVoting! licence…");
    }

    [When("states change from resolving through delayed or unavailable")]
    public async Task DelayedAsync()
    {
        using var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        scenario.Faults.ReleaseEntitlementQueries();
        await received.WaitAsync();
        await Expect(Page.GetByTestId("entitlement-gate-heading")).ToHaveTextAsync(
            "Licence activation is taking longer than expected.", new() { Timeout = 20_000 });
        await Expect(Page.GetByTestId("entitlement-gate-heading")).ToBeFocusedAsync();
    }

    [Then("the gate announces meaningful changes without polling spam")]
    public async Task StableAnnouncementsAsync()
    {
        var status = Gate.GetByRole(AriaRole.Status);
        await Expect(status).ToHaveAttributeAsync("aria-live", "polite");
        (await status.InnerTextAsync()).Should().NotBeNullOrWhiteSpace();
        var mutations = await status.EvaluateAsync<int>("""
            element => new Promise(resolve => {
                let changes = 0;
                const observer = new MutationObserver(() => changes++);
                observer.observe(element, { childList: true, characterData: true, subtree: true });
                setTimeout(() => { observer.disconnect(); resolve(changes); }, 6500);
            })
            """);
        mutations.Should().Be(0, "unchanged status must stay silent across two ordinary polling intervals");
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
    }

    [Then("Retry and Lock have visible focus names and deterministic focus placement")]
    public async Task KeyboardAsync()
    {
        await Page.Keyboard.PressAsync("Tab");
        var retry = Gate.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true });
        var lockButton = Gate.GetByRole(AriaRole.Button, new() { Name = "Lock", Exact = true });
        await Expect(retry).ToBeFocusedAsync();
        await VisibleFocusAsync(retry);
        await Page.Keyboard.PressAsync("Tab");
        await Expect(lockButton).ToBeFocusedAsync();
        await VisibleFocusAsync(lockButton);
        var fits = await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth");
        fits.Should().BeTrue("the gate must reflow at 320 CSS pixels with enlarged text");
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Unlock HushVoting!", Exact = true })).ToBeVisibleAsync();
    }

    private static async Task VisibleFocusAsync(ILocator control)
    {
        await Expect(control).ToBeVisibleAsync();
        var focus = await control.EvaluateAsync<bool>("element => { const s = getComputedStyle(element); return element.matches(':focus-visible') && ((s.outlineStyle !== 'none' && parseFloat(s.outlineWidth) > 0) || s.boxShadow !== 'none'); }");
        focus.Should().BeTrue("keyboard focus needs a visible outline or shadow");
        var box = await control.BoundingBoxAsync();
        box.Should().NotBeNull();
        box!.Width.Should().BeGreaterThanOrEqualTo(44);
        box.Height.Should().BeGreaterThanOrEqualTo(44);
    }
}
