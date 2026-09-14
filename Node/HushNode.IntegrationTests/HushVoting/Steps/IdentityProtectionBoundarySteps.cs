using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007 AC-007-017 -> Phase 7 Task 7.2.
// Observe the real secret-transfer boundary; never retain its value in the probe.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityProtectionBoundarySteps(HushVotingScenario scenario, HushVotingIdentityJourney identity)
{
    private bool _networkOrConsoleLeak;

    [Given("Alice cannot enter a device password until her real six-word recovery challenge succeeds")]
    public async Task SequenceAsync()
    {
        scenario.Page.Console += (_, message) => _networkOrConsoleLeak |= message.Text.Contains(HushVotingScenario.DevicePassword, StringComparison.Ordinal);
        scenario.Page.Request += (_, request) => _networkOrConsoleLeak |=
            request.Url.Contains(HushVotingScenario.DevicePassword, StringComparison.Ordinal)
            || (request.PostData?.Contains(HushVotingScenario.DevicePassword, StringComparison.Ordinal) ?? false);
        await scenario.Page.AddInitScriptAsync("""
            (() => {
                const original = MessagePort.prototype.postMessage;
                let transfers = 0, commands = 0, transferId = null, ordered = true;
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'secret-transfer' && message.purpose === 'devicePassword') {
                        transfers++; transferId = message.operationId;
                    }
                    if (message?.kind === 'operation' && message.operation === 'provisionFromValidatedBundle') {
                        commands++;
                        ordered = ordered && transferId === message.operationId
                            && !('devicePassword' in (message.payload ?? {})) && !('password' in (message.payload ?? {}));
                    }
                    return Reflect.apply(original, this, args);
                };
                window.__hvProtectionFacts = () => ({ transfers, commands, ordered });
            })();
            """);
        var words = await identity.GenerateCandidateAsync();
        await Expect(scenario.Page.Locator("input[type=password]")).ToHaveCountAsync(0);
        await scenario.Page.GetByRole(AriaRole.Checkbox).CheckAsync();
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        var challenge = scenario.Page.Locator("input[id^=recovery-word-]");
        await Expect(challenge).ToHaveCountAsync(6);
        await Expect(scenario.Page.Locator("input[type=password]")).ToHaveCountAsync(0);
        await Expect(scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Verify words", Exact = true })).ToBeDisabledAsync();
        foreach (var field in await challenge.AllAsync())
        {
            var position = int.Parse((await field.GetAttributeAsync("id"))!["recovery-word-".Length..], System.Globalization.CultureInfo.InvariantCulture);
            await HushVotingIdentityJourney.FillSecretAsync(field, words[position - 1]);
        }
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Verify words", Exact = true }).ClickAsync();
        await Expect(scenario.Page.GetByLabel("Device password", new() { Exact = true })).ToHaveValueAsync("");
        await Expect(scenario.Page.GetByLabel("Confirm device password", new() { Exact = true })).ToHaveValueAsync("");
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [When("Alice protects the candidate using the direct worker secret-transfer boundary")]
    public async Task ProtectAsync()
    {
        await HushVotingIdentityJourney.FillSecretAsync(scenario.Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(scenario.Page.GetByLabel("Confirm device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await NoStateLeakAsync();
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Protect this device and continue", Exact = true }).ClickAsync();
        await Expect(scenario.Page.GetByRole(AriaRole.Heading, new() { Name = "Review HushNetwork identity", Exact = true })).ToBeVisibleAsync(new() { Timeout = 30_000 });
        (await scenario.Page.EvaluateAsync<bool>("() => { const f = window.__hvProtectionFacts(); return f.transfers === 1 && f.commands === 1 && f.ordered; }")).Should().BeTrue();
        await Expect(scenario.Page.Locator("input[type=password]")).ToHaveCountAsync(0);
        await NoStateLeakAsync();
        var stored = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        stored.DevicePasswordProtected.Should().BeTrue();
        stored.KeysMatch.Should().BeTrue();
        stored.Active.Should().BeFalse();
    }

    private async Task NoStateLeakAsync()
    {
        // Read only data descriptors of rendered React props/hooks (including
        // referenced XState actor data). Do not invoke getters, inspect closures,
        // or serialize the object graph/DOM input refs into artifacts.
        var absent = await scenario.Page.EvaluateAsync<bool>("""
            password => {
                const pending = [history.state], seen = new WeakSet();
                for (const element of document.querySelectorAll('*')) {
                    const key = Object.keys(element).find(key => key.startsWith('__reactFiber$'));
                    for (let fiber = key && element[key]; fiber; fiber = fiber.return) {
                        pending.push(fiber.memoizedProps, fiber.memoizedState);
                    }
                }
                for (const storage of [localStorage, sessionStorage]) {
                    for (let i = 0; i < storage.length; i++) pending.push(storage.getItem(storage.key(i)));
                }
                let visited = 0;
                while (pending.length) {
                    const value = pending.pop();
                    if (typeof value === 'string') { if (value.includes(password)) return false; continue; }
                    if (!value || typeof value !== 'object' || seen.has(value)
                        || value instanceof Node || value instanceof Window || value instanceof MessagePort) continue;
                    if (++visited > 100000) throw new Error('Protection state inspection exceeded its bound');
                    seen.add(value);
                    if (value instanceof Map) { for (const item of value.values()) pending.push(item); }
                    else if (value instanceof Set) { for (const item of value) pending.push(item); }
                    else for (const field of Object.values(Object.getOwnPropertyDescriptors(value))) {
                        if ('value' in field) pending.push(field.value);
                    }
                }
                return true;
            }
            """, HushVotingScenario.DevicePassword);
        absent.Should().BeTrue("device passwords must stay out of rendered application state and web storage");
        _networkOrConsoleLeak.Should().BeFalse();
    }

    [Then("device inputs clear without exposing the password in rendered state history console or HTTP traffic and the exact identity registers")]
    public async Task RegisterAsync()
    {
        await identity.SubmitAndIndexAsync();
        await identity.UnlockAndBootstrapAsync();
        await NoStateLeakAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync();
    }
}
