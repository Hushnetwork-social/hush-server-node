using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-008 AC-008-052 -> Phase 2 Tasks 2.3/2.4,
// Phase 3 Tasks 3.5/3.6, Phase 7 Tasks 7.1/7.2.
// FEAT-009 AC-009-055 -> Phase 2 Tasks 2.5/2.6, Phase 3 Tasks 3.7/3.8.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RestoredProtectionMetadataSteps(HushVotingScenario scenario, RecoveryProfileSteps recovery,
    AuthenticationSteps authentication, HushVotingIdentityJourney identity, CredentialFileSteps file)
{
    private IPage Page => scenario.Page;

    [Given("Alice has restored and locked a real password-protected recovery vault")]
    public async Task ReadyAsync()
    {
        await recovery.RegisteredAsync();
        await recovery.RestoreAsync();
        await recovery.ConfirmAsync();
        await recovery.ProtectAsync();
        await authentication.LockAsync();
    }

    [Given("Alice has imported and locked a real password-protected credential-file vault")]
    public async Task FileReadyAsync()
    {
        await file.SourceWithWordsAsync();
        await file.DecryptAsync();
        await file.ImportedAsync();
        await file.ProtectAsync();
        await file.SourceUnchangedAsync();
        await authentication.LockAsync();
    }

    [When("closed recovery metadata encounters unknown downgraded native unwrapped and future-version faults")]
    [When("closed imported metadata encounters unknown downgraded native unwrapped and future-version faults")]
    public async Task RejectAsync()
    {
        foreach (var mode in new[] { "unknown", "none", "session-only", "webauthn-prf", "ubuntu-secret-service", "android-keystore", "version", "unwrapped" })
        {
            await Page.ReloadAsync();
            await authentication.SafeLockedPreviewAsync();
            await Expect(Page.GetByLabel("Device password", new() { Exact = true })).ToHaveValueAsync("");
            var queries = scenario.Faults.IdentityQueryCount;
            await UnlockButton.ClickAsync();
            await Expect(Page.GetByText("Enter your device password.", new() { Exact = true })).ToBeVisibleAsync();
            scenario.Faults.IdentityQueryCount.Should().Be(queries);
            await using var worker = await HushVotingWorkerProbe.AttachAsync(scenario);
            try
            {
                (await worker.EvaluateBooleanAsync("""
                    (() => {
                        const decrypt = SubtleCrypto.prototype.decrypt;
                        let injected = 0;
                        SubtleCrypto.prototype.decrypt = async function(...args) {
                            const result = await Reflect.apply(decrypt, this, args);
                            const bytes = new Uint8Array(result);
                            if (bytes.length > 32 && bytes[0] === 123) {
                                const record = JSON.parse(new TextDecoder().decode(bytes));
                                if (Object.hasOwn(record, 'protectionModeClass')) {
                                    injected++;
                                    const mode = 'FAULT_MODE';
                                    if (mode === 'version') record.schemaVersion = 999;
                                    else if (mode === 'unwrapped') record.unwrappedDataKey = 'forbidden-mode-override';
                                    else record.protectionModeClass = mode;
                                    bytes.fill(0);
                                    return new TextEncoder().encode(JSON.stringify(record)).buffer;
                                }
                            }
                            return result;
                        };
                        globalThis.hvProtectionFaultObserved = () => injected === 1;
                        globalThis.hvRestoreProtectionFault = () => {
                            SubtleCrypto.prototype.decrypt = decrypt;
                            delete globalThis.hvProtectionFaultObserved;
                            delete globalThis.hvRestoreProtectionFault;
                        };
                        return true;
                    })()
                    """.Replace("FAULT_MODE", mode, StringComparison.Ordinal))).Should().BeTrue();
                await UnlockAsync();
                await Expect(Page.Locator(".error-surface[role=alert]")).ToBeVisibleAsync();
                (await worker.EvaluateBooleanAsync("globalThis.hvProtectionFaultObserved()")).Should().BeTrue();
                await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
                await Expect(Page.GetByTestId("entitlement-gate")).ToHaveCountAsync(0);
                scenario.Faults.IdentityQueryCount.Should().Be(queries);
                scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
            }
            finally
            {
                await worker.EvaluateBooleanAsync("(() => { globalThis.hvRestoreProtectionFault?.(); return true; })()");
            }
        }
    }

    [Then("only the unchanged password-protected record and fresh exact verification restore access")]
    public async Task RecoverAsync()
    {
        await Page.ReloadAsync();
        await authentication.SafeLockedPreviewAsync();
        var queries = scenario.Faults.IdentityQueryCount;
        await UnlockAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(queries);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        var stored = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        stored.KeysMatch.Should().BeTrue();
        stored.Active.Should().BeTrue();
        stored.DevicePasswordProtected.Should().BeTrue();
        stored.ConcreteKeysOnly.Should().BeTrue();
    }

    private ILocator UnlockButton => Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Unlock HushVoting") });

    private async Task UnlockAsync()
    {
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await UnlockButton.ClickAsync();
    }
}
