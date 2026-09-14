using System.Text;
using System.Text.Json;

namespace HushVoting.IntegrationTests.Infrastructure;

// Test-runner inputs only. No production application receives these variables.
internal sealed class HushVotingCorpusInputs : IDisposable
{
    internal static readonly string[] Names = ["HUSH_TEST_KEYS_DIR", "HUSH_TEST_DAT_PASSWORD", "HUSH_TEST_CORPUS_INVENTORY"];
    private string _directory;
    private string _inventory;
    private readonly Dictionary<string, bool> _expectedMnemonic = new(StringComparer.Ordinal);
    public string Password { get; private set; }

    internal HushVotingCorpusInputs(string directory, string password, string inventory)
        => (_directory, Password, _inventory) = (directory, password, inventory);

    public static HushVotingCorpusInputs? CaptureEnvironment()
    {
        try
        {
            if (Environment.GetEnvironmentVariable("HUSHVOTING_CONTROLLED_CORPUS") != "1") return null;
            var directory = Environment.GetEnvironmentVariable(Names[0]);
            var password = Environment.GetEnvironmentVariable(Names[1]);
            var inventory = Environment.GetEnvironmentVariable(Names[2]);
            if (string.IsNullOrEmpty(directory) || password is null || string.IsNullOrEmpty(inventory))
                throw new InvalidOperationException("Controlled corpus runtime inputs are missing; no source was opened.");
            // Short scan values cannot be distinguished from ordinary report text by the current guard.
            if (password.Length < 4 || Encoding.UTF8.GetByteCount(password) > 4096)
                throw new InvalidOperationException("Controlled corpus password is outside the harness scan boundary; no source was opened.");
            RequireLocalOperatorEnvironment();
            return new(directory, password, inventory);
        }
        finally
        {
            foreach (var name in Names) Environment.SetEnvironmentVariable(name, null);
            Environment.SetEnvironmentVariable("HUSHVOTING_CONTROLLED_CORPUS", null);
        }
    }

    public static void RemoveFromChildEnvironment(IDictionary<string, string?> environment)
    {
        foreach (var name in Names) environment.Remove(name);
        environment.Remove("HUSHVOTING_CONTROLLED_CORPUS");
    }

    internal static void RequireLocalOperatorEnvironment()
    {
        if (!IsLocalOperatorEnvironment(OperatingSystem.IsLinux(), Environment.GetEnvironmentVariable))
            throw new InvalidOperationException("Controlled corpus requires the local operator environment; no source was opened.");
    }

    internal static bool IsLocalOperatorEnvironment(bool linux, Func<string, string?> read)
        => linux && !new[] { "CI", "GITHUB_ACTIONS", "TF_BUILD", "GITLAB_CI", "CODESPACES", "CLOUD_SHELL" }
                .Any(name => !string.IsNullOrEmpty(read(name)))
            && (read("DOCKER_HOST") is not { Length: > 0 } host || host == "unix:///var/run/docker.sock")
            && (read("DOCKER_CONTEXT") is not { Length: > 0 } context || context == "default");

    public async Task<IReadOnlyList<string>> ReadInventoryAsync(bool representativesOnly)
    {
        await HushVotingArtifactClient.RequireAsync();
        await HushVotingCorpusEnvelope.RegisterChunksAsync(Password);
        await HushVotingCorpusEnvelope.RegisterChunksAsync(_directory);
        await HushVotingCorpusEnvelope.RegisterChunksAsync(_inventory);
        try
        {
            var client = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_CLIENT_ROOT") ?? throw new InvalidOperationException();
            var workspace = Path.GetDirectoryName(Path.GetFullPath(client))!;
            bool InWorkspace(string path) => Path.GetFullPath(path) == workspace
                || Path.GetFullPath(path).StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            if (InWorkspace(_directory) || InWorkspace(_inventory)) throw new InvalidOperationException();
            // Inventory stays external and secret; its bytes and entries never enter evidence.
            using var inventory = (await HushVotingCorpusSource.OpenAsync(_inventory)).Source
                ?? throw new InvalidOperationException();
            using var json = JsonDocument.Parse(inventory.Snapshot);
            var root = json.RootElement;
            if (root.GetProperty("schema").GetString() != "hushvoting-controlled-corpus-inventory-v1") throw new InvalidOperationException();
            var entries = root.GetProperty("files").EnumerateArray().Take(33).ToArray();
            if (entries.Length is < 1 or > 32) throw new InvalidOperationException();
            var selected = new List<string>(); var all = new HashSet<string>(StringComparer.Ordinal);
            var classes = new HashSet<string>(StringComparer.Ordinal); var represented = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                var name = entry.GetProperty("file").GetString()!;
                if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name || !name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                    || name.Contains('\\') || !all.Add(name)) throw new InvalidOperationException();
                var producer = entry.GetProperty("producer").GetString();
                var shape = entry.GetProperty("mnemonic").GetString();
                if (producer is not ("P-01" or "P-02") || shape is not ("present" or "absent")) throw new InvalidOperationException();
                var qualificationClass = producer + "/" + shape;
                classes.Add(qualificationClass);
                var representative = entry.GetProperty("representative").GetBoolean();
                if (representative) represented.Add(qualificationClass);
                var path = Path.Combine(_directory, name);
                _expectedMnemonic[path] = shape == "present";
                await HushVotingCorpusEnvelope.RegisterChunksAsync(name);
                await HushVotingCorpusEnvelope.RegisterChunksAsync(path);
                if (!representativesOnly || representative) selected.Add(path);
            }
            var directoryEntries = Directory.EnumerateFileSystemEntries(_directory).Take(129).ToArray();
            if (directoryEntries.Length > 128) throw new InvalidOperationException();
            var available = directoryEntries
                .Where(path => path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
            if (!all.SetEquals(available!) || !classes.SetEquals(represented) || selected.Count == 0
                || await inventory.CheckUnchangedAsync() != CorpusSourceFailure.None) throw new InvalidOperationException();
            return selected;
        }
        catch { throw new InvalidOperationException("Controlled corpus inventory is missing, changed, unsupported or incomplete; source diagnostics omitted."); }
    }

    public bool MatchesApprovedShape(string path, HushVotingCorpusEnvelope oracle)
        => _expectedMnemonic.TryGetValue(path, out var expected) && expected == oracle.HasMnemonic;

    public void Dispose() { Password = ""; _directory = ""; _inventory = ""; _expectedMnemonic.Clear(); }
}
