using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using Microsoft.Extensions.DependencyInjection;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityCreationSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity)
{
    private IPage Page => scenario.Page;
    private IReadOnlyList<string> _words = [];
    private int[] _positions = [];
    private int _mismatch;
    private ILocator Challenge => Page.Locator("input[id^=recovery-word-]");
    private ILocator Button(string name) => Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    [Given("Alice awaits fresh creation custody before reaching generation with a candidate operation counter")]
    public async Task ObserveGenerationAsync()
    {
        // AC-007-002 regression (AUD-026): delay only the public custody
        // request. Release its original bytes to the real worker; no replies
        // or successful application states are manufactured.
        await Page.AddInitScriptAsync("""
            (() => {
                let count = 0;
                let release = null;
                window.__hvHoldCreationInspection = false;
                window.__hvCreationInspectionHeld = false;
                const send = MessagePort.prototype.postMessage;
                MessagePort.prototype.postMessage = function(...args) {
                    if (window.__hvHoldCreationInspection && args[0]?.kind === 'operation' && args[0]?.operation === 'inspectStartup') {
                        window.__hvHoldCreationInspection = false;
                        window.__hvCreationInspectionHeld = true;
                        release = () => Reflect.apply(send, this, args);
                        return;
                    }
                    if (args[0]?.kind === 'operation' && args[0]?.operation === 'createCandidate') count++;
                    return Reflect.apply(send, this, args);
                };
                Object.defineProperty(window, '__hvCandidateCount', { get: () => count });
                window.__hvReleaseCreationInspection = () => { const pending = release; release = null; pending?.(); };
            })();
            """);
        await Page.GotoAsync("/");
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Create User") })).ToBeVisibleAsync();
        await Page.EvaluateAsync("() => { window.__hvHoldCreationInspection = true; }");
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Create User") }).ClickAsync();
        try
        {
            await Page.WaitForFunctionAsync("() => window.__hvCreationInspectionHeld", null, new() { Timeout = 5_000 });
            // Cross the old 250 ms readiness timer while the real authority
            // still has not received this request.
            await Task.Delay(400);
            await Expect(Page.GetByText("Checking this device before setup…", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(Page.GetByLabel("Profile name / alias", new() { Exact = true })).ToHaveCountAsync(0);
            await Expect(Page.Locator("input[type=password]")).ToHaveCountAsync(0);
            await Expect(Page.GetByTestId("recovery-list")).ToHaveCountAsync(0);
            (await Page.EvaluateAsync<int>("window.__hvCandidateCount")).Should().Be(0);
            scenario.Faults.IdentityQueryCount.Should().Be(0);
            scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        }
        finally { await Page.EvaluateAsync("() => window.__hvReleaseCreationInspection()"); }
        await Page.GetByLabel("Profile name / alias", new() { Exact = true }).FillAsync(HushVotingIdentityJourney.Alias);
        await Button("Continue").ClickAsync();
        await Expect(Button("Generate recovery words")).ToBeVisibleAsync();
        (await Page.EvaluateAsync<int>("window.__hvCandidateCount")).Should().Be(0);
    }

    [When("Alice double-clicks the real Generate recovery words control")]
    public async Task GenerateOnceAsync()
    {
        await Button("Generate recovery words").DblClickAsync();
        _words = await identity.ReadCandidateAsync();
    }

    [Then("one worker candidate supplies a valid 24-word P-01 identity that survives protection and live registration")]
    public async Task SingleCandidateAsync()
    {
        (await Page.EvaluateAsync<int>("window.__hvCandidateCount")).Should().Be(1);
        _words.Count.Should().Be(24);
        identity.Keys.SigningPublicKey.Should().MatchRegex("^0[23][0-9a-f]{64}$");
        identity.Keys.EncryptPublicKey.Should().MatchRegex("^0[23][0-9a-f]{64}$");
        await identity.ConfirmRecoveryAndProtectAsync(_words);
        (await Page.EvaluateAsync<int>("window.__hvCandidateCount")).Should().Be(1);
        await identity.SubmitAndIndexAsync();
        await DerivationIndependentAsync();
    }

    [Given("a valid candidate")]
    [Given("an active reveal authority")]
    [Given("words have been displayed for a candidate")]
    [Given("recovery words are visible")]
    [Given("the recovery screen is visible")]
    public async Task CandidateAsync() => _words = await identity.GenerateCandidateAsync();

    [When("Alice explicitly copies the visible recovery words to the real browser clipboard")]
    public async Task CopyRecoveryAsync()
    {
        await scenario.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await Page.EvaluateAsync("""
            () => {
                window.__hvClipboardReads = 0;
                const read = navigator.clipboard.readText.bind(navigator.clipboard);
                navigator.clipboard.readText = (...args) => { window.__hvClipboardReads++; return read(...args); };
                // The test uses the captured native read; product reads remain counted.
                window.__hvInspectClipboard = read;
            }
            """);
        var copy = Button("Copy words");
        (await copy.GetAttributeAsync("class"))!.Contains("button-default", StringComparison.Ordinal).Should().BeFalse();
        await Expect(Page.GetByText("Cleanup is best effort", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(Page.GetByText("may overwrite newer clipboard content", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Browser screenshots and clipboard history cannot be prevented", new() { Exact = false })).ToBeVisibleAsync();
        await copy.ClickAsync();
        var copied = await Page.EvaluateAsync<string>("window.__hvInspectClipboard()");
        if (copied != string.Join(' ', _words)) throw new InvalidOperationException("Explicit copy did not contain the current recovery phrase.");
    }

    [Then("copy is secondary and warned and the browser clears the clipboard within thirty seconds without reading it")]
    public async Task ClipboardDeadlineAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(31));
        while (!await Page.EvaluateAsync<bool>("async () => (await window.__hvInspectClipboard()).length === 0"))
            await Task.Delay(100, deadline.Token);
        (await Page.EvaluateAsync<int>("window.__hvClipboardReads")).Should().Be(0);
        await Expect(Page.GetByTestId("recovery-list").Locator("li")).ToHaveCountAsync(24);
    }

    [Then("Alice can still protect and register the same candidate after clipboard cleanup")]
    public async Task RegisterCopiedCandidateAsync()
    {
        await identity.ConfirmRecoveryAndProtectAsync(_words);
        await identity.SubmitAndIndexAsync();
        await DerivationIndependentAsync();
    }

    [When("Alice protects and registers that generated identity")]
    public async Task ProtectGeneratedAsync()
    {
        await Expect(Page.Locator("input[type=password]")).ToHaveCountAsync(0);
        await identity.ConfirmRecoveryAndProtectAsync(_words);
        await identity.SubmitAndIndexAsync();
    }

    [Then("the encrypted vault and live profile match independent derivation from the words alone")]
    public async Task DerivationIndependentAsync()
    {
        var expected = HushVotingTestIdentity.DeriveP01(string.Join(' ', _words));
        var stored = await HushVotingVaultInspection.InspectAsync(Page, expected, HushVotingIdentityJourney.Alias, false);
        stored.KeysMatch.Should().BeTrue();
        stored.ConcreteKeysOnly.Should().BeTrue();
        var profile = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = expected.SigningPublicKey }, deadline: DateTime.UtcNow.AddSeconds(10));
        (profile.Successfull && profile.PublicSigningAddress == expected.SigningPublicKey && profile.PublicEncryptAddress == expected.EncryptPublicKey).Should().BeTrue();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        await Expect(Page.GetByTestId("recovery-list")).ToHaveCountAsync(0);
    }

    [Given("Alice reaches empty device-password fields after her real recovery challenge")]
    public async Task EmptyDeviceProtectionAsync()
    {
        await CandidateAsync();
        await Expect(Page.Locator("input[type=password], input[type=file]")).ToHaveCountAsync(0);
        await ChallengeAsync();
        await Expect(Page.Locator("input[type=password], input[type=file]")).ToHaveCountAsync(0);
        await AnswerAsync(false);
        await Expect(Page.GetByLabel("Device password", new() { Exact = true })).ToHaveValueAsync("");
        await Expect(Page.GetByLabel("Confirm device password", new() { Exact = true })).ToHaveValueAsync("");
        await Expect(Page.GetByLabel("Backup-file password", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("recovery-list")).ToHaveCountAsync(0);
    }

    [When("Alice protects the new identity without entering any backup-file password")]
    public async Task DeviceOnlyProtectionAsync()
    {
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Confirm device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await Button("Protect this device and continue").ClickAsync();
        await Expect(Button("Create HushNetwork identity")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("input[type=password], input[type=file]")).ToHaveCountAsync(0);
        await Expect(Page.GetByLabel("Backup-file password", new() { Exact = true })).ToHaveCountAsync(0);
        (await Page.Locator("body").InnerTextAsync()).Contains(HushVotingScenario.DevicePassword, StringComparison.Ordinal).Should().BeFalse();
    }

    [Then("creation clears device input and registers only the independently derived identity")]
    public async Task DevicePurposeVerifiedAsync()
    {
        await identity.SubmitAndIndexAsync();
        await DerivationIndependentAsync();
        scenario.Faults.SubmittedTransactions.Any(transaction => transaction.Contains(HushVotingScenario.DevicePassword, StringComparison.Ordinal)).Should().BeFalse();
        await Expect(Page.GetByLabel("Device password", new() { Exact = true })).ToHaveValueAsync("");
        await Expect(Page.GetByLabel("Backup-file password", new() { Exact = true })).ToHaveCountAsync(0);
    }

    [Then("identity onboarding creates no personal feed or social state before the separate licence gate")]
    public async Task NoSocialSideEffectsAsync()
    {
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        using (var transaction = System.Text.Json.JsonDocument.Parse(scenario.Faults.SubmittedTransactions.Single()))
            transaction.RootElement.GetProperty("PayloadKind").ValueEquals("351cd60b-3fdf-48d4-b608-e93c0100f7d0").Should().BeTrue();
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "GetIdentity", "GetBlockchainHeight", "SubmitSignedTransaction" };
        scenario.Faults.RequestMethods.All(allowed.Contains).Should().BeTrue();
        using var scope = scenario.Node.Services.CreateScope();
        var feeds = scope.ServiceProvider.GetRequiredService<HushNode.Feeds.Storage.IFeedsStorageService>();
        (await feeds.HasPersonalFeed(identity.Keys.SigningPublicKey)).Should().BeFalse();
        // Root-owned entitlement bootstrap is a subsequent authenticated feature.
        await identity.UnlockAndBootstrapAsync();
        (await feeds.HasPersonalFeed(identity.Keys.SigningPublicKey)).Should().BeFalse();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        (await Page.EvaluateAsync<bool>("async () => (await indexedDB.databases()).every(db => db.name === 'hushvoting-vault') && localStorage.length === 0 && sessionStorage.length === 0")).Should().BeTrue();
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("feed|social|chat", System.Text.RegularExpressions.RegexOptions.IgnoreCase) })).ToHaveCountAsync(0);
    }

    [When("Save Recovery Words renders")]
    public async Task RecoveryRendersAsync()
    {
        (await Page.GetByTestId("recovery-list").EvaluateAsync<string>("element => element.tagName")).Should().Be("OL");
    }

    [Then("24 numbered words are shown in a responsive semantic ordered layout")]
    public async Task OrderedWordsAsync()
    {
        var labels = await Page.GetByTestId("recovery-list").Locator("li > span:first-child").AllTextContentsAsync();
        labels.Should().Equal(Enumerable.Range(1, 24).Select(n => n.ToString("00", System.Globalization.CultureInfo.InvariantCulture)));
        foreach (var width in new[] { 320, 768, 1280 })
        {
            await Page.SetViewportSizeAsync(width, 800);
            var layoutFits = await Page.GetByTestId("recovery-list").EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth && document.documentElement.scrollWidth <= innerWidth");
            layoutFits.Should().BeTrue("recovery words must reflow without horizontal clipping");
        }
    }

    [Then("the reveal lasts at most 60 seconds per reveal")]
    public async Task RevealDeadlineAsync()
    {
        // Real browser time: the production reveal timer runs without a test clock or injected state.
        try { await Expect(Page.GetByTestId("recovery-list")).ToHaveCountAsync(0, new() { Timeout = 60_500 }); }
        catch (Exception error) when (error is PlaywrightException or TimeoutException) { throw new InvalidOperationException("Recovery reveal exceeded its permitted lifetime; secret-bearing diagnostics suppressed."); }
        await Expect(Button("Continue")).ToBeDisabledAsync();
        var text = await Page.Locator("body").InnerTextAsync();
        if (text.Contains(string.Join(' ', _words), StringComparison.Ordinal)) throw new InvalidOperationException("Concealed phrase remained in accessible page text.");
        await Button("Show words").ClickAsync();
        var reviewed = await identity.ReadCandidateAsync();
        if (!reviewed.SequenceEqual(_words)) throw new InvalidOperationException("An explicit reveal after expiry changed the candidate.");
        await Expect(Button("Continue")).ToBeDisabledAsync();
        await identity.ConfirmRecoveryAndProtectAsync(_words);
        await identity.SubmitAndIndexAsync();
    }

    [When("the user requests Regenerate")]
    public async Task RequestRegenerationAsync() => await Button("Regenerate").ClickAsync();

    [Then("a destructive confirmation is required")]
    public async Task RegenerationConsentAsync()
    {
        await Expect(Page.GetByRole(AriaRole.Alertdialog, new() { Name = "Regenerate recovery words?", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("recovery-list")).ToHaveCountAsync(0);
        await Button("Keep current words").ClickAsync();
        var current = await Page.GetByTestId("recovery-list").Locator("li > span:last-child").AllTextContentsAsync();
        if (!current.SequenceEqual(_words)) throw new InvalidOperationException("Cancelling regeneration changed the candidate.");
        await RequestRegenerationAsync();
    }

    [Then("the complete old candidate is destroyed, confirmation state resets, and a wholly new candidate is created")]
    public async Task RegeneratedAsync()
    {
        var oldSigning = identity.Keys.SigningPublicKey;
        await Button("Regenerate and destroy previous words").ClickAsync();
        var regenerated = await identity.ReadCandidateAsync();
        if (regenerated.SequenceEqual(_words) || identity.Keys.SigningPublicKey == oldSigning)
            throw new InvalidOperationException("Confirmed regeneration retained the old candidate.");
        await Expect(Page.GetByRole(AriaRole.Checkbox)).Not.ToBeCheckedAsync();
        await Expect(Button("Continue")).ToBeDisabledAsync();
        _words = regenerated;
        await identity.ConfirmRecoveryAndProtectAsync(_words);
        await identity.SubmitAndIndexAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
    }

    [When("the confirmation challenge renders")]
    public async Task ChallengeAsync()
    {
        await Page.GetByRole(AriaRole.Checkbox).CheckAsync();
        await Button("Continue").ClickAsync();
        await Expect(Challenge).ToHaveCountAsync(6);
        _positions = (await Challenge.EvaluateAllAsync<string[]>("nodes => nodes.map(n => n.id.replace('recovery-word-', ''))"))
            .Select(p => int.Parse(p, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    }

    [Given("a six-position challenge")]
    [Given("the same candidate and challenge")]
    public async Task GivenChallengeAsync() { await CandidateAsync(); await ChallengeAsync(); }

    [Then("six unpredictable distinct positions are requested in randomized display order")]
    public void SixPositions()
    {
        _positions.Should().HaveCount(6).And.OnlyHaveUniqueItems();
        _positions.Should().OnlyContain(p => p >= 1 && p <= 24);
        // The browser observes the requested positions; the CSPRNG source itself is app-twin evidence.
    }

    [Then("release builds provide no bypass")]
    public async Task NoBypassAsync()
    {
        await Expect(Button("Verify words")).ToBeDisabledAsync();
        await Expect(Page.Locator("input[type=password]")).ToHaveCountAsync(0);
        await Expect(Button("Create HushNetwork identity")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("recovery-list")).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        await AnswerAsync(false);
        await Expect(Page.GetByLabel("Device password", new() { Exact = true })).ToBeVisibleAsync();
    }

    private async Task AnswerAsync(bool mismatch)
    {
        _mismatch = _positions[0];
        foreach (var position in _positions)
            await HushVotingIdentityJourney.FillSecretAsync(Page.Locator("#recovery-word-" + position), mismatch && position == _mismatch ? "incorrect" : _words[position - 1]);
        await Button("Verify words").ClickAsync();
    }

    [When("one answer mismatches")]
    public async Task MismatchAsync() => await AnswerAsync(true);

    [Then("the error identifies only that position")]
    public async Task MismatchPositionAsync()
    {
        await Expect(Challenge).ToHaveCountAsync(6);
        await Expect(Page.Locator("#recovery-word-" + _mismatch)).ToHaveAttributeAsync("aria-describedby", "recovery-mismatch");
        await Expect(Page.Locator("input[aria-describedby=recovery-mismatch]")).ToHaveCountAsync(1);
        await Expect(Page.Locator("#recovery-mismatch")).ToHaveTextAsync($"Word {_mismatch} does not match. Please check your saved words.");
    }

    [Then("never echoes the expected or any other word")]
    public async Task NoWordEchoAsync()
    {
        await Expect(Page.GetByTestId("recovery-list")).ToHaveCountAsync(0);
        // Compare internally, without letting a failing assertion serialize the phrase.
        var text = await Page.Locator("body").InnerTextAsync();
        await Expect(Page.Locator("#recovery-mismatch")).ToHaveTextAsync($"Word {_mismatch} does not match. Please check your saved words.");
        if (text.Contains(string.Join(' ', _words), StringComparison.Ordinal)) throw new InvalidOperationException("Recovery phrase remained visible during confirmation.");
        foreach (var input in await Challenge.AllAsync())
            if ((await input.InputValueAsync()).Length != 0) throw new InvalidOperationException("Challenge input retained a credential answer.");
    }

    [When("three attempts mismatch")]
    public async Task ThreeFailuresAsync()
    {
        for (var i = 0; i < 3; i++) await AnswerAsync(true);
    }

    [Then("the challenge is invalidated and protected review resumes")]
    public async Task ClosedChallengeAsync()
    {
        await Expect(Challenge).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Create user · Confirm recovery" })).ToBeVisibleAsync();
        await Expect(Button("Verify words")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("recovery-list")).ToHaveCountAsync(0);
    }

    [Then("the candidate is not regenerated or exposed")]
    public async Task SameCandidateAsync()
    {
        await ClosedChallengeAsync();
        // Explicit protected review is the only way to reveal again; it must retain the same candidate.
        await Button("Review all words").ClickAsync();
        await Expect(Page.GetByTestId("recovery-list").Locator("li")).ToHaveCountAsync(24);
        var reviewed = await Page.GetByTestId("recovery-list").Locator("li > span:last-child").AllTextContentsAsync();
        if (!reviewed.SequenceEqual(_words)) throw new InvalidOperationException("Recovery review regenerated the candidate.");
        await Expect(Button("Continue")).ToBeDisabledAsync();
    }
}
