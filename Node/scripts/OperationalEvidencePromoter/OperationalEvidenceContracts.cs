using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OperationalEvidencePromoter;

public sealed record OperationalEvidencePromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string RestrictedTemplateRoot)
{
    public string SchemasRoot => Path.Combine(SourceRoot, "schemas");
    public string ExamplesRoot => Path.Combine(SourceRoot, "examples");

    public static OperationalEvidencePromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);
        return new OperationalEvidencePromotionPaths(
            root,
            Path.Combine(root, "hush-memory-bank", "Overview", "HushVotingReadiness", "Operational-Evidence"),
            Path.Combine(root, "hush-documents", "PrivateServer_ElectronicVoting", "Operational-Security", "FEAT-133-Operational-Evidence"));
    }
}

public static class OperationalEvidenceContracts
{
    public const string AcceptedRunFixture = "runs/internal-rehearsal-operational-run.json";
    public const string CheckResultsFixture = "check-results/operational-check-results.json";
    public const string ReadinessFragmentFixture = "readiness/operational-readiness-fragment.json";
    public const string HandoffFixture = "handoffs/operational-handoff.json";

    public static readonly string[] RequiredSchemaFiles =
    [
        "operational-run.schema.json",
        "deployment-binding-source.schema.json",
        "custody-handoff-source.schema.json",
        "access-control-source.schema.json",
        "logging-source.schema.json",
        "backup-restore-source.schema.json",
        "incident-source.schema.json",
        "auditor-room-source.schema.json",
        "exceptions-source.schema.json",
        "operational-check-results.schema.json",
        "operational-exceptions.schema.json",
        "operational-readiness-fragment.schema.json",
        "operational-handoff.schema.json",
    ];

    public static readonly string[] RequiredSourceRefKeys =
    [
        "deploymentBindingSource",
        "custodyHandoffSource",
        "accessControlSource",
        "loggingSource",
        "backupRestoreSource",
        "incidentSource",
        "auditorRoomSource",
        "exceptionsSource",
        "checkResults",
        "operationalExceptions",
        "readinessFragment",
        "handoff",
    ];

    public static readonly string[] RequiredOpsCheckIds =
    [
        "OPS-000",
        "OPS-001",
        "OPS-002",
        "OPS-003",
        "OPS-004",
        "OPS-005",
        "OPS-006",
        "OPS-007",
        "OPS-008",
    ];

    private static readonly Regex DirectProviderAccountIdPattern = new(@"\b\d{12}\b", RegexOptions.Compiled);

    private static readonly string[] PublicForbiddenFragments =
    [
        "arn:aws:kms",
        "alias/",
        "kmsKeyId",
        "kms_key_id",
        "BEGIN PRIVATE KEY",
        "aws_secret_access_key",
        "aws_access_key_id",
        "AKIA",
        "password=",
        "secret=",
        "client_secret",
        "connectionstring",
        "token=",
        "decrypt authority",
        "raw log contains",
        "raw support log",
        "raw anomaly log",
        "voter data row",
        "voteChoice",
        "voterEmail",
        "operator contact",
    ];

    public static IReadOnlyList<string> ValidateSchemaSet(string schemasRoot)
    {
        var errors = new List<string>();
        foreach (var schemaFile in RequiredSchemaFiles)
        {
            var path = Path.Combine(schemasRoot, schemaFile);
            if (!File.Exists(path))
            {
                errors.Add($"Missing schema file: {schemaFile}");
                continue;
            }

            var schema = ReadJsonObject(path, schemaFile, errors);
            if (schema is null)
            {
                continue;
            }

            if (schema["required"] is not JsonArray required || required.Count == 0)
            {
                errors.Add($"{schemaFile} must define a non-empty top-level required array.");
            }
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateSourceFixtureSet(OperationalEvidencePromotionPaths paths)
    {
        var errors = new List<string>();
        var run = ReadExample(paths, AcceptedRunFixture, errors);
        if (run is not null)
        {
            errors.AddRange(ValidateOperationalRun(run, paths));
        }

        var checkResults = ReadExample(paths, CheckResultsFixture, errors);
        if (checkResults is not null)
        {
            errors.AddRange(ValidateOperationalCheckResults(checkResults));
        }

        var readiness = ReadExample(paths, ReadinessFragmentFixture, errors);
        if (readiness is not null)
        {
            errors.AddRange(ValidateReadinessFragment(readiness));
        }

        var handoff = ReadExample(paths, HandoffFixture, errors);
        if (handoff is not null)
        {
            errors.AddRange(ValidateOperationalHandoff(handoff));
        }

        errors.AddRange(ValidatePublicExamplesAreSafe(paths.ExamplesRoot));
        if (!Directory.Exists(paths.RestrictedTemplateRoot))
        {
            errors.Add("Restricted FEAT-133 template root is missing.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateOperationalRun(
        JsonObject run,
        OperationalEvidencePromotionPaths paths)
    {
        var errors = new List<string>();
        foreach (var propertyName in new[]
                 {
                     "runId",
                     "schemaVersion",
                     "generatedAt",
                     "status",
                     "claimLevel",
                     "rehearsalPublicId",
                     "sourceGap",
                     "acceptanceGate",
                     "deploymentProfile",
                     "custodyProfile",
                     "sourceRefs",
                     "outputRoots",
                     "feat132Refs",
                     "feat131Refs",
                     "sp08Refs",
                     "sp10Refs",
                     "evidenceState",
                     "placeholderState",
                     "exceptionPolicy",
                     "signoff",
                     "residualRisk",
                     "claimEffect",
                 })
        {
            RequirePresent(run, propertyName, errors);
        }

        RequireValue(run, "acceptanceGate", "AT-RDY-006", errors);
        RequireValue(run, "claimLevel", "internal_non_binding_rehearsal", errors);

        if (run["sourceRefs"] is JsonObject sourceRefs)
        {
            foreach (var key in RequiredSourceRefKeys)
            {
                var relativePath = GetStringOrDefault(sourceRefs, key);
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    errors.Add($"sourceRefs.{key} is required.");
                    continue;
                }

                var fullPath = Path.GetFullPath(Path.Combine(paths.SourceRoot, relativePath));
                var fullSourceRoot = EnsureTrailingSeparator(Path.GetFullPath(paths.SourceRoot));
                if (!fullPath.StartsWith(fullSourceRoot, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"sourceRefs.{key} escapes the operational source root.");
                }
                else if (!File.Exists(fullPath))
                {
                    errors.Add($"sourceRefs.{key} points to a missing file: {relativePath}");
                }
            }
        }

        ValidateFeat132Refs(run["feat132Refs"] as JsonObject, errors);
        ValidateFeat131Refs(run["feat131Refs"] as JsonObject, errors);
        ValidatePlaceholderState(run, errors);
        ScanForbiddenPublicMaterial(run, "$", errors);
        return errors;
    }

    public static IReadOnlyList<string> ValidateOperationalCheckResults(JsonObject checkResults)
    {
        var errors = new List<string>();
        RequireValue(checkResults, "runId", "OPS-RUN-REHEARSAL-20260519-001", errors);
        RequireValue(checkResults, "claimLevel", "internal_non_binding_rehearsal", errors);
        RequireValue(checkResults, "status", "accepted_with_warnings", errors);
        if (checkResults["checks"] is not JsonArray checks)
        {
            errors.Add("checks is required and must be an array.");
            return errors;
        }

        var checkIds = checks
            .OfType<JsonObject>()
            .Select(check => GetStringOrDefault(check, "checkId"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var requiredCheckId in RequiredOpsCheckIds)
        {
            if (!checkIds.Contains(requiredCheckId))
            {
                errors.Add($"checks must contain {requiredCheckId}.");
            }
        }

        if (checkResults["blockers"] is not JsonArray blockers || blockers.Count > 0)
        {
            errors.Add("accepted internal rehearsal fixture must not contain blockers.");
        }

        if (checkResults["scoreEffect"] is not JsonObject scoreEffect ||
            GetStringOrDefault(scoreEffect, "dimensionId") != "RDY-DIM-007" ||
            scoreEffect["previousScore"]?.GetValue<int>() != 6 ||
            scoreEffect["acceptedScore"]?.GetValue<int>() != 8 ||
            scoreEffect["totalPreviousScore"]?.GetValue<int>() != 59 ||
            scoreEffect["totalAcceptedScore"]?.GetValue<int>() != 61)
        {
            errors.Add("scoreEffect must record RDY-DIM-007 6 -> 8 and total 59 -> 61.");
        }

        ScanForbiddenPublicMaterial(checkResults, "$", errors);
        return errors;
    }

    public static IReadOnlyList<string> ValidateReadinessFragment(JsonObject readiness)
    {
        var errors = new List<string>();
        RequireValue(readiness, "fragmentId", "RDY-EVID-AT-RDY-006-FEAT-133-001", errors);
        RequireValue(readiness, "featureSlice", "FEAT-133", errors);
        RequireValue(readiness, "acceptanceGate", "AT-RDY-006", errors);
        RequireValue(readiness, "dimensionId", "RDY-DIM-007", errors);
        RequireValue(readiness, "registerPromotionOwner", "FEAT-130", errors);
        if (readiness["doesNotMutateRegister"]?.GetValue<bool>() != true)
        {
            errors.Add("doesNotMutateRegister must be true.");
        }

        if (readiness["dimensionScoreChange"] is not JsonObject dimensionScoreChange ||
            dimensionScoreChange["previousScore"]?.GetValue<int>() != 6 ||
            dimensionScoreChange["acceptedScore"]?.GetValue<int>() != 8)
        {
            errors.Add("dimensionScoreChange must record 6 -> 8.");
        }

        if (readiness["totalScoreChange"] is not JsonObject totalScoreChange ||
            totalScoreChange["previousScore"]?.GetValue<int>() != 59 ||
            totalScoreChange["acceptedScore"]?.GetValue<int>() != 61)
        {
            errors.Add("totalScoreChange must record 59 -> 61.");
        }

        RequireNonEmptyArray(readiness, "warnings", errors);
        RequirePresent(readiness, "feat132RegisterPromotionTraceability", errors);
        ScanForbiddenPublicMaterial(readiness, "$", errors);
        return errors;
    }

    public static IReadOnlyList<string> ValidateOperationalHandoff(JsonObject handoff)
    {
        var errors = new List<string>();
        RequireValue(handoff, "producerFeature", "FEAT-133", errors);
        RequireValue(handoff, "runId", "OPS-RUN-REHEARSAL-20260519-001", errors);
        RequireNonEmptyArray(handoff, "publicPackageRefs", errors);
        RequireNonEmptyArray(handoff, "wrapperPackageRefs", errors);
        RequireNonEmptyArray(handoff, "restrictedPackageRefs", errors);
        RequirePresent(handoff, "feat132Refs", errors);
        RequirePresent(handoff, "feat131Refs", errors);
        RequirePresent(handoff, "readinessRegisterHandoff", errors);
        RequirePresent(handoff, "pilotRehearsalHandoff", errors);
        RequirePresent(handoff, "feat132RegisterPromotionTraceability", errors);
        if (handoff["consumerInstructions"] is not JsonObject consumers ||
            consumers["FEAT-130"] is null ||
            consumers["FEAT-137"] is null ||
            consumers["FEAT-141"] is null ||
            consumers["FEAT-142"] is null)
        {
            errors.Add("consumerInstructions must include FEAT-130, FEAT-137, FEAT-141, and FEAT-142.");
        }

        ScanForbiddenPublicMaterial(handoff, "$", errors);
        return errors;
    }

    public static JsonObject ReadJsonObject(string path, string displayName)
    {
        var errors = new List<string>();
        var result = ReadJsonObject(path, displayName, errors);
        if (result is null)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        return result;
    }

    private static JsonObject? ReadExample(
        OperationalEvidencePromotionPaths paths,
        string relativePath,
        List<string> errors)
    {
        var fullPath = Path.Combine(paths.ExamplesRoot, relativePath);
        return ReadJsonObject(fullPath, relativePath, errors);
    }

    private static JsonObject? ReadJsonObject(string path, string displayName, List<string> errors)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject json)
            {
                return json;
            }

            errors.Add($"{displayName} must be a JSON object.");
            return null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            errors.Add($"{displayName} could not be read as JSON: {ex.Message}");
            return null;
        }
    }

    private static void ValidateFeat132Refs(JsonObject? refs, List<string> errors)
    {
        if (refs is null)
        {
            errors.Add("feat132Refs is required.");
            return;
        }

        foreach (var propertyName in new[]
                 {
                     "webClientProofId",
                     "webClientProofHash",
                     "serverNodeProofId",
                     "serverNodeProofHash",
                     "deploymentProofSetId",
                     "bindingLedgerId",
                     "publicCatalogRef",
                     "impactClassification",
                     "restrictedRefs",
                     "unknownClassificationState",
                     "feat130RegisterPromotionState",
                 })
        {
            RequirePresent(refs, propertyName, errors);
        }

        if (GetStringOrDefault(refs, "unknownClassificationState") != "none")
        {
            errors.Add("feat132Refs.unknownClassificationState must be none for accepted FEAT-133 evidence.");
        }

        if (GetStringOrDefault(refs, "impactClassification") == "unknown_pending_classification")
        {
            errors.Add("feat132Refs.impactClassification cannot be unknown_pending_classification.");
        }
    }

    private static void ValidateFeat131Refs(JsonObject? refs, List<string> errors)
    {
        if (refs is null)
        {
            errors.Add("feat131Refs is required.");
            return;
        }

        foreach (var propertyName in new[]
                 {
                     "custodyEvidenceId",
                     "acceptedGateIds",
                     "custodyMode",
                     "custodyStatus",
                     "publicCustodyHash",
                     "restrictedCustodyIndexRef",
                     "requiredForProfile",
                     "unresolvedBlockers",
                 })
        {
            RequirePresent(refs, propertyName, errors);
        }

        if (refs["requiredForProfile"]?.GetValue<bool>() == true &&
            refs["unresolvedBlockers"] is JsonArray blockers &&
            blockers.Count > 0)
        {
            errors.Add("feat131Refs.unresolvedBlockers must be empty when custody is required for the selected profile.");
        }

        if (refs["acceptedGateIds"] is JsonArray acceptedGateIds)
        {
            foreach (var gateId in new[] { "AT-RDY-002", "AT-RDY-003", "AT-RDY-004" })
            {
                if (!acceptedGateIds.Any(node => string.Equals(node?.GetValue<string>(), gateId, StringComparison.Ordinal)))
                {
                    errors.Add($"feat131Refs.acceptedGateIds must include {gateId}.");
                }
            }
        }
    }

    private static void ValidatePlaceholderState(JsonObject run, List<string> errors)
    {
        if (run["placeholderState"] is not JsonObject placeholderState)
        {
            errors.Add("placeholderState is required.");
            return;
        }

        var status = GetStringOrDefault(run, "status");
        var hasPlaceholders = placeholderState["hasPlaceholders"]?.GetValue<bool>() == true;
        if ((status is "accepted" or "accepted_with_warnings") && hasPlaceholders)
        {
            errors.Add("accepted FEAT-133 evidence cannot contain placeholders.");
        }
    }

    private static IReadOnlyList<string> ValidatePublicExamplesAreSafe(string examplesRoot)
    {
        var errors = new List<string>();
        if (!Directory.Exists(examplesRoot))
        {
            errors.Add("Operational evidence examples root is missing.");
            return errors;
        }

        foreach (var file in Directory.EnumerateFiles(examplesRoot, "*.json", SearchOption.AllDirectories))
        {
            var json = ReadJsonObject(file, Path.GetFileName(file), errors);
            if (json is not null)
            {
                ScanForbiddenPublicMaterial(json, Path.GetRelativePath(examplesRoot, file), errors);
            }
        }

        return errors;
    }

    private static void RequireValue(JsonObject obj, string propertyName, string expected, List<string> errors)
    {
        var actual = GetStringOrDefault(obj, propertyName);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            errors.Add($"{propertyName} must be '{expected}'.");
        }
    }

    private static void RequirePresent(JsonObject obj, string propertyName, List<string> errors)
    {
        if (obj[propertyName] is null)
        {
            errors.Add($"{propertyName} is required.");
        }
    }

    private static void RequireNonEmptyArray(JsonObject obj, string propertyName, List<string> errors)
    {
        if (obj[propertyName] is not JsonArray array || array.Count == 0)
        {
            errors.Add($"{propertyName} is required and must contain at least one item.");
        }
    }

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

    private static void ScanForbiddenPublicMaterial(JsonNode? node, string path, List<string> errors)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    ScanForbiddenPublicMaterial(child, $"{path}.{name}", errors);
                }

                break;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    ScanForbiddenPublicMaterial(array[index], $"{path}[{index}]", errors);
                }

                break;
            case JsonValue value when TryGetString(value, out var text):
                foreach (var forbidden in PublicForbiddenFragments)
                {
                    if (text.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"{path} contains forbidden public material: {forbidden}");
                    }
                }

                if (DirectProviderAccountIdPattern.IsMatch(text))
                {
                    errors.Add($"{path} contains a direct provider account identifier.");
                }

                break;
        }
    }

    private static bool TryGetString(JsonValue value, out string text)
    {
        try
        {
            text = value.GetValue<string>();
            return true;
        }
        catch (InvalidOperationException)
        {
            text = string.Empty;
            return false;
        }
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
