using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DeploymentProofPackagePromoter;

public sealed record DeploymentProofPackagePromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string PublicOutputRoot,
    string RestrictedOutputRoot)
{
    public string SchemasRoot => Path.Combine(SourceRoot, "schemas");
    public string ExamplesRoot => Path.Combine(SourceRoot, "examples");
    public string ComponentProofExamplesRoot => Path.Combine(ExamplesRoot, "component-proofs");
    public string BindingExamplesRoot => Path.Combine(ExamplesRoot, "bindings");
    public string CeremonyExamplesRoot => Path.Combine(ExamplesRoot, "ceremonies");
    public string CatalogPath => Path.Combine(PublicOutputRoot, "deployment-proof-catalog.json");

    public static DeploymentProofPackagePromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);
        return new DeploymentProofPackagePromotionPaths(
            root,
            Path.Combine(root, "hush-memory-bank", "Overview", "HushVotingReadiness", "Deployment-Proof-Packages"),
            Path.Combine(root, "Deployment-Proof-Packages"),
            Path.Combine(root, "hush-documents", "PrivateServer_ElectronicVoting", "Deployment-Ceremonies"));
    }
}

public static class DeploymentProofPackageContracts
{
    public static readonly string[] RequiredSchemaFiles =
    [
        "component-deployment-proof-package.schema.json",
        "deployment-proof-set.schema.json",
        "per-election-deployment-binding-ledger.schema.json",
        "deployment-ceremony.schema.json",
        "deployment-impact-classifier-rules.schema.json",
        "deployment-impact-classification-output.schema.json",
        "deployment-proof-manifest.schema.json",
        "deployment-proof-catalog.schema.json",
        "restricted-deployment-evidence-index.schema.json",
        "readiness-fragment.schema.json",
    ];

    public static readonly string[] RequiredCeremonyStageIds =
    [
        "prepare",
        "freeze",
        "deploy_verify",
        "pre_open",
        "active_window_monitoring",
        "close_finalize_export",
    ];

    private static readonly HashSet<string> ValidComponentIds = new(StringComparer.Ordinal)
    {
        "hush-web-client",
        "hush-server-node",
    };

    private static readonly HashSet<string> ValidClassificationOutputs = new(StringComparer.Ordinal)
    {
        DeploymentImpactClasses.VotingProtocolChange,
        DeploymentImpactClasses.VotingProtocolNoChange,
        DeploymentImpactClasses.WebsiteOnlyNoProtocolChange,
        DeploymentImpactClasses.NonVotingServiceNoProtocolChange,
        DeploymentImpactClasses.OperationalConfigChange,
        DeploymentImpactClasses.EmergencyChange,
        DeploymentImpactClasses.Rollback,
        DeploymentImpactClasses.UnknownPendingClassification,
    };

    private static readonly Regex DirectAwsAccountIdPattern = new(@"\b\d{12}\b", RegexOptions.Compiled);

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
        "raw log",
        "raw support log",
        "raw anomaly log",
        "support/anomaly",
        "voter data",
        "voteChoice",
        "voterEmail",
        "operator contact",
        "kms-key-",
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

    public static IReadOnlyList<string> ValidateComponentProof(JsonObject proof)
    {
        var errors = new List<string>();
        RequireNonEmpty(proof, "deploymentProofId", errors);
        RequireNonEmpty(proof, "componentId", errors);
        RequireNonEmpty(proof, "componentVersion", errors);
        RequireNonEmpty(proof, "status", errors);
        RequireNonEmpty(proof, "cdProvider", errors);
        RequireNonEmpty(proof, "cdRunId", errors);
        var deploymentExecutionKind = RequireNonEmpty(proof, "deploymentExecutionKind", errors);
        RequireNonEmpty(proof, "deployedAt", errors);
        RequireNonEmpty(proof, "deploymentTarget", errors);
        var status = GetStringOrDefault(proof, "status");

        if (string.Equals(status, "accepted", StringComparison.Ordinal) &&
            !string.Equals(deploymentExecutionKind, "cd_deployment", StringComparison.Ordinal))
        {
            errors.Add("accepted component proof must be produced by a real CD deployment, not CI-only evidence.");
        }

        var componentId = GetStringOrDefault(proof, "componentId");
        if (componentId is not null && !ValidComponentIds.Contains(componentId))
        {
            errors.Add($"componentId '{componentId}' is not supported.");
        }

        var sourceRef = RequireObject(proof, "sourceRef", errors);
        if (sourceRef is not null)
        {
            ValidateSourceRef(sourceRef, errors);
        }

        var artifactRefs = RequireObject(proof, "artifactRefs", errors);
        if (artifactRefs is not null)
        {
            ValidateComponentArtifacts(componentId, artifactRefs, errors);
        }

        RequireObject(proof, "runtimeVerification", errors);
        var classification = RequireObject(proof, "deploymentImpactClassification", errors);
        if (classification is not null)
        {
            ValidateClassificationOutput(classification, errors);
        }

        RequireArray(proof, "accountabilityAttestations", errors);
        RequireObject(proof, "publicRepositoryRef", errors);
        RequireArray(proof, "restrictedEvidenceRefs", errors);
        RequireArray(proof, "supersedesProofIds", errors);
        RequireObject(proof, "generatedViews", errors);
        ScanForbiddenPublicMaterial(proof, "$", errors);
        return errors;
    }

    public static IReadOnlyList<string> ValidateProofSet(JsonObject proofSet)
    {
        var errors = new List<string>();
        RequireNonEmpty(proofSet, "proofSetId", errors);
        RequireNonEmpty(proofSet, "electionOrRehearsalPublicId", errors);
        RequireNonEmpty(proofSet, "lifecycleCheckpoint", errors);
        RequireObject(proofSet, "catalogRef", errors);
        var componentProofs = RequireObject(proofSet, "componentProofs", errors);
        if (componentProofs is not null)
        {
            RequireNonEmpty(componentProofs, "hushWebClientDeploymentProofId", errors);
            RequireNonEmpty(componentProofs, "hushServerNodeDeploymentProofId", errors);
        }

        RequireArray(proofSet, "componentProofPackageHashes", errors, minItems: 2);
        RequireArray(proofSet, "deploymentEventsSincePreviousCheckpoint", errors);
        RequireNonEmpty(proofSet, "evidenceStatus", errors);
        RequireNonEmpty(proofSet, "publicSafeResultSummary", errors);
        return errors;
    }

    public static IReadOnlyList<string> ValidateBindingLedger(JsonObject ledger)
    {
        var errors = new List<string>();
        RequireNonEmpty(ledger, "ledgerId", errors);
        RequireNonEmpty(ledger, "schemaVersion", errors);
        RequireNonEmpty(ledger, "generatedAt", errors);
        var status = RequireNonEmpty(ledger, "status", errors);
        RequireNonEmpty(ledger, "visibility", errors);
        RequireNonEmpty(ledger, "electionOrRehearsalId", errors);
        RequireObject(ledger, "lifecycleStateWindow", errors);
        var activeProofSetAtOpen = RequireObject(ledger, "activeProofSetAtOpen", errors);
        if (activeProofSetAtOpen is not null && string.Equals(status, "accepted", StringComparison.Ordinal))
        {
            RequireNonEmpty(activeProofSetAtOpen, "hushWebClientDeploymentProofId", errors);
            RequireNonEmpty(activeProofSetAtOpen, "hushServerNodeDeploymentProofId", errors);
        }

        RequireNonEmpty(ledger, "activePlatformCeremonyId", errors);
        RequireNonEmpty(ledger, "deploymentProtocolVersion", errors);
        var deploymentEvents = RequireArray(ledger, "deploymentEvents", errors, minItems: 1);
        if (deploymentEvents is not null && string.Equals(status, "accepted", StringComparison.Ordinal))
        {
            ValidateAcceptedDeploymentEvents(deploymentEvents, errors);
        }

        var reconciliation = RequireObject(ledger, "catalogReconciliation", errors);
        if (reconciliation?["checkpointsCovered"] is JsonArray checkpoints)
        {
            foreach (var checkpoint in new[] { "Draft -> Open", "Open -> Close", "Close -> Finalize", "Open -> Void", "Close -> Void", "final_package_export" })
            {
                if (!checkpoints.Any(node => string.Equals(node?.GetValue<string>(), checkpoint, StringComparison.Ordinal)))
                {
                    errors.Add($"catalogReconciliation.checkpointsCovered must include '{checkpoint}'.");
                }
            }
        }
        else
        {
            errors.Add("catalogReconciliation.checkpointsCovered is required.");
        }

        RequireNonEmpty(ledger, "unknownClassificationPolicy", errors);
        RequireNonEmpty(ledger, "finalBindingSummary", errors);
        return errors;
    }

    public static IReadOnlyList<string> ValidateCeremony(JsonObject ceremony)
    {
        var errors = new List<string>();
        foreach (var propertyName in new[]
                 {
                     "ceremonyId",
                     "ceremonyVersion",
                     "status",
                     "claimLevel",
                     "rehearsalElectionId",
                     "deploymentProfile",
                     "deploymentProtocolVersion",
                     "publicDeploymentProofRepository",
                 })
        {
            RequireNonEmpty(ceremony, propertyName, errors);
        }

        RequireObject(ceremony, "componentDeploymentProofs", errors);
        RequireObject(ceremony, "deploymentProofSet", errors);
        RequireObject(ceremony, "deploymentEnvironment", errors);
        RequireObject(ceremony, "releaseRefs", errors);
        var custodyProfile = RequireObject(ceremony, "custodyProfile", errors);
        var custodyEvidenceRefs = RequireArray(ceremony, "electionCustodyEvidenceRefs", errors, minItems: 1);
        ValidateCustodyEvidence(custodyProfile, custodyEvidenceRefs, errors);
        RequireObject(ceremony, "environmentEvidence", errors);
        ValidateCeremonyStages(ceremony, errors);
        RequireObject(ceremony, "rules", errors);
        var classification = RequireObject(ceremony, "deploymentImpactClassification", errors);
        if (classification is not null)
        {
            ValidateClassificationOutput(classification, errors);
            if (string.Equals(GetStringOrDefault(ceremony, "status"), "accepted", StringComparison.Ordinal) &&
                string.Equals(GetStringOrDefault(classification, "outputClass"), DeploymentImpactClasses.UnknownPendingClassification, StringComparison.Ordinal))
            {
                errors.Add("accepted ceremony cannot contain unresolved unknown_pending_classification.");
            }
        }

        RequireArray(ceremony, "finalPackageRefs", errors, minItems: 1);
        RequireArray(ceremony, "verifierOutputRefs", errors, minItems: 1);
        RequireArray(ceremony, "exceptions", errors);
        RequireArray(ceremony, "accountabilityAttestations", errors, minItems: 1);
        RequireObject(ceremony, "generatedViews", errors);
        RequireObject(ceremony, "readinessFragment", errors);
        return errors;
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
        catch (JsonException ex)
        {
            errors.Add($"{displayName} is not valid JSON: {ex.Message}");
            return null;
        }
    }

    private static void ValidateSourceRef(JsonObject sourceRef, List<string> errors)
    {
        RequireNonEmpty(sourceRef, "repository", errors);
        var refType = RequireNonEmpty(sourceRef, "refType", errors);
        var value = RequireNonEmpty(sourceRef, "value", errors);
        var immutable = sourceRef["immutable"]?.GetValue<bool>() == true;
        if (!immutable)
        {
            errors.Add("sourceRef.immutable must be true.");
        }

        if (!string.Equals(refType, "commit", StringComparison.Ordinal) &&
            !string.Equals(refType, "tag_immutable", StringComparison.Ordinal))
        {
            errors.Add("sourceRef.refType must be commit or tag_immutable.");
        }

        if (value is "main" or "master" or "latest" or "develop" || value?.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase) == true)
        {
            errors.Add("sourceRef.value must be immutable and cannot be a mutable branch or latest tag.");
        }
    }

    private static void ValidateComponentArtifacts(string? componentId, JsonObject artifactRefs, List<string> errors)
    {
        if (string.Equals(componentId, "hush-web-client", StringComparison.Ordinal))
        {
            RequireNonEmpty(artifactRefs, "webArtifactHash", errors);
            RequireNonEmpty(artifactRefs, "clientBundleHash", errors);
            return;
        }

        if (string.Equals(componentId, "hush-server-node", StringComparison.Ordinal))
        {
            RequireNonEmpty(artifactRefs, "backendImageDigest", errors);
            RequireNonEmpty(artifactRefs, "serverReleaseRef", errors);
            RequireNonEmpty(artifactRefs, "verifierHash", errors);
            RequireNonEmpty(artifactRefs, "packageExporterHash", errors);
            RequireNonEmpty(artifactRefs, "dbMigrationState", errors);
        }
    }

    private static void ValidateClassificationOutput(JsonObject classification, List<string> errors)
    {
        RequireNonEmpty(classification, "classificationId", errors);
        var outputClass = RequireNonEmpty(classification, "outputClass", errors);
        if (outputClass is not null && !ValidClassificationOutputs.Contains(outputClass))
        {
            errors.Add($"deploymentImpactClassification.outputClass '{outputClass}' is not supported.");
        }

        RequireArray(classification, "matchedRules", errors, minItems: 1);
        RequireNonEmpty(classification, "reason", errors);
        if (classification["blocksAcceptedEvidence"] is null)
        {
            errors.Add("deploymentImpactClassification.blocksAcceptedEvidence is required.");
        }
    }

    private static void ValidateAcceptedDeploymentEvents(JsonArray deploymentEvents, List<string> errors)
    {
        foreach (var deploymentEvent in deploymentEvents.OfType<JsonObject>())
        {
            var eventId = GetStringOrDefault(deploymentEvent, "eventId") ?? "<unknown>";
            var classification = GetStringOrDefault(deploymentEvent, "classification");
            if (string.Equals(classification, DeploymentImpactClasses.UnknownPendingClassification, StringComparison.Ordinal))
            {
                errors.Add($"{eventId} cannot remain unknown_pending_classification in accepted binding ledger evidence.");
            }

            if (string.Equals(classification, DeploymentImpactClasses.EmergencyChange, StringComparison.Ordinal) ||
                string.Equals(classification, DeploymentImpactClasses.Rollback, StringComparison.Ordinal))
            {
                RequireNonEmpty(deploymentEvent, "reason", errors);
                RequireArray(deploymentEvent, "checksRerun", errors, minItems: 1);
                RequireNonEmpty(deploymentEvent, "accountabilityMarker", errors);
            }
        }
    }

    private static void ValidateCustodyEvidence(JsonObject? custodyProfile, JsonArray? custodyEvidenceRefs, List<string> errors)
    {
        if (custodyProfile is null)
        {
            return;
        }

        var custodyMode = GetStringOrDefault(custodyProfile, "custodyMode");
        if (custodyMode is null)
        {
            return;
        }

        if (custodyMode.Contains("fake", StringComparison.OrdinalIgnoreCase) ||
            custodyMode.Contains("dev", StringComparison.OrdinalIgnoreCase) ||
            custodyMode.Contains("local", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("accepted election/rehearsal custody evidence cannot use fake, dev, or local custody.");
        }

        if (!custodyMode.Contains("kms", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (custodyEvidenceRefs is null || custodyEvidenceRefs.Count == 0)
        {
            errors.Add("custody mode requires FEAT-131 custody evidence refs by public hash/id and restricted index ref.");
            return;
        }

        foreach (var custodyRef in custodyEvidenceRefs.OfType<JsonObject>())
        {
            RequireNonEmpty(custodyRef, "publicHashRef", errors);
            RequireNonEmpty(custodyRef, "restrictedIndexRef", errors);
        }
    }

    private static void ValidateCeremonyStages(JsonObject ceremony, List<string> errors)
    {
        var stages = RequireArray(ceremony, "ceremonyStages", errors, minItems: RequiredCeremonyStageIds.Length);
        if (stages is null)
        {
            return;
        }

        var stageIds = stages
            .OfType<JsonObject>()
            .Select(stage => GetStringOrDefault(stage, "stageId"))
            .Where(stageId => !string.IsNullOrWhiteSpace(stageId))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var stageId in RequiredCeremonyStageIds)
        {
            if (!stageIds.Contains(stageId))
            {
                errors.Add($"ceremonyStages must include '{stageId}'.");
            }
        }
    }

    private static JsonObject? RequireObject(JsonObject obj, string propertyName, List<string> errors)
    {
        if (obj[propertyName] is JsonObject nested)
        {
            return nested;
        }

        errors.Add($"{propertyName} is required and must be an object.");
        return null;
    }

    private static JsonArray? RequireArray(JsonObject obj, string propertyName, List<string> errors, int minItems = 0)
    {
        if (obj[propertyName] is not JsonArray array)
        {
            errors.Add($"{propertyName} is required and must be an array.");
            return null;
        }

        if (array.Count < minItems)
        {
            errors.Add($"{propertyName} must contain at least {minItems} item(s).");
        }

        return array;
    }

    private static string? RequireNonEmpty(JsonObject obj, string propertyName, List<string> errors)
    {
        var value = GetStringOrDefault(obj, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{propertyName} is required.");
            return null;
        }

        return value;
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

                if (DirectAwsAccountIdPattern.IsMatch(text))
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
}
