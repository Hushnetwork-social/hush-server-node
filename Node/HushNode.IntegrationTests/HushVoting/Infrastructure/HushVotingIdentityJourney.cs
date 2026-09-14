using System.Text.RegularExpressions;
using Olimpo.KeyDerivation;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Infrastructure;

/// <summary>HushVoting-owned UI setup through the delivered credential authority.</summary>
internal sealed class HushVotingIdentityJourney(HushVotingScenario scenario)
{
    public const string Alias = "HushVoting E2E Alice";
    // Test-owned material is retained only in memory; never put it in assertions or artifacts.
    public DerivedKeys Keys { get; private set; } = null!;
    private IPage Page => scenario.Page;
    private ILocator Button(string name) => Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex(name, RegexOptions.IgnoreCase) });

    public async Task<IReadOnlyList<string>> GenerateCandidateAsync(string alias = Alias)
    {
        await Page.GotoAsync("/");
        await Button("^Create User").ClickAsync();
        await Page.GetByLabel("Profile name / alias", new() { Exact = true }).FillAsync(alias);
        await Button("^Continue$").ClickAsync();
        await Button("^Generate recovery words$").ClickAsync();
        return await ReadCandidateAsync();
    }

    public async Task<IReadOnlyList<string>> ReadCandidateAsync()
    {
        await Expect(Page.GetByTestId("recovery-list").Locator("li")).ToHaveCountAsync(24, new() { Timeout = 30_000 });
        var words = await Page.GetByTestId("recovery-list").Locator("li > span:last-child").AllTextContentsAsync();
        try { Keys = HushVotingTestIdentity.DeriveP01(string.Join(' ', words)); }
        catch { throw new InvalidOperationException("Generated identity did not satisfy the shared derivation contract."); }
        await HushVotingArtifactClient.RegisterAsync(string.Join(' ', words), Keys.SigningPrivateKey, Keys.EncryptPrivateKey,
            Keys.SigningPublicKey, Keys.EncryptPublicKey, HushVotingScenario.DevicePassword);
        return words;
    }

    public async Task ProvisionAsync()
    {
        var words = await GenerateCandidateAsync();
        await ConfirmRecoveryAndProtectAsync(words);
        await SubmitAndIndexAsync();
    }

    public async Task ConfirmRecoveryAndProtectAsync(IReadOnlyList<string> words)
    {
        await Page.GetByRole(AriaRole.Checkbox).CheckAsync();
        await Button("^Continue$").ClickAsync();
        var challenge = Page.Locator("input[id^=recovery-word-]");
        await Expect(challenge).ToHaveCountAsync(6);
        foreach (var field in await challenge.AllAsync())
        {
            var position = int.Parse((await field.GetAttributeAsync("id"))!["recovery-word-".Length..], System.Globalization.CultureInfo.InvariantCulture);
            await FillSecretAsync(field, words[position - 1]);
        }
        await Button("^Verify").ClickAsync();
        await FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await FillSecretAsync(Page.GetByLabel("Confirm device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await Button("^Protect this device and continue$").ClickAsync();
        await Expect(Button("^Create HushNetwork identity$")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    public async Task SubmitAndIndexAsync()
    {
        using (var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30)))
        {
            await Button("^Create HushNetwork identity$").ClickAsync();
            await received.WaitAsync();
        }
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        // This helper deliberately leaves a registered, locked identity for
        // subsequent unlock coverage. Stop automatic confirmation before indexing.
        await Button("^Lock$").ClickAsync();
        await scenario.Blocks.ProduceBlockAsync();
        await Page.ReloadAsync();
        await Expect(Button("^Unlock HushVoting")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    public async Task UnlockAndBootstrapAsync()
    {
        using var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(45));
        await FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await Button("^Unlock HushVoting").ClickAsync();
        try { await received.WaitAsync(); }
        catch (TimeoutException)
        {
            var surfaces = await Page.Locator("[data-testid]").EvaluateAllAsync<string[]>("nodes => nodes.map(n => n.dataset.testid)");
            throw new InvalidOperationException("No bootstrap submission. Surfaces: " + string.Join(',', surfaces)
                + "; RPC outcomes: " + string.Join(';', scenario.Faults.Outcomes));
        }
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await scenario.Blocks.ProduceBlockAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 45_000 });
    }

    public async Task AuthenticateAsync()
    {
        await ProvisionAsync();
        await UnlockAndBootstrapAsync();
    }

    public async Task AuthenticateUntilEntitlementAsync()
    {
        await ProvisionAsync();
        scenario.Faults.HoldEntitlementQueries();
        await FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await Button("^Unlock HushVoting").ClickAsync();
        await scenario.Faults.EntitlementArrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Expect(Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync();
    }

    public static async Task FillSecretAsync(ILocator input, string secret)
    {
        // Playwright's fill failure includes the supplied value in its call log.
        // Keep that exception out of result artifacts and report only the operation.
        try { await input.FillAsync(secret); }
        catch (Exception error) when (error is PlaywrightException or TimeoutException) { throw new InvalidOperationException("Credential entry failed; secret-bearing diagnostics suppressed."); }
    }
}
