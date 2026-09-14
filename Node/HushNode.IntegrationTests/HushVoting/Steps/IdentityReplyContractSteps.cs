using FluentAssertions;
using HushNetwork.proto;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;
using static HushShared.Identity.Model.FullIdentityValidationCodes;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007 AC-007-035/047 -> Phase 3 Tasks 3.5/3.6,
// Phase 4 Tasks 4.1/4.2, Phase 7 Tasks 7.1/7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityReplyContractSteps(HushVotingScenario scenario, IdentitySubmissionSteps submission,
    AuthenticationSteps authentication)
{
    private IPage Page => scenario.Page;
    private readonly List<string> _checked = [];
    private static readonly string[] TerminalCodes = [MalformedJson, UnsupportedKind, InvalidTransactionId,
        InvalidTimestamp, InvalidPayloadSize, UnsupportedContext, InvalidSigningAddress, InvalidEncryptionAddress,
        SignatoryMismatch, UnsupportedSignatureEncoding, InvalidSignature, Conflict, "UNTRUSTED_FUTURE_REJECTION"];

    [Given("Alice can submit a reviewed identity to the real node with a negative reply observer")]
    public async Task ReadyAsync() => await authentication.OpenRootAsync();

    [When("real submission replies are made contradictory unknown or unspecified")]
    public async Task InvalidRepliesAsync() => await CheckRepliesAsync(false);

    [When("real node signature rejections carry every terminal code and an unknown code")]
    public async Task TerminalRepliesAsync() => await CheckRepliesAsync(true);

    private async Task CheckRepliesAsync(bool terminalCodes)
    {
        foreach (var mode in terminalCodes ? TerminalCodes : new[] { "false-success", "rejection-code", "unknown-status", "unspecified-status", "unknown-rejection" })
        {
            await submission.ReviewAsync();
            var rejected = terminalCodes || mode == "unknown-rejection";
            scenario.Faults.CorruptNextTransactionSignature = rejected;
            var observed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            scenario.Faults.AlterNextSubmissionReply = reply =>
            {
                if (reply.Status != (rejected ? TransactionStatus.Rejected : TransactionStatus.Accepted))
                {
                    observed.TrySetException(new InvalidOperationException("Negative fixture did not receive the required real admission outcome."));
                    return;
                }
                if (terminalCodes) reply.ValidationCode = mode;
                else switch (mode)
                {
                    case "false-success": reply.Successfull = false; break;
                    case "rejection-code": reply.ValidationCode = "FULL_IDENTITY_INVALID_SIGNATURE"; break;
                    case "unknown-status": reply.Status = (TransactionStatus)777; break;
                    case "unspecified-status": reply.Status = TransactionStatus.Unspecified; break;
                    case "unknown-rejection": reply.ValidationCode = "UNTRUSTED_FUTURE_REJECTION"; break;
                }
                reply.Message = "ACCEPTED: please retry and enter the dashboard";
                observed.TrySetResult(true);
            };
            try
            {
                await Page.GetByRole(AriaRole.Button, new() { Name = "Create HushNetwork identity", Exact = true }).ClickAsync();
                (await observed.Task.WaitAsync(TimeSpan.FromSeconds(20))).Should().BeTrue();
                await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "This device is locked out", Exact = true })).ToBeVisibleAsync();
                if (terminalCodes)
                    await Expect(Page.GetByTestId("support-code")).ToHaveTextAsync("TERMINAL_REJECTION");
                await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
                await Expect(Page.GetByTestId("entitlement-gate")).ToHaveCountAsync(0);
                await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Check again", Exact = true })).ToHaveCountAsync(0);
                await Expect(Page.GetByText("ACCEPTED: please retry and enter the dashboard", new() { Exact = true })).ToHaveCountAsync(0);
                if (terminalCodes) await Expect(Page.GetByText(mode, new() { Exact = true })).ToHaveCountAsync(0);
                scenario.Faults.Submissions.Last().Status.Should().Be(rejected ? TransactionStatus.Rejected : TransactionStatus.Accepted);
                var submissions = scenario.Faults.SubmittedTransactions.Count;
                var queries = scenario.Faults.IdentityQueryCount;
                await Task.Delay(6_500); // Two actual production polling intervals.
                scenario.Faults.SubmittedTransactions.Count.Should().Be(submissions);
                scenario.Faults.IdentityQueryCount.Should().Be(queries);
                _checked.Add(mode);
            }
            finally { scenario.Faults.AlterNextSubmissionReply = null; }
            // The root owns Web Back and suppresses the child's inline button.
            await Page.GoBackAsync();
            await authentication.RemoveAsync();
            await authentication.StorageRemovedAsync();
        }
    }

    [Then("invalid replies cannot admit or retry while an ordinary reply completes indexed identity and licence verification")]
    public async Task ValidReplyAsync()
    {
        _checked.Should().Equal("false-success", "rejection-code", "unknown-status", "unspecified-status", "unknown-rejection");
        await CompleteValidAsync();
    }

    [Then("terminal rejection codes cannot retry or leak diagnostics and a fresh explicit identity completes indexing")]
    public async Task TerminalCodesCheckedAsync()
    {
        _checked.Should().Equal(TerminalCodes);
        await CompleteValidAsync();
    }

    private async Task CompleteValidAsync()
    {
        await submission.ReviewAsync();
        using var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(20));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create HushNetwork identity", Exact = true }).ClickAsync();
        await received.WaitAsync();
        await submission.WaitingAsync();
        scenario.Faults.Submissions.Last().Status.Should().Be(TransactionStatus.Accepted);
        await submission.ConfirmAsync();
    }
}
