using System.Text.RegularExpressions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class AuthenticationSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity, FeatureContext feature)
{
    private IPage Page => scenario.Page;
    private ILocator Button(string text) => Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex(text, RegexOptions.IgnoreCase) });

    [Given("a provisioned vault exists on this device")]
    public async Task ProvisionAsync() => await identity.ProvisionAsync();

    [Given("an authenticated session is active")]
    [Given("Alice has completed exact EPIC-001 identity authentication")]
    [Given("Alice's identity is authenticated")]
    public async Task AuthenticateAsync()
    {
        if (feature.FeatureInfo.Tags.Contains("HV-FEAT-016", StringComparer.Ordinal))
            await identity.AuthenticateUntilEntitlementAsync();
        else await identity.AuthenticateAsync();
    }

    [When("the returning user submits a wrong device password")]
    [When("the user submits a wrong device password")]
    public async Task WrongPasswordAsync()
    {
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), "wrong-password-for-hushvoting-test");
        await Button("^Unlock HushVoting").ClickAsync();
    }

    [Then("the combined credential error is shown")]
    public async Task CredentialErrorAsync() => await Expect(Page.GetByTestId("locked-outcome-error")).ToBeVisibleAsync(new() { Timeout = 30_000 });

    [Then("the user stays locked with the safe identity preview only")]
    [Then("the locked surface shows only safe identity preview fields")]
    public async Task SafeLockedPreviewAsync()
    {
        await Expect(Button("^Unlock HushVoting")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        if ((await Page.Locator("input[type=password]").InputValueAsync()).Length != 0)
            throw new InvalidOperationException("Locked password input retained credential material.");
        await Expect(Page.GetByTestId("recovery-list")).ToHaveCountAsync(0);
    }

    [When("the application restarts")]
    public async Task RestartAsync()
    {
        await Page.ReloadAsync();
        await Expect(Button("^Unlock HushVoting")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Then("no protected content mounts")]
    [Then("no success is ever claimed")]
    public async Task NoProtectedContentAsync() => await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);

    [Given("the identity lookup endpoint is unreachable")]
    public void IdentityOutage() => scenario.Faults.TransportUnavailable = true;

    [When("verification is attempted")]
    public async Task AttemptVerificationAsync()
    {
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await Button("^Unlock HushVoting").ClickAsync();
    }

    [Then("the offline retry surface is shown with a bounded retry")]
    public async Task OfflineAsync()
    {
        await Expect(Page.GetByText("Checking your identity with the network…", new() { Exact = true }).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.GetByText(new Regex("offline", RegexOptions.IgnoreCase)).First).ToBeVisibleAsync();
        if (scenario.Faults.RejectedIdentityQueries == 0)
            throw new InvalidOperationException("The outage must be observed at the real node's identity RPC boundary.");
        await NoProtectedContentAsync();
    }

    [When("the user locks the device")]
    public async Task LockAsync()
    {
        await Button(HushVotingIdentityJourney.Alias).ClickAsync();
        await Page.GetByRole(AriaRole.Dialog, new() { Name = "User information" }).GetByRole(AriaRole.Button, new() { Name = "Lock", Exact = true }).ClickAsync();
    }

    [Then("protected content unmounts and the locked surface returns")]
    public async Task LockedAsync() => await SafeLockedPreviewAsync();

    [When("the user confirms local removal")]
    public async Task RemoveAsync()
    {
        await Button("^Remove local user$").ClickAsync();
        await Page.GetByLabel("Type REMOVE to continue", new() { Exact = true }).FillAsync("REMOVE");
        await Page.GetByRole(AriaRole.Checkbox).CheckAsync();
        await Button("^Remove local user$").ClickAsync();
    }

    [Then("every vault artifact is deleted and verified absent")]
    public async Task StorageRemovedAsync()
    {
        await Expect(Button("^Create User")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var counts = await Page.EvaluateAsync<int[]>("""
            async () => {
                const databases = await indexedDB.databases();
                if (!databases.some(d => d.name === 'hushvoting-vault')) return [];
                const db = await new Promise((resolve, reject) => { const r = indexedDB.open('hushvoting-vault'); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(new Error('Vault inspection failed')); });
                try {
                    return await Promise.all([...db.objectStoreNames].map(name => new Promise((resolve, reject) => {
                        const r = db.transaction(name).objectStore(name).count(); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(new Error('Vault count failed'));
                    })));
                } finally { db.close(); }
            }
            """);
        if (counts.Any(count => count != 0)) throw new InvalidOperationException("Removal left stored vault records.");
        await Page.ReloadAsync();
        await Expect(Button("^Create User")).ToBeVisibleAsync();
    }

    [Given("the ordinary development server serves the real root")]
    [Given("there is no local user")]
    [Given("the first-run entry renders")]
    [When("a fresh browser context opens the root")]
    public async Task OpenRootAsync()
    {
        await Page.GotoAsync("/");
        try { await Expect(Button("^Create User")).ToBeVisibleAsync(new() { Timeout = 20_000 }); }
        catch (Exception error) when (error is PlaywrightException or TimeoutException)
        {
            // First-run diagnostics contain identifiers only, never DOM/credential contents.
            var surfaces = await Page.Locator("[data-testid]").EvaluateAllAsync<string[]>("nodes => nodes.map(n => n.dataset.testid)");
            throw new InvalidOperationException("First-run entry unavailable; surface identifiers: " + string.Join(", ", surfaces));
        }
    }

    [When("the first-run entry renders")]
    public async Task EntryRendersAsync() => await Expect(Button("^Create User")).ToBeVisibleAsync();

    [Then("Create User, Restore Credential File, and Restore Recovery Words are shown with equal primary weight")]
    [Then("the three first-run choices return")]
    [Then("the first-run entry returns")]
    public async Task FirstRunChoicesAsync()
    {
        var names = new[] { "^Create User", "^Restore Credential File", "^Restore Recovery Words" };
        var classes = new List<string?>();
        foreach (var name in names)
        {
            await Expect(Button(name)).ToHaveCountAsync(1);
            await Expect(Button(name)).ToBeVisibleAsync();
            classes.Add(await Button(name).GetAttributeAsync("class"));
        }
        if (classes.Distinct().Count() != 1) throw new InvalidOperationException("First-run actions must have equal visual weight.");
    }

    [Then("no password field exists anywhere on the entry")]
    public async Task NoPasswordAsync() => await Expect(Page.Locator("input[type=password]")).ToHaveCountAsync(0);

    [Then("the visible URL stays root-only")]
    [Then("the URL stays root-only")]
    public void RootOnlyUrl()
    {
        var uri = new Uri(Page.Url);
        if (uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("Authentication navigation must keep a root-only URL.");
    }

    [Then("the real target-aware composition is selected and never a synthetic actor")]
    public async Task RealCompositionAsync()
    {
        await Expect(Button("^Create User")).ToBeVisibleAsync();
        var hasVaultDatabase = await Page.EvaluateAsync<bool>("async () => (await indexedDB.databases()).some(db => db.name === 'hushvoting-vault')");
        if (!hasVaultDatabase) throw new InvalidOperationException("The ordinary root must inspect the real IndexedDB vault through its worker.");
        if (Environment.GetEnvironmentVariable("NEXT_PUBLIC_HUSH_TEST_HARNESS") == "1")
            throw new InvalidOperationException("Synthetic frontend composition is forbidden for HushVoting E2E.");
        RootOnlyUrl();
    }

    [Given("the user selects Create User")]
    [When("the user selects Create User")]
    [When("Create User is selected")]
    public async Task SelectCreateAsync()
    {
        if (Page.Url == "about:blank") await OpenRootAsync();
        await Button("^Create User").ClickAsync();
    }

    [Then("the real child flow mounts without a placeholder")]
    public async Task ChildFlowAsync()
    {
        await Expect(Page.GetByRole(AriaRole.Heading, new() { NameRegex = new Regex("Security check|Create user.*Profile", RegexOptions.IgnoreCase) })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Setting up…", new() { Exact = true })).ToHaveCountAsync(0);
        RootOnlyUrl();
    }

    [When("the user selects Create User and then goes Back")]
    public async Task CreateThenBackAsync()
    {
        await SelectCreateAsync();
        await ChildFlowAsync();
        await Page.GoBackAsync();
    }

    [Then("no screenshot, trace, video, DOM snapshot, or raw log is captured")]
    [Then("no secret-bearing evidence is captured")]
    public void CaptureDisabled()
    {
        if (scenario.CaptureEnabled || Page.Video is not null)
            throw new InvalidOperationException("Credential-bearing HushVoting scenarios prohibit browser capture.");
    }

    public void NoBrowserArtifacts()
    {
        var output = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_OUTPUT")!;
        var prohibited = new HashSet<string> { ".png", ".jpg", ".webm", ".zip", ".har", ".html", ".log" };
        if (Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Any(p => prohibited.Contains(Path.GetExtension(p))))
            throw new InvalidOperationException("Prohibited raw browser artifact found in HushVoting evidence.");
        CaptureDisabled();
    }
}
