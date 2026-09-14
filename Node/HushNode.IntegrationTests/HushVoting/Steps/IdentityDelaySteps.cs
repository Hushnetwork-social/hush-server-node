using System.Diagnostics;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007 AC-007-041 -> Phase 7 Task 7.2.
// Real wall-clock deadline and real server indexing; no timer acceleration.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityDelaySteps(HushVotingScenario scenario, HushVotingIdentityJourney identity,
    IdentitySubmissionSteps submission)
{
    private readonly Stopwatch _waiting = new();
    private string _exact = "";
    private string _vaultFingerprint = "";

    [Given("Alice's accepted identity remains unindexed with its exact transaction sealed on the device")]
    public async Task WaitingAsync()
    {
        await submission.PendingAsync();
        _waiting.Start();
        _exact = scenario.Faults.SubmittedTransactions.Single();
        await SamePendingAsync();
        _vaultFingerprint = await FingerprintAsync();
    }

    [When("three real minutes elapse without producing an identity block")]
    public async Task DeadlineAsync()
    {
        var untilNearDeadline = TimeSpan.FromSeconds(175) - _waiting.Elapsed;
        if (untilNearDeadline > TimeSpan.Zero) await Task.Delay(untilNearDeadline);
        await submission.WaitingAsync();
        await Expect(scenario.Page.GetByRole(AriaRole.Heading, new() { Name = "Blockchain confirmation delayed", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        _waiting.Elapsed.TotalSeconds.Should().BeInRange(178, 195);
    }

    [Then("the delayed screen stops automatic RPCs and retains the sealed vault while Check again remains lookup-only")]
    public async Task DelayedAsync()
    {
        await Expect(scenario.Page.GetByText("Your local identity remains safely stored.", new() { Exact = false })).ToBeVisibleAsync();
        // The root's governed Back action remains outside the delay surface;
        // AC-007-058/059 separately require it to lock retained local state.
        var buttons = await scenario.Page.GetByTestId("create-surface").GetByRole(AriaRole.Button).AllAsync();
        var names = new List<string>();
        foreach (var button in buttons) if (await button.IsVisibleAsync()) names.Add((await button.InnerTextAsync()).Trim());
        names.Should().BeEquivalentTo("Check again", "Lock");
        var count = scenario.Faults.IdentityQueryCount;
        await Task.Delay(6_500);
        scenario.Faults.IdentityQueryCount.Should().Be(count);
        scenario.Faults.IdentityResponseDelay = TimeSpan.FromMilliseconds(400);
        try
        {
            await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Check again", Exact = true }).DblClickAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (scenario.Faults.IdentityLookups.Count < count + 1) await Task.Delay(30, deadline.Token);
            await Expect(scenario.Page.GetByRole(AriaRole.Heading, new() { Name = "Blockchain confirmation delayed", Exact = true })).ToBeVisibleAsync();
            await Task.Delay(6_500);
            scenario.Faults.IdentityQueryCount.Should().Be(count + 1, "manual absence must not restart polling");
        }
        finally { scenario.Faults.IdentityResponseDelay = TimeSpan.Zero; }
        await SamePendingAsync();
        (await FingerprintAsync()).Should().Be(_vaultFingerprint, "polling and the delay transition must not rewrite either encrypted slot or journal");
    }

    [Then("a manual exact check after real identity indexing completes only through separate licence indexing")]
    public async Task ConfirmAsync()
    {
        await scenario.Blocks.ProduceBlockAsync();
        using var baseline = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(25));
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Check again", Exact = true }).ClickAsync();
        await Expect(scenario.Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await submission.NoShellAsync();
        await baseline.WaitAsync();
        await scenario.Blocks.ProduceBlockAsync();
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        var active = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        active.KeysMatch.Should().BeTrue();
        active.Active.Should().BeTrue();
        active.PendingTransactionCleared.Should().BeTrue();
    }

    private async Task SamePendingAsync()
    {
        await submission.NoShellAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        var pending = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys,
            HushVotingIdentityJourney.Alias, false, expectedTransaction: _exact);
        pending.KeysMatch.Should().BeTrue();
        pending.PendingTransactionMatches.Should().BeTrue();
        pending.PendingRegistration.Should().BeTrue();
        pending.Active.Should().BeFalse();
    }

    private Task<string> FingerprintAsync() => scenario.Page.EvaluateAsync<string>("""
        async () => {
            const db = await new Promise((resolve, reject) => {
                const r = indexedDB.open('hushvoting-vault');
                r.onsuccess = () => resolve(r.result);
                r.onerror = () => reject(new Error('Vault fingerprint unavailable'));
            });
            try {
                const stores = ['vaultJournal', 'vaultSlots'];
                const tx = db.transaction(stores, 'readonly');
                const snapshot = await Promise.all(stores.map(name => new Promise((resolve, reject) => {
                    const rows = [], cursor = tx.objectStore(name).openCursor();
                    cursor.onerror = () => reject(new Error('Vault fingerprint unavailable'));
                    cursor.onsuccess = () => {
                        if (!cursor.result) { resolve({ name, rows }); return; }
                        if (rows.length >= 8) { reject(new Error('Vault fingerprint exceeds row bound')); return; }
                        rows.push([cursor.result.key, cursor.result.value]);
                        cursor.result.continue();
                    };
                })));
                const bytes = new TextEncoder().encode(JSON.stringify(snapshot, (_, value) => value instanceof Uint8Array ? Array.from(value) : value));
                if (bytes.length > 8 * 1024 * 1024) throw new Error('Vault fingerprint exceeds byte bound');
                try { return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)), b => b.toString(16).padStart(2, '0')).join(''); }
                finally { bytes.fill(0); }
            } finally { db.close(); }
        }
        """);
}
