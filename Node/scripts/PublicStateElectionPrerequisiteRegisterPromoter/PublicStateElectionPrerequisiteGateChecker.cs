using System.Text.Json.Nodes;

namespace PublicStateElectionPrerequisiteRegisterPromoter;

public sealed record PublicStateBlockerDecision(
    string BlockerId,
    string Severity,
    string Status,
    string Decision,
    string Reason);

public sealed record PublicStateGateEvaluation(
    string Status,
    PublicStateBlockerDecision PublicStateDecision,
    bool ScoreChangeAllowed,
    bool DirectRegisterMutation,
    bool PublicStateClaimAllowed,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Diagnostics);

public static class PublicStateElectionPrerequisiteGateChecker
{
    public static readonly string[] ForbiddenPublicClaimNeedles =
    [
        "public election ready",
        "state election certified",
        "government authorized",
        "legally sufficient public voting system",
        "independently approved for public elections",
        "public-sector equivalent voting system",
    ];

    public static readonly string[] ForbiddenRestrictedMaterialNeedles =
    [
        "voter identity",
        "vote choice",
        "receipt secret",
        "trustee share",
        "private key",
        "legal opinion body",
        "authority private memorandum",
    ];

    private static readonly HashSet<string> BlockingStatuses = new(StringComparer.Ordinal)
    {
        "missing",
        "unknown",
        "blocked",
        "rejected",
        "superseded",
    };

    public static PublicStateGateEvaluation Evaluate(JsonObject source)
    {
        var validationErrors = PublicStateElectionPrerequisiteContracts.ValidateSource(source);
        var blockers = new SortedSet<string>(validationErrors.Select(error => $"VALIDATION: {error}"), StringComparer.Ordinal);
        var diagnostics = new SortedSet<string>(StringComparer.Ordinal);

        var scorePolicy = PublicStateElectionPrerequisiteContracts.RequireObject(source, "scorePolicy");
        if (PublicStateElectionPrerequisiteContracts.GetBool(scorePolicy, "scoreChangeAllowed", true))
        {
            blockers.Add("FEAT149-SCORE-MOVEMENT-FORBIDDEN");
            diagnostics.Add("FEAT-149 v1 cannot move readiness score.");
        }

        if (PublicStateElectionPrerequisiteContracts.GetBool(scorePolicy, "directRegisterMutation", true))
        {
            blockers.Add("FEAT149-DIRECT-REGISTER-MUTATION-FORBIDDEN");
            diagnostics.Add("FEAT-149 v1 cannot mutate the readiness register directly.");
        }

        AddBoundaryBlockers(source, blockers, diagnostics);
        AddPrerequisiteGroupBlockers(source, blockers, diagnostics);
        AddDependencyBlockers(source, blockers, diagnostics);
        AddBlockerPolicyGuard(source, blockers, diagnostics);
        AddPublicOutputBlockers(source, blockers, diagnostics);

        blockers.Add(PublicStateElectionPrerequisiteContracts.PublicStateBlockerId);
        diagnostics.Add("The public/state election blocker remains open in FEAT-149 v1.");

        return new PublicStateGateEvaluation(
            "blocked",
            new PublicStateBlockerDecision(
                PublicStateElectionPrerequisiteContracts.PublicStateBlockerId,
                "red",
                "open",
                "keep_policy_blocked",
                "Public/state election readiness remains outside the v1 claim policy until a future FEAT-130 promotion accepts jurisdiction-specific external authority evidence."),
            ScoreChangeAllowed: false,
            DirectRegisterMutation: false,
            PublicStateClaimAllowed: false,
            blockers.ToArray(),
            diagnostics.ToArray());
    }

    private static void AddBoundaryBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics)
    {
        var boundary = PublicStateElectionPrerequisiteContracts.RequireObject(source, "claimBoundary");
        foreach (var item in new[]
        {
            ("targetJurisdiction", "FEAT149-JURISDICTION-ELECTION-TYPE-MISSING"),
            ("electionType", "FEAT149-JURISDICTION-ELECTION-TYPE-MISSING"),
            ("competentAuthority", "FEAT149-COMPETENT-AUTHORITY-MISSING"),
        })
        {
            var boundaryItem = PublicStateElectionPrerequisiteContracts.RequireObject(boundary, item.Item1);
            var status = PublicStateElectionPrerequisiteContracts.GetString(boundaryItem, "status");
            if (status != "accepted")
            {
                blockers.Add(item.Item2);
                diagnostics.Add($"claimBoundary.{item.Item1} is {status}.");
            }
        }
    }

    private static void AddPrerequisiteGroupBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics)
    {
        foreach (var group in PublicStateElectionPrerequisiteContracts.RequireArray(source, "prerequisiteGroups").OfType<JsonObject>())
        {
            var groupId = PublicStateElectionPrerequisiteContracts.GetString(group, "groupId", "unknown");
            var status = PublicStateElectionPrerequisiteContracts.GetString(group, "status");
            var mandatory = PublicStateElectionPrerequisiteContracts.GetBool(group, "mandatory");

            if (mandatory && BlockingStatuses.Contains(status))
            {
                foreach (var blockerId in PublicStateElectionPrerequisiteContracts.GetStringArray(group, "blockerIds"))
                {
                    blockers.Add(blockerId);
                }

                blockers.Add($"FEAT149-{ToBlockerToken(groupId)}-{status.ToUpperInvariant()}");
                diagnostics.Add($"{groupId} has blocking status {status}.");
            }

            if (mandatory &&
                (status == "accepted" || status == "externally_not_applicable") &&
                PublicStateElectionPrerequisiteContracts.GetStringArray(group, "evidenceRefs").Count == 0)
            {
                blockers.Add($"FEAT149-{ToBlockerToken(groupId)}-EVIDENCE-REF-MISSING");
                diagnostics.Add($"{groupId} is {status} but has no evidence refs.");
            }
        }
    }

    private static void AddDependencyBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics)
    {
        var dependency = PublicStateElectionPrerequisiteContracts.RequireObject(source, "feat148Dependency");
        var currentStatus = PublicStateElectionPrerequisiteContracts.GetString(dependency, "currentStatus");
        if (currentStatus == "blocked")
        {
            blockers.Add("FEAT149-FEAT148-DEPENDENCY-NOT-SATISFIED");
            diagnostics.Add("FEAT-148 production organizational rollout remains blocked.");
        }

        if (PublicStateElectionPrerequisiteContracts.GetString(dependency, "dependencyType") != "necessary_but_not_sufficient" ||
            PublicStateElectionPrerequisiteContracts.GetString(dependency, "sufficiency") != "not_sufficient")
        {
            blockers.Add("FEAT149-FEAT148-SUFFICIENCY-OVERCLAIM");
            diagnostics.Add("FEAT-148 must remain necessary but not sufficient.");
        }
    }

    private static void AddBlockerPolicyGuard(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics)
    {
        var policy = PublicStateElectionPrerequisiteContracts.RequireObject(source, "blockerPolicy");
        if (PublicStateElectionPrerequisiteContracts.GetString(policy, "currentSeverity") != "red" ||
            PublicStateElectionPrerequisiteContracts.GetString(policy, "currentStatus") != "open" ||
            PublicStateElectionPrerequisiteContracts.GetString(policy, "v1Decision") != "keep_policy_blocked")
        {
            blockers.Add(PublicStateElectionPrerequisiteContracts.PublicStateBlockerId);
            diagnostics.Add("Public/state blocker policy attempted to move from red/open.");
        }
    }

    private static void AddPublicOutputBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics)
    {
        var publicSamples = PublicStateElectionPrerequisiteContracts.RequireArray(source, "publicArtifactSamples");
        foreach (var sample in publicSamples.OfType<JsonObject>())
        {
            var content = PublicStateElectionPrerequisiteContracts.GetString(sample, "content");
            foreach (var forbidden in ForbiddenPublicClaimNeedles)
            {
                if (content.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    blockers.Add("FEAT149-PUBLIC-SAFE-WORDING-OVERCLAIM");
                    diagnostics.Add($"Public output contains forbidden claim '{forbidden}'.");
                }
            }

            foreach (var forbidden in ForbiddenRestrictedMaterialNeedles)
            {
                if (content.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    blockers.Add("FEAT149-PUBLIC-OUTPUT-RESTRICTED-MATERIAL");
                    diagnostics.Add($"Public output contains restricted material '{forbidden}'.");
                }
            }
        }
    }

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
