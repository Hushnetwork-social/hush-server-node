using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityProfileSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity)
{
    private IPage Page => scenario.Page;
    private ILocator Alias => Page.GetByLabel("Profile name / alias", new() { Exact = true });
    private ILocator Button(string name) => Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
    private const string UnnormalizedAlias = "\u2003Cafe\u0301\u00a0";

    [Given("Alice explicitly creates and authenticates a Public identity through HushVoting")]
    public async Task AuthenticatedPublicAsync()
    {
        await ProfileAsync();
        await PublicConsentAsync();
        await identity.UnlockAndBootstrapAsync();
    }

    [When("a second device submits the same signed identity keys with changed alias and Private visibility")]
    public async Task DuplicateUpdateAsync()
    {
        var reply = await scenario.Blockchain.SubmitSignedTransactionAsync(new()
        {
            SignedTransaction = HushVotingServerIdentity.Sign(identity.Keys, "Attempted replacement", isPublic: false)
        }, deadline: DateTime.UtcNow.AddSeconds(15));
        reply.Status.Should().Be(HushNetwork.proto.TransactionStatus.AlreadyExists);
        await scenario.Blocks.ProduceBlockAsync();
    }

    [Then("the indexed Public profile stays unchanged and HushVoting offers no duplicate-submission visibility update")]
    public async Task ImmutableVisibilityAsync()
    {
        var profile = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = identity.Keys.SigningPublicKey }, deadline: DateTime.UtcNow.AddSeconds(10));
        (profile.Successfull && profile.IsPublic && profile.ProfileName == HushVotingIdentityJourney.Alias
            && profile.PublicSigningAddress == identity.Keys.SigningPublicKey && profile.PublicEncryptAddress == identity.Keys.EncryptPublicKey).Should().BeTrue();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync();
        await Button(HushVotingIdentityJourney.Alias).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Radio)).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("visibility|edit profile|change profile", RegexOptions.IgnoreCase) })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Create User") })).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(3, "the browser creates only the original identity and licence; the third request is the rejected duplicate fixture");
    }

    [Given("a new alias with outer Unicode whitespace")]
    [Given("the Profile screen renders")]
    public async Task ProfileAsync()
    {
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Create User") }).ClickAsync();
        await Expect(Alias).ToBeVisibleAsync();
    }

    [When("profile validation runs")]
    public async Task ValidateAsync()
    {
        await Alias.FillAsync(UnnormalizedAlias);
        await Button("Continue").ClickAsync();
        await Expect(Button("Generate recovery words")).ToBeVisibleAsync();
    }

    [Then("the alias is trimmed, normalized to NFC, and accepted within 1-64 graphemes and 256 UTF-8 bytes")]
    public async Task NormalizedAsync()
    {
        await Button("Generate recovery words").ClickAsync();
        var words = await identity.ReadCandidateAsync();
        await identity.ConfirmRecoveryAndProtectAsync(words);
        await Expect(Page.GetByText("Café", new() { Exact = true })).ToBeVisibleAsync();
        await identity.SubmitAndIndexAsync();
        using var transaction = JsonDocument.Parse(scenario.Faults.SubmittedTransactions.Single());
        transaction.RootElement.GetProperty("Payload").GetProperty("IdentityAlias").GetString().Should().Be("Café");

        // Each additional boundary gets a fresh device context; the indexed identity remains real.
        foreach (var accepted in new[] { "a", new string('a', 64), string.Concat(Enumerable.Repeat("😀", 64)) })
            await ValidateOnFreshDeviceAsync(accepted, true);
        foreach (var rejected in new[] { "\u2003", new string('a', 65), "a" + new string('\u0301', 130) })
            await ValidateOnFreshDeviceAsync(rejected, false);
    }

    [Then("disallowed controls, bidi, and unsafe invisible characters are rejected")]
    public async Task UnsafeAliasesAsync()
    {
        foreach (var rejected in new[] { "bad\u0001alias", "bad\u0085alias", "bad\u202ealias", "bad\u200balias", "bad\u00adalias", "bad\ufeffalias" })
            await ValidateOnFreshDeviceAsync(rejected, false);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1, "invalid profiles must never create an identity transaction");
    }

    private async Task ValidateOnFreshDeviceAsync(string alias, bool accepted)
    {
        await using var context = await Page.Context.Browser!.NewContextAsync(new() { BaseURL = new Uri(Page.Url).GetLeftPart(UriPartial.Authority) });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Create User") }).ClickAsync();
        await page.GetByLabel("Profile name / alias", new() { Exact = true }).FillAsync(alias);
        await page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        if (accepted) await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Generate recovery words", Exact = true })).ToBeVisibleAsync();
        else
        {
            await Expect(page.Locator("#create-alias-error")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("recovery-list")).ToHaveCountAsync(0);
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Generate recovery words", Exact = true })).ToHaveCountAsync(0);
        }
    }

    [When("the user reviews visibility")]
    public async Task ReviewVisibilityAsync() => await Expect(Page.GetByRole(AriaRole.Group)).ToBeVisibleAsync();

    [Then("Private is selected by default")]
    public async Task PrivateDefaultAsync()
    {
        await Expect(Page.GetByRole(AriaRole.Radio, new() { NameRegex = new Regex("^Private") })).ToBeCheckedAsync();
        await Expect(Page.GetByRole(AriaRole.Radio, new() { NameRegex = new Regex("^Public") })).Not.ToBeCheckedAsync();
    }

    [Then("choosing Public shows a plain-language exposure/permanence warning requiring explicit acknowledgement")]
    public async Task PublicConsentAsync()
    {
        await Alias.FillAsync(HushVotingIdentityJourney.Alias);
        await Page.GetByRole(AriaRole.Radio, new() { NameRegex = new Regex("^Public") }).CheckAsync();
        var warning = Page.GetByRole(AriaRole.Region, new() { Name = "Public visibility cannot be changed later", Exact = true });
        await Expect(warning).ToBeVisibleAsync();
        await Expect(warning).ToContainTextAsync("permanent");
        await Button("Continue").ClickAsync();
        await Expect(Page.Locator("#create-alias-error")).ToBeVisibleAsync();
        await Expect(Button("Generate recovery words")).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Checkbox).CheckAsync();
        await Button("Continue").ClickAsync();
        await Expect(Button("Generate recovery words")).ToBeVisibleAsync();
        await Button("Generate recovery words").ClickAsync();
        var words = await identity.ReadCandidateAsync();
        await identity.ConfirmRecoveryAndProtectAsync(words);
        await identity.SubmitAndIndexAsync();
        using var transaction = JsonDocument.Parse(scenario.Faults.SubmittedTransactions.Single());
        transaction.RootElement.GetProperty("Payload").GetProperty("IsPublic").GetBoolean().Should().BeTrue();
    }
}
