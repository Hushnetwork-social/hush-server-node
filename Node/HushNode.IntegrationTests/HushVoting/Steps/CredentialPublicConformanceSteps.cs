// EPIC-001 -> FEAT-009 AC-009-073 -> Phase 6 Tasks 6.9/6.10,
// Phase 7 Tasks 7.1/7.2. Public synthetic corpus only; not external qualification.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using Olimpo.KeyDerivation;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialPublicConformanceSteps(HushVotingScenario scenario, CredentialFileSteps file)
{
    private DerivedKeys _keys = null!;
    private byte[] _envelope = [];
    private string _password = "";

    [Given("both real compatibility runtimes execute the complete public v1 corpus with identical expected outcomes")]
    public async Task ConformanceAsync()
    {
        var client = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_CLIENT_ROOT")
            ?? throw new InvalidOperationException("Use the owned HushVoting runner.");
        var corpus = Path.Combine(client, "conformance", "identity", "v1");
        var vectorPath = Path.Combine(corpus, "vectors", "dat-vectors.json");
        var manifestPath = Path.Combine(corpus, "manifest.json");
        var vectorDigest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(vectorPath))).ToLowerInvariant();
        var manifestDigest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(manifestPath))).ToLowerInvariant();
        using (var vectors = JsonDocument.Parse(await File.ReadAllTextAsync(vectorPath)))
        {
            vectors.RootElement.GetProperty("vectors").GetArrayLength().Should().Be(15);
            var positive = vectors.RootElement.GetProperty("vectors").EnumerateArray().Single(v => v.GetProperty("id").GetString() == "D-001");
            positive.GetProperty("expected").GetString().Should().Be("OK");
            _envelope = Convert.FromHexString(positive.GetProperty("envelopeHex").GetString()!);
            _password = positive.GetProperty("password").GetString()!;
            var payloadJson = positive.GetProperty("expectedPayloadJson").GetString()!;
            using var payload = JsonDocument.Parse(payloadJson);
            string Field(string name) => payload.RootElement.GetProperty(name).GetString()!;
            _keys = new DerivedKeys(Field("PublicSigningAddress"), Field("PrivateSigningKey"), Field("PublicEncryptAddress"), Field("PrivateEncryptKey"));
            await HushVotingArtifactClient.RegisterAsync(_keys.SigningPrivateKey, _keys.EncryptPrivateKey,
                Field("Mnemonic"), _password, payloadJson, Convert.ToHexString(_envelope).ToLowerInvariant(), Convert.ToBase64String(_envelope));
        }

        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = client, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        HushVotingCorpusInputs.RemoveFromChildEnvironment(start.Environment);
        start.ArgumentList.Add(Path.Combine(client, "scripts", "credential-file-restore", "conformance.mjs"));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Public conformance did not start.");
        // The existing command executes real implementations and owns bounded
        // child stages. Do not copy child diagnostics into browser-test artifacts.
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(210));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            if (process.ExitCode != 0) throw new InvalidOperationException("Public compatibility conformance failed; inspect its aggregate report.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }

        var reports = Path.Combine(client, "conformance", "reports", "credential-file-restore");
        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(reports, "summary.json")));
        var result = summary.RootElement;
        result.GetProperty("result").GetString().Should().Be("PASS");
        result.GetProperty("publicDatVectors").GetInt32().Should().Be(15);
        result.GetProperty("fullCorpusChecksPerRuntime").GetInt32().Should().Be(115);
        result.GetProperty("manifestDigest").GetString().Should().Be(manifestDigest);
        result.GetProperty("vectorDigest").GetString().Should().Be(vectorDigest);
        foreach (var runtime in new[] { "typescript", "dotnet" })
        {
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(reports, runtime + ".json")));
            var root = report.RootElement;
            root.GetProperty("runtime").GetString().Should().Be(runtime);
            root.GetProperty("result").GetString().Should().Be("PASS");
            root.GetProperty("contractVersion").GetString().Should().Be("1.0.0");
            root.GetProperty("summary").GetProperty("total").GetInt32().Should().Be(115);
            root.GetProperty("summary").GetProperty("passed").GetInt32().Should().Be(115);
            root.GetProperty("summary").GetProperty("failed").GetInt32().Should().Be(0);
            root.GetProperty("records").GetArrayLength().Should().Be(0);
        }
        var output = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_OUTPUT")
            ?? throw new InvalidOperationException("Owned conformance evidence directory is missing.");
        foreach (var name in new[] { "summary", "typescript", "dotnet" })
            File.Copy(Path.Combine(reports, name + ".json"), Path.Combine(output, "public-conformance-" + name + ".json"), overwrite: false);
    }

    [When("Alice imports the unchanged public positive vector through the real browser picker and live identity lookup")]
    public async Task ImportAsync()
    {
        await HushVotingServerIdentity.RegisterAsync(scenario, _keys, HushVotingIdentityJourney.Alias);
        await scenario.Page.GotoAsync("/");
        await file.OpenAsync();
        await file.ChooseAsync(_envelope, "public-v1.dat");
        await HushVotingIdentityJourney.FillSecretAsync(scenario.Page.GetByTestId("backup-password-input"), _password);
        await scenario.Page.GetByTestId("submit-password").ClickAsync();
        await file.ImportedAsync();
    }

    [Then("the same public-vector keys gain access only after separate device protection and real licence indexing")]
    public async Task VerifiedAsync()
    {
        try
        {
            await file.ProtectAsync();
            (await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(scenario.Page, _keys, HushVotingIdentityJourney.Alias, false)).Should().BeTrue();
            await Expect(scenario.Page.GetByTestId("backup-preservation-notice")).ToContainTextAsync("HushVoting did not retain any recovery words");
            scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        }
        finally { CryptographicOperations.ZeroMemory(_envelope); _envelope = []; _password = ""; }
    }
}
