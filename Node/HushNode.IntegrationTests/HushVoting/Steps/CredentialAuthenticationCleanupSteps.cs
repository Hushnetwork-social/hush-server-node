using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-009 AC-009-022 -> Phase 7 Task 7.2.
// App Twins separately inspect actual password/plaintext buffers at Web Crypto.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialAuthenticationCleanupSteps(HushVotingScenario scenario,
    HushVotingIdentityJourney identity, CredentialFileSteps file)
{
    private int _queries;
    private bool _echo;
    private const string IncorrectPassword = "failed-import-password-probe";

    [Given("Alice selects one real encrypted backup for repeated failed authentication and same-epoch Retry")]
    public async Task ReadyAsync()
    {
        await file.BackupAsync();
        scenario.Page.Console += (_, message) => _echo |= message.Text.Contains(IncorrectPassword, StringComparison.Ordinal)
            || message.Text.Contains(identity.Keys.SigningPrivateKey, StringComparison.Ordinal)
            || message.Text.Contains(identity.Keys.EncryptPrivateKey, StringComparison.Ordinal);
        await scenario.Page.EvaluateAsync("""
            () => {
                const original = MessagePort.prototype.postMessage;
                let files = 0, passwords = 0, imports = 0, bounded = true, safe = true, operationId = null;
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'secret-transfer' && message.purpose === 'fileBytes') {
                        files++; operationId = message.operationId;
                        bounded = bounded && typeof message.value === 'string' && message.value.length <= 1398104;
                    }
                    if (message?.kind === 'secret-transfer' && message.purpose === 'filePassword') passwords++;
                    if (message?.kind === 'operation' && message.operation === 'importFileCandidate') {
                        imports++;
                        safe = safe && message.operationId === operationId
                            && !('filePassword' in (message.payload ?? {})) && !('fileBytes' in (message.payload ?? {}));
                    }
                    return Reflect.apply(original, this, args);
                };
                window.__hvImportCleanup = () => ({ files, passwords, imports, bounded, safe });
            }
            """);
        await file.ChoosePreparedAsync();
        _queries = scenario.Faults.IdentityQueryCount;
    }

    [When("two wrong passwords fail without selecting or transferring another file")]
    public async Task FailuresAsync()
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await HushVotingIdentityJourney.FillSecretAsync(scenario.Page.GetByTestId("backup-password-input"), IncorrectPassword);
            await scenario.Page.GetByTestId("submit-password").ClickAsync();
            await Expect(scenario.Page.GetByText("The backup password is incorrect or the credential file is damaged.", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(scenario.Page.GetByTestId("backup-password-input")).ToHaveValueAsync("");
            await Expect(scenario.Page.GetByTestId("backup-password-input")).ToHaveAttributeAsync("type", "password");
            await Expect(scenario.Page.GetByTestId("submit-password")).ToBeDisabledAsync();
            await Expect(scenario.Page.GetByTestId("empty-password-option")).Not.ToBeCheckedAsync();
            await Expect(scenario.Page.GetByTestId("restore-device-password")).ToHaveCountAsync(0);
            await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
            scenario.Faults.IdentityQueryCount.Should().Be(_queries);
            scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
            (await scenario.Page.EvaluateAsync<bool>("attempt => { const f = window.__hvImportCleanup(); return f.files === 1 && f.passwords === attempt && f.imports === attempt && f.safe && f.bounded; }", attempt)).Should().BeTrue();
            (await scenario.Page.EvaluateAsync<bool>("""
                async () => {
                    if (localStorage.length || sessionStorage.length) return false;
                    const db = await new Promise((resolve, reject) => { const r = indexedDB.open('hushvoting-vault'); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(new Error('Storage inspection unavailable')); });
                    try {
                        const counts = await Promise.all(['vaultSlots', 'vaultJournal'].map(name => new Promise((resolve, reject) => {
                            const r = db.transaction(name, 'readonly').objectStore(name).count();
                            r.onsuccess = () => resolve(r.result); r.onerror = () => reject(new Error('Storage inspection unavailable'));
                        })));
                        return counts.every(count => count === 0);
                    } finally { db.close(); }
                }
                """)).Should().BeTrue();
            _echo.Should().BeFalse();
        }
    }

    [Then("only a fresh password can reuse the bounded ciphertext and restore the same keys through live identity and licence verification")]
    public async Task RetryAsync()
    {
        await file.SubmitPreparedPasswordAsync();
        await file.ImportedAsync();
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(_queries);
        (await scenario.Page.EvaluateAsync<bool>("() => { const f = window.__hvImportCleanup(); return f.files === 1 && f.passwords === 3 && f.imports === 3 && f.safe && f.bounded; }")).Should().BeTrue();
        await file.ProtectAsync();
        var stored = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        stored.KeysMatch.Should().BeTrue();
        stored.ConcreteKeysOnly.Should().BeTrue();
        stored.Active.Should().BeTrue();
        _echo.Should().BeFalse();
    }
}
