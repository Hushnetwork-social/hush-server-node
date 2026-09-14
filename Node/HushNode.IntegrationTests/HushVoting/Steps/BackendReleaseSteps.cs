// EPIC-001 -> FEAT-007 AC-076 / FEAT-008 AC-083 / FEAT-009 AC-087.
// Phase 7 Tasks 7.1/7.2; FEAT-011 Phase 3 Tasks 3.1-3.8 and Tasks 7.M1-7.M3.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class BackendReleaseSteps(HushVotingScenario scenario, ScenarioContext context,
    HushVotingIdentityJourney identity, RecoveryRecreateSteps recovery, CredentialProfileSteps file)
{
    private string _evidence = "";
    private string _digest = "";
    private string _journey = "";

    [Given("the complete HushServerNode signature binding admission status and TwinTest release matrix has verified current-build evidence")]
    public async Task PrerequisiteAsync()
    {
        await HushVotingArtifactClient.RequireAsync();
        _evidence = Environment.GetEnvironmentVariable("HUSHVOTING_BACKEND_RELEASE_EVIDENCE") ?? "";
        if (!File.Exists(_evidence))
            throw new InvalidOperationException("Run test:backend-release:bdd to supply the complete backend prerequisite.");
        _digest = await DigestAsync();
        await VerifyEvidenceAsync();
    }

    [When("the real Web missing-profile flow submits its exact signed identity and waits for indexed confirmation")]
    public async Task JourneyAsync()
    {
        var tags = context.ScenarioInfo.Tags;
        if (tags.Contains("HV-ID-CREATE-SECURITY-008"))
        {
            _journey = "creation";
            await identity.AuthenticateAsync();
        }
        else if (tags.Contains("HV-RW-SECURITY-012"))
        {
            _journey = "recovery";
            var words = string.Join(' ', Enumerable.Repeat("abandon", 23).Append("art"));
            var web = HushVotingTestIdentity.DeriveP01(words);
            var historical = Olimpo.KeyDerivation.DeterministicKeyGenerator.DeriveKeys(words);
            await HushVotingArtifactClient.RegisterAsync(words, HushVotingScenario.DevicePassword,
                web.SigningPrivateKey, web.EncryptPrivateKey, historical.SigningPrivateKey, historical.EncryptPrivateKey);
            await recovery.AbsentAsync();
            await recovery.SelectAsync();
            await recovery.ReviewAsync();
            await recovery.AutomaticRegistrationAsync();
        }
        else if (tags.Contains("HV-DAT-SECURITY-AC087"))
        {
            _journey = "import";
            await file.MissingAsync();
            await file.ReviewAsync();
            await file.ConsentAsync();
            await file.RegisterAsync();
        }
        else throw new InvalidOperationException("Unknown backend release-criterion consumer.");
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync();
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2, "one identity and its separate indexed licence are required");
        scenario.Faults.RequestMethods.Should().NotContain(method => method.Contains("Feed", StringComparison.OrdinalIgnoreCase));
    }

    [Then("the complete backend prerequisite and this indexed Web journey share the executed build without approving other release gates")]
    public async Task ConfirmAsync()
    {
        (await DigestAsync()).Should().Be(_digest, "prerequisite evidence must remain unchanged during the Web journey");
        await VerifyEvidenceAsync();
        var output = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_OUTPUT")!;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "hushvoting-backend-release-consumer-v1", journey = _journey,
            backendPrerequisiteDigest = _digest, backendPrerequisite = "PASS",
            indexedWebJourney = "PASS", wholeProductRelease = "NOT_EVALUATED"
        });
        await File.WriteAllBytesAsync(Path.Combine(output, "backend-release-" + _journey + ".json"), bytes);
        await HushVotingArtifactClient.CheckAsync();
    }

    private async Task<string> DigestAsync() => Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(_evidence))).ToLowerInvariant();

    private async Task VerifyEvidenceAsync()
    {
        var client = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_CLIENT_ROOT")!;
        var output = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_OUTPUT")!;
        var selection = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_SELECTION")!;
        var start = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { Path.GetFullPath(Path.Combine(client, "..", "hush-server-node/scripts/hushvoting-backend-release.py")),
                     "verify", "--evidence", _evidence, "--provenance", Path.Combine(output, selection + ".provenance.json") })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Backend evidence verifier could not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0) throw new InvalidOperationException("Complete current-build backend prerequisite verification failed.");
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }
}
