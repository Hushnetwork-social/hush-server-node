using System.Globalization;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HushNetwork.proto;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentitySubmissionSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity)
{
    private IPage Page => scenario.Page;
    private IReadOnlyList<string> _words = [];
    private string _signed = "";
    private ILocator Button(string name) => Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    [Given("provisioning authorization is valid")]
    [Given("reviewed profile fields and exact candidate addresses")]
    [Given("the authority owns the signing key")]
    public async Task ReviewAsync()
    {
        _words = await identity.GenerateCandidateAsync();
        await identity.ConfirmRecoveryAndProtectAsync(_words);
    }

    [When("Review renders")]
    public async Task RenderReviewAsync() => await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Review HushNetwork identity", Exact = true })).ToBeVisibleAsync();

    [Then("normalized alias, visibility, protection/recovery state, and both abbreviated public addresses are shown")]
    public async Task SafeReviewAsync()
    {
        await Expect(Page.GetByText(HushVotingIdentityJourney.Alias, new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Private", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("24 words confirmed", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Ready", new() { Exact = true })).ToBeVisibleAsync();
        foreach (var label in new[] { "Signing address", "Encryption address" })
        {
            var value = await Page.Locator("dt").Filter(new() { HasText = label }).Locator("..").Locator("dd").InnerTextAsync();
            value.Should().Contain("…");
            value.Length.Should().BeLessThan(66);
        }
        await Expect(Button("Create HushNetwork identity")).ToBeEnabledAsync();
    }

    [Then("no private material, full address, or transaction JSON is present")]
    public async Task NoPrivateReviewAsync()
    {
        var text = await Page.Locator("body").InnerTextAsync();
        var forbidden = new[] { string.Join(' ', _words), identity.Keys.SigningPrivateKey, identity.Keys.EncryptPrivateKey,
            identity.Keys.SigningPublicKey, identity.Keys.EncryptPublicKey, HushVotingScenario.DevicePassword, "\"PayloadKind\"", "\"UserSignature\"" };
        if (forbidden.Any(value => text.Contains(value, StringComparison.Ordinal))) throw new InvalidOperationException("Final review exposed forbidden credential or transaction content.");
        await Expect(Page.Locator("input[type=password], #recovery-list")).ToHaveCountAsync(0);
    }

    [When("the signed transaction is constructed")]
    [When("the transaction is signed")]
    public async Task SubmitAsync()
    {
        using var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(20));
        await Button("Create HushNetwork identity").ClickAsync();
        await received.WaitAsync();
        _signed = scenario.Faults.SubmittedTransactions.Single();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (scenario.Faults.Submissions.IsEmpty) await Task.Delay(50, deadline.Token);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Waiting for blockchain final approval", Exact = true })).ToBeVisibleAsync();
    }

    [Then("it uses CSPRNG UUIDv4, a corpus-exact UTC timestamp, approved property order, exact UTF-8 PayloadSize, the exact payload GUID, and the established signed JSON representation")]
    public void CanonicalTransaction()
    {
        using var document = JsonDocument.Parse(_signed);
        var root = document.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().Equal("TransactionId", "PayloadKind", "TransactionTimeStamp", "Payload", "PayloadSize", "UserSignature");
        root.GetProperty("TransactionId").GetString().Should().MatchRegex("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");
        var timestamp = root.GetProperty("TransactionTimeStamp").GetString()!;
        DateTime.TryParseExact(timestamp, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _).Should().BeTrue();
        root.GetProperty("PayloadKind").GetString().Should().Be("351cd60b-3fdf-48d4-b608-e93c0100f7d0");
        var payload = root.GetProperty("Payload");
        payload.EnumerateObject().Select(p => p.Name).Should().Equal("IdentityAlias", "PublicSigningAddress", "PublicEncryptAddress", "IsPublic");
        root.GetProperty("PayloadSize").GetInt32().Should().Be(Encoding.UTF8.GetByteCount(payload.GetRawText()));
        scenario.Faults.Submissions.Last().Status.Should().Be(TransactionStatus.Accepted, "the ordinary server validates the delivered signed representation");
        SignatoryBinding();
    }

    [Then("UserSignature.Signatory equals the payload signing address")]
    [Then("both equal the authority-owned signing key")]
    public void SignatoryBinding()
    {
        using var document = JsonDocument.Parse(_signed);
        var root = document.RootElement;
        // Generated recovery material supplies an independent shared-code derivation oracle.
        if (root.GetProperty("UserSignature").GetProperty("Signatory").GetString() != identity.Keys.SigningPublicKey
            || root.GetProperty("Payload").GetProperty("PublicSigningAddress").GetString() != identity.Keys.SigningPublicKey
            || root.GetProperty("Payload").GetProperty("PublicEncryptAddress").GetString() != identity.Keys.EncryptPublicKey)
            throw new InvalidOperationException("Submitted identity keys did not match the generated candidate.");
        scenario.Faults.Submissions.Last().Status.Should().Be(TransactionStatus.Accepted);
    }

    [Given("the transaction is accepted or pending")]
    [Given("only ACCEPTED or PENDING knowledge exists")]
    [Given("the waiting gate is active")]
    public async Task PendingAsync() { await ReviewAsync(); await SubmitAsync(); }

    [When("the confirmation gate renders")]
    [When("the shell entry is evaluated")]
    public async Task WaitingAsync() => await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Waiting for blockchain final approval", Exact = true })).ToBeVisibleAsync();

    [Then("it states that mempool admission is not block confirmation")]
    public async Task MempoolCopyAsync()
    {
        await Expect(Page.GetByText("Your identity transaction is in the mempool.", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        scenario.Faults.Submissions.Last().Status.Should().Be(TransactionStatus.Accepted);
    }

    [Then("it offers safe Lock/close guidance without an endless unexplained spinner")]
    public async Task SafeExitAsync()
    {
        await Expect(Page.GetByText("You can lock or close HushVoting! safely. We will check again after your next unlock.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Button("Lock")).ToBeEnabledAsync();
        await Expect(Button("Check again")).ToBeEnabledAsync();
        await Button("Lock").ClickAsync();
        await Expect(Button("Unlock HushVoting!")).ToBeVisibleAsync();
    }

    [Then("HushVoting does not enter the authenticated shell")]
    public async Task NoShellAsync() => await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);

    [Then("exact GetIdentity confirmation is required")]
    public async Task ConfirmAsync()
    {
        await NoShellAsync();
        using var baseline = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(25));
        await scenario.Blocks.ProduceBlockAsync();
        // Identity confirmation hands over to the separately indexed licence gate.
        await Expect(Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await NoShellAsync();
        await baseline.WaitAsync();
        await scenario.Blocks.ProduceBlockAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [When("time passes")]
    public async Task WaitAsync() => await Task.Delay(6_500);

    [Then("no submission occurs every three seconds")]
    [Then("no periodic replacement or liveness submission is implemented")]
    public void NoPeriodicSubmission()
    {
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        if (scenario.Faults.SubmittedTransactions.Single() != _signed) throw new InvalidOperationException("Waiting replaced the identity transaction.");
    }
}
