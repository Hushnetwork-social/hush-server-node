using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-008 AC-008-017 -> Phase 3 Tasks 3.1/3.2,
// Phase 5 Tasks 5.1/5.2 and Phase 7 Tasks 7.1/7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryContainmentSteps(HushVotingScenario scenario, RecoveryWordEntrySteps entry,
    RecoveryRecreateSteps recreate, HushVotingRecoveryContainment containment)
{
    [Given("Alice enters recovery words with observed React worker network and storage boundaries")]
    public async Task EnterAsync()
    {
        await containment.ArmAsync();
        await entry.EntryAsync();
        await scenario.Page.GetByTestId("count-24").CheckAsync();
        for (var i = 0; i < 24; i++)
            await HushVotingIdentityJourney.FillSecretAsync(scenario.Page.Locator("#rw-" + (i + 1)), HushVotingRecoveryContainment.Words[i]);
        await containment.CheckAsync("typed-input");
        // Exercise the distinct full-phrase replacement buffer before Verify.
        try { await scenario.Page.Locator("#rw-1").EvaluateAsync("""
            (input, phrase) => {
                const data = new DataTransfer(); data.setData('text/plain', phrase);
                input.dispatchEvent(new ClipboardEvent('paste', { bubbles: true, cancelable: true, clipboardData: data }));
            }
            """, string.Join(' ', HushVotingRecoveryContainment.Words)); }
        catch { throw new InvalidOperationException("Recovery paste handoff failed; sensitive diagnostics suppressed."); }
        await Expect(scenario.Page.GetByRole(AriaRole.Alertdialog)).ToBeVisibleAsync();
        await containment.CheckAsync("paste-replacement");
    }

    [When("Alice confirms replacement verifies and completes real recovered registration and protection")]
    public async Task CompleteAsync()
    {
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Replace all", Exact = true }).ClickAsync();
        await scenario.Page.EvaluateAsync("""
            () => {
                const inputs = [...document.querySelectorAll('input')].filter(node => node.type === 'password' || node.id.startsWith('rw-'));
                globalThis.hvRecoveryInputsReleased = () => inputs.length === 25 && inputs.every(node => !node.isConnected && node.value === '');
            }
            """);
        await using var worker = await HushVotingWorkerProbe.AttachAsync(scenario);
        (await worker.EvaluateBooleanAsync(containment.WorkerScript)).Should().BeTrue();
        try
        {
            await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true }).ClickAsync();
            await Expect(scenario.Page.GetByTestId("candidate-list").Locator("li")).ToHaveCountAsync(2);
            await Expect(scenario.Page.GetByTestId("word-grid")).ToHaveCountAsync(0);
            (await scenario.Page.EvaluateAsync<bool>("() => hvRecoveryInputsReleased()")).Should().BeTrue("both the detached word grid and pending paste buffer must clear after Verify");
            await containment.CheckAsync("candidate-review");
            await recreate.SelectAsync();
            await recreate.ReviewAsync();
            await containment.CheckAsync("protection");
            await recreate.RegisterAsync();
            (await worker.EvaluateBooleanAsync("hvRecoveryStorageClean()")).Should().BeTrue("real worker storage writes and logs must contain no plaintext recovery material");
        }
        finally { (await worker.EvaluateBooleanAsync("hvRecoveryStorageRestore()")).Should().BeTrue(); }
    }

    [Then("the recovery journey contains no secret in ordinary UI state transport history storage or completed artifacts")]
    public Task VerifyAsync() => containment.FinishAsync();
}
