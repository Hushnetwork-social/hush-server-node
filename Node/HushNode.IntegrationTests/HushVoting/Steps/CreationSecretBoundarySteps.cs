using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007 AC-007-016 -> Phase 2 Tasks 2.1/2.2,
// Phase 7 Tasks 7.1/7.2. A failing React boundary blocks the broader criterion.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CreationSecretBoundarySteps(HushVotingScenario scenario, HushVotingIdentityJourney identity)
{
    private IReadOnlyList<string> _words = [];

    [Given("the creation flow runs")]
    public async Task CreateAsync()
    {
        await HushVotingArtifactClient.RequireAsync();
        _words = await identity.GenerateCandidateAsync();
    }

    [When("secrets are handled")]
    [Then("the real creation display excludes recovery words from React props")]
    public async Task ObserveAsync()
    {
        // Inspect the real renderer's current ancestor props without installing
        // a synthetic view or serializing any secret-bearing object to .NET.
        bool[] facts;
        try
        {
            facts = await scenario.Page.GetByTestId("recovery-list").EvaluateAsync<bool[]>("""
                (list, words) => {
                    const key = Object.keys(list).find(key => key.startsWith('__reactFiber$'));
                    let fiber = key ? list[key] : null;
                    const seen = new WeakSet();
                    let visited = 0, complete = true, leaked = false;
                    const check = value => {
                        if (typeof value === 'string') return value.includes(words.join(' '));
                        if (!value || typeof value !== 'object' || value instanceof Node) return false;
                        if (seen.has(value)) return false;
                        seen.add(value);
                        if (++visited > 10000) { complete = false; return false; }
                        if (Array.isArray(value) && value.length === words.length && value.every((word, i) => word === words[i])) return true;
                        const prototype = Object.getPrototypeOf(value);
                        if (!Array.isArray(value) && prototype !== Object.prototype && prototype !== null) return false;
                        return Object.values(Object.getOwnPropertyDescriptors(value)).some(property => 'value' in property && check(property.value));
                    };
                    let ancestors = 0;
                    while (fiber && ancestors++ < 100) {
                        leaked ||= check(fiber.memoizedProps);
                        fiber = fiber.return;
                    }
                    return [Boolean(key) && ancestors > 1, complete && fiber === null, leaked];
                }
                """, _words);
        }
        catch
        {
            throw new InvalidOperationException("Creation React boundary inspection failed; secret-bearing diagnostics suppressed.");
        }
        facts[0].Should().BeTrue("the inspection must reach actual React ancestors");
        facts[1].Should().BeTrue("an incomplete inspection cannot supply passing evidence");
        facts[2].Should().BeFalse("AC-007-016 prohibits the complete phrase in React props, including the onboarding child publication");
    }

    // The original final Then remains unbound until its complete cross-layer
    // assertions exist. A React-only correction cannot make this original pass.
}
