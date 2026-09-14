// EPIC-001 -> FEAT-008 AC-008-051 / FEAT-009 AC-009-054.
// FEAT-008 Phase 3 Tasks 3.5/3.6; FEAT-009 Phase 3 Tasks 3.7/3.8;
// both Phase 6 Tasks 6.1/6.2 and Phase 7 Tasks 7.1/7.2.
// Web process-loss branch. Other lifecycle and authority-memory evidence stays open.
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoverySessionOnlySteps(HushVotingScenario scenario, RecoveryProfileSteps recovery,
    CredentialFileSteps file, AuthenticationSteps authentication, HushVotingIdentityJourney identity)
{
    private IPage Page => scenario.Page;
    private bool _file;

    [Given("Alice restores her registered identity from (words|a credential file) to choose a temporary Web session")]
    public async Task ReadyAsync(string source)
    {
        _file = source == "a credential file";
        await scenario.UseRestartableBrowserAsync();
        if (_file)
        {
            await file.BackupAsync();
            await file.DecryptAsync();
            await file.ImportedAsync();
        }
        else
        {
            await recovery.RegisteredAsync();
            await recovery.RestoreAsync();
            await recovery.ConfirmAsync();
        }
        await NoVaultRecordsAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
    }

    [When("Alice explicitly chooses and acknowledges session-only protection without a device password")]
    public async Task ChooseAsync()
    {
        var mode = Page.GetByTestId(_file ? "protection-sessionOnly" : "mode-session");
        await Expect(mode).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await mode.CheckAsync();
        await Expect(Page.GetByLabel("Device password", new() { Exact = true })).ToHaveCountAsync(0);
        if (!_file)
        {
            await Page.GetByTestId("recovery-no-retention-ack").CheckAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true })).ToBeDisabledAsync();
            await Page.GetByTestId("session-ack").CheckAsync();
        }
        else
        {
            // The explicit acknowledgement control is required before file session activation.
            await Expect(Page.GetByTestId("submit-protection")).ToBeDisabledAsync();
            await Page.GetByRole(AriaRole.Checkbox, new() { NameRegex = new System.Text.RegularExpressions.Regex("session|saved|save", System.Text.RegularExpressions.RegexOptions.IgnoreCase) }).CheckAsync();
        }
        var queries = scenario.Faults.IdentityQueryCount;
        using var baseline = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        await (_file ? Page.GetByTestId("submit-protection")
            : Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true })).ClickAsync();
        await Expect(Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await baseline.WaitAsync();
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(queries);
        await scenario.Blocks.ProduceBlockAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var profile = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = identity.Keys.SigningPublicKey }, deadline: DateTime.UtcNow.AddSeconds(10));
        (profile.Successfull && profile.PublicSigningAddress == identity.Keys.SigningPublicKey
            && profile.PublicEncryptAddress == identity.Keys.EncryptPublicKey).Should().BeTrue();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        await NoVaultRecordsAsync();
    }

    [Then("browser process loss leaves no remembered session identity and requires recovery input again")]
    public async Task LostAsync()
    {
        await scenario.CrashAndRestartBrowserAsync();
        await authentication.FirstRunChoicesAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByLabel("Device password", new() { Exact = true })).ToHaveCountAsync(0);
        await NoVaultRecordsAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
    }

    [Then("explicit Lock forgets the temporary identity and returns to first-run choices")]
    public async Task LockAsync()
    {
        await Page.Locator(".authenticated-user-trigger").ClickAsync();
        await Page.GetByRole(AriaRole.Dialog, new() { Name = "User information" })
            .GetByRole(AriaRole.Button, new() { Name = "Lock", Exact = true }).ClickAsync();
        await authentication.FirstRunChoicesAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByLabel("Device password", new() { Exact = true })).ToHaveCountAsync(0);
        await NoVaultRecordsAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
    }

    private async Task NoVaultRecordsAsync()
    {
        var empty = await Page.EvaluateAsync<bool>("""
            async () => {
                if (!(await indexedDB.databases()).some(d => d.name === 'hushvoting-vault')) return true;
                const db = await new Promise((resolve, reject) => {
                    const r = indexedDB.open('hushvoting-vault');
                    r.onsuccess = () => resolve(r.result);
                    r.onerror = () => reject(new Error('Session vault inspection failed'));
                });
                try {
                    const counts = await Promise.all([...db.objectStoreNames].map(name => new Promise((resolve, reject) => {
                        const r = db.transaction(name).objectStore(name).count();
                        r.onsuccess = () => resolve(r.result);
                        r.onerror = () => reject(new Error('Session record count failed'));
                    })));
                    return counts.every(count => count === 0);
                } finally { db.close(); }
            }
            """);
        empty.Should().BeTrue("session-only recovery must not create persisted vault records");
    }
}
