using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryProtectionSteps(HushVotingScenario scenario, RecoveryProfileSteps profile, AuthenticationSteps authentication, HushVotingIdentityJourney identity)
{
    private IPage Page => scenario.Page;
    private int _before;

    [Given("Alice has confirmed her registered recovery profile and reached device protection")]
    public async Task ReadyAsync()
    {
        await profile.RegisteredAsync();
        await profile.RestoreAsync();
        await profile.ConfirmAsync();
        _before = scenario.Faults.IdentityQueryCount;
    }

    [When("Alice enters valid device passwords without acknowledging recovery-word non-retention")]
    public async Task PrepareAsync()
    {
        await Expect(Page.GetByTestId("recovery-no-retention-ack")).Not.ToBeCheckedAsync();
        await FillProtectionAsync();
    }

    private async Task FillProtectionAsync()
    {
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Confirm device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
    }

    [Then("recovery cannot stage credentials before explicit non-retention acknowledgement")]
    public async Task ConsentRequiredAsync()
    {
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true })).ToBeDisabledAsync();
        await Expect(Page.GetByText("I understand that HushVoting will not save my recovery words.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        scenario.Faults.IdentityQueryCount.Should().Be(_before);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
    }

    [Then("activation has freshly verified both recovered public keys against the node")]
    public void FreshPair()
    {
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(_before);
        var lookup = scenario.Faults.IdentityLookups.Last();
        if (!lookup.Reply.Successfull || lookup.SigningAddress != identity.Keys.SigningPublicKey || lookup.Reply.PublicSigningAddress != identity.Keys.SigningPublicKey || lookup.Reply.PublicEncryptAddress != identity.Keys.EncryptPublicKey)
            throw new InvalidOperationException("Restored activation did not freshly verify the original exact public-key pair.");
    }

    [When("the node becomes unavailable after recovery review and Alice stages protection")]
    public async Task OfflineAsync()
    {
        scenario.Faults.IdentityUnavailable = true;
        await Page.GetByTestId("recovery-no-retention-ack").CheckAsync();
        await FillProtectionAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Expect(Page.Locator("#rw-quarantine")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.RejectedIdentityQueries.Should().BeGreaterThan(0);
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(_before);
    }

    [Then("the staged recovery stays unauthenticated and only its bounded locked preview survives restart")]
    public async Task StageSurvivesAsync()
    {
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("entitlement-gate")).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        await Page.ReloadAsync();
        await authentication.SafeLockedPreviewAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Restore Recovery Words") })).ToHaveCountAsync(0);
        var text = await Page.Locator("body").InnerTextAsync();
        if (text.Contains(identity.Keys.SigningPublicKey) || text.Contains(identity.Keys.EncryptPublicKey) || text.Contains(identity.Keys.SigningPrivateKey) || text.Contains(identity.Keys.EncryptPrivateKey))
            throw new InvalidOperationException("The staged restore preview exposed full keys.");
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Unlock HushVoting") }).ClickAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByText("Checking your identity with the network…", new() { Exact = true }).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (scenario.Faults.RejectedIdentityQueries < 2 && DateTime.UtcNow < deadline) await Task.Delay(20);
        scenario.Faults.RejectedIdentityQueries.Should().BeGreaterThanOrEqualTo(2);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
    }

    [Then("the staged recovered identity activates only after connectivity returns")]
    public async Task OnlineAsync()
    {
        scenario.Faults.IdentityUnavailable = false;
        await Page.ReloadAsync();
        await authentication.SafeLockedPreviewAsync();
        await identity.UnlockAndBootstrapAsync();
        FreshPair();
    }
}
