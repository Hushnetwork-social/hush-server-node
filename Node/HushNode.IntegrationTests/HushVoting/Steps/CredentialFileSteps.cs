using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialFileSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity, AuthenticationSteps authentication)
{
    private IPage Page => scenario.Page;
    private const string BackupPassword = "  päss 🔑  ";
    private byte[] _backup = [];
    private string? _sourcePath;
    private int _lookupCount;

    [Given("Alice has an independently encrypted HUSH v1 backup of her registered identity")]
    public async Task BackupAsync() => await PrepareBackupAsync(false);

    [Given("Alice has an independently encrypted registered backup containing its matching legacy recovery words")]
    public async Task BackupWithWordsAsync() => await PrepareBackupAsync(true);

    [Given("Alice selects a real unchanged source file containing her encrypted registered keys and recovery words")]
    public async Task SourceWithWordsAsync()
    {
        await PrepareBackupAsync(true);
        _sourcePath = await scenario.CreateEncryptedCredentialSourceAsync(_backup);
        await HushVotingArtifactClient.RegisterAsync(_sourcePath, Path.GetFileName(_sourcePath), BackupPassword, Convert.ToBase64String(_backup));
    }

    // Only the encrypted fixture created by SourceWithWordsAsync is removed;
    // this simulates unavailable external media without touching a user source.
    internal void MakeOwnedSourceUnavailable()
    {
        if (_sourcePath is null) throw new InvalidOperationException("No owned credential source was arranged.");
        File.Delete(_sourcePath);
        if (File.Exists(_sourcePath)) throw new InvalidOperationException("Owned source remained available.");
        Array.Clear(_backup);
        _backup = [];
        _sourcePath = null;
    }

    [Then("the original file remains byte for byte unchanged and the verified root explains recovery words were not retained")]
    public async Task SourceUnchangedAsync()
    {
        await VerifySourceBytesAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("backup-preservation-notice")).ToContainTextAsync("Your original backup is unchanged");
        await Expect(Page.GetByTestId("backup-preservation-notice")).ToContainTextAsync("HushVoting did not retain any recovery words");
        (await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false)).Should().BeTrue();
    }

    internal async Task VerifySourceBytesAsync()
    {
        var bytes = await File.ReadAllBytesAsync(_sourcePath ?? throw new InvalidOperationException("No owned source fixture."));
        try { System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(bytes, _backup).Should().BeTrue("the app must leave the selected source untouched"); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }

    [When("Alice locks and removes the restored local identity while keeping her original backup")]
    public async Task RemoveRestoredAsync()
    {
        await authentication.LockAsync();
        await authentication.RemoveAsync();
        await authentication.StorageRemovedAsync();
    }

    [Then("local import data is absent after restart and the external backup is still unchanged")]
    public async Task RemovedSourcePreservedAsync()
    {
        await VerifySourceBytesAsync();
        await authentication.FirstRunChoicesAsync();
        (await Page.EvaluateAsync<bool>("async () => localStorage.length === 0 && sessionStorage.length === 0 && (await caches.keys()).length === 0")).Should().BeTrue();
        await Expect(Page.GetByTestId("backup-preservation-notice")).ToHaveCountAsync(0);
        await OpenAsync();
        await Expect(Page.GetByTestId("credential-file-input")).ToHaveValueAsync("");
        await Expect(Page.GetByTestId("selected-file-name")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2, "local removal never removes or recreates the blockchain identity or licence");
    }

    [Then("browser persistence contains no credential source copy filename path or source digest after restoration")]
    public async Task NoPersistentSourceAsync()
    {
        var path = _sourcePath ?? throw new InvalidOperationException("No owned source fixture.");
        var digest = System.Security.Cryptography.SHA256.HashData(_backup);
        var probes = new[] { path, Path.GetFileName(path), Convert.ToBase64String(_backup), Convert.ToHexString(_backup).ToLowerInvariant(),
            Convert.ToHexString(digest).ToLowerInvariant(), Convert.ToBase64String(digest) };
        bool clean;
        try
        {
            clean = await Page.EvaluateAsync<bool>("""
                async probes => {
                    const forbiddenFields = new Set(['sourcepath','sourceuri','sourcehandle','filename','filepath','backuppassword','sourcefilename','temporaryciphertext','credentialfilebytes']);
                    const inspected = new WeakSet();
                    const clean = value => {
                        if (typeof value === 'string') return !probes.some(probe => value.includes(probe));
                        if (value === null || typeof value !== 'object') return true;
                        if (inspected.has(value)) return true;
                        inspected.add(value);
                        if (value instanceof Blob) return false;
                        if (value instanceof Uint8Array || value instanceof ArrayBuffer) {
                            const bytes = value instanceof Uint8Array ? value : new Uint8Array(value);
                            try {
                                if (bytes.byteLength > 1048576) return false;
                                let binary = '';
                                for (let offset = 0; offset < bytes.length; offset += 8192) binary += String.fromCharCode(...bytes.subarray(offset, offset + 8192));
                                if (!clean(btoa(binary))) return false;
                                const text = new TextDecoder().decode(bytes);
                                if (!clean(text)) return false;
                                try { return clean(JSON.parse(text)); } catch { return true; }
                            } finally { bytes.fill(0); } // getAll returned a structured clone, never the stored bytes.
                        }
                        return Object.entries(value).every(([key,item]) => !forbiddenFields.has(key.toLowerCase()) && clean(item));
                    };
                    for (const storage of [localStorage, sessionStorage]) {
                        for (let index = 0; index < storage.length; index++) {
                            const key = storage.key(index);
                            if (!clean(key) || !clean(storage.getItem(key))) return false;
                        }
                    }
                    for (const info of await indexedDB.databases()) {
                        const db = await new Promise((resolve, reject) => { const r = indexedDB.open(info.name); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(new Error('Storage inspection unavailable')); });
                        try {
                            for (const name of db.objectStoreNames) {
                                const rows = await new Promise((resolve, reject) => { const r = db.transaction(name, 'readonly').objectStore(name).getAll(undefined, 101); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(new Error('Storage inspection unavailable')); });
                                if (rows.length > 100 || !rows.every(clean)) return false;
                            }
                        } finally { db.close(); }
                    }
                    if ((await caches.keys()).length !== 0) return false;
                    const directory = await navigator.storage.getDirectory();
                    for await (const entry of directory.values()) { if (entry) return false; }
                    return true;
                }
                """, probes);
        }
        catch { throw new InvalidOperationException("Credential-source persistence inspection failed; source details omitted."); }
        clean.Should().BeTrue("only the encrypted credential vault may persist, never the imported source or its metadata");
        await VerifySourceBytesAsync();
        (await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false)).Should().BeTrue();
    }

    [When("Alice cancels an import and fails backup authentication without changing the original file")]
    public async Task CancelAndFailWithoutMutationAsync()
    {
        var queries = scenario.Faults.IdentityQueryCount;
        await ChoosePreparedAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("backup-password-input"), "cancelled-input");
        await Page.GoBackAsync();
        await authentication.FirstRunChoicesAsync();
        await VerifySourceBytesAsync();
        await OpenAsync();
        await ChoosePreparedAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("backup-password-input"), "wrong-backup-password");
        await Page.GetByTestId("submit-password").ClickAsync();
        await Expect(Page.GetByText("The backup password is incorrect or the credential file is damaged.", new() { Exact = true })).ToBeVisibleAsync();
        await VerifySourceBytesAsync();
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveValueAsync("");
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        scenario.Faults.IdentityQueryCount.Should().Be(queries);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        await Page.GetByTestId("choose-different-file").ClickAsync();
        await Expect(Page.GetByTestId("credential-file-input")).ToHaveValueAsync("");
    }

    private async Task PrepareBackupAsync(bool includeWords)
    {
        var words = await identity.GenerateCandidateAsync();
        await identity.ConfirmRecoveryAndProtectAsync(words);
        await identity.SubmitAndIndexAsync();
        _backup = HushVotingCredentialFile.Create(identity.Keys, "Old backup alias", BackupPassword, isPublic: true,
            mnemonic: includeWords ? string.Join(' ', words) : null);
        await authentication.RemoveAsync();
        await authentication.StorageRemovedAsync();
        await OpenAsync();
    }

    public async Task OpenAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Restore Credential File") }).ClickAsync();
        await Expect(Page.GetByTestId("choose-file")).ToBeVisibleAsync();
    }

    public async Task ChooseAsync(byte[] bytes, string name = "voter.dat")
    {
        try { await Page.GetByTestId("credential-file-input").SetInputFilesAsync(new FilePayload { Name = name, MimeType = "application/octet-stream", Buffer = bytes }); }
        catch (Exception error) when (error is PlaywrightException or TimeoutException) { throw new InvalidOperationException("Credential-file selection failed; encrypted fixture diagnostics omitted."); }
        await Expect(Page.GetByTestId("backup-password-input")).ToBeVisibleAsync();
    }

    [When("Alice decrypts the backup with its exact untrimmed UTF-8 password")]
    public async Task DecryptAsync()
    {
        await ChoosePreparedAsync();
        await Expect(Page.GetByTestId("empty-password-option")).Not.ToBeCheckedAsync();
        _lookupCount = scenario.Faults.IdentityQueryCount;
        await SubmitPreparedPasswordAsync();
    }

    internal async Task SubmitPreparedPasswordAsync()
    {
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("backup-password-input"), BackupPassword);
        await Page.GetByTestId("submit-password").ClickAsync();
    }

    internal async Task ChoosePreparedAsync()
    {
        if (_sourcePath is null) await ChooseAsync(_backup);
        else
        {
            try { await Page.GetByTestId("credential-file-input").SetInputFilesAsync(_sourcePath); }
            catch (Exception error) when (error is PlaywrightException or TimeoutException) { throw new InvalidOperationException("Credential-file selection failed; source diagnostics omitted."); }
            await Expect(Page.GetByTestId("backup-password-input")).ToBeVisibleAsync();
        }
    }

    [Then("the browser decrypts the approved PBKDF2 and AES-GCM envelope without altering its keys")]
    public async Task ImportedAsync() => await ImportedWithTransactionCountAsync(1);

    internal async Task ImportedWithTransactionCountAsync(int expectedTransactionCount)
    {
        await Expect(Page.GetByTestId("restore-device-password")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(_lookupCount);
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("selected-file-name")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("restore-device-password")).ToHaveValueAsync("");
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(expectedTransactionCount);
    }

    [Then("separate device protection restores that identity through the live node")]
    public async Task ProtectAsync()
    {
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password"), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password-confirmation"), HushVotingScenario.DevicePassword);
        var before = scenario.Faults.IdentityQueryCount;
        using var baseline = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        await Page.GetByTestId("submit-protection").ClickAsync();
        await Expect(Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await baseline.WaitAsync();
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(before);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await scenario.Blocks.ProduceBlockAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = HushVotingIdentityJourney.Alias, Exact = true })).ToBeVisibleAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
    }
}
