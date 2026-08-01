using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Json.Schema;

namespace HushIdentityCompatibilityConformance.Corpus;

/// <summary>
/// Pinned-corpus input validation (contract Task 4.1).
/// Verifies the manifest digest supplied by CI/release configuration, every
/// manifest-listed file (path, byte length, SHA-256), rejects unexpected data
/// files, and validates every document against its local JSON Schema 2020-12
/// document (no remote references) before any vector executes.
///
/// Any integrity or schema failure produces exit code 2 and a secret-safe
/// input failure report; no vector output is emitted.
/// </summary>
public static class CorpusValidator
{
    public sealed class ManifestEntry
    {
        public string Path { get; init; } = string.Empty;
        public long Bytes { get; init; }
        public string Sha256 { get; init; } = string.Empty;
    }

    public sealed class ValidationResult
    {
        public bool Valid { get; init; }
        public string[] Errors { get; init; } = Array.Empty<string>();
    }

    private static readonly string[] SchemaDirectories = { "schemas", "producers", "vectors", "lookup" };

    /// <summary>Kind -> schema $id mapping, mirroring the corpus validate.mjs.</summary>
    private static readonly Dictionary<string, string> KindToSchemaId = new()
    {
        ["producer-inventory"] = "urn:hushvoting:conformance:identity:v1:schemas:inventory",
        ["mnemonic-vectors"] = "urn:hushvoting:conformance:identity:v1:schemas:mnemonic-vectors",
        ["key-vectors"] = "urn:hushvoting:conformance:identity:v1:schemas:key-vectors",
        ["dat-vectors"] = "urn:hushvoting:conformance:identity:v1:schemas:dat-vectors",
        ["canonical-byte-vectors"] = "urn:hushvoting:conformance:identity:v1:schemas:canonical-byte-vectors",
        ["signature-vectors"] = "urn:hushvoting:conformance:identity:v1:schemas:signature-vectors",
        ["negative-vectors"] = "urn:hushvoting:conformance:identity:v1:schemas:negative-vectors",
        ["lookup-outcomes"] = "urn:hushvoting:conformance:identity:v1:schemas:lookup-outcomes",
        ["manifest"] = "urn:hushvoting:conformance:identity:v1:schemas:manifest",
    };

    private const string ProducerSchemaId = "urn:hushvoting:conformance:identity:v1:schemas:producer";

    /// <summary>Enumerate the expected corpus data files (relative slash paths).</summary>
    public static List<string> ExpectedDataFiles(string corpusRoot)
    {
        var files = new List<string>();
        foreach (var dir in SchemaDirectories)
        {
            var full = System.IO.Path.Combine(corpusRoot, dir);
            if (!Directory.Exists(full)) continue;
            foreach (var f in Directory.EnumerateFiles(full, "*.json").OrderBy(x => x, StringComparer.Ordinal))
            {
                files.Add(RelativeSlash(corpusRoot, f));
            }
        }
        files.Add("inventory.json");
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    public static ValidationResult Validate(string corpusRoot, string expectedManifestDigest)
    {
        var errors = new List<string>();
        if (!Directory.Exists(corpusRoot))
        {
            return new ValidationResult { Valid = false, Errors = new[] { $"corpus directory not found: {corpusRoot}" } };
        }

        var manifestPath = System.IO.Path.Combine(corpusRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return new ValidationResult { Valid = false, Errors = new[] { "manifest.json missing (run scripts/generate-manifest.mjs)" } };
        }

        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        var manifestDigest = Sha256Hex(manifestBytes);
        if (!string.Equals(manifestDigest, expectedManifestDigest, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"manifest digest mismatch: expected {expectedManifestDigest}, got {manifestDigest}");
        }

        List<ManifestEntry> entries;
        JsonNode? manifestNode;
        try
        {
            manifestNode = JsonNode.Parse(manifestBytes) ?? throw new InvalidOperationException("empty manifest");
            var files = manifestNode["files"]?.AsArray() ?? throw new InvalidOperationException("manifest has no files array");
            entries = files.Select(f => new ManifestEntry
            {
                Path = (string?)f?["path"] ?? string.Empty,
                Bytes = (long?)f?["bytes"] ?? -1,
                Sha256 = (string?)f?["sha256"] ?? string.Empty,
            }).ToList();
        }
        catch (Exception ex)
        {
            return new ValidationResult { Valid = false, Errors = new[] { $"manifest.json is not valid JSON: {ex.Message}" } };
        }

        // Manifest document itself is validated against manifest.schema.json.
        var manifestSchema = LoadSchemas(corpusRoot).GetValueOrDefault("urn:hushvoting:conformance:identity:v1:schemas:manifest");
        if (manifestSchema is not null)
        {
            var manifestResult = manifestSchema.Evaluate(manifestNode, new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (!manifestResult.IsValid)
            {
                errors.Add($"manifest.json failed manifest.schema.json: {string.Join("; ", manifestResult.Details.Where(d => d.HasErrors).Select(d => d.InstanceLocation.ToString()).Distinct().Take(8))}");
            }
        }

        if (entries.Count == 0)
        {
            errors.Add("manifest lists no files");
        }

        var expectedFiles = ExpectedDataFiles(corpusRoot);
        var listed = new HashSet<string>(entries.Select(e => e.Path), StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var full = System.IO.Path.Combine(corpusRoot, entry.Path);
            if (!File.Exists(full))
            {
                errors.Add($"manifest lists missing file {entry.Path}");
                continue;
            }
            var bytes = File.ReadAllBytes(full);
            if (bytes.LongLength != entry.Bytes) errors.Add($"manifest byte length mismatch for {entry.Path}");
            var sha = Sha256Hex(bytes);
            if (!string.Equals(sha, entry.Sha256, StringComparison.OrdinalIgnoreCase)) errors.Add($"manifest digest mismatch for {entry.Path}");
        }

        foreach (var f in expectedFiles)
        {
            if (!listed.Contains(f)) errors.Add($"corpus file not listed in manifest: {f}");
        }
        foreach (var entry in entries)
        {
            if (!expectedFiles.Contains(entry.Path)) errors.Add($"manifest lists unexpected file {entry.Path}");
        }

        // ---- schema validation -------------------------------------------------
        var schemas = LoadSchemas(corpusRoot);
        foreach (var rel in expectedFiles)
        {
            if (rel.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase)) continue;
            var node = JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(corpusRoot, rel)));
            if (node is null) { errors.Add($"{rel}: not valid JSON"); continue; }
            var kind = (string?)node["kind"];
            string? schemaId = null;
            if (rel.StartsWith("producers/", StringComparison.Ordinal)) schemaId = ProducerSchemaId;
            else if (kind is not null && KindToSchemaId.TryGetValue(kind, out var mapped)) schemaId = mapped;
            if (schemaId is null) { errors.Add($"{rel}: unknown kind {kind}"); continue; }
            if (!schemas.TryGetValue(schemaId, out var schema))
            {
                errors.Add($"{rel}: schema {schemaId} not found");
                continue;
            }
            var result = schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (!result.IsValid)
            {
                var detail = string.Join("; ", result.Details.Where(d => d.HasErrors).Select(d => $"{d.InstanceLocation}").Distinct().Take(8));
                errors.Add($"{rel} failed schema {schemaId}: {detail}");
            }
        }

        return new ValidationResult { Valid = errors.Count == 0, Errors = errors.ToArray() };
    }

    private static Dictionary<string, JsonSchema> LoadSchemas(string corpusRoot)
    {
        var schemas = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);
        var schemaDir = System.IO.Path.Combine(corpusRoot, "schemas");
        if (!Directory.Exists(schemaDir)) return schemas;
        foreach (var f in Directory.EnumerateFiles(schemaDir, "*.schema.json"))
        {
            try
            {
                var schema = JsonSchema.FromFile(f);
                var id = (string?)JsonNode.Parse(File.ReadAllText(f))?["$id"];
                if (id is not null) schemas[id] = schema;
            }
            catch
            {
                // malformed schema files are caught by the per-document validation pass
            }
        }
        return schemas;
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string RelativeSlash(string root, string full)
    {
        return System.IO.Path.GetRelativePath(root, full).Replace('\\', '/');
    }
}
