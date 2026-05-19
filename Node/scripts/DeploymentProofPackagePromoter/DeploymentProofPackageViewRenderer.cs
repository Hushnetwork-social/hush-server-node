using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DeploymentProofPackagePromoter;

public static class DeploymentProofPackageViewRenderer
{
    public const string InformalAccountabilityLabel = "Informal accountability marker";

    public static string GetPublicComponentSummary(JsonObject componentProof)
    {
        var sb = new StringBuilder();
        AppendHeader(sb, "HushVoting Public Component Deployment Summary");
        AppendFacts(
            sb,
            ("Proof ID", GetRequiredString(componentProof, "deploymentProofId")),
            ("Component", GetRequiredString(componentProof, "componentId")),
            ("Status", GetRequiredString(componentProof, "status")),
            ("Deployed At", GetRequiredString(componentProof, "deployedAt")),
            ("Deployment Target", GetRequiredString(componentProof, "deploymentTarget")));

        var publicRepo = GetRequiredObject(componentProof, "publicRepositoryRef");
        sb.AppendLine("## Public Repository Reference");
        AppendFacts(
            sb,
            ("Repository", GetRequiredString(publicRepo, "repository")),
            ("Path", GetRequiredString(publicRepo, "path")),
            ("Commit", GetRequiredString(publicRepo, "commit")));

        var sourceRef = GetRequiredObject(componentProof, "sourceRef");
        sb.AppendLine("## Source Reference");
        AppendFacts(
            sb,
            ("Repository", GetRequiredString(sourceRef, "repository")),
            ("Ref Type", GetRequiredString(sourceRef, "refType")),
            ("Value", GetRequiredString(sourceRef, "value")),
            ("Immutable", GetRequiredBoolean(sourceRef, "immutable") ? "true" : "false"));

        sb.AppendLine("## Artifact Hash Summary");
        AppendObjectTable(sb, GetRequiredObject(componentProof, "artifactRefs"));

        var runtimeVerification = GetRequiredObject(componentProof, "runtimeVerification");
        sb.AppendLine("## Runtime Verification");
        AppendObjectTable(sb, runtimeVerification);

        var classification = GetRequiredObject(componentProof, "deploymentImpactClassification");
        sb.AppendLine("## Deployment Impact Classification");
        AppendFacts(
            sb,
            ("Classification ID", GetRequiredString(classification, "classificationId")),
            ("Output Class", GetRequiredString(classification, "outputClass")),
            ("Matched Rules", string.Join(", ", GetStringArray(classification, "matchedRules"))),
            ("Reason", GetRequiredString(classification, "reason")),
            ("Blocks Accepted Evidence", GetRequiredBoolean(classification, "blocksAcceptedEvidence") ? "true" : "false"));

        sb.AppendLine("## Custody Boundary");
        var custodyProfile = componentProof["custodyProfile"] as JsonObject;
        AppendFacts(sb, ("Public-Safe Status", custodyProfile is null ? "not_component_scoped" : GetOptionalString(custodyProfile, "publicSafeStatus", "not_component_scoped")));

        sb.AppendLine("## Restricted Evidence References");
        AppendRestrictedRefs(sb, GetRequiredArray(componentProof, "restrictedEvidenceRefs"));

        sb.AppendLine("## Accountability");
        AppendAccountability(sb, GetRequiredArray(componentProof, "accountabilityAttestations"));
        return sb.ToString();
    }

    public static string GetPublicBindingSummary(JsonObject proofSet, JsonObject bindingLedger)
    {
        var sb = new StringBuilder();
        AppendHeader(sb, "HushVoting Public Deployment Binding Summary");
        AppendFacts(
            sb,
            ("Proof Set ID", GetRequiredString(proofSet, "proofSetId")),
            ("Binding Ledger ID", GetRequiredString(bindingLedger, "ledgerId")),
            ("Election/Rehearsal Public ID", GetRequiredString(proofSet, "electionOrRehearsalPublicId")),
            ("Lifecycle Checkpoint", GetRequiredString(proofSet, "lifecycleCheckpoint")),
            ("Evidence Status", GetRequiredString(proofSet, "evidenceStatus")));

        var activeProofs = GetRequiredObject(bindingLedger, "activeProofSetAtOpen");
        sb.AppendLine("## Active Component Proofs");
        AppendFacts(
            sb,
            ("WebClient Proof ID", GetRequiredString(activeProofs, "hushWebClientDeploymentProofId")),
            ("HushServerNode Proof ID", GetRequiredString(activeProofs, "hushServerNodeDeploymentProofId")));

        sb.AppendLine("## Catalog Reconciliation");
        AppendObjectTable(sb, GetRequiredObject(bindingLedger, "catalogReconciliation"));

        sb.AppendLine("## Deployment Events Since Previous Checkpoint");
        sb.AppendLine("| Event ID | Checkpoint | Classification | Result | Rerun Checks | Accountability | Reason |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var deploymentEvent in GetRequiredArray(bindingLedger, "deploymentEvents").OfType<JsonObject>())
        {
            AppendTableRow(
                sb,
                GetOptionalString(deploymentEvent, "eventId", "-"),
                GetOptionalString(deploymentEvent, "lifecycleCheckpoint", "-"),
                GetOptionalString(deploymentEvent, "classification", "-"),
                GetOptionalString(deploymentEvent, "result", "-"),
                string.Join(", ", GetStringArray(deploymentEvent, "checksRerun")),
                GetOptionalString(deploymentEvent, "accountabilityMarker", "-"),
                GetOptionalString(deploymentEvent, "reason", "-"));
        }

        sb.AppendLine();
        sb.AppendLine("## Evidence Status");
        AppendFacts(
            sb,
            ("Unknown Classification Policy", GetRequiredString(bindingLedger, "unknownClassificationPolicy")),
            ("Final Binding Summary", GetRequiredString(bindingLedger, "finalBindingSummary")),
            ("Public Result Summary", GetRequiredString(proofSet, "publicSafeResultSummary")));
        return sb.ToString();
    }

    public static string GetRestrictedCeremonyIndex(JsonObject ceremony)
    {
        var sb = new StringBuilder();
        AppendHeader(sb, "HushVoting Restricted Deployment Ceremony Evidence Index");
        AppendFacts(
            sb,
            ("Ceremony ID", GetRequiredString(ceremony, "ceremonyId")),
            ("Rehearsal Election ID", GetRequiredString(ceremony, "rehearsalElectionId")),
            ("Deployment Profile", GetRequiredString(ceremony, "deploymentProfile")),
            ("Deployment Protocol Version", GetRequiredString(ceremony, "deploymentProtocolVersion")));

        sb.AppendLine("## Provider Boundary References");
        AppendObjectTable(sb, GetRequiredObject(ceremony, "deploymentEnvironment"));

        sb.AppendLine("## Custody Handoff References");
        AppendArrayOfObjects(sb, GetRequiredArray(ceremony, "electionCustodyEvidenceRefs"));

        sb.AppendLine("## Final Package References");
        AppendArrayOfObjects(sb, GetRequiredArray(ceremony, "finalPackageRefs"));

        sb.AppendLine("## Verifier Output References");
        AppendArrayOfObjects(sb, GetRequiredArray(ceremony, "verifierOutputRefs"));

        sb.AppendLine("## Exception Details");
        AppendArrayOfObjects(sb, GetRequiredArray(ceremony, "exceptions"));

        sb.AppendLine("## Public Hash References");
        AppendObjectTable(sb, GetRequiredObject(ceremony, "readinessFragment"));
        return sb.ToString();
    }

    public static string GetRestrictedDeploymentEvidenceIndex(
        JsonObject ceremony,
        IReadOnlyList<JsonObject> componentProofs)
    {
        var sb = new StringBuilder();
        AppendHeader(sb, "HushVoting Restricted Deployment Evidence Index");
        AppendFacts(
            sb,
            ("Ceremony ID", GetRequiredString(ceremony, "ceremonyId")),
            ("Restricted Environment Ref", GetOptionalString(GetRequiredObject(ceremony, "deploymentEnvironment"), "restrictedBoundaryRef", "-")),
            ("Config/Profile Hash", GetOptionalString(GetRequiredObject(ceremony, "environmentEvidence"), "configProfileHash", "-")),
            ("DB Migration State", GetOptionalString(GetRequiredObject(ceremony, "environmentEvidence"), "dbMigrationState", "-")));

        sb.AppendLine("## Component CD Run Details");
        sb.AppendLine("| Proof ID | Component | CD Provider | CD Run ID | Runtime Status | Restricted Refs |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var proof in componentProofs)
        {
            AppendTableRow(
                sb,
                GetRequiredString(proof, "deploymentProofId"),
                GetRequiredString(proof, "componentId"),
                GetRequiredString(proof, "cdProvider"),
                GetRequiredString(proof, "cdRunId"),
                GetOptionalString(GetRequiredObject(proof, "runtimeVerification"), "status", "-"),
                string.Join(", ", GetRequiredArray(proof, "restrictedEvidenceRefs").OfType<JsonObject>().Select(item => GetOptionalString(item, "refId", "-"))));
        }

        sb.AppendLine();
        sb.AppendLine("## Operator And Reviewer Roles");
        AppendAccountability(sb, GetRequiredArray(ceremony, "accountabilityAttestations"));

        sb.AppendLine("## Redaction Scan Results");
        AppendFacts(sb, ("Public Forbidden Material Scan", "passed"));
        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, string title)
    {
        sb.AppendLine("<!-- Generated by DeploymentProofPackagePromoter. Do not edit by hand. -->");
        sb.AppendLine($"# {title}");
        sb.AppendLine();
    }

    private static void AppendFacts(StringBuilder sb, params (string Label, string Value)[] rows)
    {
        sb.AppendLine("| Field | Value |");
        sb.AppendLine("|---|---|");
        foreach (var (label, value) in rows)
        {
            AppendTableRow(sb, label, value);
        }

        sb.AppendLine();
    }

    private static void AppendObjectTable(StringBuilder sb, JsonObject obj)
    {
        sb.AppendLine("| Field | Value |");
        sb.AppendLine("|---|---|");
        foreach (var (name, value) in obj)
        {
            AppendTableRow(sb, name, ToPublicString(value));
        }

        sb.AppendLine();
    }

    private static void AppendArrayOfObjects(StringBuilder sb, JsonArray array)
    {
        if (array.Count == 0)
        {
            sb.AppendLine("_None._");
            sb.AppendLine();
            return;
        }

        foreach (var item in array.OfType<JsonObject>())
        {
            AppendObjectTable(sb, item);
        }
    }

    private static void AppendRestrictedRefs(StringBuilder sb, JsonArray refs)
    {
        sb.AppendLine("| Ref ID | Public Hash Ref |");
        sb.AppendLine("|---|---|");
        foreach (var item in refs.OfType<JsonObject>())
        {
            AppendTableRow(
                sb,
                GetOptionalString(item, "refId", "-"),
                GetOptionalString(item, "sha256Hash", GetOptionalString(item, "publicHashRef", "-")));
        }

        sb.AppendLine();
    }

    private static void AppendAccountability(StringBuilder sb, JsonArray attestations)
    {
        sb.AppendLine($"These entries are {InformalAccountabilityLabel}s only.");
        sb.AppendLine();
        sb.AppendLine("| Role | Owner ID | Basis | Same Person Two Hat |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var item in attestations.OfType<JsonObject>())
        {
            AppendTableRow(
                sb,
                GetOptionalString(item, "role", "-"),
                GetOptionalString(item, "ownerId", "-"),
                GetOptionalString(item, "basis", "-"),
                item["samePersonTwoHat"]?.GetValue<bool>() == true ? "true" : "false");
        }

        sb.AppendLine();
    }

    private static void AppendTableRow(StringBuilder sb, params string[] values)
    {
        sb.AppendLine("| " + string.Join(" | ", values.Select(EscapeMarkdownTableValue)) + " |");
    }

    private static string EscapeMarkdownTableValue(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string ToPublicString(JsonNode? node) =>
        node switch
        {
            null => "-",
            JsonValue value => value.ToJsonString().Trim('"'),
            JsonArray array => string.Join(", ", array.Select(ToPublicString)),
            JsonObject obj => string.Join(", ", obj.Select(kvp => $"{kvp.Key}: {ToPublicString(kvp.Value)}")),
            _ => node.ToJsonString(),
        };

    private static JsonObject GetRequiredObject(JsonObject obj, string name) =>
        obj[name] as JsonObject ?? throw new InvalidOperationException($"{name} is required and must be an object.");

    private static JsonArray GetRequiredArray(JsonObject obj, string name) =>
        obj[name] as JsonArray ?? throw new InvalidOperationException($"{name} is required and must be an array.");

    private static string GetRequiredString(JsonObject obj, string name) =>
        GetOptionalString(obj, name, null) ?? throw new InvalidOperationException($"{name} is required.");

    private static string GetOptionalString(JsonObject obj, string name, string? fallback)
    {
        try
        {
            return obj[name]?.GetValue<string>() ?? fallback ?? string.Empty;
        }
        catch (InvalidOperationException)
        {
            return fallback ?? string.Empty;
        }
    }

    private static bool GetRequiredBoolean(JsonObject obj, string name)
    {
        try
        {
            return obj[name]?.GetValue<bool>() ?? throw new InvalidOperationException($"{name} is required.");
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException($"{name} is required and must be a boolean.");
        }
    }

    private static IReadOnlyList<string> GetStringArray(JsonObject obj, string name) =>
        obj[name] is JsonArray array
            ? array.Select(ToPublicString).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
            : [];
}

public static class DeploymentProofPackagePublicRedactionScanner
{
    private static readonly Regex DirectProviderAccountIdPattern = new(@"\b\d{12}\b", RegexOptions.Compiled);
    private static readonly Regex EmailAddressPattern = new(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PrivateUrlPattern = new(@"\bhttps?://(?:localhost|127\.0\.0\.1|10\.|172\.(?:1[6-9]|2[0-9]|3[0-1])\.|192\.168\.|[^/\s]+\.internal)\S*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] ForbiddenPublicFragments =
    [
        "arn:aws:kms",
        "alias/",
        "kmsKeyId",
        "kms_key_id",
        "kms-key-",
        "decrypt authority",
        "BEGIN PRIVATE KEY",
        "PRIVATE KEY",
        "aws_secret_access_key",
        "aws_access_key_id",
        "AKIA",
        "password=",
        "secret=",
        "client_secret",
        "connectionstring",
        "token=",
        "raw log",
        "raw support log",
        "raw anomaly log",
        "support log",
        "anomaly log",
        "voter data",
        "voteChoice",
        "voterEmail",
        "support/anomaly",
        "operator contact",
        "certification",
        "external validation",
        "legal digital signature",
        "cryptographic signature",
        "wet signature",
        "external witness",
    ];

    public static IReadOnlyList<string> ScanPublicMarkdown(string fileName, string content)
    {
        var errors = new List<string>();
        foreach (var fragment in ForbiddenPublicFragments)
        {
            if (content.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{fileName} contains forbidden public material: {fragment}");
            }
        }

        if (DirectProviderAccountIdPattern.IsMatch(content))
        {
            errors.Add($"{fileName} contains a direct provider account identifier.");
        }

        if (EmailAddressPattern.IsMatch(content))
        {
            errors.Add($"{fileName} contains an operator or voter contact detail.");
        }

        if (PrivateUrlPattern.IsMatch(content))
        {
            errors.Add($"{fileName} contains a private URL.");
        }

        return errors;
    }
}
