using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007 AC-007-007 -> Phase 3 Tasks 3.1/3.2, Phase 7 Tasks 7.1/7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityHiddenGenerationSteps(HushVotingScenario scenario,
    HushVotingIdentityJourney identity, IdentitySubmissionSteps submission)
{
    private IPage Page => scenario.Page;

    [Given("Alice is ready to generate before a controlled hidden worker derivation failure")]
    public async Task ReadyAsync()
    {
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Create User") }).ClickAsync();
        await Page.GetByLabel("Profile name / alias", new() { Exact = true }).FillAsync(HushVotingIdentityJourney.Alias);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
    }

    [When("three failed hidden attempts reveal nothing and a fresh attempt recovers from one derivation failure")]
    public async Task FailAndRecoverAsync()
    {
        // First prove bounded exhaustion and absence of any published candidate.
        await GenerateWithFaultAsync(exhaust: true);
        await Page.GoBackAsync();
        await ReadyAsync();
        // Fresh entry's actual inspectStartup rejects residual candidate custody.
        await GenerateWithFaultAsync(exhaust: false);
        var words = await identity.ReadCandidateAsync();
        await identity.ConfirmRecoveryAndProtectAsync(words);
    }

    private async Task GenerateWithFaultAsync(bool exhaust)
    {
        await using var worker = await HushVotingWorkerProbe.AttachAsync(scenario);
        try
        {
            (await worker.EvaluateBooleanAsync("""
                (() => {
                    const encode = TextEncoder.prototype.encode, random = Crypto.prototype.getRandomValues;
                    const buffers = [], copies = [];
                    let failures = 0, distinct = true;
                    Crypto.prototype.getRandomValues = function(buffer) {
                        const result = Reflect.apply(random, this, [buffer]);
                        if (buffer instanceof Uint8Array && buffer.length === 32) {
                            distinct = distinct && copies.every(prior => prior.some((byte, index) => byte !== buffer[index]));
                            buffers.push(buffer); copies.push(buffer.slice());
                        }
                        return result;
                    };
                    TextEncoder.prototype.encode = function(value) {
                        if (value === 'encryption' && failures < FAULT_LIMIT) {
                            failures++;
                            throw new Error('Controlled hidden derivation failure');
                        }
                        return Reflect.apply(encode, this, [value]);
                    };
                    globalThis.hvHiddenGenerationFacts = () => failures === FAULT_LIMIT
                        && buffers.length === EXPECTED_ATTEMPTS && distinct
                        && buffers.every(buffer => buffer.every(byte => byte === 0));
                    globalThis.hvRestoreHiddenGeneration = () => {
                        TextEncoder.prototype.encode = encode; Crypto.prototype.getRandomValues = random;
                        copies.forEach(copy => copy.fill(0)); buffers.length = 0; copies.length = 0;
                    };
                    return true;
                })()
                """.Replace("FAULT_LIMIT", exhaust ? "3" : "1", StringComparison.Ordinal)
                    .Replace("EXPECTED_ATTEMPTS", exhaust ? "3" : "2", StringComparison.Ordinal))).Should().BeTrue();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Generate recovery words", Exact = true }).ClickAsync();
            if (exhaust)
            {
                await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "This device is locked out", Exact = true })).ToBeVisibleAsync();
                await Expect(Page.GetByTestId("recovery-list")).ToHaveCountAsync(0);
                await Expect(Page.Locator("input[type=password]")).ToHaveCountAsync(0);
            }
            else await Expect(Page.GetByTestId("recovery-list").Locator("li")).ToHaveCountAsync(24);
            (await worker.EvaluateBooleanAsync("globalThis.hvHiddenGenerationFacts()")).Should().BeTrue();
            scenario.Faults.IdentityQueryCount.Should().Be(0);
            scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        }
        finally { await worker.EvaluateBooleanAsync("(() => { globalThis.hvRestoreHiddenGeneration?.(); return true; })()"); }
    }

    [Then("only the regenerated valid candidate can be protected and registered with real identity and licence indexing")]
    public async Task RegisterAsync()
    {
        await submission.SubmitAsync();
        submission.SignatoryBinding();
        await submission.ConfirmAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        var stored = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        stored.KeysMatch.Should().BeTrue();
        stored.Active.Should().BeTrue();
    }
}
