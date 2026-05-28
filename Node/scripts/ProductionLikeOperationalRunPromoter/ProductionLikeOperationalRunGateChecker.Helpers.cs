using System.Text.Json.Nodes;

namespace ProductionLikeOperationalRunPromoter;

public static partial class ProductionLikeOperationalRunGateChecker
{
    private static void AddEvidenceGroupBlockers(
        JsonObject group,
        string blockerCode,
        IReadOnlyList<string> requiredArrayProperties,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        AddEvidenceStatusBlocker(group, "status", blockerCode, blockers, diagnostics, ref hasPlaceholderEvidence);

        foreach (var property in requiredArrayProperties)
        {
            if (!ProductionLikeOperationalRunContracts.HasArrayItems(group, property))
            {
                AddBlocker(blockers, diagnostics, blockerCode);
            }
        }

        AddEvidenceRefBlockers(group, blockerCode, blockers, diagnostics, ref hasPlaceholderEvidence);
        AddSourceBlockerIds(group, blockers, diagnostics);
    }

    private static void AddEvidenceRefBlockers(
        JsonObject group,
        string blockerCode,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        if (!group.TryGetPropertyValue("evidenceRefs", out var node) || node is not JsonArray refs)
        {
            return;
        }

        foreach (var reference in refs.OfType<JsonObject>())
        {
            AddEvidenceStatusBlocker(reference, "status", blockerCode, blockers, diagnostics, ref hasPlaceholderEvidence);
            if (ProductionLikeOperationalRunContracts.GetString(reference, "freshness") is "stale" or "superseded" or "blocked")
            {
                AddBlocker(blockers, diagnostics, blockerCode);
            }
        }
    }

    private static void AddEvidenceStatusBlocker(
        JsonObject group,
        string property,
        string blockerCode,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        var status = ProductionLikeOperationalRunContracts.GetString(group, property);
        if (status == "placeholder")
        {
            hasPlaceholderEvidence = true;
            diagnostics.Add("FEAT154-PLACEHOLDER-EVIDENCE-BLOCKS-SCORE");
            return;
        }

        if (BlockingEvidenceStatuses.Contains(status))
        {
            AddBlocker(blockers, diagnostics, blockerCode);
        }
    }

    private static void AddSourceBlockerIds(
        JsonObject group,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics)
    {
        foreach (var blockerId in ProductionLikeOperationalRunContracts.GetStringArray(group, "blockerIds"))
        {
            AddBlocker(blockers, diagnostics, blockerId);
        }
    }

    private static void AddBlocker(
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        string code)
    {
        blockers.Add(code);
        diagnostics.Add(code);
    }

    private static string ResolveStatus(
        IReadOnlyCollection<string> blockers,
        IReadOnlyCollection<string> limitations,
        bool hasPlaceholderEvidence)
    {
        if (blockers.Count > 0)
        {
            return "blocked";
        }

        if (hasPlaceholderEvidence)
        {
            return "development_placeholder";
        }

        return limitations.Count > 0 ? "accepted_with_limitations" : "accepted";
    }

    private static string GetGroupStatus(JsonObject parent, string property) =>
        ProductionLikeOperationalRunContracts.GetString(
            ProductionLikeOperationalRunContracts.RequireObject(parent, property),
            "status");

    private static string ToBlockerToken(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_').ToArray();
        var token = new string(chars);
        while (token.Contains("__", StringComparison.Ordinal))
        {
            token = token.Replace("__", "_", StringComparison.Ordinal);
        }

        return token.Trim('_');
    }
}
