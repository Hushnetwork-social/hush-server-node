using System.Text.Json.Nodes;

namespace HushIdentityCompatibilityConformance.Corpus;

/// <summary>
/// POCO projections of the corpus vector documents. Every field is optional so
/// a single model covers the negative/tamper documents; operation semantics
/// decide which fields are required (mirrors the TS runner interfaces).
/// </summary>
public sealed class MnemonicVector
{
    public string Id { get; init; } = string.Empty;
    public string ProducerId { get; init; } = string.Empty;
    public string Mnemonic { get; init; } = string.Empty;
    public string SeedHex { get; init; } = string.Empty;
    public string SigningPrivateKeyHex { get; init; } = string.Empty;
    public string EncryptionPrivateKeyHex { get; init; } = string.Empty;
    public string SigningPublicKeyHex { get; init; } = string.Empty;
    public string EncryptionPublicKeyHex { get; init; } = string.Empty;
    public string PublicKeyEncoding { get; init; } = string.Empty;
    public int WordCount { get; init; }
}

public sealed class KeyVector
{
    public string Id { get; init; } = string.Empty;
    public string? ProducerId { get; init; }
    public string Operation { get; init; } = string.Empty;
    public string? PrivateScalarHex { get; init; }
    public string? Encoding { get; init; }
    public string? InputHex { get; init; }
    public string? ExpectedPublicKeyHex { get; init; }
    public string? ExpectedPointXHex { get; init; }
    public string? ExpectedPointYHex { get; init; }
    public string Expected { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
}

public sealed class DatVector
{
    public string Id { get; init; } = string.Empty;
    public string? ProducerId { get; init; }
    public string Operation { get; init; } = string.Empty;
    public string? EnvelopeHex { get; init; }
    public string? Password { get; init; }
    public string? PayloadJson { get; init; }
    public string? ExpectedPayloadJson { get; init; }
    public string Expected { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
}

public sealed class CanonicalVector
{
    public string Id { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string Json { get; init; } = string.Empty;
    public string Utf8Hex { get; init; } = string.Empty;
    public int Utf8Length { get; init; }
}

public sealed class SignatureVector
{
    public string Id { get; init; } = string.Empty;
    public string? ProducerId { get; init; }
    public string Operation { get; init; } = string.Empty;
    public string MessageUtf8 { get; init; } = string.Empty;
    public string PublicKeyHex { get; init; } = string.Empty;
    public string? SignatureCompactHex { get; init; }
    public string? SignatureCompactBase64 { get; init; }
    public string? SignatureDerHex { get; init; }
    public string Expected { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
}

public sealed class NegativeVector
{
    public string Id { get; init; } = string.Empty;
    public string? ProducerId { get; init; }
    public string Operation { get; init; } = string.Empty;
    public string Input { get; init; } = string.Empty;
    public string? Passphrase { get; init; }
    public string Expected { get; init; } = string.Empty;
    public string ErrorCode { get; init; } = string.Empty;
}

public sealed class LookupCandidate
{
    public string ProducerId { get; init; } = string.Empty;
    public string SigningAddress { get; init; } = string.Empty;
    public string EncryptionAddress { get; init; } = string.Empty;
}

public sealed class LookupScenario
{
    public string Id { get; init; } = string.Empty;
    public string? Label { get; init; }
    public List<LookupCandidate> Candidates { get; init; } = new();
    public LookupExpected Expected { get; init; } = new();
}

public sealed class LookupExpected
{
    public int MatchCount { get; init; }
    public bool Ambiguous { get; init; }
    public List<string>? Producers { get; init; }
}

public sealed class LookupOutcomes
{
    public List<LookupRegistryEntry> Registry { get; init; } = new();
    public List<LookupScenario> Scenarios { get; init; } = new();
}

public sealed class LookupRegistryEntry
{
    public string SigningAddress { get; init; } = string.Empty;
    public string EncryptionAddress { get; init; } = string.Empty;
    public string ProfileAlias { get; init; } = string.Empty;
}

/// <summary>Loader for the fixed corpus document set (validated beforehand).</summary>
public static class CorpusDocuments
{
    public static List<T> ReadVectors<T>(string corpusRoot, string rel) where T : new()
    {
        var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(corpusRoot, rel)));
        var vectors = doc?["vectors"]?.AsArray() ?? throw new InvalidOperationException($"no vectors in {rel}");
        return vectors.Select(v => FromNode<T>(v!)).ToList();
    }

    public static T FromNode<T>(JsonNode node) where T : new()
    {
        var obj = node.AsObject();
        var instance = new T();
        foreach (var prop in typeof(T).GetProperties())
        {
            // JSON documents use lowerCamelCase keys; property names are PascalCase.
            var jsonName = obj.AsEnumerable().FirstOrDefault(kv => string.Equals(kv.Key, prop.Name, StringComparison.OrdinalIgnoreCase)).Key;
            if (jsonName is null) continue;
            var value = obj[jsonName];
            if (value is null) continue;
            if (prop.PropertyType == typeof(string))
            {
                // covers both string and string? (same runtime type)
                prop.SetValue(instance, value.GetValue<string>());
            }
            else if (prop.PropertyType == typeof(int))
            {
                prop.SetValue(instance, value.GetValue<int>());
            }
            else if (prop.PropertyType == typeof(bool))
            {
                prop.SetValue(instance, value.GetValue<bool>());
            }
            else if (prop.PropertyType == typeof(List<LookupCandidate>))
            {
                prop.SetValue(instance, value.AsArray().Select(c => FromNode<LookupCandidate>(c!)).ToList());
            }
            else if (prop.PropertyType == typeof(List<LookupRegistryEntry>))
            {
                prop.SetValue(instance, value.AsArray().Select(c => FromNode<LookupRegistryEntry>(c!)).ToList());
            }
            else if (prop.PropertyType == typeof(List<string>))
            {
                prop.SetValue(instance, value.AsArray().Select(v => v!.GetValue<string>()).ToList());
            }
            else if (prop.PropertyType == typeof(LookupExpected))
            {
                prop.SetValue(instance, FromNode<LookupExpected>(value));
            }
        }
        return instance;
    }

    public static LookupOutcomes ReadLookup(string corpusRoot)
    {
        var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(corpusRoot, "lookup/outcomes.json")));
        return new LookupOutcomes
        {
            Registry = doc!["registry"]!.AsArray().Select(c => FromNode<LookupRegistryEntry>(c!)).ToList(),
            Scenarios = doc!["scenarios"]!.AsArray().Select(c => FromNode<LookupScenario>(c!)).ToList(),
        };
    }
}
