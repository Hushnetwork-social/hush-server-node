using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-009 AC-009-001 -> Phase 3 Tasks 3.1/3.2,
// Phase 5 Tasks 5.1/5.2, Phase 7 Tasks 7.1/7.2.
// FEAT-008 AC-008-001 -> Phase 5 Tasks 5.7/5.8, Phase 6 Tasks 6.1/6.2,
// Phase 7 Tasks 7.1/7.2. Shared only within the isolated HushVoting suite.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class LocalCustodySteps(HushVotingScenario scenario, HushVotingIdentityJourney identity,
    AuthenticationSteps authentication, CredentialFileSteps file, IdentitySubmissionSteps submission)
{
    private IPage Page => scenario.Page;
    private const string BackupPassword = "custody-fixture-backup-password";
    private string _staged = "";
    private string _active = "";
    private byte[] _backup = [];
    private IReadOnlyList<string> _words = [];
    private readonly List<string> _blocked = [];

    [Given("Alice has real staged and active vault snapshots and an independent registered backup")]
    [Given("Alice has real staged and active vault snapshots and her original registered recovery words")]
    public async Task ReadyAsync()
    {
        _words = await identity.GenerateCandidateAsync();
        await identity.ConfirmRecoveryAndProtectAsync(_words);
        _staged = await StorageAsync("snapshot");
        var stage = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        (stage.PendingRegistration && stage.KeysMatch && !stage.Active).Should().BeTrue();
        await submission.SubmitAsync();
        await submission.ConfirmAsync();
        _active = await StorageAsync("snapshot");
        var active = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        (active.Active && active.KeysMatch).Should().BeTrue();
        _backup = HushVotingCredentialFile.Create(identity.Keys, HushVotingIdentityJourney.Alias, BackupPassword, isPublic: false);
        await authentication.LockAsync();
        await authentication.RemoveAsync();
        await authentication.StorageRemovedAsync();
    }

    [When("stale first-run views encounter staged active rollback removal corrupt and competing custody")]
    public async Task BlockChangedCustodyAsync() => await CheckChangedCustodyAsync(false);

    [When("stale recovery entries encounter staged active rollback removal corrupt and competing custody")]
    public async Task BlockRecoveryCustodyAsync() => await CheckChangedCustodyAsync(true);

    private async Task CheckChangedCustodyAsync(bool recovery)
    {
        foreach (var mode in new[] { "staged", "active", "rollback", "removal", "corrupt", "competing" })
        {
            await authentication.FirstRunChoicesAsync();
            await ArmInspectionAsync();
            await Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex(recovery ? "^Restore Recovery Words" : "^Restore Credential File") }).ClickAsync();
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                while (!await Page.EvaluateAsync<bool>("() => hvCustodyFacts.held")) await Task.Delay(50, deadline.Token);
            await Expect(Page.GetByText(recovery ? "Checking local credentials before recovery…" : "Checking local credentials before restore…", new() { Exact = true })).ToBeVisibleAsync();
            await NoPickerAsync();
            IPage? competing = null;
            try
            {
                if (mode == "competing")
                {
                    competing = await Page.Context.NewPageAsync();
                    await competing.GotoAsync("/");
                    await competing.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Create User") }).ClickAsync();
                    await competing.GetByLabel("Profile name / alias", new() { Exact = true }).FillAsync("Competing fixture candidate");
                    await competing.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
                    await competing.GetByRole(AriaRole.Button, new() { Name = "Generate recovery words", Exact = true }).ClickAsync();
                    await Expect(competing.GetByTestId("recovery-list").Locator("li")).ToHaveCountAsync(24);
                }
                else await StorageAsync(mode, mode == "staged" ? _staged : _active);
                var before = await StorageAsync("snapshot");
                var queries = scenario.Faults.IdentityQueryCount;
                (await Page.EvaluateAsync<bool>("() => hvReleaseCustodyInspection()")).Should().BeTrue();
                await Expect(recovery ? Page.Locator("#rw-entry-custody-error") : Page.GetByTestId("restore-panel").GetByRole(AriaRole.Alert)).ToBeVisibleAsync(new() { Timeout = 30_000 });
                await NoPickerAsync();
                (await Page.EvaluateAsync<int>("() => hvCustodyFacts.reads")).Should().Be(0);
                (await Page.EvaluateAsync<int>("() => hvCustodyFacts.phraseTransfers")).Should().Be(0);
                (await StorageAsync("snapshot") == before).Should().BeTrue("failed entry must not modify existing encrypted storage");
                scenario.Faults.IdentityQueryCount.Should().Be(queries);
                scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
                _blocked.Add(mode);
            }
            finally
            {
                if (competing is not null)
                {
                    // The owning child performs its real cleanup. Do not erase
                    // the candidate by overwriting worker state from the fixture.
                    await competing.GoBackAsync();
                    await Expect(competing.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Create User") })).ToBeVisibleAsync();
                    await competing.CloseAsync();
                }
                await StorageAsync("clear");
                await Page.ReloadAsync();
            }
        }
    }

    [Then("every fresh inspection blocks the file picker without reading or replacing credentials")]
    [Then("every fresh recovery inspection blocks phrase entry without deriving or replacing credentials")]
    public void AllCasesBlocked() => _blocked.Should().Equal("staged", "active", "rollback", "removal", "corrupt", "competing");

    [Then("verified empty custody permits the original backup to restore through the live node")]
    public async Task RestoreAsync()
    {
        await authentication.FirstRunChoicesAsync();
        await file.OpenAsync();
        await file.ChooseAsync(_backup);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("backup-password-input"), BackupPassword);
        await Page.GetByTestId("submit-password").ClickAsync();
        await Expect(Page.GetByTestId("restore-device-password")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password"), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password-confirmation"), HushVotingScenario.DevicePassword);
        var queries = scenario.Faults.IdentityQueryCount;
        await Page.GetByTestId("submit-protection").ClickAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(queries);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2, "restoration reuses the already indexed identity and licence");
        (await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false)).Should().BeTrue();
    }

    [Then("verified empty custody permits the original recovery words to restore through the live node")]
    public async Task RestoreWordsAsync()
    {
        await authentication.FirstRunChoicesAsync();
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Restore Recovery Words") }).ClickAsync();
        await Expect(Page.GetByTestId("word-grid").Locator("input")).ToHaveCountAsync(24);
        foreach (var position in Enumerable.Range(1, _words.Count))
            await HushVotingIdentityJourney.FillSecretAsync(Page.Locator("#rw-" + position), _words[position - 1]);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Confirm this identity", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("safe-alias")).ToHaveTextAsync(HushVotingIdentityJourney.Alias);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to protect this device", Exact = true }).ClickAsync();
        await Page.GetByTestId("recovery-no-retention-ack").CheckAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Confirm device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        var queries = scenario.Faults.IdentityQueryCount;
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(queries);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        (await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false)).Should().BeTrue();
    }

    private async Task NoPickerAsync()
    {
        await Expect(Page.GetByTestId("choose-file")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("credential-file-input")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("word-grid")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
    }

    private async Task ArmInspectionAsync() => await Page.EvaluateAsync("""
        () => {
            let pending = null;
            const facts = { held: false, reads: 0, phraseTransfers: 0 };
            window.hvCustodyFacts = facts;
            const send = MessagePort.prototype.postMessage;
            MessagePort.prototype.postMessage = function(...args) {
                if (args[0]?.kind === 'secret-transfer' && args[0].purpose === 'mnemonic') facts.phraseTransfers++;
                if (!facts.held && args[0]?.kind === 'operation' && args[0].operation === 'inspectStartup') {
                    facts.held = true;
                    pending = () => Reflect.apply(send, this, args);
                    return;
                }
                return Reflect.apply(send, this, args);
            };
            const slice = File.prototype.slice;
            File.prototype.slice = function(...args) { facts.reads++; return Reflect.apply(slice, this, args); };
            window.hvReleaseCustodyInspection = () => {
                if (!pending) return false;
                const release = pending; pending = null; release(); return true;
            };
        }
        """);

    // Only this scenario's encrypted slots/journal and public negative markers.
    // Snapshots stay in test memory and never enter logs or artifacts.
    private async Task<string> StorageAsync(string mode, string snapshot = "")
    {
        try
        {
            return await Page.EvaluateAsync<string>("""
                async ({ mode, snapshot }) => {
                    const db = await new Promise((resolve, reject) => { const r = indexedDB.open('hushvoting-vault'); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(new Error('Fixture storage unavailable')); });
                    const stores = ['vaultSlots', 'vaultJournal', 'operationalSidecars'];
                    try {
                        if (mode === 'snapshot') {
                            const result = {};
                            for (const store of stores) result[store] = await new Promise((resolve, reject) => {
                                const tx = db.transaction(store, 'readonly'), rows = [];
                                const cursor = tx.objectStore(store).openCursor();
                                cursor.onsuccess = () => { const value = cursor.result; if (value) { rows.push([value.key, value.value]); value.continue(); } };
                                tx.oncomplete = () => resolve(rows); tx.onerror = () => reject(new Error('Fixture read failed'));
                            });
                            return JSON.stringify(result, (_, value) => value instanceof Uint8Array ? { bytes: Array.from(value), fixtureBytes: true } : value);
                        }
                        const saved = snapshot ? JSON.parse(snapshot, (_, value) => value?.fixtureBytes === true ? new Uint8Array(value.bytes) : value) : null;
                        await new Promise((resolve, reject) => {
                            const tx = db.transaction(stores, 'readwrite');
                            for (const store of stores) tx.objectStore(store).clear();
                            if (mode === 'active' || mode === 'staged')
                                for (const store of stores) for (const [key, value] of saved[store]) tx.objectStore(store).put(value, key);
                            if (mode === 'rollback') for (const [key, value] of saved.vaultSlots) tx.objectStore('vaultSlots').put(value, key);
                            if (mode === 'removal') tx.objectStore('operationalSidecars').put({ inProgress: true, startedAt: 0, stage: 'deleting' }, 'removalTombstone');
                            if (mode === 'corrupt') tx.objectStore('vaultJournal').put({ activeSlot: 'slot-a', generation: 1 }, 'current');
                            tx.oncomplete = () => resolve(); tx.onerror = () => reject(new Error('Fixture write failed'));
                        });
                        return '';
                    } finally { db.close(); }
                }
                """, new { mode, snapshot });
        }
        catch { throw new InvalidOperationException("Owned custody fixture operation failed; encrypted record diagnostics omitted."); }
    }
}
