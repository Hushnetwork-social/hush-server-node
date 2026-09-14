// EPIC-001 -> FEAT-007 AC-007-066 -> Phase 6 Tasks 6.2/6.4,
// Phase 7 Tasks 7.1/7.2. Web evidence only; native equivalence remains unqualified.
using System.Collections.Concurrent;
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
internal sealed class WebTransportConformanceSteps(HushVotingScenario scenario,
    HushVotingIdentityJourney identity, IdentitySubmissionSteps submission, AuthenticationSteps authentication)
{
    private readonly List<TransactionStatus> _checked = [];
    private readonly ConcurrentBag<Task> _pageReads = [];
    private readonly ConcurrentQueue<(string Kind, bool Matches, string Status, bool Found)> _replies = [];
    private IPage Page => scenario.Page;

    [Given("Alice can compare real node replies with the production Web BFF and worker")]
    public async Task ReadyAsync()
    {
        await HushVotingArtifactClient.RequireAsync();
        await authentication.OpenRootAsync();
    }

    [When("real admission and indexing produce Accepted Pending AlreadyExists and Rejected for her Web creation requests")]
    public async Task ExerciseAsync()
    {
        EventHandler<IResponse> pageObserver = (_, response) =>
        {
            if (response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == "/api/identity")
                _pageReads.Add(ObservePageLookupAsync(response));
        };
        scenario.Context.Response += pageObserver;
        try
        {
            foreach (var status in new[] { TransactionStatus.Accepted, TransactionStatus.Pending,
                         TransactionStatus.AlreadyExists, TransactionStatus.Rejected })
            {
                var offset = _replies.Count;
                await submission.ReviewAsync();
                await using (var observer = await HushVotingWorkerNetworkObserver.AttachAsync(scenario, Observe))
                {
                    var submittedOffset = scenario.Faults.SubmittedTransactions.Count;
                    var admissionOffset = scenario.Faults.Submissions.Count;
                    var raced = status is TransactionStatus.Pending or TransactionStatus.AlreadyExists;
                    if (raced) scenario.Faults.HoldNextSubmission();
                    scenario.Faults.CorruptNextTransactionSignature = status == TransactionStatus.Rejected;
                    try
                    {
                        await Page.GetByRole(AriaRole.Button, new() { Name = "Create HushNetwork identity", Exact = true }).ClickAsync();
                        if (raced)
                        {
                            await scenario.Faults.SubmissionArrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
                            var signed = scenario.Faults.SubmittedTransactions.Skip(submittedOffset).Single();
                            await HushVotingArtifactClient.RegisterAsync(signed);
                            using var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(20));
                            var first = await scenario.Blockchain.SubmitSignedTransactionAsync(new() { SignedTransaction = signed },
                                deadline: DateTime.UtcNow.AddSeconds(15));
                            first.Status.Should().Be(TransactionStatus.Accepted);
                            await received.WaitAsync();
                            // The browser's original bytes reach ordinary admission again.
                            // Indexing is the only difference between Pending and AlreadyExists.
                            if (status == TransactionStatus.AlreadyExists)
                            {
                                await scenario.Blocks.ProduceBlockAsync();
                                scenario.Faults.IdentityResponseDelay = TimeSpan.FromSeconds(3);
                            }
                        }
                    }
                    finally { scenario.Faults.ReleaseSubmission(); }

                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    while (!_replies.Skip(offset).Any(reply => reply.Kind == "submit"))
                    {
                        await observer.VerifyAsync();
                        await Task.Delay(25, deadline.Token);
                    }
                    var wire = _replies.Skip(offset).Single(reply => reply.Kind == "submit");
                    wire.Matches.Should().BeTrue("the BFF must preserve the actual node reply fields");
                    wire.Status.Should().Be(WireStatus(status));
                    scenario.Faults.Submissions.Skip(admissionOffset).Count(reply => reply.Status == status).Should().Be(1);
                    await submission.NoShellAsync();
                    var staged = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
                    staged.KeysMatch.Should().BeTrue();
                    staged.PendingRegistration.Should().BeTrue();
                    staged.Active.Should().BeFalse();

                    if (status == TransactionStatus.Rejected)
                    {
                        var rejected = scenario.Faults.Submissions.Last();
                        rejected.ValidationCode.Should().Be(HushShared.Identity.Model.FullIdentityValidationCodes.InvalidSignature);
                        rejected.Successfull.Should().BeFalse();
                        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "This device is locked out", Exact = true })).ToBeVisibleAsync();
                        await Expect(Page.GetByTestId("support-code")).ToHaveTextAsync("TERMINAL_REJECTION");
                        var lookups = scenario.Faults.IdentityQueryCount;
                        await Task.Delay(6_500);
                        scenario.Faults.IdentityQueryCount.Should().Be(lookups);
                        scenario.Faults.SubmittedTransactions.Count.Should().Be(submittedOffset + 1);
                        var absent = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = identity.Keys.SigningPublicKey },
                            deadline: DateTime.UtcNow.AddSeconds(10));
                        absent.Successfull.Should().BeFalse();
                        await Page.GoBackAsync();
                    }
                    else
                    {
                        if (status == TransactionStatus.AlreadyExists)
                        {
                            // Hold the fresh lookup long enough to prove admission alone did not activate the vault.
                            await Expect(Page.GetByTestId("entitlement-gate")).ToHaveCountAsync(0);
                            scenario.Faults.IdentityResponseDelay = TimeSpan.Zero;
                            await Expect(Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync(new() { Timeout = 30_000 });
                            using var licenceDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                            while (scenario.Faults.Submissions.Count < admissionOffset + 3)
                                await Task.Delay(25, licenceDeadline.Token);
                            await scenario.Blocks.ProduceBlockAsync();
                            await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
                        }
                        else { await submission.WaitingAsync(); await submission.ConfirmAsync(); }
                        var active = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
                        active.Active.Should().BeTrue();
                        active.KeysMatch.Should().BeTrue();
                        active.PendingTransactionCleared.Should().BeTrue();
                        scenario.Faults.SubmittedTransactions.Count.Should().Be(submittedOffset + (raced ? 3 : 2));
                        await authentication.LockAsync();
                    }
                    await observer.VerifyAsync();
                    await Task.WhenAll(_pageReads.ToArray());
                    var lookupsObserved = _replies.Skip(offset).Where(reply => reply.Kind == "lookup").ToArray();
                    lookupsObserved.Should().Contain(reply => !reply.Found);
                    if (status != TransactionStatus.Rejected) lookupsObserved.Should().Contain(reply => reply.Found);
                    _checked.Add(status);
                }
                await authentication.RemoveAsync();
                await authentication.StorageRemovedAsync();
            }
        }
        finally
        {
            scenario.Faults.ReleaseSubmission();
            scenario.Faults.IdentityResponseDelay = TimeSpan.Zero;
            scenario.Faults.CorruptNextTransactionSignature = false;
            scenario.Context.Response -= pageObserver;
            await Task.WhenAll(_pageReads.ToArray());
        }
    }

    private async Task ObservePageLookupAsync(IResponse response)
    {
        try
        {
            using var body = JsonDocument.Parse(await response.BodyAsync());
            using var request = JsonDocument.Parse(response.Request.PostData ?? "{}");
            Observe("/api/identity", request.RootElement, body.RootElement);
        }
        catch { _replies.Enqueue(("invalid", false, "", false)); }
    }

    // Observe delivered responses only. No route fulfillment, mocked positive reply,
    // production module import, or mutation of server responses is used.
    private void Observe(string path, JsonElement request, JsonElement body)
    {
        try
        {
            var reply = body.GetProperty("reply");
            var success = reply.GetProperty("successfull").GetBoolean();
            var message = reply.GetProperty("message").GetString();
            if (path == "/api/identity")
            {
                var address = request.GetProperty("publicSigningAddress").GetString();
                var matches = scenario.Faults.IdentityLookups.Any(node => node.SigningAddress == address
                    && node.Reply.Successfull == success && node.Reply.Message == message
                    && node.Reply.ProfileName == reply.GetProperty("profileName").GetString()
                    && node.Reply.PublicSigningAddress == reply.GetProperty("publicSigningAddress").GetString()
                    && node.Reply.PublicEncryptAddress == reply.GetProperty("publicEncryptAddress").GetString()
                    && node.Reply.IsPublic == reply.GetProperty("isPublic").GetBoolean());
                _replies.Enqueue(("lookup", matches, "", success));
            }
            else
            {
                using var transaction = JsonDocument.Parse(request.GetProperty("signedTransaction").GetString()!);
                if (!transaction.RootElement.GetProperty("PayloadKind").ValueEquals("351cd60b-3fdf-48d4-b608-e93c0100f7d0")) return;
                var status = reply.GetProperty("status").GetString()!;
                var code = reply.GetProperty("validationCode").GetString();
                var matches = scenario.Faults.Submissions.Any(node => node.Successfull == success
                    && node.Message == message && WireStatus(node.Status) == status && node.ValidationCode == code);
                _replies.Enqueue(("submit", matches, status, false));
            }
        }
        catch { _replies.Enqueue(("invalid", false, "", false)); } // Never retain secret-bearing network diagnostics.
    }

    private static string WireStatus(TransactionStatus status) => status == TransactionStatus.AlreadyExists
        ? "ALREADY_EXISTS" : status.ToString().ToUpperInvariant();

    [Then("the Web mappings preserve node fields and require exact lookup and licence indexing before authenticated access")]
    public async Task ConformantAsync()
    {
        _checked.Should().Equal(TransactionStatus.Accepted, TransactionStatus.Pending, TransactionStatus.AlreadyExists, TransactionStatus.Rejected);
        _replies.Should().NotBeEmpty();
        _replies.All(reply => reply.Matches).Should().BeTrue("every observed identity response must match actual node fields");
        await HushVotingArtifactClient.CheckAsync();
    }
}
