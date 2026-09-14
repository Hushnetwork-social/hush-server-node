using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using Olimpo.KeyDerivation;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryCandidateSteps(HushVotingScenario scenario, RecoveryWordEntrySteps entry)
{
    private IPage Page => scenario.Page;
    private int _setsChecked;

    [Given("a checksum-valid phrase")]
    public async Task ReadyAsync() => await entry.EntryAsync();

    private async Task VerifyCountAsync(int count)
    {
        var words = Enumerable.Repeat("abandon", count - 1).Append(count == 12 ? "about" : "art").ToArray();
        await Page.GetByTestId("count-" + count).CheckAsync();
        for (var position = 1; position <= count; position++)
            await HushVotingIdentityJourney.FillSecretAsync(Page.Locator("#rw-" + position), words[position - 1]);
        var before = scenario.Faults.RequestMethods.Count(method => method == "GetIdentity");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true }).ClickAsync();
        await Expect(Page.GetByTestId("zero-hint")).ToBeVisibleAsync();
        var expected = new List<DerivedKeys> { HushVotingTestIdentity.DeriveP01(string.Join(" ", words)) };
        if (count == 24) expected.Add(DeterministicKeyGenerator.DeriveKeys(string.Join(" ", words)));
        var cards = Page.GetByTestId("candidate-list").Locator("li");
        await Expect(cards).ToHaveCountAsync(expected.Count);
        (scenario.Faults.RequestMethods.Count(method => method == "GetIdentity") - before).Should().Be(expected.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            var card = cards.Nth(index);
            await card.GetByRole(AriaRole.Button, new() { Name = "Reveal full addresses", Exact = true }).ClickAsync();
            var displayed = await card.Locator("dd").AllTextContentsAsync();
            if (!displayed.SequenceEqual(new[] { expected[index].SigningPublicKey, expected[index].EncryptPublicKey }))
                throw new InvalidOperationException("A recovered address pair differs from independent approved derivation.");
            await card.GetByRole(AriaRole.Button, new() { Name = "Hide full addresses", Exact = true }).ClickAsync();
        }
        if (count == 24) await Expect(cards.Nth(1)).ToContainTextAsync("Historical Hush desktop and .NET identities");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Selected", Exact = true })).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        _setsChecked++;
    }

    [When("Alice verifies supported phrases with twelve and twenty-four words")]
    public async Task BothCountsAsync()
    {
        await VerifyCountAsync(12);
        await Page.GoBackAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Create User") })).ToBeVisibleAsync();
        await entry.EntryAsync();
        await VerifyCountAsync(24);
    }

    [Then("only applicable approved formats reach the node and their addresses match independent derivation")]
    public void BothChecked() => _setsChecked.Should().Be(2);

    [When("Alice verifies the twenty-four-word phrase")]
    public async Task TwentyFourAsync() => await VerifyCountAsync(24);

    [Then("exact historical address-pair duplicates preserve their source labels while distinct pairs remain separate")]
    public void DedupChecked() => _setsChecked.Should().Be(1);
}
