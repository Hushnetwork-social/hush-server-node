using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryPasteSteps(HushVotingScenario scenario, RecoveryWordEntrySteps entry)
{
    private IPage Page => scenario.Page;
    private string[] _original = [];
    private int _checkedPositions;

    [Given("a focused word box and a clipboard phrase")]
    public async Task SetupAsync()
    {
        await entry.EntryAsync();
        await scenario.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        _original = [.. Enumerable.Repeat("abandon", 11), "about"];
        await Page.GetByTestId("count-12").CheckAsync();
    }

    private async Task PasteAsync(int position, string phrase)
    {
        try
        {
            await Page.EvaluateAsync("value => navigator.clipboard.writeText(value)", phrase);
            await Page.Locator("#rw-" + position).PressAsync("Control+V");
        }
        catch (Exception error) when (error is PlaywrightException or TimeoutException) { throw new InvalidOperationException("Recovery clipboard action failed; input omitted."); }
    }

    private async Task AssertWordsAsync(string[] expected)
    {
        await Expect(Page.GetByTestId("word-grid").Locator("input")).ToHaveCountAsync(expected.Length);
        for (var position = 1; position <= expected.Length; position++)
            if (await Page.Locator("#rw-" + position).InputValueAsync() != expected[position - 1])
                throw new InvalidOperationException("Recovery paste changed, lost, or misordered an input.");
    }

    [When("Alice pastes a complete phrase from every supported field position")]
    public async Task EveryPositionAsync()
    {
        foreach (var count in new[] { 12, 24 })
        {
            await Page.GetByTestId("count-" + count).CheckAsync();
            var words = count == 12 ? _original : [.. Enumerable.Repeat("abandon", 23), "art"];
            for (var position = 1; position <= count; position++)
            {
                await PasteAsync(position, "  " + string.Join("\t\n", words).ToUpperInvariant() + "  ");
                await AssertWordsAsync(words);
                await Page.GetByRole(AriaRole.Button, new() { Name = "Clear all", Exact = true }).ClickAsync();
                _checkedPositions++;
            }
        }
    }

    [Then("the whole grid receives exactly one normalized word per field")]
    public void EveryPositionChecked() => _checkedPositions.Should().Be(36);

    [When("Alice pastes mismatched and unsupported phrase counts over existing words")]
    public async Task MismatchesAsync()
    {
        await PasteAsync(3, string.Join(" ", _original));
        foreach (var count in new[] { 1, 11, 13, 15, 18, 21, 24, 25 })
        {
            await PasteAsync(7, string.Join(" ", Enumerable.Repeat("ability", count)));
            await Expect(Page.Locator("#rw-paste-error")).ToContainTextAsync("must contain exactly 12 or 24 words");
            await AssertWordsAsync(_original);
            await Expect(Page.GetByTestId("count-12")).ToBeCheckedAsync();
            await Expect(Page.GetByRole(AriaRole.Alertdialog)).ToHaveCountAsync(0);
            _checkedPositions++;
        }
    }

    [Then("the paste reports a count error and leaves every existing field and the selected count unchanged")]
    public void CountErrorsChecked() => _checkedPositions.Should().Be(8);

    [When("Alice pastes a replacement phrase over existing words")]
    public async Task ReplacementAsync()
    {
        await PasteAsync(1, string.Join(" ", _original));
        await PasteAsync(7, string.Join(" ", Enumerable.Repeat("ability", 12)));
        await Expect(Page.GetByRole(AriaRole.Alertdialog, new() { Name = "Replace words" })).ToBeVisibleAsync();
        await AssertWordsAsync(_original);
    }

    [Then("only explicit Replace all changes the entire grid and Cancel retains the original words")]
    public async Task ConsentAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await AssertWordsAsync(_original);
        var replacement = Enumerable.Repeat("ability", 12).ToArray();
        await PasteAsync(12, string.Join(" ", replacement));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Replace all", Exact = true }).ClickAsync();
        await AssertWordsAsync(replacement);
        await Expect(Page.GetByRole(AriaRole.Alertdialog)).ToHaveCountAsync(0);
    }

    [When("Alice pastes a count-correct phrase containing unknown words")]
    public async Task UnknownAsync()
    {
        _original[2] = "notabipword";
        _original[8] = "alsonotabipword";
        await PasteAsync(6, string.Join(" ", _original));
    }

    [Then("only unknown numbered positions are marked and their values are never echoed")]
    public async Task UnknownPositionsAsync()
    {
        await AssertWordsAsync(_original);
        await Expect(Page.GetByTestId("word-grid").Locator("input[aria-invalid=true]")).ToHaveCountAsync(2);
        foreach (var position in new[] { 3, 9 })
        {
            await Expect(Page.Locator("#rw-" + position)).ToHaveAttributeAsync("aria-invalid", "true");
            await Expect(Page.Locator("#rw-" + position)).ToHaveAttributeAsync("type", position == 3 ? "text" : "password");
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Review word " + position, Exact = true })).ToBeVisibleAsync();
        }
        await Expect(Page.Locator("#rw-3")).ToBeFocusedAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Review word 9", Exact = true }).ClickAsync();
        await Expect(Page.Locator("#rw-9")).ToBeFocusedAsync();
        await Expect(Page.Locator("#rw-9")).ToHaveAttributeAsync("type", "text");
        await Expect(Page.Locator("#rw-3")).ToHaveAttributeAsync("type", "password");
        if ((await Page.Locator("body").InnerTextAsync()).Contains("notabipword")) throw new InvalidOperationException("Paste validation echoed an unknown recovery word.");
    }

    [When("Alice pastes from her clipboard and clears the recovery grid")]
    public async Task ClearPastedAsync()
    {
        await PasteAsync(4, string.Join(" ", _original));
        await AssertWordsAsync(_original);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Clear all", Exact = true }).ClickAsync();
        await AssertWordsAsync(Enumerable.Repeat("", 12).ToArray());
    }

    [Then("the clipboard retains the source phrase and no file is read by the app")]
    public async Task SourceRemainsAsync()
    {
        var unchanged = await Page.EvaluateAsync<bool>("async expected => await navigator.clipboard.readText() === expected", string.Join(" ", _original));
        unchanged.Should().BeTrue("the app must not modify the user's source clipboard");
        await Expect(Page.Locator("input[type=file]")).ToHaveCountAsync(0);
        scenario.Faults.RequestMethods.Should().NotContain("GetIdentity");
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }
}
