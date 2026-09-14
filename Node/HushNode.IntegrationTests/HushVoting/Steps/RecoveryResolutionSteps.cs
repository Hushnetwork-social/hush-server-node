using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using Olimpo.KeyDerivation;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryResolutionSteps(HushVotingScenario scenario, RecoveryWordEntrySteps entry)
{
    private IPage Page => scenario.Page;
    private static readonly string Phrase = string.Join(" ", Enumerable.Repeat("abandon", 23).Append("art"));
    private readonly DerivedKeys _web = HushVotingTestIdentity.DeriveP01(Phrase);
    private readonly DerivedKeys _historical = DeterministicKeyGenerator.DeriveKeys(Phrase);
    private const string HistoricalAlias = "Historical voting identity";
    private const string LiteralAlias = "<img src=x onerror=alert(1)>";
    private int _beforeLookups;
    private int _beforeTransactions;

    [Given("the node has only the first of two recovered identity formats registered")]
    public async Task OneRegisteredAsync() => await HushVotingServerIdentity.RegisterAsync(scenario, _web, "Web voting identity");

    [Given("the node has both recovered identity formats registered")]
    public async Task BothRegisteredAsync()
    {
        await OneRegisteredAsync();
        await HushVotingServerIdentity.RegisterAsync(scenario, _historical, HistoricalAlias, isPublic: true);
    }

    [Given("neither recovered identity format has a blockchain profile")]
    public void NeitherRegistered() => scenario.Faults.SubmittedTransactions.Count.Should().Be(0);

    [Given("the first recovered signing address is registered with a different encryption key")]
    public async Task MismatchAsync() => await HushVotingServerIdentity.RegisterAsync(scenario, _web, "Mismatched profile", encryptionAddress: _historical.EncryptPublicKey);

    [Given("the recovered blockchain profile has a literal markup alias and Public visibility")]
    public async Task LiteralAsync() => await HushVotingServerIdentity.RegisterAsync(scenario, _web, LiteralAlias, isPublic: true);

    [When("Alice resolves the twenty-four-word identity set against the node")]
    public async Task ResolveAsync()
    {
        await entry.EntryAsync();
        _beforeLookups = scenario.Faults.IdentityLookups.Count;
        _beforeTransactions = scenario.Faults.SubmittedTransactions.Count;
        var words = Phrase.Split(' ');
        for (var i = 0; i < words.Length; i++) await HushVotingIdentityJourney.FillSecretAsync(Page.Locator("#rw-" + (i + 1)), words[i]);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true }).ClickAsync();
    }

    [Then("both distinct formats have actual exact-profile or authoritative absence replies before confirmation")]
    public async Task AllResolvedAsync()
    {
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Confirm this identity", Exact = true })).ToBeVisibleAsync();
        var replies = scenario.Faults.IdentityLookups.Skip(_beforeLookups).ToArray();
        if (replies.Length != 2 || replies[0].SigningAddress != _web.SigningPublicKey || replies[1].SigningAddress != _historical.SigningPublicKey
            || !replies[0].Reply.Successfull || replies[0].Reply.PublicSigningAddress != _web.SigningPublicKey
            || replies[0].Reply.PublicEncryptAddress != _web.EncryptPublicKey || replies[1].Reply.Successfull)
            throw new InvalidOperationException("Recovery did not resolve the complete distinct address set through actual node replies.");
        await Expect(Page.GetByTestId("candidate-list").Locator("li")).ToHaveCountAsync(1);
        await NoStagingAsync();
    }

    private async Task NoStagingAsync()
    {
        await Expect(Page.GetByLabel("Device password", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_beforeTransactions);
    }

    [Then("no registered profile is selected until Alice explicitly chooses the historical identity")]
    public async Task SelectHistoricalAsync()
    {
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Choose your identity", Exact = true })).ToBeVisibleAsync();
        var cards = Page.GetByTestId("candidate-list").Locator("li");
        await Expect(cards).ToHaveCountAsync(2);
        await Expect(cards.Nth(0)).ToContainTextAsync("Web voting identity");
        await Expect(cards.Nth(0)).ToContainTextAsync("Private");
        await Expect(cards.Nth(1)).ToContainTextAsync(HistoricalAlias);
        await Expect(cards.Nth(1)).ToContainTextAsync("Public");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Selected", Exact = true })).ToHaveCountAsync(0);
        var proceed = Page.GetByRole(AriaRole.Button, new() { Name = "Continue to protect this device", Exact = true });
        await Expect(proceed).ToBeDisabledAsync();
        await NoStagingAsync();
        await cards.Nth(1).GetByRole(AriaRole.Button, new() { Name = "Select this identity", Exact = true }).ClickAsync();
        await Expect(cards.Nth(1).GetByRole(AriaRole.Button, new() { Name = "Selected", Exact = true })).ToBeVisibleAsync();
        await proceed.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Protect this device", Exact = true })).ToBeVisibleAsync();
    }

    [Then("device protection activates the selected historical blockchain identity")]
    public async Task ProtectHistoricalAsync()
    {
        await Page.GetByTestId("recovery-no-retention-ack").CheckAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Confirm device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        using var baseline = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await baseline.WaitAsync();
        await Expect(Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await scenario.Blocks.ProduceBlockAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = HistoricalAlias, Exact = true })).ToBeVisibleAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_beforeTransactions + 1);
        var reply = scenario.Faults.IdentityLookups.Last();
        if (reply.SigningAddress != _historical.SigningPublicKey || reply.Reply.PublicEncryptAddress != _historical.EncryptPublicKey)
            throw new InvalidOperationException("Protection did not freshly verify the selected historical identity pair.");
    }

    [Then("the absent candidates require source-guided selection without a default")]
    public async Task NoDefaultAbsentAsync()
    {
        var cards = Page.GetByTestId("candidate-list").Locator("li");
        await Expect(cards).ToHaveCountAsync(2);
        await Expect(cards.Nth(0)).ToContainTextAsync("Hush Web Client");
        await Expect(cards.Nth(1)).ToContainTextAsync("Historical Hush desktop and .NET identities");
        await Expect(Page.GetByText("No profile currently exists on this blockchain.", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Selected", Exact = true })).ToHaveCountAsync(0);
        var proceed = Page.GetByRole(AriaRole.Button, new() { Name = "Continue to review profile", Exact = true });
        await Expect(proceed).ToBeDisabledAsync();
        await NoStagingAsync();
        await cards.Nth(1).GetByRole(AriaRole.Button, new() { Name = "Reveal full addresses", Exact = true }).ClickAsync();
        if (!(await cards.Nth(1).Locator("dd").AllTextContentsAsync()).SequenceEqual(new[] { _historical.SigningPublicKey, _historical.EncryptPublicKey }))
            throw new InvalidOperationException("The historical source hint did not reveal its independently derived public addresses.");
        await cards.Nth(1).GetByRole(AriaRole.Button, new() { Name = "Hide full addresses", Exact = true }).ClickAsync();
        await cards.Nth(1).GetByRole(AriaRole.Button, new() { Name = "Select this identity", Exact = true }).ClickAsync();
        await Expect(proceed).ToBeEnabledAsync();
        await proceed.ClickAsync();
        await Expect(Page.GetByTestId("recreate-alias")).ToBeVisibleAsync();
    }

    [Then("a signing-only recovery match fails closed before any selection or staging")]
    public async Task MismatchRejectedAsync()
    {
        await Expect(Page.Locator("#rw-quarantine")).ToContainTextAsync("Recovery is blocked");
        await Expect(Page.GetByTestId("candidate-list")).ToHaveCountAsync(0);
        (scenario.Faults.IdentityLookups.Count - _beforeLookups).Should().Be(1);
        await NoStagingAsync();
    }

    [Then("explicit reveal permits copying only the chosen public addresses and Hide removes them")]
    public async Task RevealCopyAsync()
    {
        var cards = Page.GetByTestId("candidate-list").Locator("li");
        await Expect(cards).ToHaveCountAsync(2);
        scenario.CaptureEnabled.Should().BeFalse();
        await scenario.Context.GrantPermissionsAsync(new[] { "clipboard-read", "clipboard-write" }, new() { Origin = scenario.BaseUrl });
        var ordinary = await Page.Locator("body").InnerTextAsync();
        if (ordinary.Contains(_web.SigningPublicKey) || ordinary.Contains(_web.EncryptPublicKey) || ordinary.Contains(Phrase))
            throw new InvalidOperationException("Recovery exposed unrequested identity material.");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Copy signing address", Exact = true })).ToHaveCountAsync(0);
        await cards.First.GetByRole(AriaRole.Button, new() { Name = "Reveal full addresses", Exact = true }).ClickAsync();
        if (!(await cards.First.Locator("dd").AllTextContentsAsync()).SequenceEqual(new[] { _web.SigningPublicKey, _web.EncryptPublicKey }))
            throw new InvalidOperationException("Explicit reveal differs from the independent public-key pair.");
        await cards.First.GetByRole(AriaRole.Button, new() { Name = "Copy signing address", Exact = true }).ClickAsync();
        if (await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()") != _web.SigningPublicKey)
            throw new InvalidOperationException("Copy signing address did not write the selected public signing key.");
        await cards.First.GetByRole(AriaRole.Button, new() { Name = "Copy encryption address", Exact = true }).ClickAsync();
        if (await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()") != _web.EncryptPublicKey)
            throw new InvalidOperationException("Copy encryption address did not write the selected public encryption key.");
        await cards.First.GetByRole(AriaRole.Button, new() { Name = "Hide full addresses", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Copy encryption address", Exact = true })).ToHaveCountAsync(0);
        var concealed = await Page.Locator("body").InnerTextAsync();
        if (concealed.Contains(_web.SigningPublicKey) || concealed.Contains(_web.EncryptPublicKey))
            throw new InvalidOperationException("Hide retained full public addresses in ordinary content.");
        await NoStagingAsync();
    }

    [Then("recovery shows the authoritative escaped alias and visibility without profile editing")]
    public async Task SafeProfileAsync()
    {
        await Expect(Page.GetByTestId("safe-alias")).ToHaveTextAsync(LiteralAlias);
        await Expect(Page.GetByTestId("safe-alias")).ToHaveAttributeAsync("dir", "auto");
        await Expect(Page.GetByTestId("safe-alias").Locator("img, script")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("candidate-list")).ToContainTextAsync("Public");
        await Expect(Page.GetByTestId("recreate-alias")).ToHaveCountAsync(0);
        await NoStagingAsync();
    }
}
