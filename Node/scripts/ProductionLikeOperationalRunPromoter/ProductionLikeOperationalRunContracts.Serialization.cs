using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProductionLikeOperationalRunPromoter;

public static partial class ProductionLikeOperationalRunContracts
{
    public const string CanonicalizationVersion = "production-like-operational-run-canonical-json.v1";

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static JsonNode? Clone(JsonNode? node) => node?.DeepClone();

    public static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());

    public static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    public static string CanonicalJson(JsonNode node) =>
        NormalizeLineEndings(node.ToJsonString(CanonicalJsonOptions)) + "\n";

    public static string Sha256Hex(string content) =>
        Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(NormalizeLineEndings(content))))
            .ToLowerInvariant();

    public static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    public static IReadOnlyList<ProductionLikeOperationalRunPublicOutputFinding> ScanPublicOutput(
        JsonObject source,
        IEnumerable<(string RelativePath, string Content)> generatedPublicOutputs)
    {
        var findings = new List<ProductionLikeOperationalRunPublicOutputFinding>();
        var publicSafety = RequireObject(source, "publicSafety");
        var forbiddenClaimCategories = GetStringArray(publicSafety, "forbiddenClaimCategories");

        foreach (var output in generatedPublicOutputs)
        {
            ScanText(output.RelativePath, output.Content, forbiddenClaimCategories, findings);
        }

        return findings;
    }

    private static void ScanText(
        string relativePath,
        string content,
        IReadOnlyList<string> forbiddenClaimCategories,
        List<ProductionLikeOperationalRunPublicOutputFinding> findings)
    {
        foreach (var forbiddenClaim in forbiddenClaimCategories)
        {
            if (content.Contains(forbiddenClaim, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new ProductionLikeOperationalRunPublicOutputFinding(
                    relativePath,
                    "overclaim",
                    forbiddenClaim));
            }
        }

        foreach (var forbiddenMaterial in ProductionLikeOperationalRunGateChecker.ForbiddenPublicMaterialNeedles)
        {
            if (content.Contains(forbiddenMaterial, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new ProductionLikeOperationalRunPublicOutputFinding(
                    relativePath,
                    "restricted_material",
                    forbiddenMaterial));
            }
        }
    }
}
