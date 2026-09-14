using System.Text.Json;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using HushNode.Feeds.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryRecreateSteps(HushVotingScenario scenario, RecoveryCandidateSteps candidates)
{
    private IPage Page => scenario.Page;
    private const string Alias = "Recovered voting identity";
    private int[]? _initialSocialRows;

    [Given("Alice starts recovery without a profile and with observed backend social storage")]
    public async Task ObserveAbsentAsync()
    {
        // The real node initializes its own personal feed via a startup transaction.
        // Index that fixture work before observing recovery's storage effects.
        await scenario.Blocks.ProduceBlockAsync();
        _initialSocialRows = await ReadSocialRowCountsAsync();
        await AbsentAsync();
    }

    [Given("Alice owns recovery words without a blockchain profile")]
    public async Task AbsentAsync()
    {
        await candidates.ReadyAsync();
        await candidates.TwentyFourAsync();
        await Expect(Page.GetByTestId("zero-hint")).ToContainTextAsync("not generating new keys");
        await Expect(Page.GetByText("No profile currently exists on this blockchain.", new() { Exact = false })).ToBeVisibleAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [When("Alice selects her recovered identity for profile creation")]
    public async Task SelectAsync()
    {
        var proceed = Page.GetByRole(AriaRole.Button, new() { Name = "Continue to review profile", Exact = true });
        await Expect(proceed).ToBeDisabledAsync();
        await Page.GetByTestId("candidate-list").Locator("li").First.GetByRole(AriaRole.Button, new() { Name = "Select this identity", Exact = true }).ClickAsync();
        await Expect(proceed).ToBeEnabledAsync();
        await proceed.ClickAsync();
        await Expect(Page.GetByTestId("recreate-alias")).ToBeVisibleAsync();
    }

    [Then("the absence explanation requires explicit creation with the recovered keys")]
    [Then("the recreation alias starts empty and Private while Public requires acknowledgement")]
    public async Task ReviewAsync()
    {
        await Expect(Page.GetByTestId("recreate-alias")).ToHaveValueAsync("");
        await Expect(Page.GetByTestId("visibility-private")).ToBeCheckedAsync();
        var create = Page.GetByRole(AriaRole.Button, new() { Name = "Create HushNetwork identity", Exact = true });
        await Expect(create).ToBeDisabledAsync();
        await Page.GetByTestId("recreate-alias").FillAsync(Alias);
        await Page.GetByTestId("visibility-public").CheckAsync();
        await Expect(create).ToBeDisabledAsync();
        await Page.GetByTestId("public-ack").CheckAsync();
        await Expect(create).ToBeEnabledAsync();
        await create.ClickAsync();
        await Expect(Page.GetByTestId("mode-password")).ToBeCheckedAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [Then("registration and protection preserve the recovered signing and encryption addresses")]
    public async Task RegisterAsync() => await RegisterCoreAsync(false);

    [Then("restored registration polls the unchanged node contract until exact block confirmation")]
    public async Task AutomaticRegistrationAsync() => await RegisterCoreAsync(true);

    [Then("recovery submits only its identity and creates no feed or social state before the separate licence handoff")]
    public async Task NoSocialSideEffectsAsync()
    {
        _initialSocialRows.Should().NotBeNull("social storage must be observed before recovery begins");
        await RegisterCoreAsync(false, async () =>
        {
            // No block has indexed the new identity: the root cannot yet start FEAT-016.
            scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
            var recoveryMethods = new HashSet<string>(StringComparer.Ordinal)
                { "GetIdentity", "GetBlockchainHeight", "SubmitSignedTransaction" };
            scenario.Faults.RequestMethods.All(recoveryMethods.Contains).Should().BeTrue();
            (await ReadSocialRowCountsAsync()).Should().Equal(_initialSocialRows!);
            await AssertNoBrowserSocialStateAsync();
        });
        // FEAT-016 owns the subsequent required baseline transaction, not recovery.
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        using var licence = JsonDocument.Parse(scenario.Faults.SubmittedTransactions.Last());
        licence.RootElement.GetProperty("PayloadKind").GetString().Should().Be("71370664-5eb4-4ce9-b96a-d7e7ffe53db5");
        licence.RootElement.GetProperty("Payload").GetProperty("TransitionIntent").GetString().Should().Be("baseline_free");
        var fullJourneyMethods = new HashSet<string>(StringComparer.Ordinal)
            { "GetIdentity", "GetBlockchainHeight", "SubmitSignedTransaction", "GetMyEntitlement" };
        scenario.Faults.RequestMethods.All(fullJourneyMethods.Contains).Should().BeTrue();
        (await ReadSocialRowCountsAsync()).Should().Equal(_initialSocialRows!);
        await AssertNoBrowserSocialStateAsync();
    }

    private async Task<int[]> ReadSocialRowCountsAsync()
    {
        using var scope = scenario.Node.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();
        // Every current FeedsDbContext table, including HushSocial and auxiliary state.
        return [await db.Feeds.CountAsync(), await db.FeedParticipants.CountAsync(),
            await db.FeedMessages.CountAsync(), await db.GroupFeeds.CountAsync(),
            await db.GroupFeedParticipants.CountAsync(), await db.GroupFeedKeyGenerations.CountAsync(),
            await db.GroupFeedEncryptedKeys.CountAsync(), await db.GroupFeedMemberCommitments.CountAsync(),
            await db.FeedReadPositions.CountAsync(), await db.Attachments.CountAsync(),
            await db.SocialPosts.CountAsync(), await db.SocialPostAudienceCircles.CountAsync()];
    }

    private async Task AssertNoBrowserSocialStateAsync()
    {
        (await Page.EvaluateAsync<bool>("async () => (await indexedDB.databases()).every(db => db.name === 'hushvoting-vault') && localStorage.length === 0 && sessionStorage.length === 0 && (await caches.keys()).length === 0")).Should().BeTrue();
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("feed|social|chat", System.Text.RegularExpressions.RegexOptions.IgnoreCase) })).ToHaveCountAsync(0);
    }

    private async Task RegisterCoreAsync(bool automatic, Func<Task>? beforeIndex = null)
    {
        await Page.GetByTestId("recovery-no-retention-ack").CheckAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Confirm device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        using (var identity = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30)))
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
            await identity.WaitAsync();
        }
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Waiting for your restored identity", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        var keys = HushVotingTestIdentity.DeriveP01(string.Join(" ", Enumerable.Repeat("abandon", 23).Append("art")));
        using (var submitted = JsonDocument.Parse(scenario.Faults.SubmittedTransactions.Single()))
        {
            var root = submitted.RootElement;
            root.GetProperty("PayloadKind").GetString().Should().Be("351cd60b-3fdf-48d4-b608-e93c0100f7d0");
            var payload = root.GetProperty("Payload");
            if (payload.GetProperty("PublicSigningAddress").GetString() != keys.SigningPublicKey || payload.GetProperty("PublicEncryptAddress").GetString() != keys.EncryptPublicKey)
                throw new InvalidOperationException("Restored profile registration changed the recovered keys.");
            payload.GetProperty("IdentityAlias").GetString().Should().Be(Alias);
            payload.GetProperty("IsPublic").GetBoolean().Should().BeTrue();
        }
        if (beforeIndex is not null) await beforeIndex();
        if (automatic)
        {
            var queries = scenario.Faults.IdentityQueryCount;
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (scenario.Faults.IdentityQueryCount <= queries && DateTime.UtcNow < deadline) await Task.Delay(20);
            scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(queries, "the staged registration must poll without a user click");
            scenario.Faults.SubmittedTransactions.Count.Should().Be(1, "polling must not resubmit a pending identity");
            await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        }
        using var baseline = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        await scenario.Blocks.ProduceBlockAsync();
        if (!automatic) await Page.GetByRole(AriaRole.Button, new() { Name = "Check again", Exact = true }).ClickAsync();
        await Expect(Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await baseline.WaitAsync();
        await scenario.Blocks.ProduceBlockAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = Alias, Exact = true })).ToBeVisibleAsync();
    }
}
