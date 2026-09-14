using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialProtectionSteps(HushVotingScenario scenario, CredentialFileSteps file, AuthenticationSteps authentication, HushVotingIdentityJourney identity)
{
    private IPage Page => scenario.Page;
    private int _before;

    [Given("Alice has decrypted her registered backup and reached separate device protection")]
    public async Task ReadyAsync()
    {
        await file.BackupAsync();
        await file.DecryptAsync();
        await file.ImportedAsync();
    }

    [When("Alice protects the restored keys with a separately entered device password")]
    public async Task SeparateAsync()
    {
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("empty-password-option")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("restore-device-password")).ToHaveValueAsync("");
        await Expect(Page.GetByTestId("restore-device-password-confirmation")).ToHaveValueAsync("");
        await Expect(Page.GetByTestId("submit-protection")).ToBeDisabledAsync();
        await file.ProtectAsync();
    }

    [Then("the backup password cannot unlock the restored vault and only the new device password succeeds")]
    public async Task SeparateVerifiedAsync()
    {
        await authentication.LockAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), "  päss 🔑  ");
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Unlock HushVoting") }).ClickAsync();
        await authentication.CredentialErrorAsync();
        await authentication.SafeLockedPreviewAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Unlock HushVoting") }).ClickAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
    }

    // EPIC-001 -> FEAT-009 AC-009-051 -> Phase 3 Tasks 3.7/3.8,
    // Phase 5 Tasks 5.5/5.6, Phase 7 Tasks 7.1/7.2. Web device-password evidence.
    [When("Alice confirms a separately entered password in the default device protection mode")]
    public async Task DefaultPasswordAsync()
    {
        await Expect(Page.GetByTestId("protection-devicePassword")).ToBeCheckedAsync();
        await Expect(Page.GetByTestId("restore-device-password")).ToHaveValueAsync("");
        await Expect(Page.GetByTestId("restore-device-password-confirmation")).ToHaveValueAsync("");
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("empty-password-option")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("submit-protection")).ToBeDisabledAsync();
        var queries = scenario.Faults.IdentityQueryCount;
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password"), HushVotingScenario.DevicePassword);
        await Expect(Page.GetByTestId("submit-protection")).ToBeDisabledAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password-confirmation"), "different-confirmation");
        await Expect(Page.GetByTestId("submit-protection")).ToBeDisabledAsync();
        scenario.Faults.IdentityQueryCount.Should().Be(queries);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        await file.ProtectAsync();
    }

    [Then("independent decryption verifies the password wrapped concrete keys and live restoration")]
    public async Task DefaultPasswordVerifiedAsync()
    {
        // The inspector independently derives Argon2id/HKDF, authenticates the
        // AES-GCM wrapped DEK and record with canonical AAD, and emits only facts.
        var stored = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        stored.DevicePasswordProtected.Should().BeTrue();
        stored.KeysMatch.Should().BeTrue();
        stored.ConcreteKeysOnly.Should().BeTrue();
        stored.MetadataMatches.Should().BeTrue();
        stored.NetworkMatches.Should().BeTrue();
        stored.Active.Should().BeTrue();
        stored.PendingTransactionCleared.Should().BeTrue();
        await Expect(Page.GetByTestId("restore-device-password")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("restore-device-password-confirmation")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync();
    }

    [When("the node becomes unavailable after import lookup and Alice stages device protection")]
    public async Task OfflineStagingAsync()
    {
        _before = scenario.Faults.IdentityQueryCount;
        scenario.Faults.IdentityUnavailable = true;
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password"), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password-confirmation"), HushVotingScenario.DevicePassword);
        await Page.GetByTestId("submit-protection").ClickAsync();
        await Expect(Page.GetByTestId("restore-panel").GetByRole(AriaRole.Alert)).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(_before);
        scenario.Faults.RejectedIdentityQueries.Should().BeGreaterThan(0);
    }

    [Then("staged credentials never authenticate offline and recovery succeeds only after a fresh online lookup")]
    public async Task OnlineRequiredAsync()
    {
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("entitlement-gate")).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        _before = scenario.Faults.IdentityQueryCount;
        scenario.Faults.IdentityUnavailable = false;
        await Page.ReloadAsync();
        await authentication.SafeLockedPreviewAsync();
        await identity.UnlockAndBootstrapAsync();
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(_before);
    }

    [Then("independent decryption finds only the exact concrete keys and authenticated profile network and protection metadata in the pending vault")]
    public async Task PendingVaultAsync()
    {
        var stored = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        stored.KeysMatch.Should().BeTrue();
        stored.MetadataMatches.Should().BeTrue();
        stored.NetworkMatches.Should().BeTrue();
        stored.DevicePasswordProtected.Should().BeTrue();
        stored.ConcreteKeysOnly.Should().BeTrue();
        stored.PendingRegistration.Should().BeTrue();
        stored.Active.Should().BeFalse();
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
    }
}
