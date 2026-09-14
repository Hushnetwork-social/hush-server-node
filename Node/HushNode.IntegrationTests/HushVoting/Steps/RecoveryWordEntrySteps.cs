using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryWordEntrySteps(HushVotingScenario scenario)
{
    private IPage Page => scenario.Page;
    private int _correctableFailures;

    [When("Alice submits unknown words or a phrase with an invalid checksum")]
    public async Task InvalidPhrasesAsync()
    {
        await Page.GetByTestId("count-12").CheckAsync();
        foreach (var unknown in new[] { true, false })
        {
            var words = Enumerable.Repeat("abandon", 12).ToArray();
            if (unknown) words[2] = "notabipword";
            for (var position = 1; position <= words.Length; position++)
                await HushVotingIdentityJourney.FillSecretAsync(Page.Locator("#rw-" + position), words[position - 1]);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Show all words", Exact = true }).ClickAsync();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true }).ClickAsync();
            await Expect(Page.Locator(unknown ? "#rw-error-UNKNOWN_WORD" : "#rw-error-checksum")).ToContainTextAsync(unknown ? "not in the supported word list" : "failed its checksum");
            await Expect(Page.GetByTestId("word-grid").Locator("input")).ToHaveCountAsync(12);
            for (var position = 1; position <= words.Length; position++)
            {
                var input = Page.Locator("#rw-" + position);
                if (await input.InputValueAsync() != words[position - 1]) throw new InvalidOperationException("Validation lost or changed a recovery input.");
                // FEAT-008 validation feedback focuses the first invalid field;
                // the focused word is visible for correction, all others stay concealed.
                await Expect(input).ToHaveAttributeAsync("type", unknown && position == 3 ? "text" : "password");
            }
            if (unknown) await Expect(Page.Locator("#rw-3")).ToBeFocusedAsync();
            else await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Recovery word errors", Exact = true })).ToBeFocusedAsync();
            await Expect(Page.GetByTestId("word-grid").Locator("input[aria-invalid=true]")).ToHaveCountAsync(unknown ? 1 : 0);
            if (unknown) await Expect(Page.Locator("#rw-3")).ToHaveAttributeAsync("aria-invalid", "true");
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true })).ToBeEnabledAsync();
            var safeText = await Page.Locator("body").InnerTextAsync();
            if (safeText.Contains("notabipword") || safeText.Contains(string.Join(" ", words))) throw new InvalidOperationException("Validation copied recovery input into ordinary page content.");
            _correctableFailures++;
        }
    }

    [Then("numbered validation errors retain concealed inputs for correction")]
    public void CorrectionChecks() => _correctableFailures.Should().Be(2);

    [Then("the invalid phrase is neither persisted nor sent to HushServerNode")]
    public async Task InvalidPhraseStaysLocalAsync()
    {
        scenario.Faults.RequestMethods.Should().NotContain("GetIdentity");
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        var persisted = await Page.EvaluateAsync<bool>("""
            async () => {
                const forbidden = /notabipword|abandon/i;
                if (forbidden.test(JSON.stringify(localStorage)) || forbidden.test(JSON.stringify(sessionStorage))) return true;
                for (const metadata of await indexedDB.databases()) {
                    const db = await new Promise((resolve, reject) => {
                        const request = indexedDB.open(metadata.name);
                        request.onsuccess = () => resolve(request.result);
                        request.onerror = () => reject(new Error('Storage inspection failed'));
                    });
                    try {
                        for (const store of db.objectStoreNames) {
                            const rows = await new Promise((resolve, reject) => {
                                const request = db.transaction(store).objectStore(store).getAll();
                                request.onsuccess = () => resolve(request.result);
                                request.onerror = () => reject(new Error('Storage inspection failed'));
                            });
                            if (forbidden.test(JSON.stringify(rows))) return true;
                        }
                    } finally { db.close(); }
                }
                return false;
            }
            """);
        persisted.Should().BeFalse("invalid recovery phrases belong only to the dedicated correction inputs");
        await Page.ReloadAsync();
        await EntryAsync();
        foreach (var input in await Page.GetByTestId("word-grid").Locator("input").AllAsync())
            if ((await input.InputValueAsync()).Length != 0) throw new InvalidOperationException("Invalid recovery input survived a reload.");
    }

    [Given("a twelve-or-twenty-four word selector with indexed fields")]
    public async Task EntryAsync()
    {
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Restore Recovery Words") }).ClickAsync();
        await Expect(Page.GetByTestId("word-grid")).ToBeVisibleAsync();
        // FEAT-008 AC-008-004 visible-network portion only. Network-change
        // invalidation is a separate pending requirement, not proved here.
        await Expect(Page.GetByTestId("recovery-network")).ToHaveTextAsync("Target network: hushnetwork-devnet · Test network");
    }

    [When("the user selects a word count")]
    public async Task SelectCountAsync()
    {
        await Expect(Page.GetByRole(AriaRole.Radio)).ToHaveCountAsync(2);
        await Expect(Page.GetByTestId("count-12")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("count-24")).ToBeVisibleAsync();
    }

    [Then("exactly that many indexed responsive fields render with accessible labels")]
    public async Task GridAsync()
    {
        foreach (var count in new[] { 12, 24 })
        {
            await Page.GetByTestId("count-" + count).CheckAsync();
            await Expect(Page.GetByTestId("word-grid").Locator("input")).ToHaveCountAsync(count);
            foreach (var position in Enumerable.Range(1, count))
            {
                var input = Page.GetByLabel($"Recovery word {position} of {count}", new() { Exact = true });
                await Expect(input).ToBeVisibleAsync();
                await Expect(input).ToHaveAttributeAsync("autocomplete", "off");
                await Expect(input).ToHaveAttributeAsync("spellcheck", "false");
            }
            foreach (var width in new[] { 320, 768, 1280 })
            {
                await Page.SetViewportSizeAsync(width, 800);
                var fits = await Page.GetByTestId("word-grid").EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth && document.documentElement.scrollWidth <= innerWidth");
                fits.Should().BeTrue("the indexed recovery fields must reflow at supported Web widths");
            }
        }
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        scenario.Faults.RequestMethods.Should().NotContain("GetIdentity", "word-count selection must stay local");
    }

    [When("Alice enters recovery words and moves focus between fields")]
    public async Task EnterAndMoveAsync()
    {
        await HushVotingIdentityJourney.FillSecretAsync(Page.Locator("#rw-1"), "abandon");
        await Expect(Page.Locator("#rw-1")).ToHaveAttributeAsync("type", "text");
        await HushVotingIdentityJourney.FillSecretAsync(Page.Locator("#rw-2"), "ability");
    }

    [Then("only the focused word is visible until an explicit Show action")]
    public async Task FocusedOnlyAsync()
    {
        await Expect(Page.Locator("#rw-1")).ToHaveAttributeAsync("type", "password");
        await Expect(Page.Locator("#rw-2")).ToHaveAttributeAsync("type", "text");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Show all words", Exact = true }).ClickAsync();
        await Expect(Page.Locator("#rw-1")).ToHaveAttributeAsync("type", "text");
        await Expect(Page.Locator("#rw-2")).ToHaveAttributeAsync("type", "text");
    }

    [Then("Hide and lifecycle loss conceal all completed words")]
    public async Task ConcealAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Hide all words", Exact = true }).ClickAsync();
        await Expect(Page.Locator("#rw-1")).ToHaveAttributeAsync("type", "password");
        await Expect(Page.Locator("#rw-2")).ToHaveAttributeAsync("type", "password");
        await Page.ReloadAsync();
        await Expect(Page.GetByTestId("word-grid")).ToHaveCountAsync(0);
        await EntryAsync();
        foreach (var input in await Page.GetByTestId("word-grid").Locator("input").AllAsync())
            if ((await input.InputValueAsync()).Length != 0) throw new InvalidOperationException("Recovery input was restored across page lifecycle loss.");
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }
}
