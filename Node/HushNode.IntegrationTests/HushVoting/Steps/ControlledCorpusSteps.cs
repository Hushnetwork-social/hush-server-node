using System.Security.Cryptography;
using System.Text.Json;
using HushVoting.IntegrationTests.Hooks;
using HushVoting.IntegrationTests.Infrastructure;
using Olimpo.KeyDerivation;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.Steps;

// FEAT-009 AC-009-074–077, Phase 6 Tasks 6.9/6.10 and Phase 7 Tasks 7.1/7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class ControlledCorpusSteps(HushVotingScenario scenario, ScenarioContext context)
{
    private const string PublicPassword = "public-corpus-harness-password";
    private string? _publicDirectory;
    private IReadOnlyList<string> _paths = [];
    private string _password = "";
    private CorpusAggregate? _result;
    private Func<string, HushVotingCorpusEnvelope, bool>? _validateShape;
    private HushVotingCorpusInputs? _publicInputs;
    private string _inputKind = "controlled";

    [Given("the Web corpus uses generated test fixtures unless explicit local inputs are supplied")]
    public async Task ControlledAsync()
    {
        var inputs = HushVotingHooks.CorpusInputs;
        if (inputs is null)
        {
            if (!context.ScenarioInfo.Tags.Contains("HV-GENERATED-CORPUS"))
                throw new InvalidOperationException("Generated corpus requires its owned scenario category.");
            _inputKind = "generated-test-fixtures";
            await GeneratePublicAsync();
            return;
        }
        await new HushVotingCorpusJourney(scenario).PrepareAsync();
        _paths = await inputs.ReadInventoryAsync(context.ScenarioInfo.Tags.Contains("HV-DAT-EXTERNAL-AC075"));
        _password = inputs.Password;
        _validateShape = inputs.MatchesApprovedShape;
    }

    [When("every selected controlled source completes the real Web import and owned-node activation")]
    public async Task RunAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        _result = await new HushVotingCorpusJourney(scenario).RunAsync(_paths, _password, deadline.Token, validateShape: _validateShape);
    }

    [When("an unowned fixture is refused before source opening and the owned fixture completes controlled import")]
    public async Task RefuseThenRunAsync()
    {
        var refused = await new HushVotingCorpusJourney(new HushVotingScenario()).RunAsync(_paths, _password);
        if (refused.Attempted != 0 || refused.Imported != 0 || refused.Failed != _paths.Count)
            throw new InvalidOperationException("Unowned corpus fixture was not refused before source opening.");
        await RunAsync();
    }

    [Then("the controlled corpus has complete passing aggregate evidence and every source remains unchanged")]
    public async Task CompleteAsync()
    {
        if (_result is not null) await WriteAggregateAsync(_inputKind, _result);
        if (_result is not { Available: > 0, Failed: 0, Cancelled: false } result
            || result.Attempted != result.Available || result.Imported != result.Available || result.Unchanged != result.Available)
            throw new InvalidOperationException("Controlled corpus execution is incomplete or failed; per-source diagnostics are intentionally unavailable.");
        if (_inputKind == "generated-test-fixtures") await PreserveGeneratedArtifactsAsync();
    }

    [Given("four synthetic public credential sources exercise both approved producers and mnemonic shapes")]
    public async Task PublicAsync()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The corpus harness requires Linux.");
        if (!context.ScenarioInfo.Tags.Any(tag => tag is "HV-CORPUS-CHECK-POSITIVE" or "HV-CORPUS-CHECK-CHANGE" or "HV-CORPUS-CHECK-REFUSAL"))
            throw new InvalidOperationException("Public corpus self-tests cannot replace an original controlled-corpus scenario.");
        await GeneratePublicAsync();
    }

    private async Task GeneratePublicAsync()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The corpus harness requires Linux.");
        _password = PublicPassword;
        _publicDirectory = Path.Combine(Path.GetTempPath(), "hv-corpus-public-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_publicDirectory);
        File.SetUnixFileMode(_publicDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var paths = new List<string>();
        foreach (var producer in new[] { "P-01", "P-02" })
        {
            var words = producer == "P-01" ? string.Join(' ', Enumerable.Repeat("abandon", 11).Append("about"))
                : string.Join(' ', Enumerable.Repeat("abandon", 23).Append("art"));
            var keys = producer == "P-01" ? HushVotingTestIdentity.DeriveP01(words) : DeterministicKeyGenerator.DeriveKeys(words);
            await HushVotingArtifactClient.RegisterAsync(PublicPassword, words, keys.SigningPrivateKey, keys.EncryptPrivateKey);
            foreach (var withWords in new[] { false, true })
            {
                var path = Path.Combine(_publicDirectory, Guid.NewGuid().ToString("N") + ".dat");
                var bytes = HushVotingCredentialFile.Create(keys, "Public corpus fixture", _password, mnemonic: withWords ? words : null);
                try { await File.WriteAllBytesAsync(path, bytes); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                paths.Add(path);
            }
        }
        _paths = paths;
        var inventoryPath = Path.Combine(_publicDirectory, "public-inventory.json");
        await File.WriteAllTextAsync(inventoryPath, JsonSerializer.Serialize(new
        {
            schema = "hushvoting-controlled-corpus-inventory-v1",
            files = paths.Select((path, index) => new { file = Path.GetFileName(path), producer = index < 2 ? "P-01" : "P-02", mnemonic = index % 2 == 0 ? "absent" : "present", representative = true })
        }));
        _publicInputs = new(_publicDirectory, _password, inventoryPath);
        await new HushVotingCorpusJourney(scenario).PrepareAsync();
        var representatives = await _publicInputs.ReadInventoryAsync(true);
        _paths = await _publicInputs.ReadInventoryAsync(false);
        if (representatives.Count != 4 || !_paths.ToHashSet().SetEquals(paths))
            throw new InvalidOperationException("Public inventory did not cover both producers and mnemonic shapes.");
        _validateShape = _publicInputs.MatchesApprovedShape;
    }

    private async Task PreserveGeneratedArtifactsAsync()
    {
        // User-approved public test inputs, separate from protected runtime evidence.
        // Never copy an operator-supplied corpus or emit its credentials/metadata.
        if (_publicDirectory is null || _publicInputs is null || _paths.Count != 4)
            throw new InvalidOperationException("Only the four owned generated fixtures may be retained.");
        var output = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_OUTPUT")
            ?? throw new InvalidOperationException("Owned output is missing.");
        var run = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_RUN_ID")
            ?? throw new InvalidOperationException("Owned run identifier is missing.");
        var id = context.ScenarioInfo.Tags.Single(tag => tag.StartsWith("HV-DAT-EXTERNAL-AC", StringComparison.Ordinal));
        var artifacts = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "generated-corpus", run, id);
        Directory.CreateDirectory(artifacts);
        foreach (var path in _paths) File.Copy(path, Path.Combine(artifacts, Path.GetFileName(path)), overwrite: false);
        File.Copy(Path.Combine(_publicDirectory, "public-inventory.json"), Path.Combine(artifacts, "inventory.json"), overwrite: false);
        await File.WriteAllTextAsync(Path.Combine(artifacts, "README.txt"),
            "Generated public HUSH v1 test fixtures; never use these identities or this password outside tests.\n"
            + "Password: " + PublicPassword + "\n"
            + "Two approved producer algorithms (P-01/P-02), each with and without mnemonic.\n"
            + "These are generated inputs, not historical user backups or historical-producer qualification.\n");
    }

    [Then("all four public sources pass without being reported as controlled-corpus qualification")]
    public async Task PublicCompleteAsync()
    {
        if (_result is not { Available: 4, Attempted: 4, Imported: 4, Unchanged: 4, Failed: 0, Cancelled: false })
            throw new InvalidOperationException("Public corpus harness positive test failed.");
        await WriteAggregateAsync("public-self-test", _result);
    }

    [When("only the owned public source is changed after its real browser import")]
    public async Task MutationAsync()
    {
        if (_publicDirectory is null) throw new InvalidOperationException("Mutation requires an owned public fixture.");
        var path = _paths[0];
        _result = await new HushVotingCorpusJourney(scenario).RunAsync([path], _password,
            publicMutationProbe: () => File.AppendAllTextAsync(path, "public-mutation"));
    }

    [Then("the harness detects the changed source without emitting identifying records")]
    public async Task MutationDetectedAsync()
    {
        if (_result is not { Available: 1, Attempted: 1, Imported: 1, Unchanged: 0, Failed: 1, Cancelled: false })
            throw new InvalidOperationException("Public corpus source-change diagnostic failed.");
        await WriteAggregateAsync("public-self-test", _result);
    }

    [When("an unowned fixture and a cancelled run attempt to open the public corpus")]
    public async Task RefusalAsync()
    {
        var refused = await new HushVotingCorpusJourney(new HushVotingScenario()).RunAsync(_paths, _password);
        if (refused is not { Attempted: 0, Imported: 0, Unchanged: 0, Failed: 4 })
            throw new InvalidOperationException("Unowned fixture opened a corpus source.");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _result = await new HushVotingCorpusJourney(scenario).RunAsync(_paths, _password, cancellation.Token);
    }

    [Then("neither attempt opens a source or produces a passing corpus claim")]
    public async Task RefusedAsync()
    {
        if (_result is not { Available: 4, Attempted: 0, Imported: 0, Unchanged: 0, Cancelled: true })
            throw new InvalidOperationException("Cancelled corpus execution opened a source.");
        await WriteAggregateAsync("public-self-test", _result);
    }

    private async Task WriteAggregateAsync(string inputs, CorpusAggregate result)
    {
        var output = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_OUTPUT")
            ?? throw new InvalidOperationException("Owned aggregate output is missing.");
        // Names, paths, classes, order, per-file outcomes and digests are never report fields.
        var id = context.ScenarioInfo.Tags.Single(tag => tag.StartsWith("HV-CORPUS-CHECK-", StringComparison.Ordinal)
            || tag.StartsWith("HV-DAT-EXTERNAL-AC", StringComparison.Ordinal));
        await File.WriteAllTextAsync(Path.Combine(output, id + ".corpus.json"), JsonSerializer.Serialize(new
        {
            schema = "hushvoting-corpus-aggregate-v1", inputs,
            available = result.Available, attempted = result.Attempted, imported = result.Imported,
            unchanged = result.Unchanged, failed = result.Failed, cancelled = result.Cancelled
        }));
        await HushVotingArtifactClient.CheckAsync();
    }

    [AfterScenario(Order = 0)]
    public void Cleanup()
    {
        _password = ""; _paths = []; _validateShape = null;
        _publicInputs?.Dispose(); _publicInputs = null;
        if (_publicDirectory is not null && Directory.Exists(_publicDirectory)) Directory.Delete(_publicDirectory, true);
        _publicDirectory = null;
    }
}
