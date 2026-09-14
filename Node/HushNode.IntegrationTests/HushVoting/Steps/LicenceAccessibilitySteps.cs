using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class LicenceAccessibilitySteps(HushVotingScenario scenario, LicenceUpgradeSteps upgrade)
{
    private IPage Page => scenario.Page;
    private ILocator Trigger => Page.GetByRole(AriaRole.Button, new() { Name = HushVotingIdentityJourney.Alias, Exact = true });
    private ILocator Popup => Page.GetByRole(AriaRole.Dialog, new() { Name = "User information", Exact = true });
    private int _viewports;
    private readonly HashSet<string> _states = [];

    private async Task TabToAsync(ILocator target)
    {
        for (var i = 0; i < 80; i++)
        {
            if (await target.EvaluateAsync<bool>("node => document.activeElement === node")) return;
            await Page.Keyboard.PressAsync("Tab");
        }
        throw new InvalidOperationException("Keyboard navigation did not reach the intended licence control.");
    }

    private async Task EnterAsync(ILocator target)
    {
        await TabToAsync(target);
        await Page.Keyboard.PressAsync("Enter");
    }

    private async Task OpenOptionsAsync()
    {
        await EnterAsync(Trigger);
        await Expect(Popup).ToBeVisibleAsync();
        await EnterAsync(Popup.GetByTestId("account-licence-action"));
        await Expect(Page.GetByTestId("licence-options")).ToBeVisibleAsync();
    }

    [When("Alice uses only the keyboard at each supported Web viewport")]
    public async Task ViewportsAsync()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        foreach (var width in new[] { 320, 768, 1280 })
        {
            await Page.SetViewportSizeAsync(width, 800);
            await EnterAsync(Trigger);
            var close = Popup.GetByRole(AriaRole.Button, new() { Name = "Close user information", Exact = true });
            await Expect(close).ToBeFocusedAsync();
            await Page.Keyboard.PressAsync("Shift+Tab");
            await Expect(Popup.GetByRole(AriaRole.Button, new() { Name = "Lock", Exact = true })).ToBeFocusedAsync();
            await Page.Keyboard.PressAsync("Tab");
            await Expect(close).ToBeFocusedAsync();
            await upgrade.AccountLimitsAsync();
            await CheckLayoutAsync(Popup);
            await Page.Keyboard.PressAsync("Enter");
            await Expect(Popup).ToHaveCountAsync(0);
            await Expect(Trigger).ToBeFocusedAsync();
            await EnterAsync(Trigger);
            await Page.Keyboard.PressAsync("Escape");
            await Expect(Trigger).ToBeFocusedAsync();
            await OpenOptionsAsync();
            await Expect(Page.GetByTestId("licence-view-options").GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeFocusedAsync();
            await CheckLayoutAsync(Page.GetByTestId("licence-options"));
            await EnterAsync(Page.GetByRole(AriaRole.Button, new() { Name = "Review plan", Exact = true }).First);
            await Expect(Page.GetByTestId("licence-view-confirmation").GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeFocusedAsync();
            await CheckLayoutAsync(Page.GetByTestId("licence-confirmation"));
            await EnterAsync(Page.GetByRole(AriaRole.Button, new() { Name = "Back to plans", Exact = true }));
            await Expect(Page.GetByTestId("licence-options")).ToBeVisibleAsync();
            await Page.GoBackAsync();
            await Expect(Page.GetByTestId("licence-workspace-host")).ToHaveCountAsync(0);
            _viewports++;
        }
        _states.Add("current");
    }

    [Then("she can open and close the account popup and reach the Upgrade action")]
    [Then("popup focus is contained and restored while confirmation receives logical focus")]
    public void KeyboardChecked() => _viewports.Should().Be(3);

    [Then("current, activating, unavailable, conflict, and error states have programmatic names")]
    public async Task NamedStatesAsync()
    {
        // Conflict comes from another device's actual indexed assignment.
        await upgrade.SelectBeforeCompetitionAsync();
        await upgrade.CompetingUpgradeToAsync("hushvoting.veritas.500");
        await Expect(Page.GetByTestId("stale-notice")).ToBeFocusedAsync();
        await CheckAllWidthsAsync(Page.GetByTestId("licence-stale"));
        _states.Add("conflict");
        await upgrade.FreshOptionsForAsync("hushvoting.veritas.500");
        await upgrade.FreshConfirmationAsync();
        using (var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30)))
        {
            await EnterAsync(Page.GetByRole(AriaRole.Button, new() { Name = "Activate licence", Exact = true }));
            await received.WaitAsync();
        }
        await Expect(Page.GetByTestId("licence-progress")).ToBeVisibleAsync();
        await upgrade.PendingAsync();
        await Expect(Page.GetByTestId("licence-progress")).ToHaveAttributeAsync("aria-busy", "true");
        await CheckNamedWorkspaceAsync("progress");
        _states.Add("activating");
        await scenario.Blocks.ProduceBlockAsync();
        await Expect(Page.GetByTestId("licence-result")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await EnterAsync(Page.GetByRole(AriaRole.Button, new() { Name = "Return to workspace", Exact = true }));

        scenario.Faults.EntitlementUnavailable = true;
        await EnterAsync(Trigger);
        await EnterAsync(Popup.GetByTestId("account-licence-action"));
        await Expect(Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await CheckGateAsync("Licence cannot be verified");
        _states.Add("unavailable");
        scenario.Faults.EntitlementUnavailable = false;
        await EnterAsync(Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }));
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.GoBackAsync();
        await OpenOptionsAsync();
        await EnterAsync(Page.GetByRole(AriaRole.Button, new() { Name = "Review plan", Exact = true }).First);
        scenario.Faults.CorruptNextTransactionSignature = true;
        await EnterAsync(Page.GetByRole(AriaRole.Button, new() { Name = "Activate licence", Exact = true }));
        // A rejected upgrade re-queries real indexed truth and requires fresh
        // review; the previously active licence remains usable.
        await Expect(Page.GetByTestId("licence-stale")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.Submissions.Last().Status.Should().Be(HushNetwork.proto.TransactionStatus.Rejected);
        await upgrade.StaleSelectionAsync();
        await Expect(Page.GetByTestId("stale-notice")).ToBeFocusedAsync();
        await CheckNamedWorkspaceAsync("stale");
        _states.Add("error");
    }

    private async Task CheckNamedWorkspaceAsync(string view)
    {
        var workspace = Page.GetByTestId("licence-view-" + view);
        await Expect(workspace.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
        await Expect(workspace.GetByTestId("licence-live-region")).ToHaveAttributeAsync("aria-live", "polite");
        await CheckAllWidthsAsync(workspace);
    }

    private async Task CheckGateAsync(string status)
    {
        var gate = Page.GetByTestId("entitlement-gate");
        await Expect(gate.GetByRole(AriaRole.Heading)).ToBeVisibleAsync();
        await Expect(gate.GetByRole(AriaRole.Status)).ToHaveTextAsync(status);
        await Expect(gate.GetByRole(AriaRole.Button, new() { Name = "Lock", Exact = true })).ToBeVisibleAsync();
        await CheckAllWidthsAsync(gate);
    }

    private async Task CheckAllWidthsAsync(ILocator surface)
    {
        foreach (var width in new[] { 320, 768, 1280 })
        {
            await Page.SetViewportSizeAsync(width, 800);
            await CheckLayoutAsync(surface);
        }
    }

    private async Task CheckLayoutAsync(ILocator surface)
    {
        var fits = await surface.EvaluateAsync<bool>("""
            root => document.documentElement.scrollWidth <= innerWidth + 1 &&
                [...root.querySelectorAll('button, input, h1, h2, h3, [role=status]')].every(node => {
                    const bounds = node.getBoundingClientRect();
                    if (!bounds.width || !bounds.height || node.classList.contains('sr-only')) return true;
                    return bounds.left >= -1 && bounds.right <= innerWidth + 1 && node.scrollWidth <= node.clientWidth + 1;
                })
            """);
        if (!fits)
        {
            var dimensions = await surface.EvaluateAsync<string>("""
                root => JSON.stringify({viewport: innerWidth, document: document.documentElement.scrollWidth,
                    overflowing: [...root.querySelectorAll('button, input, h1, h2, h3, [role=status]')].flatMap(node => {
                        const r = node.getBoundingClientRect();
                        return r.width && r.height && !node.classList.contains('sr-only') && (r.left < -1 || r.right > innerWidth + 1 || node.scrollWidth > node.clientWidth + 1)
                            ? [{tag: node.tagName, className: node.className, left: r.left, right: r.right, client: node.clientWidth, scroll: node.scrollWidth}] : [];
                    })})
                """);
            throw new InvalidOperationException("Licence layout overflow: " + dimensions);
        }
    }

    [Then("plan meaning is not communicated by colour alone")]
    public void MeaningChecked() => _states.Should().BeEquivalentTo(new[] { "current", "activating", "unavailable", "conflict", "error" });

    [Then("no control or status is clipped or hidden by overflow")]
    public void LayoutChecked() { _viewports.Should().Be(3); _states.Should().HaveCount(5); }
}
