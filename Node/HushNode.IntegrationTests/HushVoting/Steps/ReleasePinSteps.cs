// EPIC-001 -> FEAT-007 AC-007-075 / FEAT-008 AC-008-084 / FEAT-009 AC-009-088.
// Owning Phase 7 evidence tasks. A pin-integrity pass is not release admission.
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class ReleasePinSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity,
    RecoverySuccessSteps recovery, CredentialFileSteps file)
{
    private string _journey = "";
    private string _path = "";
    private string _digest = "";

    [Given("Alice completes the real (creation|recovery|import) Web journey for input pin evidence")]
    public async Task JourneyAsync(string journey)
    {
        _journey = journey;
        await HushVotingArtifactClient.RequireAsync();
        switch (journey)
        {
            case "creation": await identity.AuthenticateAsync(); break;
            case "recovery":
                await recovery.ReviewAsync(); await recovery.VerifyAsync(); await recovery.AutomaticSuccessAsync(); break;
            case "import":
                await file.BackupAsync(); await file.DecryptAsync(); await file.ImportedAsync(); await file.ProtectAsync(); break;
            default: throw new InvalidOperationException("Unknown input-pin journey.");
        }
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync();
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
    }

    [When("the exact running build and public contract inputs are recorded by content digest")]
    public async Task RecordAsync()
    {
        var snapshot = await HushVotingReleasePins.CaptureAsync(_journey);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, new JsonSerializerOptions { WriteIndented = true });
        _digest = HushVotingReleasePins.Digest(bytes);
        var output = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_OUTPUT")!;
        _path = Path.Combine(output, "web-input-pins-" + _digest + ".json");
        await using var stream = new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(bytes);
    }

    [Then("the recorded pins match the executed sources builds public corpora dependencies and Web handoff definitions")]
    public async Task VerifyAsync()
    {
        var expected = await HushVotingReleasePins.CaptureAsync(_journey);
        var bytes = await File.ReadAllBytesAsync(_path);
        HushVotingReleasePins.Matches(bytes, _digest, expected).Should().BeTrue("the stored archive must match independently re-read inputs");
        Path.GetFileName(_path).Should().Be("web-input-pins-" + _digest + ".json");
        // A changed but well-shaped digest, missing pin, mutable label or fabricated
        // release approval must fail even if someone recomputes the outer digest.
        foreach (var defect in new[] { "changed", "missing", "mutable", "approval" })
        {
            var altered = expected.DeepClone().AsObject();
            if (defect == "missing") altered["pins"]!.AsObject().Remove("frontend-build");
            else if (defect == "approval") altered["releaseAdmission"] = "PASS";
            else altered["pins"]!["frontend-build"] = defect == "mutable" ? "latest" : new string('0', 64);
            var changed = JsonSerializer.SerializeToUtf8Bytes(altered);
            HushVotingReleasePins.Matches(changed, HushVotingReleasePins.Digest(changed), expected).Should().BeFalse();
        }
        expected["releaseAdmission"]!.GetValue<string>().Should().Be("NOT_EVALUATED");
        await HushVotingArtifactClient.CheckAsync();
    }

    [Then("input pin integrity does not promote missing qualifications or assert release readiness")]
    public async Task PreserveQualificationsAsync()
    {
        var report = JsonNode.Parse(await File.ReadAllTextAsync(_path))!;
        var source = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("HUSHVOTING_E2E_OUTPUT")!, "external-qualifications.json")))!;
        JsonNode.DeepEquals(report["externalQualifications"], source["externalQualifications"]).Should().BeTrue();
        report["releaseAdmission"]!.GetValue<string>().Should().Be("NOT_EVALUATED");
    }
}
