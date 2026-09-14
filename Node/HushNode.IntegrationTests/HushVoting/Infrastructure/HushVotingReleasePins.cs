// EPIC-001 -> FEAT-007 AC-007-075 / FEAT-008 AC-008-084 / FEAT-009 AC-009-088.
// Phase 7 evidence tasks: exact input pins are distinct from release admission.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HushVoting.IntegrationTests.Infrastructure;

internal static class HushVotingReleasePins
{
    internal static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string HashFile(string path) => Digest(File.ReadAllBytes(path));

    internal static async Task<JsonObject> CaptureAsync(string journey)
    {
        if (journey is not ("creation" or "recovery" or "import")) throw new InvalidOperationException("Unknown release journey.");
        var client = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_CLIENT_ROOT")!;
        var output = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_OUTPUT")!;
        var selection = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_SELECTION")!;
        if (string.IsNullOrEmpty(client) || string.IsNullOrEmpty(output) || string.IsNullOrEmpty(selection)
            || selection.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new InvalidOperationException("Release evidence requires the owned runner.");
        var server = Path.GetFullPath(Path.Combine(client, "..", "hush-server-node"));
        var provenancePath = Path.Combine(output, selection + ".provenance.json");
        var provenance = JsonNode.Parse(await File.ReadAllTextAsync(provenancePath))!.AsObject();
        var pins = new JsonObject
        {
            ["client-source-tree"] = provenance["sources"]!["client"]!.GetValue<string>(),
            ["server-source-tree"] = provenance["sources"]!["server"]!.GetValue<string>(),
            ["frontend-build"] = provenance["artifacts"]!["frontend"]!.GetValue<string>(),
            ["dotnet-build"] = provenance["artifacts"]!["dotnet"]!.GetValue<string>(),
            ["runner-provenance"] = HashFile(provenancePath),
            ["frontend-dependency-lock"] = HashFile(Path.Combine(client, "package-lock.json")),
            ["server-package-versions"] = HashFile(Path.Combine(server, "Node", "Directory.Packages.props")),
            ["server-resolved-runtime-dependencies"] = HashFile(Path.Combine(AppContext.BaseDirectory, "HushNode.IntegrationTests.deps.json")),
            ["running-identity-assembly"] = HashFile(typeof(HushNode.Identity.FullIdentityValidator).Assembly.Location),
            ["identity-protocol"] = HashFile(Path.Combine(server, "Protos", "hushIdentity.proto")),
            ["transaction-protocol"] = HashFile(Path.Combine(server, "Protos", "hushBlockchain.proto")),
            ["licence-protocol"] = HashFile(Path.Combine(server, "Protos", "hushVotingLicence.proto")),
            ["web-adapter-protocol"] = HashFile(Path.Combine(client, "src/lib/browser-vault/contracts/protocol.ts")),
            ["web-adapter-handoff-v1"] = HashFile(Path.Combine(client, "conformance/browser-vault/v1/HANDOFF.md")),
            ["recovery-handoff-definition"] = HashFile(Path.Combine(client, "src/lib/recovery-words/integration/handoff.ts")),
            ["import-handoff-definition"] = HashFile(Path.Combine(client, "src/lib/credential-file-restore/integration/handoff.ts")),
            ["recovery-contracts"] = HashTree(Path.Combine(client, "src/lib/recovery-words/contracts")),
            ["import-contracts"] = HashTree(Path.Combine(client, "src/lib/credential-file-restore/contracts")),
            ["external-qualification-ledger"] = HashFile(Path.Combine(output, "external-qualifications.json"))
        };
        var versions = new JsonObject();
        foreach (var name in new[] { "identity", "vault" })
        {
            var corpus = Path.Combine(client, "conformance", name, "v1");
            VerifyCorpus(corpus);
            pins[name + "-corpus-manifest"] = HashFile(Path.Combine(corpus, "manifest.json"));
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(corpus, "manifest.json")))!;
            versions[name] = manifest["contractVersion"]!.GetValue<string>();
        }
        return new JsonObject
        {
            ["schema"] = "hushvoting-web-input-pins-v1", ["journey"] = journey,
            ["releaseAdmission"] = "NOT_EVALUATED",
            ["sourceState"] = "base-revisions-plus-exact-working-tree-digests",
            ["clientBaseRevision"] = await RevisionAsync(client), ["serverBaseRevision"] = await RevisionAsync(server),
            ["corpusContractVersions"] = versions, ["pins"] = pins,
            ["externalQualifications"] = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(output, "external-qualifications.json")))!["externalQualifications"]!.DeepClone()
        };
    }

    private static string HashTree(string directory)
    {
        var files = Directory.GetFiles(directory, "*.ts", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        if (files.Length == 0) throw new InvalidOperationException("Required contract tree is absent.");
        var entries = files.Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/') + "\0" + HashFile(path));
        return Digest(Encoding.UTF8.GetBytes(string.Join('\n', entries)));
    }

    internal static void VerifyCorpus(string directory)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")))!;
        if (manifest["contractVersion"]?.GetValue<string>() != "1.0.0")
            throw new InvalidOperationException("Unexpected public corpus contract version.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = manifest["files"]!.AsArray();
        if (entries.Count == 0) throw new InvalidOperationException("Empty public corpus manifest.");
        foreach (var entry in entries)
        {
            var relative = entry!["path"]!.GetValue<string>();
            if (Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Split('/').Any(p => p is "" or "." or "..") || !seen.Add(relative))
                throw new InvalidOperationException("Invalid public corpus member path.");
            var current = directory;
            foreach (var part in relative.Split('/'))
            {
                current = Path.Combine(current, part);
                FileSystemInfo member = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                if (member.LinkTarget is not null) throw new InvalidOperationException("Public corpus symlinks are not accepted.");
            }
            var path = Path.Combine(directory, relative);
            if (!File.Exists(path) || new FileInfo(path).LinkTarget is not null
                || new FileInfo(path).Length != entry["bytes"]!.GetValue<long>() || HashFile(path) != entry["sha256"]!.GetValue<string>())
                throw new InvalidOperationException("Public corpus member does not match its immutable manifest.");
        }
    }

    internal static bool Matches(byte[] bytes, string digest, JsonObject expected)
    {
        try
        {
            if (Digest(bytes) != digest) return false;
            var observed = JsonNode.Parse(bytes);
            return JsonNode.DeepEquals(observed, expected)
                && expected["releaseAdmission"]?.GetValue<string>() == "NOT_EVALUATED"
                && expected["pins"]!.AsObject().All(p => p.Value is JsonValue v && v.TryGetValue<string>(out var value)
                    && value.Length == 64 && value.All(c => char.IsAsciiHexDigit(c)));
        }
        catch (JsonException) { return false; }
    }

    private static async Task<string> RevisionAsync(string root)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { "-C", root, "rev-parse", "HEAD" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot resolve repository revision.");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(deadline.Token);
            var revision = (await stdout).Trim();
            await stderr;
            if (process.ExitCode != 0 || revision.Length != 40 || !revision.All(char.IsAsciiHexDigit))
                throw new InvalidOperationException("Repository revision is unavailable.");
            return revision;
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }
}
