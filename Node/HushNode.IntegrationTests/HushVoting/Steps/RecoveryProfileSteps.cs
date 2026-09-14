using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryProfileSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity, AuthenticationSteps authentication, RecoveryWordEntrySteps entry)
{
    private IPage Page => scenario.Page;
    private IReadOnlyList<string> _words = [];
    private int _identityQueries;

    [Given("Alice has registered her identity and removed its local vault")]
    public async Task RegisteredAsync()
    {
        _words = await identity.GenerateCandidateAsync();
        await identity.ConfirmRecoveryAndProtectAsync(_words);
        await identity.SubmitAndIndexAsync();
        await authentication.RemoveAsync();
        await authentication.StorageRemovedAsync();
    }

    [When("Alice restores her recovery words against the live node")]
    public async Task RestoreAsync()
    {
        await EnterWordsAndVerifyAsync();
        try { await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Confirm this identity", Exact = true })).ToBeVisibleAsync(); }
        catch (Exception error) when (error is PlaywrightException or TimeoutException)
        {
            var surfaces = await Page.Locator("[data-testid]").EvaluateAllAsync<string[]>("nodes => nodes.map(node => node.dataset.testid)");
            var errors = await Page.Locator("[id^=rw-error-]").EvaluateAllAsync<string[]>("nodes => nodes.map(node => node.id)");
            throw new InvalidOperationException("Recovery review unavailable; surfaces=" + string.Join(',', surfaces) + "; error identifiers=" + string.Join(',', errors) + "; RPC outcomes=" + string.Join(';', scenario.Faults.Outcomes));
        }
    }

    public async Task EnterWordsAndVerifyAsync()
    {
        await entry.EntryAsync();
        _identityQueries = scenario.Faults.RequestMethods.Count(method => method == "GetIdentity");
        foreach (var position in Enumerable.Range(1, _words.Count))
            await HushVotingIdentityJourney.FillSecretAsync(Page.Locator("#rw-" + position), _words[position - 1]);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true }).ClickAsync();
    }

    [Then("the only matching blockchain profile requires confirmation before protection")]
    public async Task ConfirmAsync()
    {
        (scenario.Faults.RequestMethods.Count(method => method == "GetIdentity") - _identityQueries).Should().Be(2, "both distinct approved formats must be resolved before choosing a profile");
        await Expect(Page.GetByTestId("candidate-list").Locator("li")).ToHaveCountAsync(1);
        await Expect(Page.GetByTestId("safe-alias")).ToHaveTextAsync(HushVotingIdentityJourney.Alias);
        await Expect(Page.GetByTestId("candidate-list")).ToContainTextAsync("Private");
        await Expect(Page.GetByTestId("word-grid")).ToHaveCountAsync(0);
        await Expect(Page.GetByLabel("Device password", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        var visible = await Page.Locator("body").InnerTextAsync();
        if (visible.Contains(string.Join(" ", _words)) || visible.Contains(identity.Keys.SigningPublicKey))
            throw new InvalidOperationException("Ordinary recovery review exposed unrequested identity material.");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to protect this device", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Protect this device", Exact = true })).ToBeVisibleAsync();
    }

    [When("device protection restores the same identity through fresh online verification")]
    [Then("device protection restores the same identity through fresh online verification")]
    public async Task ProtectAsync()
    {
        await Expect(Page.GetByTestId("mode-password")).ToBeCheckedAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true })).ToBeDisabledAsync();
        await Page.GetByTestId("recovery-no-retention-ack").CheckAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Confirm device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        var queryCount = scenario.Faults.RequestMethods.Count(method => method == "GetIdentity");
        using var baseline = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Expect(Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await baseline.WaitAsync();
        scenario.Faults.RequestMethods.Count(method => method == "GetIdentity").Should().BeGreaterThan(queryCount);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2, "only the original identity and the restored identity's baseline licence may be submitted");
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await scenario.Blocks.ProduceBlockAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = HushVotingIdentityJourney.Alias, Exact = true })).ToBeVisibleAsync();
    }
}
