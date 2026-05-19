using System.Text.Json.Nodes;

namespace OperationalEvidencePromoter;

public sealed record OperationalEvidenceCheckResult(
    string CheckId,
    string Status,
    string Severity,
    string Reason,
    IReadOnlyList<string> EvidenceRefs,
    IReadOnlyList<string> ClaimImpact);

public sealed record OperationalEvidenceCheckSetResult(
    string RunId,
    string Status,
    IReadOnlyList<OperationalEvidenceCheckResult> Checks,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> NotApplicable,
    IReadOnlyList<string> PlaceholderFindings,
    IReadOnlyList<string> ForbiddenMaterialFindings)
{
    public bool BlocksAcceptedEvidence => Blockers.Count > 0 || PlaceholderFindings.Count > 0 || ForbiddenMaterialFindings.Count > 0;
}

public static class OperationalEvidenceCheckStatuses
{
    public const string Passed = "passed";
    public const string Warning = "warning";
    public const string Blocked = "blocked";
    public const string NotApplicable = "not_applicable";
}

public static class OperationalEvidenceChecker
{
    public static OperationalEvidenceCheckSetResult Evaluate(
        OperationalEvidencePromotionPaths paths,
        string runRelativePath = OperationalEvidenceContracts.AcceptedRunFixture)
    {
        var run = OperationalEvidenceContracts.ReadJsonObject(
            Path.Combine(paths.ExamplesRoot, runRelativePath),
            runRelativePath);
        return Evaluate(paths, run);
    }

    public static OperationalEvidenceCheckSetResult Evaluate(
        OperationalEvidencePromotionPaths paths,
        JsonObject run)
    {
        var sources = LoadSources(paths, run);
        var checks = new List<OperationalEvidenceCheckResult>
        {
            EvaluateOps000(run),
            EvaluateOps001(run),
            EvaluateOps002(run, sources),
            EvaluateOps003(run),
            EvaluateOps004(run),
            EvaluateOps005(paths, run),
            EvaluateOps006(sources),
            EvaluateOps007(sources),
            EvaluateOps008(sources),
        };

        var placeholderFindings = GetPlaceholderFindings(run);
        var forbiddenMaterialFindings = OperationalEvidenceContracts.ValidateSourceFixtureSet(paths)
            .Where(error => error.Contains("forbidden public material", StringComparison.OrdinalIgnoreCase) ||
                            error.Contains("direct provider account identifier", StringComparison.OrdinalIgnoreCase))
            .Concat(checks
                .Where(check => check.CheckId == "OPS-005" && check.Status == OperationalEvidenceCheckStatuses.Blocked)
                .Select(check => check.Reason))
            .ToArray();
        var blockers = checks
            .Where(check => check.Status == OperationalEvidenceCheckStatuses.Blocked)
            .Select(check => check.CheckId)
            .ToArray();
        var warnings = checks
            .Where(check => check.Status == OperationalEvidenceCheckStatuses.Warning)
            .Select(check => check.CheckId)
            .ToArray();
        var notApplicable = checks
            .Where(check => check.Status == OperationalEvidenceCheckStatuses.NotApplicable)
            .Select(check => check.CheckId)
            .ToArray();
        var status = blockers.Length > 0 || placeholderFindings.Count > 0 || forbiddenMaterialFindings.Length > 0
            ? "blocked"
            : warnings.Length > 0
                ? "accepted_with_warnings"
                : "accepted";

        return new OperationalEvidenceCheckSetResult(
            GetStringOrDefault(run, "runId") ?? "<unknown>",
            status,
            checks,
            blockers,
            warnings,
            notApplicable,
            placeholderFindings,
            forbiddenMaterialFindings);
    }

    private static OperationalEvidenceCheckResult EvaluateOps000(JsonObject run)
    {
        var deploymentProfile = run["deploymentProfile"] as JsonObject;
        var profileId = deploymentProfile is null ? null : GetStringOrDefault(deploymentProfile, "profileId");
        var profileVersion = deploymentProfile is null ? null : GetStringOrDefault(deploymentProfile, "profileVersion");
        if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(profileVersion))
        {
            return Blocked("OPS-000", "Deployment profile id and version are required.");
        }

        return Passed("OPS-000", "Deployment profile is present and supported.", [profileId, profileVersion]);
    }

    private static OperationalEvidenceCheckResult EvaluateOps001(JsonObject run)
    {
        var feat132Refs = run["feat132Refs"] as JsonObject;
        var sp08Refs = run["sp08Refs"] as JsonObject;
        if (feat132Refs is null || sp08Refs is null)
        {
            return Blocked("OPS-001", "FEAT-132 deployment refs and SP-08 release integrity refs are required.");
        }

        foreach (var field in new[]
                 {
                     "webClientProofId",
                     "webClientProofHash",
                     "serverNodeProofId",
                     "serverNodeProofHash",
                     "deploymentProofSetId",
                     "bindingLedgerId",
                     "publicCatalogRef",
                 })
        {
            if (feat132Refs[field] is null)
            {
                return Blocked("OPS-001", $"FEAT-132 reference '{field}' is required.");
            }
        }

        if (GetStringOrDefault(feat132Refs, "unknownClassificationState") != "none" ||
            GetStringOrDefault(feat132Refs, "impactClassification") == "unknown_pending_classification")
        {
            return Blocked("OPS-001", "FEAT-132 deployment classification is unresolved.");
        }

        if (string.IsNullOrWhiteSpace(GetStringOrDefault(sp08Refs, "releaseManifestHash")) ||
            string.IsNullOrWhiteSpace(GetStringOrDefault(sp08Refs, "immutableDeploymentRef")) ||
            sp08Refs["agreesWithFeat132DeploymentRefs"]?.GetValue<bool>() != true)
        {
            return Blocked("OPS-001", "SP-08 release integrity refs must agree with FEAT-132 deployment refs.");
        }

        return Passed("OPS-001", "FEAT-132 deployment proof refs and SP-08 release refs agree.");
    }

    private static OperationalEvidenceCheckResult EvaluateOps002(
        JsonObject run,
        IReadOnlyDictionary<string, JsonObject> sources)
    {
        var access = GetSource(sources, "accessControlSource");
        if (access is null)
        {
            return Warning("OPS-002", "Access-control source is missing for internal rehearsal.");
        }

        if (access["roleCounts"] is null || access["snapshotHash"] is null)
        {
            return IsInternalRehearsal(run)
                ? Warning("OPS-002", "Access-control snapshot is incomplete but warning-allowed for internal rehearsal.")
                : Blocked("OPS-002", "Access-control snapshot is incomplete.");
        }

        return Passed("OPS-002", "Access-control snapshot is present and public/restricted split is clean.");
    }

    private static OperationalEvidenceCheckResult EvaluateOps003(JsonObject run)
    {
        var refs = run["feat131Refs"] as JsonObject;
        if (refs is null)
        {
            return Blocked("OPS-003", "FEAT-131 custody refs are required when custody is required for the selected profile.");
        }

        var custodyRequired = refs["requiredForProfile"]?.GetValue<bool>() == true;
        if (!custodyRequired)
        {
            return NotApplicable("OPS-003", "Custody evidence is not required by this profile.");
        }

        if (GetStringOrDefault(refs, "custodyStatus") != "accepted")
        {
            return Blocked("OPS-003", "FEAT-131 custody status must be accepted.");
        }

        if (refs["unresolvedBlockers"] is JsonArray blockers && blockers.Count > 0)
        {
            return Blocked("OPS-003", "FEAT-131 custody blockers remain unresolved.");
        }

        if (refs["acceptedGateIds"] is not JsonArray acceptedGates ||
            !ContainsAll(acceptedGates, ["AT-RDY-002", "AT-RDY-003", "AT-RDY-004"]))
        {
            return Blocked("OPS-003", "FEAT-131 custody refs must include accepted AT-RDY-002, AT-RDY-003, and AT-RDY-004 gates.");
        }

        return Passed("OPS-003", "Required FEAT-131 custody refs are accepted.");
    }

    private static OperationalEvidenceCheckResult EvaluateOps004(JsonObject run)
    {
        if (run["executorKeyLifecycle"] is JsonObject executorKeyLifecycle)
        {
            var evidenceStatus = GetStringOrDefault(executorKeyLifecycle, "evidenceStatus");
            return evidenceStatus == "accepted"
                ? Passed("OPS-004", "Executor/key lifecycle evidence is accepted.")
                : Warning("OPS-004", "Executor/key lifecycle evidence is incomplete for internal rehearsal.");
        }

        return NotApplicable("OPS-004", "Executor key lifecycle path is not used by this rehearsal profile.");
    }

    private static OperationalEvidenceCheckResult EvaluateOps005(
        OperationalEvidencePromotionPaths paths,
        JsonObject run)
    {
        var errors = OperationalEvidenceContracts.ValidateOperationalRun(run, paths)
            .Where(error => error.Contains("forbidden public material", StringComparison.OrdinalIgnoreCase) ||
                            error.Contains("direct provider account identifier", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return errors.Length == 0
            ? Passed("OPS-005", "Generated FEAT-133 source fixtures passed forbidden-material scans.")
            : Blocked("OPS-005", string.Join("; ", errors));
    }

    private static OperationalEvidenceCheckResult EvaluateOps006(IReadOnlyDictionary<string, JsonObject> sources)
    {
        var backup = GetSource(sources, "backupRestoreSource");
        if (backup is null)
        {
            return Warning("OPS-006", "Backup/restore source is missing for internal rehearsal.");
        }

        return GetStringOrDefault(backup, "evidenceStatus") == "accepted_with_warnings"
            ? Warning("OPS-006", "Latest valid restore test covers the same deployment profile.")
            : Passed("OPS-006", "Backup/restore evidence covers the deployment profile.");
    }

    private static OperationalEvidenceCheckResult EvaluateOps007(IReadOnlyDictionary<string, JsonObject> sources)
    {
        var incident = GetSource(sources, "incidentSource");
        if (incident is null ||
            string.IsNullOrWhiteSpace(GetStringOrDefault(incident, "declarationActorHash")) ||
            string.IsNullOrWhiteSpace(GetStringOrDefault(incident, "declarationAt")) ||
            string.IsNullOrWhiteSpace(GetStringOrDefault(incident, "publicSafeStatement")))
        {
            return Blocked("OPS-007", "Incident or no-incident declaration is required.");
        }

        return Passed("OPS-007", "Incident/no-incident declaration is present.");
    }

    private static OperationalEvidenceCheckResult EvaluateOps008(IReadOnlyDictionary<string, JsonObject> sources)
    {
        var auditorRoom = GetSource(sources, "auditorRoomSource");
        if (auditorRoom is null)
        {
            return Warning("OPS-008", "Auditor-room access evidence is missing for internal rehearsal.");
        }

        var grantCount = auditorRoom["grantCount"]?.GetValue<int>() ?? -1;
        if (grantCount == 0 && !string.IsNullOrWhiteSpace(GetStringOrDefault(auditorRoom, "noAccessDeclaration")))
        {
            return Warning("OPS-008", "Auditor-room access log exists with zero grants and no-access declaration.");
        }

        return grantCount > 0
            ? Passed("OPS-008", "Auditor-room access evidence is present.")
            : Warning("OPS-008", "Auditor-room access evidence is incomplete for internal rehearsal.");
    }

    private static IReadOnlyDictionary<string, JsonObject> LoadSources(
        OperationalEvidencePromotionPaths paths,
        JsonObject run)
    {
        var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (run["sourceRefs"] is not JsonObject sourceRefs)
        {
            return result;
        }

        foreach (var key in OperationalEvidenceContracts.RequiredSourceRefKeys)
        {
            var relativePath = GetStringOrDefault(sourceRefs, key);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var fullPath = Path.Combine(paths.SourceRoot, relativePath);
            if (File.Exists(fullPath))
            {
                result[key] = OperationalEvidenceContracts.ReadJsonObject(fullPath, relativePath);
            }
        }

        return result;
    }

    private static JsonObject? GetSource(
        IReadOnlyDictionary<string, JsonObject> sources,
        string key) =>
        sources.TryGetValue(key, out var source) ? source : null;

    private static List<string> GetPlaceholderFindings(JsonObject run)
    {
        if (run["placeholderState"] is JsonObject placeholderState &&
            placeholderState["hasPlaceholders"]?.GetValue<bool>() == true &&
            placeholderState["placeholderRefs"] is JsonArray placeholderRefs)
        {
            return placeholderRefs
                .Select(node => node?.GetValue<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToList();
        }

        return [];
    }

    private static bool ContainsAll(JsonArray array, IReadOnlyList<string> values)
    {
        var actual = array
            .Select(node => node?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
        return values.All(actual.Contains);
    }

    private static bool IsInternalRehearsal(JsonObject run) =>
        GetStringOrDefault(run, "claimLevel") == "internal_non_binding_rehearsal";

    private static OperationalEvidenceCheckResult Passed(
        string checkId,
        string reason,
        IReadOnlyList<string>? evidenceRefs = null) =>
        new(checkId, OperationalEvidenceCheckStatuses.Passed, "required", reason, evidenceRefs ?? [], []);

    private static OperationalEvidenceCheckResult Warning(string checkId, string reason) =>
        new(checkId, OperationalEvidenceCheckStatuses.Warning, "warning", reason, [], ["Internal non-binding rehearsal only."]);

    private static OperationalEvidenceCheckResult Blocked(string checkId, string reason) =>
        new(checkId, OperationalEvidenceCheckStatuses.Blocked, "blocker", reason, [], ["Accepted readiness evidence is blocked."]);

    private static OperationalEvidenceCheckResult NotApplicable(string checkId, string reason) =>
        new(checkId, OperationalEvidenceCheckStatuses.NotApplicable, "profile_specific", reason, [], []);

    private static string? GetStringOrDefault(JsonObject obj, string propertyName)
    {
        try
        {
            return obj[propertyName]?.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
