using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007 AC-007-058 -> Phase 4 Task 4.3 / Phase 7 Tasks 7.1/7.2.
// FEAT-027 user decision: Web Back discards unsaved creation and returns to root.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CreationBackSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity,
    AuthenticationSteps authentication)
{
    private IPage Page => scenario.Page;

    [Given("Alice checks both Web Back controls at every unsaved creation screen")]
    public async Task AllScreensAsync()
    {
        foreach (var browserBack in new[] { true, false })
        foreach (var stage in new[] { "profile", "generate", "recovery", "confirm", "protect" })
        {
            await Page.GotoAsync("/");
            await authentication.FirstRunChoicesAsync();
            await InstallObservationAsync();
            await Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Create User") }).ClickAsync();
            await Expect(Page.GetByLabel("Profile name / alias", new() { Exact = true })).ToBeVisibleAsync();
            if (stage != "profile")
            {
                await Page.GetByLabel("Profile name / alias", new() { Exact = true }).FillAsync(HushVotingIdentityJourney.Alias);
                await Button("Continue").ClickAsync();
                await Expect(Button("Generate recovery words")).ToBeVisibleAsync();
            }
            if (stage is "recovery" or "confirm" or "protect")
            {
                await Button("Generate recovery words").ClickAsync();
                var words = await identity.ReadCandidateAsync();
                if (stage is "confirm" or "protect")
                {
                    await Page.GetByRole(AriaRole.Checkbox).CheckAsync();
                    await Button("Continue").ClickAsync();
                    var challenge = Page.Locator("input[id^=recovery-word-]");
                    await Expect(challenge).ToHaveCountAsync(6);
                    foreach (var field in await challenge.AllAsync())
                    {
                        var position = int.Parse((await field.GetAttributeAsync("id"))!["recovery-word-".Length..], System.Globalization.CultureInfo.InvariantCulture);
                        await HushVotingIdentityJourney.FillSecretAsync(field, words[position - 1]);
                    }
                    if (stage == "protect")
                    {
                        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Verify") }).ClickAsync();
                        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
                        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Confirm device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
                    }
                }
            }
            var token = await Page.EvaluateAsync<string>("() => history.state.hvToken");
            authentication.RootOnlyUrl();
            if (browserBack) await Page.GoBackAsync();
            else await Button("Back").ClickAsync();
            await AssertRootAsync();
            var facts = await Page.EvaluateAsync<int[]>("() => hvCreationBackFacts()");
            facts.Should().Equal(stage is "recovery" or "confirm" or "protect" ? [1, 1, 1, 1] : new[] { 0, 0, 0, 1 }, "cleanup order at {0}, browser Back={1}", stage, browserBack);
            await authentication.StorageRemovedAsync();

            // Traverse genuine browser history, retaining the router's own markers.
            await Page.EvaluateAsync("token => history.pushState({...history.state, hvToken:token}, '', '/')", token);
            await Page.GoBackAsync();
            await AssertRootAsync();
            await Page.GoForwardAsync();
            await AssertRootAsync();
            await Page.ReloadAsync();
            await AssertRootAsync();
            scenario.Faults.IdentityQueryCount.Should().Be(0);
            scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        }
    }

    [When("Alice starts creation again after discarding the final candidate")]
    public async Task FreshCreationAsync()
    {
        var discarded = identity.Keys;
        await identity.GenerateCandidateAsync();
        if (identity.Keys.SigningPublicKey == discarded.SigningPublicKey || identity.Keys.EncryptPublicKey == discarded.EncryptPublicKey)
            throw new InvalidOperationException("Fresh creation reused an abandoned candidate.");
        await Button("Back").ClickAsync();
        await AssertRootAsync();
    }

    [Then("fresh generation was required and no abandoned creation reached the real node")]
    public async Task NoAbandonedIdentityAsync()
    {
        await authentication.StorageRemovedAsync();
        scenario.Faults.IdentityQueryCount.Should().Be(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        // Finish with ordinary creation and actual server indexing to verify the fixture
        // and that successful cleanup leaves a usable production creation journey.
        await identity.AuthenticateAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
    }

    private ILocator Button(string name) => Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    private async Task AssertRootAsync()
    {
        await authentication.FirstRunChoicesAsync();
        await Expect(Page.GetByTestId("recovery-list")).ToHaveCountAsync(0);
        await Expect(Page.Locator("input[id^=recovery-word-]")).ToHaveCountAsync(0);
        await Expect(Page.GetByLabel("Device password", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("create-surface")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        authentication.RootOnlyUrl();
    }

    private Task InstallObservationAsync() => Page.EvaluateAsync("""
        () => {
            const send = MessagePort.prototype.postMessage, listening = new WeakSet(), operations = new Map();
            let created = 0, destroyed = 0, inspections = 0, orderingValid = true;
            MessagePort.prototype.postMessage = function(...args) {
                const message = args[0];
                if (!listening.has(this)) {
                    listening.add(this);
                    const receive = this.onmessage;
                    this.onmessage = function(event) {
                        const result = event.data;
                        if (result?.kind === 'operation-outcome' && result.outcome === 'OK') {
                            const operation = operations.get(result.operationId);
                            if (operation === 'createCandidate') created++;
                            if (operation === 'destroyCandidate') destroyed++;
                        }
                        // Observe before resolving the client's promise; a separate listener
                        // can run after promise continuations have already sent inspection.
                        if (receive) Reflect.apply(receive, this, [event]);
                    };
                }
                if (message?.kind === 'operation') {
                    operations.set(message.operationId, message.operation);
                    if (message.operation === 'inspectStartup' && created > 0) {
                        inspections++;
                        orderingValid &&= destroyed === created;
                    }
                }
                return Reflect.apply(send, this, args);
            };
            window.hvCreationBackFacts = () => [created, destroyed, inspections, Number(orderingValid)];
        }
        """);
}
