using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProductionLikeOperationalRunPromoter;

public static partial class ProductionLikeOperationalRunContracts
{
    public const string CanonicalizationVersion = "production-like-operational-run-canonical-json.v1";

    private static readonly IReadOnlyDictionary<string, string[]> ForbiddenPublicClaimPhrasesByCategory =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["production_rollout_green"] =
            [
                "production rollout green",
                "production rollout ready",
                "production rollout readiness",
                "production rollout approved",
                "production ready",
                "ready for production rollout",
                "organizational rollout approved",
            ],
            ["public_state_election_ready"] =
            [
                "public/state election ready",
                "public/state election readiness",
                "public or state election ready",
                "public or state election readiness",
                "public election ready",
                "state election ready",
            ],
            ["legal_sufficiency"] =
            [
                "legal sufficiency",
                "legally sufficient",
                "legal approval",
                "legal validation",
            ],
            ["independent_certification"] =
            [
                "independent certification",
                "certification",
                "certified",
                "external validation",
                "externally validated",
                "independent validation",
            ],
            ["failed_finalize_continuity_complete"] =
            [
                "failed-finalize continuity complete",
                "failed-finalize continuity completion",
                "failed finalize continuity complete",
                "failed finalize continuity completion",
                "failed-finalize continuity ready",
                "failed finalize continuity ready",
                "finalization anomaly continuity complete",
            ],
        };

    private static readonly string[] AllowedNonClaimPrefixes =
    [
        "does not claim",
        "does not approve",
        "does not provide",
        "does not prove",
        "does not certify",
        "must not claim",
        "cannot claim",
        "cannot prove",
        "no claim of",
        "not a claim of",
    ];

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
        foreach (var forbiddenClaim in ExpandForbiddenClaimPhrases(forbiddenClaimCategories))
        {
            if (ContainsUnnegated(content, forbiddenClaim))
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

    private static IEnumerable<string> ExpandForbiddenClaimPhrases(IReadOnlyList<string> forbiddenClaimCategories)
    {
        foreach (var forbiddenClaimCategory in forbiddenClaimCategories)
        {
            yield return forbiddenClaimCategory;

            if (ForbiddenPublicClaimPhrasesByCategory.TryGetValue(forbiddenClaimCategory, out var phrases))
            {
                foreach (var phrase in phrases)
                {
                    yield return phrase;
                }
            }
        }
    }

    private static bool ContainsUnnegated(string content, string needle)
    {
        var searchStart = 0;
        while (searchStart < content.Length)
        {
            var index = content.IndexOf(needle, searchStart, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            if (!HasAllowedNonClaimPrefix(content, index))
            {
                return true;
            }

            searchStart = index + needle.Length;
        }

        return false;
    }

    private static bool HasAllowedNonClaimPrefix(string content, int claimIndex)
    {
        var lineStart = content.LastIndexOf('\n', Math.Max(0, claimIndex - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var prefix = content[lineStart..claimIndex].Trim().ToLowerInvariant();

        return AllowedNonClaimPrefixes.Any(prefix.Contains);
    }
}
