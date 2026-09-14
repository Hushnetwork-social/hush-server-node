using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-008 AC-008-037 -> Phase 3 Tasks 3.5/3.6, Phase 7 Tasks 7.1/7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryDestructionSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity,
    RecoveryWordEntrySteps entry)
{
    private IReadOnlyList<string> _words = [];
    private IPage Page => scenario.Page;

    [Given("Alice has an external recovery phrase for a real registered identity and an empty local vault")]
    public async Task ReadyAsync()
    {
        _words = await identity.GenerateCandidateAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true }).ClickAsync();
        await HushVotingServerIdentity.RegisterAsync(scenario, identity.Keys, HushVotingIdentityJourney.Alias);
        await entry.EntryAsync();
    }

    [When("recovery retains its selected candidate until actual encrypted read back completes then discards it before activation")]
    public async Task DestructionOrderAsync()
    {
        await using var worker = await HushVotingWorkerProbe.AttachAsync(scenario);
        (await worker.EvaluateBooleanAsync("""
            (() => {
                const set = Map.prototype.set, get = IDBObjectStore.prototype.get, put = IDBObjectStore.prototype.put, stringify = JSON.stringify;
                let candidates = null, written = false, held = false, release = null, finalLookup = false, safeLookup = true;
                const live = () => candidates ? [...candidates.values()].filter(v => v?.kind === 'words' && typeof v.mnemonic === 'string').length : -1;
                Map.prototype.set = function(key, value) {
                    if (value?.kind === 'words' && typeof value.mnemonic === 'string') candidates = this;
                    return Reflect.apply(set, this, [key, value]);
                };
                IDBObjectStore.prototype.put = function(...args) {
                    if (this.name === 'vaultSlots') written = true;
                    return Reflect.apply(put, this, args);
                };
                IDBObjectStore.prototype.get = function(...args) {
                    const request = Reflect.apply(get, this, args);
                    if (written && this.name === 'vaultSlots') {
                        const tx = this.transaction;
                        tx.addEventListener('complete', event => {
                            if (!held) {
                                event.stopImmediatePropagation(); held = true;
                                const callback = tx.oncomplete;
                                release = () => { release = null; callback?.call(tx, event); };
                            }
                        });
                    }
                    return request;
                };
                JSON.stringify = function(...args) {
                    // The BFF adapter captured fetch when the worker was created.
                    // Observe its actual public lookup body before that captured fetch is called.
                    if (written && typeof args[0]?.publicSigningAddress === 'string') { finalLookup = true; safeLookup &&= live() === 0; }
                    return Reflect.apply(stringify, this, args);
                };
                globalThis.hvRecoveryCandidates = count => live() === count;
                globalThis.hvDestructionHeld = () => held && release !== null && live() === 1 && !finalLookup;
                globalThis.hvReleaseDestruction = () => { release?.(); return true; };
                globalThis.hvDestructionComplete = () => held && release === null && live() === 0 && finalLookup && safeLookup;
                globalThis.hvRestoreDestruction = () => {
                    Map.prototype.set = set; IDBObjectStore.prototype.get = get; IDBObjectStore.prototype.put = put; JSON.stringify = stringify;
                    release?.(); candidates = null; return true;
                };
                return true;
            })()
            """)).Should().BeTrue();
        try
        {
            for (var i = 0; i < _words.Count; i++)
                await HushVotingIdentityJourney.FillSecretAsync(Page.Locator("#rw-" + (i + 1)), _words[i]);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Confirm this identity", Exact = true })).ToBeVisibleAsync();
            (await worker.EvaluateBooleanAsync("hvRecoveryCandidates(2)")).Should().BeTrue();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to protect this device", Exact = true }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Protect this device", Exact = true })).ToBeVisibleAsync();
            (await worker.EvaluateBooleanAsync("hvRecoveryCandidates(1)")).Should().BeTrue();
            await Page.GetByTestId("recovery-no-retention-ack").CheckAsync();
            await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
            await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Confirm device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
            var queries = scenario.Faults.IdentityQueryCount;
            using var baseline = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(40));
            await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await worker.EvaluateBooleanAsync("hvDestructionHeld()")) await Task.Delay(25, deadline.Token);
            await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
            await Expect(Page.GetByTestId("entitlement-gate")).ToHaveCountAsync(0);
            scenario.Faults.IdentityQueryCount.Should().Be(queries);
            scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
            (await worker.EvaluateBooleanAsync("hvReleaseDestruction()")).Should().BeTrue();
            await baseline.WaitAsync();
            (await worker.EvaluateBooleanAsync("hvDestructionComplete()")).Should().BeTrue();
            await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
            await scenario.Blocks.ProduceBlockAsync();
            await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        }
        finally { await worker.EvaluateBooleanAsync("hvRestoreDestruction()"); _words = []; }
    }

    [Then("only the selected encrypted keys remain and fresh real node verification permits access")]
    public async Task StoredKeysAsync()
    {
        (await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false)).Should().BeTrue();
        await Expect(Page.GetByTestId("word-grid")).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        var lookup = scenario.Faults.IdentityLookups.Last();
        if (!lookup.Reply.Successfull || lookup.SigningAddress != identity.Keys.SigningPublicKey
            || lookup.Reply.PublicSigningAddress != identity.Keys.SigningPublicKey || lookup.Reply.PublicEncryptAddress != identity.Keys.EncryptPublicKey)
            throw new InvalidOperationException("Staged recovery did not freshly verify the exact selected public-key pair.");
    }
}
