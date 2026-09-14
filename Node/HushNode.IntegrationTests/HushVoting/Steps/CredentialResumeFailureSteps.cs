using FluentAssertions;
using HushNode.Identity.Storage;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-009 AC-009-063 -> Phase 3 Tasks 3.7/3.8, Phase 7 Tasks 7.1/7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialResumeFailureSteps(HushVotingScenario scenario, CredentialResumeLookupSteps resume,
    AuthenticationSteps authentication, HushVotingIdentityJourney identity)
{
    private IPage Page => scenario.Page;
    private string _original = "";

    [Given("Alice restarts an imported protected stage with unavailable connectivity and an owned fault snapshot")]
    public async Task ReadyAsync()
    {
        await resume.StageAsync();
        await resume.RestartAsync();
        _original = await StorageAsync("read");
    }

    [When("resumption encounters offline lookup corrupted ciphertext unsupported version and a changed node key")]
    public async Task FailuresAsync()
    {
        var rejected = scenario.Faults.RejectedIdentityQueries;
        await UnlockAsync();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (scenario.Faults.RejectedIdentityQueries == rejected && DateTime.UtcNow < deadline) await Task.Delay(20);
        scenario.Faults.RejectedIdentityQueries.Should().BeGreaterThan(rejected);
        await Expect(Page.GetByText("Checking your identity with the network…", new() { Exact = true }).First).ToBeVisibleAsync();
        await NoAccessAsync();
        (await StorageAsync("read") == _original).Should().BeTrue("offline resumption must preserve the encrypted stage");

        scenario.Faults.IdentityUnavailable = false;
        try
        {
            foreach (var mode in new[] { "ciphertext", "version" })
            {
                await StorageAsync(mode);
                var altered = await StorageAsync("read");
                var queries = scenario.Faults.IdentityQueryCount;
                await Page.ReloadAsync();
                await authentication.SafeLockedPreviewAsync();
                await UnlockAsync();
                if (mode == "ciphertext")
                {
                    await authentication.CredentialErrorAsync();
                }
                else await Expect(Page.Locator(".error-surface[role=alert]")).ToBeVisibleAsync();
                await NoAccessAsync();
                scenario.Faults.IdentityQueryCount.Should().Be(queries, "invalid local credentials must fail before online lookup");
                (await StorageAsync("read") == altered).Should().BeTrue("rejection must not overwrite or remove the staged encrypted record");
            }

            await StorageAsync("restore");
            await SetEncryptionAddressAsync("02" + new string('1', 64));
            await Page.ReloadAsync();
            await authentication.SafeLockedPreviewAsync();
            var before = scenario.Faults.IdentityQueryCount;
            await UnlockAsync();
            await Expect(Page.Locator(".error-surface[role=alert]")).ToBeVisibleAsync();
            scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(before);
            var reply = scenario.Faults.IdentityLookups.Last().Reply;
            (reply.Successfull && reply.PublicSigningAddress == identity.Keys.SigningPublicKey
                && reply.PublicEncryptAddress != identity.Keys.EncryptPublicKey).Should().BeTrue();
            await NoAccessAsync();
            (await StorageAsync("read") == _original).Should().BeTrue();
        }
        finally
        {
            scenario.Faults.IdentityUnavailable = false;
            await StorageAsync("restore");
            await SetEncryptionAddressAsync(identity.Keys.EncryptPublicKey);
        }
    }

    [Then("only the repaired exact stage and fresh online verification complete credential activation")]
    public async Task RecoverAsync()
    {
        await Page.ReloadAsync();
        await authentication.SafeLockedPreviewAsync();
        await resume.ActivateAsync();
    }

    private async Task UnlockAsync()
    {
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Unlock HushVoting") }).ClickAsync();
    }

    private async Task NoAccessAsync()
    {
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("entitlement-gate")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("credential-file-input")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
    }

    private async Task SetEncryptionAddressAsync(string address)
    {
        using var scope = scenario.Node.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await db.Profiles.Where(profile => profile.PublicSigningAddress == identity.Keys.SigningPublicKey)
            .ExecuteUpdateAsync(setters => setters.SetProperty(profile => profile.PublicEncryptAddress, address))).Should().Be(1);
        await scenario.ClearCacheAsync();
    }

    // Only the scenario's encrypted slot is retained, in memory; never log it.
    private async Task<string> StorageAsync(string mode)
    {
        try
        {
            return await Page.EvaluateAsync<string>("""
                async ({ mode, original }) => {
                    const db = await new Promise((resolve, reject) => { const r = indexedDB.open('hushvoting-vault'); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(new Error('Fixture unavailable')); });
                    try {
                        return await new Promise((resolve, reject) => {
                            const tx = db.transaction(['vaultSlots','vaultJournal'], mode === 'read' ? 'readonly' : 'readwrite');
                            let value = '';
                            tx.onerror = () => reject(new Error('Fixture storage failed'));
                            tx.oncomplete = () => resolve(value);
                            if (mode === 'read') {
                                const pointer = tx.objectStore('vaultJournal').get('current');
                                pointer.onsuccess = () => {
                                    const key = pointer.result.activeSlot, slot = tx.objectStore('vaultSlots').get(key);
                                    slot.onsuccess = () => { value = JSON.stringify({ key, slot: { ...slot.result, bytes: Array.from(slot.result.bytes) } }); };
                                };
                            } else {
                                const saved = JSON.parse(original);
                                saved.slot.bytes = new Uint8Array(saved.slot.bytes);
                                if (mode !== 'restore') {
                                    const envelope = JSON.parse(new TextDecoder().decode(saved.slot.bytes));
                                    if (mode === 'version') envelope.envelopeFormatVersion = 999;
                                    if (mode === 'ciphertext') {
                                        const cipher = envelope.records.ordinary.ciphertext;
                                        envelope.records.ordinary.ciphertext = (cipher[0] === 'A' ? 'B' : 'A') + cipher.slice(1);
                                    }
                                    saved.slot.bytes = new TextEncoder().encode(JSON.stringify(envelope));
                                }
                                tx.objectStore('vaultSlots').put(saved.slot, saved.key);
                            }
                        });
                    } finally { db.close(); }
                }
                """, new { mode, original = _original });
        }
        catch { throw new InvalidOperationException("Owned resume fault operation failed; encrypted-record diagnostics omitted."); }
    }
}
