using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-009 AC-009-035 -> Phase 7 Task 7.2.
// Actual worker-buffer disposal is inspected independently by App Twins.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialSourceReleaseSteps(HushVotingScenario scenario, CredentialFileSteps file)
{
    private IPage Page => scenario.Page;

    [Given("Alice selects her real registered credential source with an import-release observer")]
    public async Task ReadyAsync()
    {
        await file.SourceWithWordsAsync();
        await Page.EvaluateAsync("""
            () => {
                const send = MessagePort.prototype.postMessage;
                let port, command, replaying = false, replayResult = null, successful = false;
                const listening = new WeakSet();
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'operation' && message.operation === 'importFileCandidate') {
                        port = this;
                        // Public command metadata only: no secret-transfer values are retained.
                        command = { ...message };
                        if (!listening.has(this)) {
                            listening.add(this);
                            this.addEventListener('message', event => {
                                const result = event.data;
                                if (result?.kind !== 'operation-outcome' || result.operationId !== command?.operationId) return;
                                if (replaying) replayResult = { outcome: result.outcome, reason: result.payload?.reason };
                                else successful = result.outcome === 'OK';
                            });
                        }
                    }
                    return Reflect.apply(send, this, args);
                };
                window.__hvReleasedImport = () => successful;
                window.__hvReplayReleasedImport = async () => {
                    if (!successful || !port || !command) return false;
                    replaying = true;
                    Reflect.apply(send, port, [command]);
                    const deadline = Date.now() + 5000;
                    while (replayResult === null && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve, 20));
                    return replayResult?.outcome === 'INVALID_INPUT' && replayResult.reason === 'missing-file-material';
                };
            }
            """);
        await file.ChoosePreparedAsync();
    }

    [When("the first post-validation identity request is paused before reaching the live node")]
    public async Task InspectBeforeOnlineAsync()
    {
        var queries = scenario.Faults.IdentityQueryCount;
        var arrived = new TaskCompletionSource<IRoute>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<IRoute, Task> hold = route => { arrived.TrySetResult(route); return Task.CompletedTask; };
        await Page.RouteAsync("**/api/identity", hold);
        IRoute? pending = null;
        try
        {
            await Page.EvaluateAsync("() => { window.__hvSubmittedPasswordInput = document.querySelector('[data-testid=backup-password-input]'); }");
            await file.SubmitPreparedPasswordAsync();
            pending = await arrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
            scenario.Faults.IdentityQueryCount.Should().Be(queries);
            (await Page.EvaluateAsync<bool>("() => window.__hvReleasedImport()")).Should().BeTrue();
            (await Page.EvaluateAsync<bool>("() => { const input = window.__hvSubmittedPasswordInput; return input !== null && input.value === '' && !input.isConnected; }")).Should().BeTrue();
            await Expect(Page.GetByTestId("credential-file-input")).ToHaveCountAsync(0);
            await Expect(Page.GetByTestId("selected-file-name")).ToHaveCountAsync(0);
            await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
            await Expect(Page.GetByTestId("restore-device-password")).ToHaveCountAsync(0);
            await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
            // A negative command replay reaches the real worker, with no new source or password.
            (await Page.EvaluateAsync<bool>("() => window.__hvReplayReleasedImport()")).Should().BeTrue();
            scenario.Faults.IdentityQueryCount.Should().Be(queries);
            scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
            await pending.ContinueAsync();
            pending = null;
        }
        finally
        {
            if (pending is not null) await pending.AbortAsync();
            await Page.UnrouteAsync("**/api/identity", hold);
        }
    }

    [Then("released import material cannot be reused and the unchanged source restores through real identity and licence verification")]
    public async Task CompleteAsync()
    {
        await file.ImportedAsync();
        await file.ProtectAsync();
        await file.SourceUnchangedAsync();
        await file.NoPersistentSourceAsync();
    }
}
