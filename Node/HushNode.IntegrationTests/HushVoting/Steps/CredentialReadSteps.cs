using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialReadSteps(HushVotingScenario scenario, CredentialFileSteps file)
{
    private IPage Page => scenario.Page;
    private const string Password = "bounded-source-backup";
    private byte[] _backup = [];
    private readonly HashSet<string> _checked = [];

    [Given("Alice selects a real encrypted backup through a browser source with controlled read faults")]
    public async Task SourceAsync()
    {
        _backup = HushVotingCredentialFile.Create(HushVotingTestIdentity.DeriveP01(string.Join(" ", Enumerable.Repeat("abandon", 23).Append("art"))), "Read recovery", Password);
        // Negative source faults only: bytes come from the native Blob stream.
        // This does not replace worker operations, BFF replies or UI state.
        await Page.AddInitScriptAsync("""
            (() => {
                const native = Blob.prototype.stream;
                const fault = window.__hushVotingReadFault = {mode: 'pass', cancelled: 0, size: 0};
                Blob.prototype.stream = function() {
                    const stream = native.call(this);
                    if (this.size !== fault.size || fault.mode === 'pass') return stream;
                    const reader = stream.getReader();
                    const mode = fault.mode;
                    let delivered = false;
                    let release;
                    return new ReadableStream({
                        async pull(controller) {
                            if (delivered) return new Promise(resolve => { release = resolve; });
                            delivered = true;
                            const chunk = await reader.read();
                            if (chunk.done) { controller.close(); return; }
                            controller.enqueue(chunk.value.slice(0, 16));
                            chunk.value.fill(0);
                            if (mode === 'partial') { controller.close(); await reader.cancel(); }
                        },
                        cancel() { fault.cancelled++; release?.(); return reader.cancel(); }
                    });
                };
            })();
            """);
        await Page.GotoAsync("/");
        await Page.EvaluateAsync("size => window.__hushVotingReadFault.size = size", _backup.Length);
        await file.OpenAsync();
    }

    private async Task ChooseAsync(byte[] bytes)
    {
        try { await Page.GetByTestId("credential-file-input").SetInputFilesAsync(new FilePayload { Name = "source.dat", MimeType = "application/octet-stream", Buffer = bytes }); }
        catch (Exception error) when (error is PlaywrightException or TimeoutException) { throw new InvalidOperationException("Controlled credential-source selection failed; fixture diagnostics omitted."); }
    }

    private async Task ModeAsync(string mode) => await Page.EvaluateAsync("mode => window.__hushVotingReadFault.mode = mode", mode);

    private async Task NoImportAsync()
    {
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("restore-device-password")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        scenario.Faults.IdentityQueryCount.Should().Be(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [When("the browser encounters oversized, cancelled, partial, and stalled credential reads")]
    public async Task FaultsAsync()
    {
        await ChooseAsync(new byte[1_048_577]);
        await Expect(Page.GetByTestId("restore-panel").GetByRole(AriaRole.Alert)).ToHaveTextAsync("This credential backup exceeds the 1 MiB size limit.");
        await NoImportAsync();
        _checked.Add("oversize");

        await ModeAsync("stall");
        await ChooseAsync(_backup);
        await Expect(Page.GetByTestId("restore-panel").GetByRole(AriaRole.Status)).ToHaveTextAsync("Reading credential file…");
        await NoImportAsync();
        await Page.GetByTestId("cancel-read").ClickAsync();
        await Expect(Page.GetByTestId("choose-file")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("restore-panel").GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
        (await Page.EvaluateAsync<int>("window.__hushVotingReadFault.cancelled")).Should().Be(1);
        await NoImportAsync();
        _checked.Add("cancel");

        await ModeAsync("partial");
        await ChooseAsync(_backup);
        await Expect(Page.GetByTestId("restore-panel").GetByRole(AriaRole.Alert)).ToHaveTextAsync("The credential file could not be read completely. Choose the file again.");
        await NoImportAsync();
        _checked.Add("partial");

        await ModeAsync("stall");
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        await ChooseAsync(_backup);
        await Expect(Page.GetByTestId("restore-panel").GetByRole(AriaRole.Status)).ToHaveTextAsync("Reading credential file…");
        await Expect(Page.GetByTestId("restore-panel").GetByRole(AriaRole.Alert)).ToHaveTextAsync("Reading the credential file timed out. Choose the file again.", new() { Timeout = 34_000 });
        elapsed.Stop();
        elapsed.Elapsed.TotalSeconds.Should().BeInRange(29.5, 34);
        (await Page.EvaluateAsync<int>("window.__hushVotingReadFault.cancelled")).Should().Be(2);
        await NoImportAsync();
        _checked.Add("timeout");
    }

    [Then("each failed read releases its source without import and a fresh complete read reaches the live node")]
    public async Task FreshSourceAsync()
    {
        _checked.Should().BeEquivalentTo(new[] { "oversize", "cancel", "partial", "timeout" });
        await ModeAsync("pass");
        await ChooseAsync(_backup);
        await Expect(Page.GetByTestId("backup-password-input")).ToBeVisibleAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("backup-password-input"), Password);
        await Page.GetByTestId("submit-password").ClickAsync();
        await Expect(Page.GetByTestId("create-identity")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.IdentityLookups.Count.Should().Be(1);
        scenario.Faults.IdentityLookups.Single().Reply.Successfull.Should().BeFalse();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }
}
