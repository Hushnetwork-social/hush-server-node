using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using HushShared.Elections.Verification.Model;

namespace VerifierCorpusPromoter;

public sealed record VerifierCorpusPromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string SchemasRoot,
    string ExamplesRoot,
    string PublicOutputRoot)
{
    public const string ReadinessSourceFolder = "Verifier-Corpus";
    public const string CorpusManifestFileName = "corpus-manifest.json";

    public string DefaultSourceInput => Path.Combine(ExamplesRoot, "release-baseline", CorpusManifestFileName);

    public static VerifierCorpusPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var fullRoot = Path.GetFullPath(workspaceRoot);
        var sourceRoot = Path.Combine(
            fullRoot,
            "hush-memory-bank",
            "Overview",
            "HushVotingReadiness",
            ReadinessSourceFolder);
        return new VerifierCorpusPromotionPaths(
            fullRoot,
            sourceRoot,
            Path.Combine(sourceRoot, "schemas"),
            Path.Combine(sourceRoot, "examples"),
            Path.Combine(fullRoot, "HushVoting-Verifier-Corpus"));
    }
}

public static class VerifierCorpusContracts
{
    public const string AcceptanceGate = "AT-RDY-007";
    public const string CanonicalizationVersion = "feat135-canonical-json-v1";
    public const string PublicProfileId = VerificationProfileIds.PublicAnonymousV1;

    public static readonly string[] RequiredSchemaFiles =
    [
        "corpus-manifest.schema.json",
        "fixture-manifest.schema.json",
        "expected-result.schema.json",
        "verifier-corpus-ci-run-manifest.schema.json",
        "verifier-corpus-readiness-fragment.schema.json",
        "verifier-corpus-downstream-handoff.schema.json",
    ];

    public static readonly string[] PublicForbiddenMaterialCategories =
    [
        "private_key",
        "cloud_secret",
        "provider_kms_identifier",
        "voter_private_data",
        "restricted_operational_data",
        "unsupported_claim",
        "ip_address",
    ];

    public static readonly string[] RequiredFixtureIds =
    [
        "sample-good-finalized-election",
        "tamper-missing-artifact",
        "tamper-artifact-hash",
        "tamper-malformed-package-json",
        "tamper-profile-mismatch",
        "tamper-unsupported-live-dependency",
        "tamper-wrong-election-id",
        "tamper-duplicate-nullifier",
        "tamper-accepted-set-hash",
        "tamper-published-stream-sequence",
        "tamper-published-stream-hash",
        "tamper-sp04-receipt-set-hash",
        "tamper-sp04-count",
        "tamper-sp04-accepted-binding",
        "tamper-sp05-public-named-field",
        "tamper-sp05-count-reconciliation",
        "tamper-sp08-release-manifest-hash",
        "tamper-sp08-mutable-artifact-reference",
        "tamper-sp08-component-hash-mismatch",
        "tamper-sp08-protocol-package-mismatch",
        "tamper-sp08-circuit-key-hash-mismatch",
        "tamper-sp10-forbidden-leak",
        "tamper-sp10-kms-public-value-leak",
    ];

    private static readonly HashSet<string> ValidCorpusStatuses = new(StringComparer.Ordinal)
    {
        "draft",
        "accepted",
        "blocked",
        "superseded",
    };

    private static readonly HashSet<string> ValidVisibility = new(StringComparer.Ordinal)
    {
        "public",
    };

    private static readonly HashSet<string> ValidOverallStatuses = new(StringComparer.Ordinal)
    {
        "pass",
        "warn",
        "fail",
        "notAvailable",
    };

    private static readonly HashSet<string> ValidCheckStatuses = new(StringComparer.Ordinal)
    {
        "pass",
        "warn",
        "fail",
        "notApplicable",
    };

    private static readonly Lazy<HashSet<string>> ValidResultCodes = new(CreateValidResultCodes);

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

            RequireNonEmpty(schema, "$schema", errors, schemaFile);
            RequireNonEmpty(schema, "$id", errors, schemaFile);
            if (!string.Equals(GetStringOrDefault(schema, "type"), "object", StringComparison.Ordinal))
            {
                errors.Add($"{schemaFile} must define a top-level object schema.");
            }

            if (schema["required"] is not JsonArray required || required.Count == 0)
            {
                errors.Add($"{schemaFile} must define a non-empty top-level required array.");
                continue;
            }

            var actualRequired = required
                .OfType<JsonValue>()
                .Select(value => value.GetValue<string>())
                .ToHashSet(StringComparer.Ordinal);
            foreach (var propertyName in RequiredPropertiesForSchema(schemaFile))
            {
                if (!actualRequired.Contains(propertyName))
                {
                    errors.Add($"{schemaFile} required fields must include {propertyName}.");
                }
            }
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateSourceFixtureSet(VerifierCorpusPromotionPaths paths)
    {
        var errors = new List<string>();
        var releaseRoot = Path.Combine(paths.ExamplesRoot, "release-baseline");

        try
        {
            errors.AddRange(ValidateCorpusManifest(ReadJsonObject(
                Path.Combine(releaseRoot, VerifierCorpusPromotionPaths.CorpusManifestFileName),
                VerifierCorpusPromotionPaths.CorpusManifestFileName)));
            errors.AddRange(ValidateFixtureManifest(ReadJsonObject(
                Path.Combine(releaseRoot, "fixtures", "sample-good-finalized-election.fixture-manifest.json"),
                "sample-good-finalized-election.fixture-manifest.json"),
                "sample-good-finalized-election.fixture-manifest.json"));
            errors.AddRange(ValidateFixtureManifest(ReadJsonObject(
                Path.Combine(releaseRoot, "fixtures", "tamper-missing-artifact.fixture-manifest.json"),
                "tamper-missing-artifact.fixture-manifest.json"),
                "tamper-missing-artifact.fixture-manifest.json"));
            errors.AddRange(ValidateExpectedResult(ReadJsonObject(
                Path.Combine(releaseRoot, "expected-results", "sample-good-finalized-election.json"),
                "sample-good-finalized-election.json"),
                "sample-good-finalized-election.json"));
            errors.AddRange(ValidateExpectedResult(ReadJsonObject(
                Path.Combine(releaseRoot, "expected-results", "tamper-missing-artifact.json"),
                "tamper-missing-artifact.json"),
                "tamper-missing-artifact.json"));
            errors.AddRange(ValidateReadinessFragment(ReadJsonObject(
                Path.Combine(releaseRoot, "readiness", "verifier-corpus-readiness-fragment.json"),
                "verifier-corpus-readiness-fragment.json")));
            errors.AddRange(ValidateDownstreamHandoff(ReadJsonObject(
                Path.Combine(releaseRoot, "handoff", "verifier-corpus-downstream-handoff.json"),
                "verifier-corpus-downstream-handoff.json")));
            errors.AddRange(ValidateForbiddenMaterialList(ReadJsonObject(
                Path.Combine(paths.ExamplesRoot, "public-forbidden-material.json"),
                "public-forbidden-material.json")));
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateCorpusManifest(JsonObject manifest)
    {
        var errors = ValidateJsonRequired(manifest, VerifierCorpusPromotionPaths.CorpusManifestFileName, RequiredPropertiesForSchema("corpus-manifest.schema.json")).ToList();
        ValidateStringValue(manifest, "status", ValidCorpusStatuses, errors, VerifierCorpusPromotionPaths.CorpusManifestFileName);
        ValidateStringValue(manifest, "visibility", ValidVisibility, errors, VerifierCorpusPromotionPaths.CorpusManifestFileName);

        var verifier = RequireObject(manifest, "verifier", errors, VerifierCorpusPromotionPaths.CorpusManifestFileName);
        if (verifier is not null)
        {
            foreach (var propertyName in new[] { "repository", "sourceRef", "projectPath", "runtime", "profileId", "binaryRelease" })
            {
                if (!verifier.ContainsKey(propertyName))
                {
                    errors.Add($"verifier.{propertyName} is required.");
                }
            }

            ValidateStringValue(verifier, "profileId", [PublicProfileId], errors, "verifier");
        }

        var generator = RequireObject(manifest, "generator", errors, VerifierCorpusPromotionPaths.CorpusManifestFileName);
        if (generator is not null)
        {
            ValidateStringValue(generator, "canonicalizationVersion", [CanonicalizationVersion], errors, "generator");
        }

        ValidateFixtureIndex(manifest, errors);
        return errors;
    }

    public static IReadOnlyList<string> ValidateAudit95CorpusManifest(JsonObject manifest)
    {
        var errors = ValidateCorpusManifest(manifest).ToList();
        ValidateStringValue(manifest, "corpusVersion", ["v0.3.0"], errors, VerifierCorpusPromotionPaths.CorpusManifestFileName);
        ValidateImmutableRepositoryRef(manifest["publicRepositoryRef"], "publicRepositoryRef", errors);

        var verifier = RequireObject(manifest, "verifier", errors, VerifierCorpusPromotionPaths.CorpusManifestFileName);
        if (verifier is not null)
        {
            ValidateImmutableRepositoryRef(verifier["sourceRef"], "verifier.sourceRef", errors);
            ValidateSha256Node(verifier["binaryRelease"], "verifier.binaryRelease", errors);
        }

        var ciReplay = RequireObject(manifest, "ciReplay", errors, VerifierCorpusPromotionPaths.CorpusManifestFileName);
        if (ciReplay is not null)
        {
            foreach (var propertyName in new[]
                     {
                         "workflowName",
                         "workflowPath",
                         "workflowRunId",
                         "workflowRunAttempt",
                         "runManifestRef",
                         "outputSummaryRef",
                         "corpusRepositoryRef",
                         "verifierSourceRef",
                         "verifierHash",
                     })
            {
                if (!ciReplay.ContainsKey(propertyName))
                {
                    errors.Add($"ciReplay.{propertyName} is required.");
                }
            }

            ValidateImmutableRepositoryRef(ciReplay["corpusRepositoryRef"], "ciReplay.corpusRepositoryRef", errors);
            ValidateImmutableRepositoryRef(ciReplay["verifierSourceRef"], "ciReplay.verifierSourceRef", errors);
            ValidateSha256String(ciReplay, "verifierHash", errors, "ciReplay");
            RequireNonEmpty(ciReplay, "workflowRunId", errors, "ciReplay");
            RequirePositiveInt(ciReplay, "workflowRunAttempt", errors, "ciReplay");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateFixtureManifest(JsonObject fixture, string label)
    {
        var errors = ValidateJsonRequired(fixture, label, RequiredPropertiesForSchema("fixture-manifest.schema.json")).ToList();
        ValidateStringValue(fixture, "status", ValidCorpusStatuses, errors, label);
        ValidateStringValue(fixture, "visibility", ValidVisibility, errors, label);
        ValidateStringValue(fixture, "profileId", [PublicProfileId], errors, label);
        ValidateStringValue(fixture, "expectedOverallStatus", ValidOverallStatuses, errors, label);
        ValidateStringValue(fixture, "expectedCheckStatus", ValidCheckStatuses, errors, label);
        ValidateStringValue(fixture, "expectedPrimaryResultCode", ValidResultCodes.Value, errors, label);

        if (TryGetInt(fixture, "expectedExitCode", out var exitCode) && exitCode is < 0 or > 4)
        {
            errors.Add($"{label}.expectedExitCode must be a verifier exit code between 0 and 4.");
        }

        RequireObject(fixture, "mutation", errors, label);
        RequireObject(fixture, "verifierInvocation", errors, label);
        return errors;
    }

    public static IReadOnlyList<string> ValidateExpectedResult(JsonObject expectedResult, string label)
    {
        var errors = ValidateJsonRequired(expectedResult, label, RequiredPropertiesForSchema("expected-result.schema.json")).ToList();
        ValidateStringValue(expectedResult, "profileId", [PublicProfileId], errors, label);
        ValidateStringValue(expectedResult, "expectedOverallStatus", ValidOverallStatuses, errors, label);
        ValidateStringValue(expectedResult, "expectedPrimaryResultCode", ValidResultCodes.Value, errors, label);

        if (expectedResult["requiredResultCodes"] is JsonArray resultCodes)
        {
            var containsPrimary = false;
            foreach (var code in resultCodes.OfType<JsonValue>().Select(value => value.GetValue<string>()))
            {
                if (!ValidResultCodes.Value.Contains(code))
                {
                    errors.Add($"{label}.requiredResultCodes contains unsupported verifier result code '{code}'.");
                }

                if (TryGetString(expectedResult["expectedPrimaryResultCode"], out var expectedPrimaryResultCode) &&
                    string.Equals(expectedPrimaryResultCode, code, StringComparison.Ordinal))
                {
                    containsPrimary = true;
                }
            }

            if (TryGetString(expectedResult["expectedPrimaryResultCode"], out _) && !containsPrimary)
            {
                errors.Add($"{label}.requiredResultCodes must include expectedPrimaryResultCode.");
            }
        }
        else
        {
            errors.Add($"{label}.requiredResultCodes is required and must be an array.");
        }

        if (expectedResult["requiredCheckStatuses"] is JsonObject statuses)
        {
            foreach (var (checkCode, statusNode) in statuses)
            {
                if (TryGetString(statusNode, out var status) && !ValidCheckStatuses.Contains(status))
                {
                    errors.Add($"{label}.requiredCheckStatuses.{checkCode} contains unsupported status '{status}'.");
                }
            }
        }
        else
        {
            errors.Add($"{label}.requiredCheckStatuses is required and must be an object.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateReadinessFragment(JsonObject fragment)
    {
        var errors = ValidateJsonRequired(fragment, "verifier-corpus-readiness-fragment.json", RequiredPropertiesForSchema("verifier-corpus-readiness-fragment.schema.json")).ToList();
        ValidateStringValue(fragment, "acceptanceGate", [AcceptanceGate], errors, "verifier-corpus-readiness-fragment.json");
        ValidateStringValue(fragment, "visibility", ValidVisibility, errors, "verifier-corpus-readiness-fragment.json");
        return errors;
    }

    public static IReadOnlyList<string> ValidateDownstreamHandoff(JsonObject handoff)
    {
        var errors = ValidateJsonRequired(handoff, "verifier-corpus-downstream-handoff.json", RequiredPropertiesForSchema("verifier-corpus-downstream-handoff.schema.json")).ToList();
        RequireObject(handoff, "feat136ReuseNotes", errors, "verifier-corpus-downstream-handoff.json");
        RequireObject(handoff, "feat141ConsumerInstructions", errors, "verifier-corpus-downstream-handoff.json");
        return errors;
    }

    public static IReadOnlyList<string> ValidateForbiddenMaterialList(JsonObject forbiddenMaterialList)
    {
        var errors = ValidateJsonRequired(forbiddenMaterialList, "public-forbidden-material.json", [
            "schemaVersion",
            "scanId",
            "scope",
            "terms",
            "regexes",
            "allowedSyntheticMarkers",
        ]).ToList();
        RequireArray(forbiddenMaterialList, "terms", errors, "public-forbidden-material.json", minItems: 1);
        RequireArray(forbiddenMaterialList, "regexes", errors, "public-forbidden-material.json");
        return errors;
    }

    public static IReadOnlyList<string> ValidateCiRunManifest(JsonObject manifest)
    {
        var errors = ValidateJsonRequired(manifest, "verifier-corpus-ci-run-manifest.json", RequiredPropertiesForSchema("verifier-corpus-ci-run-manifest.schema.json")).ToList();
        ValidateImmutableRepositoryRef(manifest["corpusRepositoryRef"], "corpusRepositoryRef", errors);
        ValidateImmutableRepositoryRef(manifest["verifierSourceRef"], "verifierSourceRef", errors);
        ValidateSha256String(manifest, "corpusManifestHash", errors, "verifier-corpus-ci-run-manifest.json");
        ValidateSha256String(manifest, "verifierHash", errors, "verifier-corpus-ci-run-manifest.json");
        RequireNonEmpty(manifest, "workflowRunId", errors, "verifier-corpus-ci-run-manifest.json");
        RequirePositiveInt(manifest, "workflowRunAttempt", errors, "verifier-corpus-ci-run-manifest.json");

        if (manifest["fixtures"] is JsonArray fixtures && fixtures.Count > 0)
        {
            for (var i = 0; i < fixtures.Count; i++)
            {
                if (fixtures[i] is not JsonObject fixture)
                {
                    errors.Add($"fixtures[{i}] must be an object.");
                    continue;
                }

                var label = $"fixtures[{GetStringOrDefault(fixture, "fixtureId") ?? "unknown"}]";
                foreach (var propertyName in new[]
                         {
                             "fixtureId",
                             "expectedExitCode",
                             "observedExitCode",
                             "expectedPrimaryResultCode",
                             "observedPrimaryResultCode",
                             "normalizedOutputHash",
                             "status",
                         })
                {
                    if (!fixture.ContainsKey(propertyName))
                    {
                        errors.Add($"{label}.{propertyName} is required.");
                    }
                }

                ValidateStringValue(fixture, "expectedPrimaryResultCode", ValidResultCodes.Value, errors, label);
                ValidateStringValue(fixture, "observedPrimaryResultCode", ValidResultCodes.Value, errors, label);
                ValidateSha256String(fixture, "normalizedOutputHash", errors, label);
            }
        }
        else
        {
            errors.Add("verifier-corpus-ci-run-manifest.json.fixtures is required and must be a non-empty array.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateAudit95ScoreProposal(JsonObject proposal)
    {
        var errors = ValidateJsonRequired(proposal, "verifier-corpus-audit95-score-proposal.json", [
            "schemaVersion",
            "proposalId",
            "producerFeature",
            "dimensionId",
            "proposedScoreFrom",
            "proposedScoreTo",
            "status",
            "doesNotMutateRegister",
            "evidenceRefs",
        ]).ToList();
        ValidateStringValue(proposal, "dimensionId", ["RDY-DIM-002"], errors, "verifier-corpus-audit95-score-proposal.json");

        if (!TryGetInt(proposal, "proposedScoreFrom", out var from) || from != 8)
        {
            errors.Add("verifier-corpus-audit95-score-proposal.json.proposedScoreFrom must be 8.");
        }

        if (!TryGetInt(proposal, "proposedScoreTo", out var to) || to != 10)
        {
            errors.Add("verifier-corpus-audit95-score-proposal.json.proposedScoreTo must be 10.");
        }

        if (proposal["doesNotMutateRegister"] is not JsonValue mutationValue ||
            !mutationValue.TryGetValue<bool>(out var doesNotMutate) ||
            !doesNotMutate)
        {
            errors.Add("verifier-corpus-audit95-score-proposal.json.doesNotMutateRegister must be true.");
        }

        if (proposal["evidenceRefs"] is not JsonArray evidenceRefs || evidenceRefs.Count == 0)
        {
            errors.Add("verifier-corpus-audit95-score-proposal.json.evidenceRefs is required and must be a non-empty array.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateNoSecretScanResult(JsonObject scanResult)
    {
        var errors = ValidateJsonRequired(scanResult, "verifier-corpus-no-secret-scan-result.json", [
            "schemaVersion",
            "status",
            "forbiddenCategories",
            "unexpectedFindingCount",
            "expectedTamperFindingCount",
            "findings",
        ]).ToList();
        ValidateStringValue(scanResult, "status", ["pass", "blocked", "pending"], errors, "verifier-corpus-no-secret-scan-result.json");

        if (scanResult["forbiddenCategories"] is JsonArray categories)
        {
            var actualCategories = categories
                .OfType<JsonValue>()
                .Select(value => value.GetValue<string>())
                .ToHashSet(StringComparer.Ordinal);
            foreach (var category in PublicForbiddenMaterialCategories)
            {
                if (!actualCategories.Contains(category))
                {
                    errors.Add($"verifier-corpus-no-secret-scan-result.json.forbiddenCategories must include {category}.");
                }
            }
        }
        else
        {
            errors.Add("verifier-corpus-no-secret-scan-result.json.forbiddenCategories is required and must be an array.");
        }

        return errors;
    }

    public static JsonObject ReadJsonObject(string path, string label)
    {
        var errors = new List<string>();
        var result = ReadJsonObject(path, label, errors);
        if (result is null)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        return result;
    }

    public static IReadOnlyList<string> ValidateJsonRequired(
        JsonObject value,
        string label,
        IReadOnlyList<string> requiredProperties)
    {
        var errors = new List<string>();
        foreach (var property in requiredProperties)
        {
            if (!value.ContainsKey(property) || value[property] is null)
            {
                errors.Add($"{label} is missing required property {property}.");
            }
        }

        return errors;
    }

    private static string[] RequiredPropertiesForSchema(string schemaFile) =>
        schemaFile switch
        {
            "corpus-manifest.schema.json" =>
            [
                "schemaVersion",
                "corpusId",
                "corpusVersion",
                "status",
                "visibility",
                "publicRepository",
                "publicRepositoryRef",
                "protocolPackage",
                "verifier",
                "generator",
                "goodSample",
                "fixtureIndex",
                "validationSummary",
                "noSecretScan",
                "readinessFragment",
                "downstreamHandoff",
                "generatedAt",
                "supersessionRules",
                "publicBoundaryStatement",
            ],
            "fixture-manifest.schema.json" =>
            [
                "schemaVersion",
                "fixtureId",
                "fixtureFamily",
                "status",
                "visibility",
                "profileId",
                "packagePath",
                "packageHash",
                "mutation",
                "changedArtifact",
                "expectedPrimaryResultCode",
                "expectedCheckStatus",
                "expectedOverallStatus",
                "expectedExitCode",
                "expectedOutputRef",
                "proofStatement",
                "secondaryFailuresAllowed",
                "verifierInvocation",
                "forbiddenMaterialScan",
            ],
            "expected-result.schema.json" =>
            [
                "schemaVersion",
                "fixtureId",
                "profileId",
                "expectedOverallStatus",
                "expectedExitCode",
                "expectedPrimaryResultCode",
                "requiredResultCodes",
                "requiredCheckStatuses",
                "ignoredFields",
                "stableOutputExcerpt",
                "normalizedOutputHash",
                "outputRef",
            ],
            "verifier-corpus-ci-run-manifest.schema.json" =>
            [
                "schemaVersion",
                "corpusRepository",
                "corpusRepositoryRef",
                "corpusVersion",
                "corpusManifestHash",
                "verifierRepository",
                "verifierSourceRef",
                "verifierHash",
                "workflowName",
                "workflowPath",
                "workflowRunId",
                "workflowRunAttempt",
                "runStatus",
                "generatedAt",
                "fixtures",
            ],
            "verifier-corpus-readiness-fragment.schema.json" =>
            [
                "schemaVersion",
                "fragmentId",
                "featureSlice",
                "sourceGap",
                "acceptanceGate",
                "dimensionId",
                "evidenceRefs",
                "checkResults",
                "status",
                "visibility",
                "claimEffect",
                "residualRisk",
                "doesNotMutateRegister",
                "promotionInstructions",
            ],
            "verifier-corpus-downstream-handoff.schema.json" =>
            [
                "schemaVersion",
                "handoffId",
                "producerFeature",
                "corpusVersion",
                "publicRepositoryRef",
                "manifestHash",
                "goodPackageHash",
                "fixtureIndexHash",
                "verifierSourceRef",
                "verifierHash",
                "cleanMachineValidationSummary",
                "noSecretScanResult",
                "feat136ReuseNotes",
                "feat141ConsumerInstructions",
                "residualRisk",
            ],
            _ => [],
        };

    private static void ValidateFixtureIndex(JsonObject manifest, List<string> errors)
    {
        var fixtureIndex = RequireObject(manifest, "fixtureIndex", errors, VerifierCorpusPromotionPaths.CorpusManifestFileName);
        if (fixtureIndex?["fixtures"] is not JsonArray fixtures)
        {
            errors.Add("fixtureIndex.fixtures is required and must be an array.");
            return;
        }

        var actualFixtureIds = fixtures
            .OfType<JsonObject>()
            .Select(item => GetStringOrDefault(item, "fixtureId"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var fixtureId in RequiredFixtureIds)
        {
            if (!actualFixtureIds.Contains(fixtureId))
            {
                errors.Add($"fixtureIndex.fixtures must include {fixtureId}.");
            }
        }
    }

    private static JsonObject? ReadJsonObject(string path, string label, List<string> errors)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject json)
            {
                return json;
            }

            errors.Add($"{label} must be a JSON object.");
            return null;
        }
        catch (JsonException ex)
        {
            errors.Add($"{label} is not valid JSON: {ex.Message}");
            return null;
        }
        catch (IOException ex)
        {
            errors.Add($"{label} could not be read: {ex.Message}");
            return null;
        }
    }

    private static JsonObject? RequireObject(JsonObject value, string propertyName, List<string> errors, string label)
    {
        if (value[propertyName] is JsonObject obj)
        {
            return obj;
        }

        errors.Add($"{label}.{propertyName} is required and must be an object.");
        return null;
    }

    private static void ValidateImmutableRepositoryRef(JsonNode? value, string label, List<string> errors)
    {
        if (value is JsonValue jsonValue && TryGetString(jsonValue, out var refValue))
        {
            ValidateImmutableRefValue(refValue, label, errors);
            return;
        }

        if (value is not JsonObject obj)
        {
            errors.Add($"{label} is required and must be an immutable commit or tag ref.");
            return;
        }

        var refType = GetStringOrDefault(obj, "refType");
        var immutable = obj["immutable"] is JsonValue immutableValue &&
            immutableValue.TryGetValue<bool>(out var immutableFlag) &&
            immutableFlag;
        if (!string.Equals(refType, "commit", StringComparison.Ordinal) &&
            !string.Equals(refType, "tag_immutable", StringComparison.Ordinal))
        {
            errors.Add($"{label}.refType must be commit or tag_immutable.");
        }

        if (!immutable)
        {
            errors.Add($"{label}.immutable must be true.");
        }

        ValidateImmutableRefValue(GetStringOrDefault(obj, "value"), $"{label}.value", errors);
    }

    private static void ValidateImmutableRefValue(string? value, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{label} is required.");
            return;
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "local-generated", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "local-working-tree", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "main", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "master", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains('\\') ||
            Path.IsPathRooted(normalized))
        {
            errors.Add($"{label} must be an immutable public commit SHA or immutable tag, not '{normalized}'.");
            return;
        }

        if (!IsShaLike(normalized) && !IsTagLike(normalized))
        {
            errors.Add($"{label} must be a 40-character commit SHA or immutable tag ref.");
        }
    }

    private static bool IsShaLike(string value) =>
        value.Length == 40 && value.All(Uri.IsHexDigit);

    private static bool IsTagLike(string value) =>
        value.StartsWith("refs/tags/", StringComparison.Ordinal) ||
        value.StartsWith("tags/", StringComparison.Ordinal) ||
        value.StartsWith("v", StringComparison.OrdinalIgnoreCase);

    private static void ValidateSha256Node(JsonNode? value, string label, List<string> errors)
    {
        if (value is JsonValue jsonValue && TryGetString(jsonValue, out var hash))
        {
            ValidateSha256Value(hash, label, errors);
            return;
        }

        if (value is JsonObject obj)
        {
            ValidateSha256Value(GetStringOrDefault(obj, "binaryHash") ?? GetStringOrDefault(obj, "sha256Hash"), $"{label}.binaryHash", errors);
            return;
        }

        errors.Add($"{label} is required and must include a sha256 verifier hash.");
    }

    private static void ValidateSha256String(JsonObject obj, string propertyName, List<string> errors, string label)
    {
        ValidateSha256Value(GetStringOrDefault(obj, propertyName), $"{label}.{propertyName}", errors);
    }

    private static void ValidateSha256Value(string? value, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{label} is required.");
            return;
        }

        var hash = value.Trim();
        if (!hash.StartsWith("sha256:", StringComparison.Ordinal) ||
            hash.Length != "sha256:".Length + 64 ||
            !hash["sha256:".Length..].All(Uri.IsHexDigit))
        {
            errors.Add($"{label} must be a sha256 hash.");
        }
    }

    private static void RequirePositiveInt(JsonObject value, string propertyName, List<string> errors, string label)
    {
        if (!TryGetInt(value, propertyName, out var intValue) || intValue <= 0)
        {
            errors.Add($"{label}.{propertyName} must be a positive integer.");
        }
    }

    private static JsonArray? RequireArray(JsonObject value, string propertyName, List<string> errors, string label, int minItems = 0)
    {
        if (value[propertyName] is not JsonArray array)
        {
            errors.Add($"{label}.{propertyName} is required and must be an array.");
            return null;
        }

        if (array.Count < minItems)
        {
            errors.Add($"{label}.{propertyName} must contain at least {minItems} item(s).");
        }

        return array;
    }

    private static string? RequireNonEmpty(JsonObject obj, string propertyName, List<string> errors, string label)
    {
        var value = GetStringOrDefault(obj, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{label}.{propertyName} is required.");
            return null;
        }

        return value;
    }

    private static void ValidateStringValue(JsonObject obj, string propertyName, IEnumerable<string> allowedValues, List<string> errors, string label)
    {
        var value = RequireNonEmpty(obj, propertyName, errors, label);
        if (value is null)
        {
            return;
        }

        var allowed = allowedValues as HashSet<string> ?? allowedValues.ToHashSet(StringComparer.Ordinal);
        if (!allowed.Contains(value))
        {
            errors.Add($"{label}.{propertyName} contains unsupported value '{value}'.");
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

    private static bool TryGetString(JsonNode? node, out string value)
    {
        try
        {
            value = node?.GetValue<string>() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (InvalidOperationException)
        {
            value = string.Empty;
            return false;
        }
    }

    private static bool TryGetInt(JsonObject obj, string propertyName, out int value)
    {
        try
        {
            value = obj[propertyName]?.GetValue<int>() ?? 0;
            return obj[propertyName] is not null;
        }
        catch (InvalidOperationException)
        {
            value = 0;
            return false;
        }
    }

    private static HashSet<string> CreateValidResultCodes()
    {
        return typeof(VerificationResultCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
