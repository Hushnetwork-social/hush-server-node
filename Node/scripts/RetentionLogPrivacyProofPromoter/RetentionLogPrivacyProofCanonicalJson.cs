using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RetentionLogPrivacyProofPromoter;

public static class RetentionLogPrivacyProofCanonicalJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string Serialize(JsonNode node)
    {
        var sorted = SortNode(node) ?? new JsonObject();
        return NormalizeLineEndings(sorted.ToJsonString(Options));
    }

    public static string NormalizeLineEndings(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        return normalized.EndsWith('\n') ? normalized : normalized + "\n";
    }

    public static string ComputeSha256(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static JsonNode? SortNode(JsonNode? node) =>
        node switch
        {
            null => null,
            JsonObject obj => SortObject(obj),
            JsonArray array => SortArray(array),
            _ => node.DeepClone(),
        };

    private static JsonObject SortObject(JsonObject obj)
    {
        var sorted = new JsonObject();
        foreach (var (name, child) in obj.OrderBy(property => property.Key, StringComparer.Ordinal))
        {
            sorted[name] = SortNode(child);
        }

        return sorted;
    }

    private static JsonArray SortArray(JsonArray array)
    {
        var sorted = new JsonArray();
        foreach (var child in array)
        {
            sorted.Add(SortNode(child));
        }

        return sorted;
    }
}
