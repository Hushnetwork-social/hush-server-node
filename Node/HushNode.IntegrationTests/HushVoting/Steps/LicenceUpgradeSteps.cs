using System.Globalization;
using FluentAssertions;
using HushNetwork.proto;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class LicenceUpgradeSteps(HushVotingScenario scenario, HushVotingLicenceActor actor)
{
    private IPage Page => scenario.Page;
    private ILocator Account => Page.GetByRole(AriaRole.Dialog, new() { Name = "User information" });
    private ILocator Summary => Account.GetByRole(AriaRole.Region, new() { Name = "Licence", Exact = true });
    private ILocator Button(string name) => Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
    private LicenceActiveEntitlementView Current => scenario.Faults.Entitlements.Last(e => e.Active is not null).Active;
    private LicenceActiveEntitlementView _initial = null!;
    private LicenceHigherOptionView _target = null!;
    private int _submissionCount;

    [Given("HushServerNode has indexed an active HushVoting! Veritas 2k licence for Alice")]
    public async Task IndexedVeritasAsync()
    {
        IndexedDirectFree();
        var reply = await actor.UpgradeAsync(_initial, "hushvoting.veritas.2000");
        reply.Status.Should().Be(TransactionStatus.Accepted, "safe node validation code: {0}", reply.ValidationCode);
        await scenario.Blocks.ProduceBlockAsync();
        await WaitForPlanAsync("hushvoting.veritas.2000");
        _initial = Current.Clone();
        _submissionCount = scenario.Faults.SubmittedTransactions.Count;
    }

    private async Task WaitForPlanAsync(string plan)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            var reply = await actor.QueryAsync();
            if (reply.Active?.PlanId == plan) return;
            await Task.Delay(100, deadline.Token);
        }
    }

    [Then("no activation control exists for the current Veritas 2k plan")]
    public async Task CurrentNotActionableAsync()
    {
        await Expect(Page.Locator(".licence-current-name")).ToHaveTextAsync(_initial.DisplayName);
        await Expect(Page.Locator(".licence-current-detail").GetByRole(AriaRole.Button, new() { Name = "Review plan" })).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("review-" + _initial.PlanId)).ToHaveCountAsync(0);
    }

    [Then("lower or Enterprise plans are not actionable in the delivered UI")]
    public async Task LowerNotActionableAsync()
    {
        await ServerOptionsAsync();
        await Expect(Page.GetByTestId("review-hushvoting.direct.free")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("review-hushvoting.veritas.500")).ToHaveCountAsync(0);
        await EnterpriseAsync();
    }

    [Then("HushServerNode returns a stable typed rejection for any such attempt")]
    public async Task RejectionsAsync()
    {
        var cases = new Dictionary<string, string>
        {
            [_initial.PlanId] = "LICENCE_TRANSITION_UNCHANGED",
            ["hushvoting.direct.free"] = "LICENCE_TRANSITION_NOT_HIGHER",
            ["hushvoting.veritas.500"] = "LICENCE_TRANSITION_NOT_HIGHER",
            ["hushvoting.enterprise"] = "LICENCE_ENTERPRISE_ADMIN_ONLY"
        };
        foreach (var (plan, code) in cases)
        {
            var reply = await actor.UpgradeAsync(_initial, plan);
            reply.Status.Should().Be(TransactionStatus.Rejected);
            reply.ValidationCode.Should().Be(code);
        }
    }

    [Then("Alice's plan, expiry, and assignment remain unchanged")]
    public async Task AssignmentUnchangedAsync()
    {
        await scenario.Blocks.ProduceBlockAsync();
        var reply = await actor.QueryAsync();
        reply.Active.Should().BeEquivalentTo(_initial);
        await Expect(Page.Locator(".licence-current-name")).ToHaveTextAsync(_initial.DisplayName);
        await Expect(Page.GetByTestId("licence-reference-full")).ToHaveTextAsync(_initial.LicenceReference);
    }

    [Given("Alice selected a higher Veritas plan while Direct Free was effective")]
    public async Task SelectBeforeCompetitionAsync() { IndexedDirectFree(); await ReviewAsync(); }

    [When("indexed truth changes to a different compatible current licence before activation")]
    public Task CompetingUpgradeAsync() => CompetingUpgradeToAsync("hushvoting.veritas.2000");

    public async Task CompetingUpgradeToAsync(string plan)
    {
        // Another device changes Alice's real indexed assignment after this UI took its snapshot.
        var reply = await actor.UpgradeAsync(_initial, plan);
        reply.Status.Should().Be(TransactionStatus.Accepted, "safe node validation code: {0}", reply.ValidationCode);
        await scenario.Blocks.ProduceBlockAsync();
        await WaitForPlanAsync(plan);
        // Activation performs its fresh precondition check; the old confirmation cannot commit.
        await Button("Activate licence").ClickAsync();
        await Expect(Page.GetByTestId("licence-stale")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        _submissionCount = scenario.Faults.SubmittedTransactions.Count;
    }

    [Then("the selection is cleared and the exact changed-options message is shown")]
    public async Task StaleSelectionAsync()
    {
        await Expect(Page.GetByTestId("stale-notice")).ToHaveTextAsync("Your licence or available plans have changed. Please review the updated options.");
        await Expect(Page.GetByTestId("option-selected")).ToHaveCountAsync(0);
        await Expect(Button("Activate licence")).ToHaveCountAsync(0);
    }

    [Then("fresh options are presented from the authoritative query")]
    public Task FreshOptionsAsync() => FreshOptionsForAsync("hushvoting.veritas.2000");

    public async Task FreshOptionsForAsync(string plan)
    {
        _initial = Current.Clone();
        _initial.PlanId.Should().Be(plan);
        await ServerOptionsAsync();
        await Expect(Page.Locator(".licence-current-name")).ToHaveTextAsync(_initial.DisplayName);
    }

    [Then("activation requires a new selection and explicit confirmation")]
    public async Task FreshConfirmationAsync()
    {
        _target = Current.HigherOptions.First().Clone();
        await Page.GetByTestId("review-" + _target.PlanId).ClickAsync();
        await ConfirmationAsync();
        await NoMutationAsync();
    }

    [Given("HushServerNode has indexed an active Direct Free licence for Alice")]
    public void IndexedDirectFree()
    {
        Current.PlanId.Should().Be("hushvoting.direct.free");
        Current.EligibleVoterCap.Should().Be(100);
        _initial = Current.Clone();
        _submissionCount = scenario.Faults.SubmittedTransactions.Count;
    }

    [When("the Account popup opens and its licence entry is refreshed")]
    public async Task OpenAccountAsync()
    {
        await Button(HushVotingIdentityJourney.Alias).ClickAsync();
        await Expect(Summary).ToBeVisibleAsync();
    }

    [Then("the popup licence block shows HushVoting! Direct Free as current")]
    public async Task AccountCurrentAsync() => await Expect(Summary.Locator(".licence-account-plan")).ToHaveTextAsync(_initial.DisplayName);

    [Then("the popup shows Active and the exact 100 eligible-voter limit")]
    public async Task AccountLimitsAsync()
    {
        await Expect(Summary.GetByText("Active", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Summary).ToContainTextAsync("100");
        await Expect(Summary).ToContainTextAsync("Eligible voters");
    }

    [Then("the popup shows a shortened licence reference and the Upgrade action")]
    public async Task ShortReferenceAsync()
    {
        var reference = _initial.LicenceReference;
        reference.Length.Should().BeGreaterThan(16);
        await Expect(Summary.GetByTestId("licence-short-reference")).ToContainTextAsync(reference[..8] + "…" + reference[^5..]);
        await Expect(Summary.GetByTestId("account-licence-action")).ToHaveTextAsync("Upgrade");
    }

    [Then("no licence value from another identity is present")]
    public async Task SameIdentityAsync()
    {
        await AccountCurrentAsync();
        await ShortReferenceAsync();
        await Expect(Summary.Locator(".licence-account-plan")).ToHaveCountAsync(1);
        Current.LicenceReference.Should().Be(_initial.LicenceReference);
    }

    [When("Alice opens Upgrade from the account licence block")]
    [When("Alice opens the licence page")]
    public async Task OpenOptionsAsync()
    {
        await OpenAccountAsync();
        await Summary.GetByTestId("account-licence-action").ClickAsync();
        await Expect(Page.GetByTestId("licence-options")).ToBeVisibleAsync();
    }

    [Then("the full-width licence page shows the current Direct Free detail first")]
    public async Task CurrentFirstAsync()
    {
        await Expect(Page.Locator(".licence-current-name")).ToHaveTextAsync(_initial.DisplayName);
        await Expect(Page.GetByTestId("licence-reference-full")).ToHaveTextAsync(_initial.LicenceReference);
        await Expect(Page.GetByTestId("licence-options").Locator("h2").First).ToHaveTextAsync("Current licence");
        var width = await Page.GetByTestId("licence-workspace-host").BoundingBoxAsync();
        width.Should().NotBeNull();
        width!.Width.Should().BeGreaterThan(Page.ViewportSize!.Width * 0.7f);
    }

    [Then("it lists only strictly higher Veritas plans in server order")]
    public async Task ServerOptionsAsync()
    {
        Current.HigherOptions.Should().NotBeEmpty();
        Current.HigherOptions.Should().OnlyContain(o => o.EligibleVoterCap > Current.EligibleVoterCap);
        await Expect(Page.Locator(".licence-option-name")).ToHaveTextAsync(Current.HigherOptions.Select(o => o.DisplayName).ToArray());
        await Expect(Button("Review plan")).ToHaveCountAsync(Current.HigherOptions.Count);
    }

    [Then("each option shows its exact cap and one-year term from the catalogue")]
    public async Task OptionFactsAsync()
    {
        foreach (var option in Current.HigherOptions)
        {
            option.TermYears.Should().Be(1);
            var card = Page.Locator(".licence-option").Filter(new() { Has = Page.GetByRole(AriaRole.Heading, new() { Name = option.DisplayName, Exact = true }) });
            await Expect(card).ToContainTextAsync($"Up to {option.EligibleVoterCap.ToString("N0", CultureInfo.GetCultureInfo("en-GB"))} eligible voters");
            await Expect(card).ToContainTextAsync("One-year term");
        }
    }

    [Then("Enterprise is informational with no activation, link, form, or request")]
    public async Task EnterpriseAsync()
    {
        var enterprise = Page.GetByRole(AriaRole.Complementary, new() { Name = Current.Enterprise.DisplayName });
        await Expect(enterprise).ToBeVisibleAsync();
        await Expect(enterprise.Locator("a,button,input,form,[role=button]")).ToHaveCountAsync(0);
        await Expect(enterprise).ToContainTextAsync("Contact provider — not yet available");
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_submissionCount);
    }

    [Then("no price, payment, or provider submission surface is shown")]
    public async Task NoPaymentAsync() => await Expect(Page.GetByTestId("licence-workspace-host").Locator("form,input,iframe")).ToHaveCountAsync(0);

    [When("Alice reviews a higher Veritas plan")]
    public async Task ReviewAsync()
    {
        await OpenOptionsAsync();
        _target = Current.HigherOptions.First().Clone();
        await Page.GetByTestId("review-" + _target.PlanId).ClickAsync();
        await Expect(Page.GetByTestId("licence-confirmation")).ToBeVisibleAsync();
    }

    [Then("no assignment change happens before confirmation")]
    [Then("Direct Free remains effective and no activation operation exists")]
    public async Task NoMutationAsync()
    {
        Current.Should().BeEquivalentTo(_initial);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_submissionCount);
        await Expect(Page.GetByTestId("pending-upgrade-indicator")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("licence-progress")).ToHaveCountAsync(0);
    }

    [Then("confirmation shows current and target plans with the exact one-year term and supersession")]
    public async Task ConfirmationAsync()
    {
        var confirmation = Page.GetByTestId("licence-confirmation");
        await Expect(confirmation.Locator("h3")).ToHaveTextAsync(new[] { _initial.DisplayName, _target.DisplayName });
        await Expect(confirmation).ToContainTextAsync("Once indexed, it immediately supersedes the current licence.");
        await Expect(confirmation).ToContainTextAsync("The new term lasts one year from the committed activation instant.");
        await Expect(confirmation.GetByTestId("licence-reference-full")).ToHaveTextAsync(_initial.LicenceReference);
        await Expect(Button("Activate licence")).ToBeEnabledAsync();
    }

    [When("Alice cancels confirmation")]
    public async Task CancelAsync()
    {
        await Button("Back to plans").ClickAsync();
        await Expect(Page.GetByTestId("licence-options")).ToBeVisibleAsync();
    }

    [Then("reopening requires a fresh selection and confirmation again")]
    public async Task ReselectAsync()
    {
        await Expect(Button("Activate licence")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("option-selected")).ToHaveCountAsync(0);
        await Page.GetByTestId("review-" + _target.PlanId).ClickAsync();
        await ConfirmationAsync();
        await NoMutationAsync();
    }

    [When("Alice confirms and activates a strictly higher Veritas plan")]
    public async Task ActivateAsync()
    {
        await ReviewAsync();
        using var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        await Button("Activate licence").ClickAsync();
        await received.WaitAsync();
        await Expect(Page.GetByTestId("licence-progress")).ToBeVisibleAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_submissionCount + 1);
    }

    [Then("pending shows the old indexed limits remain in effect and the pending target")]
    public async Task PendingAsync()
    {
        await Expect(Page.GetByTestId("progress-target-name")).ToHaveTextAsync(_target.DisplayName);
        await Expect(Page.GetByTestId("licence-progress")).ToContainTextAsync(_initial.DisplayName);
        await Expect(Page.GetByTestId("licence-progress")).ToContainTextAsync("Existing limits continue until indexed activation confirms the change.");
    }

    [Then("no higher capability is granted before indexed confirmation")]
    public void OldCapabilities() => Current.Should().BeEquivalentTo(_initial);

    [When("the exact sealed transaction is indexed for Alice")]
    public async Task IndexAsync() => await scenario.Blocks.ProduceBlockAsync();

    [Then("the current licence becomes the higher Veritas plan with a one-year term")]
    public async Task ActivatedAsync()
    {
        await Expect(Page.GetByTestId("licence-result")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Current.PlanId.Should().Be(_target.PlanId);
        Current.EligibleVoterCap.Should().Be(_target.EligibleVoterCap);
        Current.TermYears.Should().Be(1);
        DateTimeOffset.Parse(Current.ExpiresAtUtc, CultureInfo.InvariantCulture).Should().Be(DateTimeOffset.Parse(Current.EffectiveFromUtc, CultureInfo.InvariantCulture).AddYears(1));
        Current.LicenceReference.Should().NotBe(_initial.LicenceReference);
        await Expect(Page.GetByTestId("licence-result").Locator(".licence-current-name")).ToHaveTextAsync(_target.DisplayName);
    }

    [Then("Account refreshes to the higher plan exactly once without a repeated notification")]
    public async Task AccountUpgradedAsync()
    {
        // Exact local result consumes the success; it must not also toast it.
        await Expect(Page.GetByTestId("licence-activation-notification")).ToHaveCountAsync(0);
        await Button("Return to workspace").ClickAsync();
        await OpenAccountAsync();
        await Expect(Summary.Locator(".licence-account-plan")).ToHaveTextAsync(_target.DisplayName);
        await Page.Keyboard.PressAsync("Escape");
        await scenario.Blocks.ProduceBlockAsync();
        await OpenAccountAsync();
        await Expect(Summary.Locator(".licence-account-plan")).ToHaveTextAsync(_target.DisplayName);
        await Expect(Page.GetByTestId("licence-activation-notification")).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_submissionCount + 1);
    }

    [When("Alice selects a higher Veritas plan and uses in-app Back")]
    public async Task InAppBackAsync() { await ReviewAsync(); await CancelAsync(); }

    [Then("the options surface returns with no draft and no submitted transaction")]
    public async Task BackClearsDraftAsync() { await NoMutationAsync(); await Expect(Page.GetByTestId("option-selected")).ToHaveCountAsync(0); }

    [When("Alice opens a plan and uses browser Back")]
    public async Task BrowserBackAsync()
    {
        await Page.GetByTestId("review-" + _target.PlanId).ClickAsync();
        await Expect(Page.GetByTestId("licence-confirmation")).ToBeVisibleAsync();
        await Page.GoBackAsync();
    }

    [Then("the safe shell state returns without duplicate activation or stale plan display")]
    public async Task SafeBackAsync()
    {
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("licence-confirmation")).ToHaveCountAsync(0);
        await NoMutationAsync();
    }
}
