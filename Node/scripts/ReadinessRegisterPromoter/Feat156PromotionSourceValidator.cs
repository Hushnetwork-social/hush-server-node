using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReadinessRegisterPromoter;

public sealed record Feat156PromotionSourceValidationResult(
    bool IsValid,
    int RecalculatedScore,
    string ProductionDecision,
    string PublicStateDecision,
    IReadOnlyList<string> Errors);

public sealed class Feat156PromotionSourceValidator
{
    private const string TargetVersion = "v0.1.6";
    private const string TargetVersionId = "RDY-REG-v0.1.6";
    private const int BaselineTotal = 71;
    private const int UpstreamAcceptedTotal = 79;
    private const int TargetTotal = 80;

    private static readonly IReadOnlyDictionary<string, ExpectedMovement> ExpectedMovements =
        new Dictionary<string, ExpectedMovement>(StringComparer.Ordinal)
        {
            ["FEAT-151"] = new("RDY-DIM-002", 6, 8, 2),
            ["FEAT-152"] = new("RDY-DIM-003", 7, 8, 1),
            ["FEAT-153"] = new("RDY-DIM-004", 7, 8, 1),
            ["FEAT-154"] = new("RDY-DIM-007", 6, 8, 2),
            ["FEAT-155"] = new("RDY-DIM-009", 6, 8, 2),
            ["FEAT-156"] = new("RDY-DIM-010", 7, 8, 1),
        };

    private static readonly IReadOnlyDictionary<string, string> ExpectedArtifactHashes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FEAT151-CORPUS-MANIFEST"] = "bd6d7d179368fbb7a13811d2fea497ad68306efd949a8178778ca2890554a48c",
            ["FEAT152-RECEIPT-CHANNEL-MANIFEST"] = "d9b09012846bab1d07b7082c88fdd70c206160b0b31dd38a9655e440d5ec2c64",
            ["FEAT153-PUBLICATION-COUNTING-MANIFEST"] = "9ae9c5a78d14c4417b8283e6ba996f08e567d5776c540c27bfdfdcebb8742ca3",
            ["FEAT154-PRODUCTION-LIKE-RUN-MANIFEST"] = "62b2c9afb605bb6e0d26876629b7df122b7da566df37f536b4790a9398ecb410",
            ["FEAT155-FAILED-FINALIZE-MANIFEST"] = "9ca42435559bbcc5b91ce99428a100e14d1637f60e0947eff21d869f8b36037b",
        };

    public Feat156PromotionSourceValidationResult ValidateFile(string sourcePath, string? workspaceRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        try
        {
            var source = JsonNode.Parse(File.ReadAllText(sourcePath))?.AsObject()
                ?? throw new JsonException("Root is not a JSON object.");
            return Validate(source, workspaceRoot);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new Feat156PromotionSourceValidationResult(
                false,
                0,
                "unknown",
                "unknown",
                [$"Could not read FEAT-156 promotion source: {ex.Message}"]);
        }
    }

    public Feat156PromotionSourceValidationResult Validate(JsonObject source, string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var errors = new List<string>();

        RequireValue(source, "schemaVersion", "production-rollout-promotion-source.v1", errors);
        RequireValue(source, "featureId", "FEAT-156", errors);
        RequireValue(source, "status", "accepted", errors);

        ValidateRegisters(source, errors);
        ValidatePolicies(source, errors);
        ValidateSignoff(source, errors);
        ValidatePublicSafeRules(source, errors);
        ValidateFeatureFolders(workspaceRoot, errors);

        var scoreMovements = GetArray(source, "scoreMovements", errors);
        var recalculatedScore = ValidateScoreMovements(scoreMovements, errors);

        var productionDecision = ValidateProductionClaim(source, errors);
        var publicStateDecision = ValidatePublicStateBoundary(source, errors);

        return new Feat156PromotionSourceValidationResult(
            errors.Count == 0,
            recalculatedScore,
            productionDecision,
            publicStateDecision,
            errors);
    }

    private static void ValidateRegisters(JsonObject source, List<string> errors)
    {
        var baseline = GetObject(source, "baselineRegister", errors);
        if (baseline is not null)
        {
            RequireValue(baseline, "registerVersionId", "RDY-REG-v0.1.5", errors);
            RequireValue(baseline, "status", "AcceptedInternal", errors);
            RequireIntValue(baseline, "totalScore", BaselineTotal, errors);
            RequireValue(baseline, "strongestAllowedClaim", "friendly_organization_pilot", errors);
        }

        var target = GetObject(source, "targetRegister", errors);
        if (target is not null)
        {
            var targetVersion = GetString(target, "registerVersion");
            var internalAudit95Target = targetVersion == InternalAudit95ReadinessPlan.TargetVersion;
            var expectedTargetVersionId = internalAudit95Target
                ? $"RDY-REG-{InternalAudit95ReadinessPlan.TargetVersion}"
                : TargetVersionId;
            RequireValue(target, "registerVersionId", expectedTargetVersionId, errors);
            if (targetVersion is not (TargetVersion or InternalAudit95ReadinessPlan.TargetVersion))
            {
                errors.Add($"registerVersion must be {TargetVersion} or {InternalAudit95ReadinessPlan.TargetVersion}.");
            }

            RequireValue(target, "status", "AcceptedInternal", errors);
            RequireIntValue(target, "totalScore", TargetTotal, errors);
            RequireValue(
                target,
                "strongestAllowedClaim",
                internalAudit95Target ? "friendly_organization_pilot" : "production_organizational_rollout",
                errors);
            RequireValue(
                target,
                "publicationStatus",
                internalAudit95Target ? InternalAudit95ReadinessPlan.PublicationStatus : "production_rollout_with_limitations",
                errors);
        }

        var scoreModel = GetObject(source, "scoreModel", errors);
        if (scoreModel is not null)
        {
            RequireIntValue(scoreModel, "baselineTotal", BaselineTotal, errors);
            RequireIntValue(scoreModel, "acceptedInputDelta", 8, errors);
            RequireIntValue(scoreModel, "feat156Delta", 1, errors);
            RequireIntValue(scoreModel, "targetTotal", TargetTotal, errors);
            var internalAuditTargetScore = GetInt(scoreModel, "internalAuditTargetScore");
            if (internalAuditTargetScore > 0)
            {
                RequireIntValue(scoreModel, "internalAuditTargetScore", InternalAudit95ReadinessPlan.TargetScore, errors);
            }
            else
            {
                RequireIntValue(scoreModel, "minimumProductionLimitedScore", TargetTotal, errors);
            }

            RequireBoolValue(scoreModel, "scoreCannotBypassBlockers", true, errors);
        }
    }

    private static void ValidatePolicies(JsonObject source, List<string> errors)
    {
        var policyBaselines = GetArray(source, "policyBaselines", errors);
        if (policyBaselines is not null)
        {
            var features = policyBaselines
                .Select(node => node?.AsObject())
                .Where(node => node is not null)
                .Select(node => GetString(node!, "featureId"))
                .ToHashSet(StringComparer.Ordinal);

            if (!features.Contains("FEAT-148"))
            {
                errors.Add("FEAT-148 policy baseline is required.");
            }

            if (!features.Contains("FEAT-149"))
            {
                errors.Add("FEAT-149 public/state boundary baseline is required.");
            }
        }

        var lifecycle = GetObject(source, "evidenceLifecyclePolicy", errors);
        if (lifecycle is not null)
        {
            var requiredFeatures = GetArray(lifecycle, "requiredCompletedFeatures", errors)?
                .Select(node => node?.GetValue<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.Ordinal) ?? [];

            foreach (var featureId in ExpectedMovements.Keys.Where(featureId => featureId != "FEAT-156"))
            {
                if (!requiredFeatures.Contains(featureId))
                {
                    errors.Add($"{featureId} must be listed as a required completed feature.");
                }
            }

            RequireValue(lifecycle, "freshnessRequired", "current", errors);
            RequireBoolValue(lifecycle, "tamperCheckRequired", true, errors);
            RequireBoolValue(lifecycle, "placeholderInputsBlock", true, errors);
        }

        var claimPolicy = GetObject(source, "claimPolicy", errors);
        if (claimPolicy is not null)
        {
            RequireBoolValue(claimPolicy, "productionGreenForbidden", true, errors);
            RequireBoolValue(claimPolicy, "publicStateUnlockForbidden", true, errors);
            RequireBoolValue(claimPolicy, "legalSufficiencyForbidden", true, errors);
            RequireBoolValue(claimPolicy, "independentCertificationForbidden", true, errors);
            RequireBoolValue(claimPolicy, "fullAgmProductClaimForbidden", true, errors);
        }
    }

    private static void ValidateSignoff(JsonObject source, List<string> errors)
    {
        var signoff = GetObject(source, "signoff", errors);
        if (signoff is null)
        {
            return;
        }

        RequireValue(signoff, "status", "accepted", errors);
        if (string.IsNullOrWhiteSpace(GetString(signoff, "engineeringRole")))
        {
            errors.Add("engineeringRole is required on the FEAT-156 promotion signoff.");
        }

        if (string.IsNullOrWhiteSpace(GetString(signoff, "operationsProductRole")))
        {
            errors.Add("operationsProductRole is required on the FEAT-156 promotion signoff.");
        }
    }

    private static void ValidatePublicSafeRules(JsonObject source, List<string> errors)
    {
        var publicSafeRules = GetObject(source, "publicSafeOutputRules", errors);
        if (publicSafeRules is not null)
        {
            RequireBoolValue(publicSafeRules, "numericScorePublicDisclosure", false, errors);

            var generatedOutputSample = GetString(publicSafeRules, "generatedOutputSample");
            if (!string.IsNullOrWhiteSpace(generatedOutputSample))
            {
                var forbiddenNeedles = GetArray(publicSafeRules, "forbiddenMaterialNeedles", errors)?
                    .Select(node => node?.GetValue<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Concat(GetArray(publicSafeRules, "forbiddenClaimNeedles", errors)?
                        .Select(node => node?.GetValue<string>())
                        .Where(value => !string.IsNullOrWhiteSpace(value)) ?? [])
                    .ToArray() ?? [];

                foreach (var forbidden in forbiddenNeedles)
                {
                    if (generatedOutputSample.Contains(forbidden!, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"public-safe output contains forbidden material: {forbidden}.");
                    }
                }
            }
        }

        var target = GetObject(source, "targetRegister", errors);
        var productionClaim = target is null ? null : GetObject(target, "productionClaim", errors);
        if (productionClaim is not null)
        {
            ValidateForbiddenClaimWording(GetString(productionClaim, "wording"), errors);
        }
    }

    private static void ValidateForbiddenClaimWording(string wording, List<string> errors)
    {
        var forbiddenPhrases = new[]
        {
            "legal sufficiency",
            "legally sufficient",
            "independent certification",
            "public/state election ready",
            "government election ready",
            "full AGM management software",
            "legally binding AGM platform",
            "production green",
        };

        foreach (var phrase in forbiddenPhrases)
        {
            if (wording.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"forbidden public-safe claim wording detected: {phrase}.");
            }
        }
    }

    private static void ValidateFeatureFolders(string? workspaceRoot, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return;
        }

        var completedRoot = Path.Combine(
            workspaceRoot,
            "hush-memory-bank",
            "Features",
            "04_COMPLETED");
        foreach (var featureId in ExpectedMovements.Keys.Where(featureId => featureId != "FEAT-156"))
        {
            if (!Directory.Exists(completedRoot) ||
                !Directory.EnumerateDirectories(completedRoot, $"{featureId}*", SearchOption.TopDirectoryOnly).Any())
            {
                errors.Add($"{featureId} must be formally completed under Features/04_COMPLETED.");
            }
        }
    }

    private static int ValidateScoreMovements(JsonArray? scoreMovements, List<string> errors)
    {
        if (scoreMovements is null)
        {
            return 0;
        }

        if (scoreMovements.Count != ExpectedMovements.Count)
        {
            errors.Add("FEAT-156 promotion requires exactly six score movements.");
        }

        var movementByFeature = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var dimensions = new HashSet<string>(StringComparer.Ordinal);
        var duplicateDimensions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var movement in scoreMovements.Select((node, index) => (node, index)))
        {
            if (movement.node is not JsonObject item)
            {
                errors.Add($"scoreMovements[{movement.index}] must be an object.");
                continue;
            }

            var featureId = GetString(item, "featureId");
            if (!string.IsNullOrWhiteSpace(featureId))
            {
                movementByFeature[featureId] = item;
            }

            var dimensionId = GetString(item, "dimensionId");
            if (!string.IsNullOrWhiteSpace(dimensionId) && !dimensions.Add(dimensionId))
            {
                duplicateDimensions.Add(dimensionId);
            }

            ValidateMovementLifecycle(item, errors);
            ValidateArtifactRefs(item, errors);
        }

        foreach (var dimensionId in duplicateDimensions)
        {
            errors.Add($"duplicate score movement for dimension {dimensionId} is not allowed.");
        }

        var acceptedDelta = 0;
        var upstreamDelta = 0;
        foreach (var expected in ExpectedMovements)
        {
            if (!movementByFeature.TryGetValue(expected.Key, out var movement))
            {
                errors.Add($"required score movement for {expected.Key}/{expected.Value.DimensionId} is missing.");
                continue;
            }

            ValidateExpectedMovement(expected.Key, expected.Value, movement, errors);
            acceptedDelta += GetInt(movement, "delta");
            if (expected.Key != "FEAT-156")
            {
                upstreamDelta += GetInt(movement, "delta");
            }
        }

        var upstreamTotal = BaselineTotal + upstreamDelta;
        if (upstreamTotal != UpstreamAcceptedTotal)
        {
            errors.Add($"FEAT-151 through FEAT-155 movements must produce {UpstreamAcceptedTotal}; found {upstreamTotal}.");
        }

        var recalculatedScore = BaselineTotal + acceptedDelta;
        if (recalculatedScore != TargetTotal)
        {
            errors.Add($"recalculated score must be exactly {TargetTotal}; found {recalculatedScore}.");
        }

        if (recalculatedScore < TargetTotal)
        {
            errors.Add("production rollout limited claim requires target total score 80.");
        }

        return recalculatedScore;
    }

    private static void ValidateMovementLifecycle(JsonObject movement, List<string> errors)
    {
        var featureId = GetString(movement, "featureId");
        var status = GetString(movement, "status");
        if (status is not ("accepted" or "accepted_with_limitations"))
        {
            errors.Add($"{featureId} has non-accepted lifecycle status {status}.");
        }

        if (GetString(movement, "freshness") != "current")
        {
            errors.Add($"{featureId} evidence freshness must be current.");
        }

        if (GetBool(movement, "directRegisterMutation"))
        {
            errors.Add($"{featureId} cannot directly mutate the register before FEAT-156 promotion.");
        }

        if (GetString(movement, "registerPromotionOwner") != "FEAT-156")
        {
            errors.Add($"{featureId} registerPromotionOwner must be FEAT-156.");
        }

        var signoff = GetObject(movement, "signoff", errors);
        if (signoff is not null)
        {
            if (featureId != "FEAT-156" && !GetBool(signoff, "sourceFeatureCompleted"))
            {
                errors.Add($"{featureId} source feature must be completed before promotion.");
            }

            if (!GetBool(signoff, "acceptedForPromotion"))
            {
                errors.Add($"{featureId} must be accepted for promotion.");
            }
        }
    }

    private static void ValidateExpectedMovement(
        string featureId,
        ExpectedMovement expected,
        JsonObject movement,
        List<string> errors)
    {
        RequireValue(movement, "dimensionId", expected.DimensionId, errors);
        RequireIntValue(movement, "previousScore", expected.PreviousScore, errors);
        RequireIntValue(movement, "acceptedScore", expected.AcceptedScore, errors);
        RequireIntValue(movement, "delta", expected.Delta, errors);

        if (GetInt(movement, "previousScore") + GetInt(movement, "delta") != GetInt(movement, "acceptedScore"))
        {
            errors.Add($"{featureId} score movement is arithmetically inconsistent.");
        }
    }

    private static void ValidateArtifactRefs(JsonObject movement, List<string> errors)
    {
        var artifactRefs = GetArray(movement, "artifactRefs", errors);
        if (artifactRefs is null || artifactRefs.Count == 0)
        {
            errors.Add($"{GetString(movement, "featureId")} must have artifact refs.");
            return;
        }

        foreach (var artifactRef in artifactRefs.Select(node => node?.AsObject()).Where(node => node is not null))
        {
            var artifactId = GetString(artifactRef!, "artifactId");
            var hash = NormalizeHash(GetString(artifactRef!, "sha256Hash"));
            if (hash.Length != 64)
            {
                errors.Add($"{artifactId} must have a SHA-256 hash.");
            }

            if (hash.All(c => c == '0'))
            {
                errors.Add($"{artifactId} uses a placeholder hash.");
            }

            if (ExpectedArtifactHashes.TryGetValue(artifactId, out var expectedHash) &&
                !hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{artifactId} artifact hash mismatch.");
            }
        }
    }

    private static string ValidateProductionClaim(JsonObject source, List<string> errors)
    {
        var target = GetObject(source, "targetRegister", errors);
        var productionClaim = target is null ? null : GetObject(target, "productionClaim", errors);
        if (productionClaim is null)
        {
            return "missing";
        }

        RequireValue(productionClaim, "claimLevel", "production_organizational_rollout", errors);
        var severity = GetString(productionClaim, "severity");
        var status = GetString(productionClaim, "status");
        var scoreModel = GetObject(source, "scoreModel", errors);
        var internalAudit95Target = scoreModel is not null &&
            GetInt(scoreModel, "internalAuditTargetScore") == InternalAudit95ReadinessPlan.TargetScore;

        if (internalAudit95Target)
        {
            if (severity != "amber" || status != "future_gated")
            {
                errors.Add("production rollout must be amber and future_gated until the internal audit 95 target is reached.");
            }
        }
        else if (severity != "amber" || status != "allowed_with_limitations")
        {
            errors.Add("production rollout must be amber and allowed_with_limitations.");
        }

        if (severity == "green" || status == "allowed")
        {
            errors.Add("production green or unqualified allowed is forbidden for FEAT-156.");
        }

        if (string.IsNullOrWhiteSpace(GetString(productionClaim, "wording")))
        {
            errors.Add("production rollout claim wording is required.");
        }

        var productionDecision = FindBlockerDecision(source, "RDY-BLOCK-PRODUCTION_ORGANIZATIONAL_ROLLOUT-001");
        if (productionDecision is null)
        {
            errors.Add("production rollout blocker decision is required.");
        }
        else
        {
            RequireValue(productionDecision, "targetSeverity", internalAudit95Target ? "red" : "amber", errors);
            RequireValue(productionDecision, "targetStatus", internalAudit95Target ? "superseded" : "allowed_with_limitations", errors);
            RequireValue(productionDecision, "decision", internalAudit95Target ? "replace_with_internal_audit_95_plan" : "allow_with_limitations", errors);
        }

        return $"{severity}/{status}";
    }

    private static string ValidatePublicStateBoundary(JsonObject source, List<string> errors)
    {
        var target = GetObject(source, "targetRegister", errors);
        var publicStateClaim = target is null ? null : GetObject(target, "publicStateClaim", errors);
        if (publicStateClaim is null)
        {
            return "missing";
        }

        RequireValue(publicStateClaim, "claimLevel", "public_or_state_election", errors);
        var severity = GetString(publicStateClaim, "severity");
        var status = GetString(publicStateClaim, "status");
        var scoreModel = GetObject(source, "scoreModel", errors);
        var internalAudit95Target = scoreModel is not null &&
            GetInt(scoreModel, "internalAuditTargetScore") == InternalAudit95ReadinessPlan.TargetScore;
        if (internalAudit95Target)
        {
            if (severity != "amber" || status != "external_boundary")
            {
                errors.Add("public or state election claim must be amber and external_boundary for the internal audit 95 report.");
            }
        }
        else if (severity != "red" || status != "blocked")
        {
            errors.Add("public or state election claim must remain red and blocked.");
        }

        var publicStateDecision = FindBlockerDecision(source, "RDY-BLOCK-PUBLIC_OR_STATE_ELECTION-001");
        if (publicStateDecision is null)
        {
            errors.Add("public/state blocker decision is required.");
        }
        else
        {
            RequireValue(publicStateDecision, "targetSeverity", "red", errors);
            RequireValue(publicStateDecision, "targetStatus", internalAudit95Target ? "superseded" : "open", errors);
            RequireValue(publicStateDecision, "decision", internalAudit95Target ? "move_to_downstream_report" : "keep_policy_blocked", errors);
        }

        return $"{severity}/{status}";
    }

    private static JsonObject? FindBlockerDecision(JsonObject source, string blockerId) =>
        GetArray(source, "blockerDecisions", [])?
            .Select(node => node?.AsObject())
            .FirstOrDefault(item => item is not null && GetString(item, "blockerId") == blockerId);

    private static JsonObject? GetObject(JsonObject source, string name, List<string> errors)
    {
        if (source[name] is JsonObject obj)
        {
            return obj;
        }

        errors.Add($"{name} must be an object.");
        return null;
    }

    private static JsonArray? GetArray(JsonObject source, string name, List<string> errors)
    {
        if (source[name] is JsonArray array)
        {
            return array;
        }

        errors.Add($"{name} must be an array.");
        return null;
    }

    private static string GetString(JsonObject source, string name) =>
        source[name] is JsonValue value && value.TryGetValue<string>(out var result)
            ? result
            : string.Empty;

    private static int GetInt(JsonObject source, string name) =>
        source[name] is JsonValue value && value.TryGetValue<int>(out var result)
            ? result
            : 0;

    private static bool GetBool(JsonObject source, string name) =>
        source[name] is JsonValue value && value.TryGetValue<bool>(out var result) && result;

    private static void RequireValue(JsonObject source, string name, string expected, List<string> errors)
    {
        if (GetString(source, name) != expected)
        {
            errors.Add($"{name} must be {expected}.");
        }
    }

    private static void RequireIntValue(JsonObject source, string name, int expected, List<string> errors)
    {
        if (GetInt(source, name) != expected)
        {
            errors.Add($"{name} must be {expected}.");
        }
    }

    private static void RequireBoolValue(JsonObject source, string name, bool expected, List<string> errors)
    {
        if (GetBool(source, name) != expected)
        {
            errors.Add($"{name} must be {expected.ToString().ToLowerInvariant()}.");
        }
    }

    private static string NormalizeHash(string value) =>
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? value["sha256:".Length..].ToLowerInvariant()
            : value.ToLowerInvariant();

    private sealed record ExpectedMovement(
        string DimensionId,
        int PreviousScore,
        int AcceptedScore,
        int Delta);
}
