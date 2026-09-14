using System.Diagnostics;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryValidationSteps(HushVotingScenario scenario, RecoveryWordEntrySteps entry)
{
    private IPage Page => scenario.Page;
    private static readonly string[] Words = [.. Enumerable.Repeat("abandon", 11), "about"];
    private int _normalized;
    private int _unsupported;
    private TimeSpan _validationDuration;

    private async Task FillAsync(IReadOnlyList<string> words)
    {
        for (var i = 0; i < words.Count; i++) await HushVotingIdentityJourney.FillSecretAsync(Page.Locator("#rw-" + (i + 1)), words[i]);
    }

    private async Task UnknownRejectedAsync(string word)
    {
        await FillAsync(Words);
        await HushVotingIdentityJourney.FillSecretAsync(Page.Locator("#rw-3"), word);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true }).ClickAsync();
        await Expect(Page.Locator("#rw-error-UNKNOWN_WORD")).ToBeVisibleAsync();
        if (await Page.Locator("#rw-3").InputValueAsync() != word) throw new InvalidOperationException("Recovery silently substituted an unknown word.");
        scenario.Faults.IdentityQueryCount.Should().Be(0);
    }

    [When("Alice corrects an unknown word and verifies Unicode-normalized forms of the same phrase")]
    public async Task NormalizeAsync()
    {
        await Page.GetByTestId("count-12").CheckAsync();
        await UnknownRejectedAsync("abandno");
        var expected = HushVotingTestIdentity.DeriveP01(string.Join(" ", Words));
        foreach (var variant in new[] { 0, 1, 2 })
        {
            if (variant > 0) { await Page.GoBackAsync(); await entry.EntryAsync(); await Page.GetByTestId("count-12").CheckAsync(); }
            var input = Words.Select(word => variant switch {
                0 => word.ToUpperInvariant(),
                1 => new string(word.Select(c => (char)(c + 0xFEE0)).ToArray()),
                _ => "\t  " + word + "\u3000"
            }).ToArray();
            await FillAsync(input);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true }).ClickAsync();
            await Expect(Page.GetByTestId("candidate-list").Locator("li")).ToHaveCountAsync(1);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Reveal full addresses", Exact = true }).ClickAsync();
            if (!(await Page.GetByTestId("candidate-list").Locator("dd").AllTextContentsAsync()).SequenceEqual(new[] { expected.SigningPublicKey, expected.EncryptPublicKey }))
                throw new InvalidOperationException("Approved normalization changed the recovered public-key pair.");
            _normalized++;
        }
    }

    [Then("normalization preserves the independently derived public-key pair without substituting unknown words")]
    public void Normalized() { _normalized.Should().Be(3); scenario.Faults.IdentityQueryCount.Should().Be(3); scenario.Faults.SubmittedTransactions.Count.Should().Be(0); }

    [When("Alice submits unsupported recovery counts and non-English words")]
    public async Task UnsupportedAsync()
    {
        foreach (var count in new[] { 13, 15, 18, 21, 25 })
        {
            try {
                await Page.EvaluateAsync("value => navigator.clipboard.writeText(value)", string.Join(" ", Enumerable.Repeat("abandon", count)));
                await Page.Locator("#rw-1").PressAsync("Control+V");
            } catch (Exception error) when (error is PlaywrightException or TimeoutException) { throw new InvalidOperationException("Unsupported recovery paste failed; input omitted."); }
            await Expect(Page.Locator("#rw-paste-error")).ToBeVisibleAsync();
            foreach (var input in await Page.GetByTestId("word-grid").Locator("input").AllAsync())
                if ((await input.InputValueAsync()).Length != 0) throw new InvalidOperationException("Unsupported count partially populated the recovery inputs.");
            _unsupported++;
        }
        foreach (var word in new[] { "ábaco", "あいこくしん", "abandon-TREZOR" })
        {
            await UnknownRejectedAsync(word);
            _unsupported++;
        }
    }

    [Then("unsupported recovery input stays local and no BIP39 passphrase is collected")]
    public async Task UnsupportedStayedLocalAsync()
    {
        _unsupported.Should().Be(8);
        await Expect(Page.GetByLabel("BIP39 passphrase", new() { Exact = true })).ToHaveCountAsync(0);
        scenario.Faults.IdentityQueryCount.Should().Be(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        await Expect(Page.GetByTestId("candidate-list")).ToHaveCountAsync(0);
    }

    [When("Alice repeatedly corrects invalid phrases then double-clicks Verify for valid words")]
    public async Task RepeatedAsync()
    {
        await Page.GetByTestId("count-12").CheckAsync();
        for (var attempt = 0; attempt < 8; attempt++) await UnknownRejectedAsync("abandno");
        await FillAsync(Words);
        var started = Stopwatch.StartNew();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true }).DblClickAsync();
        await Expect(Page.GetByTestId("candidate-list").Locator("li")).ToHaveCountAsync(1);
        _validationDuration = started.Elapsed;
    }

    [Then("validation remains recoverable and starts only one bounded candidate lookup")]
    public async Task SingleOperationAsync()
    {
        _validationDuration.Should().BeLessThan(TimeSpan.FromSeconds(10));
        scenario.Faults.IdentityQueryCount.Should().Be(1);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        await Page.ReloadAsync();
        await entry.EntryAsync();
        await Page.GetByTestId("count-12").CheckAsync();
        await FillAsync(Words);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true })).ToBeEnabledAsync();
    }
}
