// EPIC-001 -> FEAT-008 AC-008-041 -> Phase 3 Tasks 3.5/3.6,
// Phase 5 Tasks 5.5/5.6, Phase 6 Tasks 6.1/6.2, Phase 7 Tasks 7.1/7.2.
// Shared password policy: FEAT-003 Phase 3 Tasks 3.3/3.4. Web execution only.
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryPasswordHierarchySteps(HushVotingScenario scenario, RecoveryProfileSteps recovery,
    AuthenticationSteps authentication, HushVotingIdentityJourney identity)
{
    private const string PasswordNfc = "Café-vault-Protection-42!";
    private const string PasswordNfd = "Cafe\u0301-vault-Protection-42!";
    private IPage Page => scenario.Page;
    private bool _passwordLeak;

    [Given("Alice confirms her recovered exact profile before choosing a Unicode device password")]
    public async Task ReadyAsync()
    {
        await HushVotingArtifactClient.RegisterAsync(PasswordNfc, PasswordNfd);
        await scenario.UseRestartableBrowserAsync();
        await recovery.RegisteredAsync();
        await recovery.RestoreAsync();
        await recovery.ConfirmAsync();
        WatchPublicChannels();
        await Expect(Page.GetByTestId("mode-password")).ToBeCheckedAsync();
    }

    [When("the Web password wraps the recovered vault key and the real node verifies unchanged identity")]
    public async Task ProtectAsync()
    {
        await Page.GetByTestId("recovery-no-retention-ack").CheckAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), PasswordNfc);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Confirm device password", new() { Exact = true }), PasswordNfc);
        var queries = scenario.Faults.IdentityQueryCount;
        using var baseline = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Expect(Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await baseline.WaitAsync();
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(queries);
        await scenario.Blocks.ProduceBlockAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await InspectAsync();
    }

    [Then("after browser process loss the canonically equivalent password unlocks the same recovered identity")]
    public async Task EquivalentAfterRestartAsync()
    {
        await scenario.CrashAndRestartBrowserAsync();
        WatchPublicChannels();
        await authentication.SafeLockedPreviewAsync();
        var queries = scenario.Faults.IdentityQueryCount;
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), PasswordNfd);
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Unlock HushVoting") }).ClickAsync();
        await Expect(Page.Locator("[data-testid=authenticated-shell], [data-testid=locked-outcome-error]").First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        (await Page.GetByTestId("locked-outcome-error").IsVisibleAsync()).Should().BeFalse("canonically equivalent device passwords must unlock the same vault");
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 1_000 });
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(queries);
        await InspectAsync();
    }

    private async Task InspectAsync()
    {
        var stored = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false, devicePassword: PasswordNfc);
        (stored.Active && stored.KeysMatch && stored.MetadataMatches && stored.NetworkMatches
            && stored.ConcreteKeysOnly && stored.DevicePasswordProtected).Should().BeTrue();
        var profile = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = identity.Keys.SigningPublicKey }, deadline: DateTime.UtcNow.AddSeconds(10));
        (profile.Successfull && profile.PublicSigningAddress == identity.Keys.SigningPublicKey
            && profile.PublicEncryptAddress == identity.Keys.EncryptPublicKey).Should().BeTrue();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        scenario.Faults.SubmittedTransactions.Any(ContainsPassword).Should().BeFalse();
        _passwordLeak.Should().BeFalse();
    }

    private void WatchPublicChannels()
    {
        Page.Console += (_, message) => _passwordLeak |= ContainsPassword(message.Text);
        Page.Request += (_, request) => _passwordLeak |= ContainsPassword(request.Url) || ContainsPassword(request.PostData ?? "");
    }

    private static bool ContainsPassword(string value) => value.Contains(PasswordNfc, StringComparison.Ordinal) || value.Contains(PasswordNfd, StringComparison.Ordinal);
}
