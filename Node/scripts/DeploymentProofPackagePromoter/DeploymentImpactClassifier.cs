namespace DeploymentProofPackagePromoter;

public static class DeploymentImpactClasses
{
    public const string VotingProtocolChange = "voting_protocol_change";
    public const string VotingProtocolNoChange = "voting_protocol_no_change";
    public const string WebsiteOnlyNoProtocolChange = "website_only_no_protocol_change";
    public const string NonVotingServiceNoProtocolChange = "non_voting_service_no_protocol_change";
    public const string OperationalConfigChange = "operational_config_change";
    public const string EmergencyChange = "emergency_change";
    public const string Rollback = "rollback";
    public const string UnknownPendingClassification = "unknown_pending_classification";
}

public sealed record DeploymentImpactClassificationInput
{
    public string ClassificationInputId { get; init; } = "classification-input";
    public IReadOnlyList<string> ChangedPaths { get; init; } = [];
    public IReadOnlyList<string> AffectedServices { get; init; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ServiceOwnershipMap { get; init; } =
        DeploymentImpactClassifier.DefaultServiceOwnershipMap;
    public bool ReleaseManifestDiffAvailable { get; init; } = true;
    public bool ArtifactRefsBeforeAfterAvailable { get; init; } = true;
    public bool SbomOrDependencyDiffAvailableWhenApplicable { get; init; } = true;
    public DeploymentRefDiffEvidence RefDiffs { get; init; } = new();
    public DeploymentSemanticChangeEvidence SemanticChanges { get; init; } = new();
    public DeploymentSpecialChangeEvidence SpecialChange { get; init; } = new();
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
}

public sealed record DeploymentRefDiffEvidence
{
    public bool? ProtocolPackageHashChanged { get; init; }
    public bool? CircuitOrKeyRefChanged { get; init; }
    public bool? BackendVotingCriticalHashChanged { get; init; }
    public bool? BackendImageDigestChanged { get; init; }
    public bool? WebArtifactHashChanged { get; init; }
    public bool? VerifierOrExporterHashChanged { get; init; }
    public bool? DbMigrationStateChanged { get; init; }
    public bool? CustodyProfileChanged { get; init; }
    public bool? DeploymentProfileChanged { get; init; }
    public bool? ConfigProfileHashChanged { get; init; }
    public bool ConfigProfileHashRecorded { get; init; }
}

public sealed record DeploymentSemanticChangeEvidence
{
    public bool BallotDefinitionChanged { get; init; }
    public bool EligibilityOrCheckoffChanged { get; init; }
    public bool CustodySemanticsChanged { get; init; }
    public bool AcceptedBallotSemanticsChanged { get; init; }
    public bool PublishedEvidenceSemanticsChanged { get; init; }
    public bool TallyOrCountingLogicChanged { get; init; }
    public bool VerifierOutputSemanticsChanged { get; init; }
    public bool FinalPackageSchemaChanged { get; init; }
    public bool ElectionCriticalDbMigrationChanged { get; init; }
}

public sealed record DeploymentSpecialChangeEvidence
{
    public bool IsEmergencyChange { get; init; }
    public bool IsRollback { get; init; }
    public bool IsNonStateBreakingFix { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<string> RerunChecks { get; init; } = [];
    public string? AccountabilityMarker { get; init; }
    public bool RollbackToLastCeremonyApprovedArtifactSet { get; init; }
    public bool StateCompatibilityEvidenceAvailable { get; init; }
}

public sealed record DeploymentImpactClassificationResult(
    string ClassificationId,
    string OutputClass,
    IReadOnlyList<string> MatchedRules,
    IReadOnlyList<string> EvidenceRefs,
    string Reason,
    bool RequiresManualOwnerReview,
    bool BlocksAcceptedEvidence,
    string AccountabilityMarker);

public static class DeploymentImpactClassifier
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultServiceOwnershipMap =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["hush-web-client"] = new[]
            {
                "hush-web-client/src/hush-voting/**",
                "hush-web-client/src/app/hush-voting/**",
            },
            ["hush-server-node-voting"] = new[]
            {
                "hush-server-node/Node/Core/Elections/**",
                "hush-server-node/Shared/HushShared.Elections*/**",
            },
            ["protocol-omega"] = new[]
            {
                "hush-memory-bank/Overview/ProtocolOmega/**",
                "protocol-omega-packages/**",
            },
            ["custody"] = new[]
            {
                "hush-documents/PrivateServer_ElectronicVoting/Operational-Security/**",
                "hush-server-node/Node/Core/Elections/Custody/**",
            },
            ["non-voting-service"] = new[]
            {
                "hush-server-node/Node/Core/Feeds/**",
                "hush-server-node/Node/Core/Reactions/**",
                "hush-server-node/Node/Core/Bank/**",
                "hush-server-node/Node/Core/UrlMetadata/**",
            },
            ["deployment-config"] = new[]
            {
                ".github/workflows/deploy-hushvoting*.yml",
                "deployment/hushvoting/**",
                "infra/hushvoting/**",
            },
        };

    public static DeploymentImpactClassificationResult Classify(DeploymentImpactClassificationInput input)
    {
        if (input.ChangedPaths.Count == 0)
        {
            return Unknown(input, "classification input did not include changed paths", "missing-changed-paths");
        }

        if (input.AffectedServices.Count == 0)
        {
            return Unknown(input, "classification input did not include affected services", "missing-affected-services");
        }

        if (!input.ReleaseManifestDiffAvailable ||
            !input.ArtifactRefsBeforeAfterAvailable ||
            !input.SbomOrDependencyDiffAvailableWhenApplicable)
        {
            return Unknown(input, "classification input is missing release, artifact, or dependency diff evidence", "incomplete-diff-evidence");
        }

        var ownerships = input.ChangedPaths
            .Select(path => ResolveOwnership(path, input.ServiceOwnershipMap))
            .ToArray();
        var unknownPath = ownerships.FirstOrDefault(ownership => ownership.OwnerId == "unknown");
        if (unknownPath is not null)
        {
            return Unknown(input, $"changed path is not mapped by service ownership rules: {unknownPath.Path}", "unknown-path");
        }

        if (input.SpecialChange.IsRollback)
        {
            return ClassifyRollback(input);
        }

        if (input.SpecialChange.IsEmergencyChange)
        {
            return ClassifyEmergency(input, ownerships);
        }

        if (HasVotingCriticalChange(input, ownerships))
        {
            return Result(
                input,
                DeploymentImpactClasses.VotingProtocolChange,
                ["voting-critical-semantic-or-ref-change"],
                "Voting-critical path, semantic field, protocol/circuit/key ref, verifier/exporter ref, custody profile, or election-critical migration changed.",
                requiresManualOwnerReview: false,
                blocksAcceptedEvidence: false);
        }

        if (input.RefDiffs.ConfigProfileHashChanged == true)
        {
            if (!input.RefDiffs.ConfigProfileHashRecorded || MissingNoProtocolChangeProofs(input).Count > 0)
            {
                return Unknown(input, "operational config change is missing config hash or no-protocol-change proof", "incomplete-operational-config-proof");
            }

            return Result(
                input,
                DeploymentImpactClasses.OperationalConfigChange,
                ["operational-config-recorded"],
                "Operational config/profile hash changed with recorded config hash and voting-critical refs proven unchanged.",
                requiresManualOwnerReview: false,
                blocksAcceptedEvidence: false);
        }

        var missingNoChangeProofs = MissingNoProtocolChangeProofs(input);
        if (missingNoChangeProofs.Count > 0)
        {
            return Unknown(input, $"missing no-protocol-change proof: {string.Join(", ", missingNoChangeProofs)}", "missing-no-protocol-change-proof");
        }

        if (ownerships.All(ownership => ownership.OwnerId == "hush-web-client"))
        {
            return Result(
                input,
                DeploymentImpactClasses.WebsiteOnlyNoProtocolChange,
                ["website-only-no-protocol-change"],
                "Only HushVoting web-client presentation paths changed and voting-critical refs stayed unchanged.",
                requiresManualOwnerReview: false,
                blocksAcceptedEvidence: false);
        }

        if (ownerships.All(ownership => ownership.OwnerId == "non-voting-service"))
        {
            return Result(
                input,
                DeploymentImpactClasses.NonVotingServiceNoProtocolChange,
                ["non-voting-service-no-protocol-change"],
                "Only mapped non-voting service paths changed and voting-critical refs stayed unchanged.",
                requiresManualOwnerReview: false,
                blocksAcceptedEvidence: false);
        }

        if (ownerships.All(ownership => ownership.OwnerId is "deployment-config" or "hush-server-node-voting") &&
            input.RefDiffs.BackendImageDigestChanged == true)
        {
            return Result(
                input,
                DeploymentImpactClasses.VotingProtocolNoChange,
                ["server-deployment-no-protocol-change"],
                "Server deployment artifact changed, but protocol, verifier/exporter, custody, database, and deployment-profile refs stayed unchanged.",
                requiresManualOwnerReview: false,
                blocksAcceptedEvidence: false);
        }

        return Unknown(input, "classification did not match a safe accepted rule", "default-unknown-policy");
    }

    private static DeploymentImpactClassificationResult ClassifyEmergency(
        DeploymentImpactClassificationInput input,
        IReadOnlyList<PathOwnership> ownerships)
    {
        if (string.IsNullOrWhiteSpace(input.SpecialChange.Reason) ||
            input.SpecialChange.RerunChecks.Count == 0 ||
            string.IsNullOrWhiteSpace(input.SpecialChange.AccountabilityMarker) ||
            !input.SpecialChange.IsNonStateBreakingFix)
        {
            return Unknown(input, "emergency change is missing reason, rerun checks, accountability marker, or non-state-breaking proof", "incomplete-emergency-proof");
        }

        if (HasVotingCriticalChange(input, ownerships))
        {
            return Result(
                input,
                DeploymentImpactClasses.VotingProtocolChange,
                ["emergency-voting-critical-change"],
                "Emergency change touched voting-critical semantics or refs and must be treated as voting protocol change.",
                requiresManualOwnerReview: true,
                blocksAcceptedEvidence: false);
        }

        var missingNoChangeProofs = MissingNoProtocolChangeProofs(input);
        if (missingNoChangeProofs.Count > 0)
        {
            return Unknown(input, $"emergency change is missing no-protocol-change proof: {string.Join(", ", missingNoChangeProofs)}", "missing-emergency-no-change-proof");
        }

        return Result(
            input,
            DeploymentImpactClasses.EmergencyChange,
            ["emergency-non-state-breaking"],
            "Emergency change includes reason, before/after refs, rerun checks, accountability marker, and non-state-breaking proof.",
            requiresManualOwnerReview: false,
            blocksAcceptedEvidence: false);
    }

    private static DeploymentImpactClassificationResult ClassifyRollback(DeploymentImpactClassificationInput input)
    {
        if (!input.SpecialChange.RollbackToLastCeremonyApprovedArtifactSet ||
            !input.SpecialChange.StateCompatibilityEvidenceAvailable ||
            input.SpecialChange.RerunChecks.Count == 0 ||
            string.IsNullOrWhiteSpace(input.SpecialChange.AccountabilityMarker))
        {
            return Unknown(input, "rollback is missing approved artifact set, state compatibility evidence, rerun checks, or accountability marker", "incomplete-rollback-proof");
        }

        return Result(
            input,
            DeploymentImpactClasses.Rollback,
            ["rollback-to-approved-artifact-set"],
            "Rollback targets the last ceremony-approved immutable artifact set and includes state compatibility evidence plus rerun checks.",
            requiresManualOwnerReview: false,
            blocksAcceptedEvidence: false);
    }

    private static bool HasVotingCriticalChange(
        DeploymentImpactClassificationInput input,
        IReadOnlyList<PathOwnership> ownerships) =>
        ownerships.Any(ownership => ownership.OwnerId is "hush-server-node-voting" or "protocol-omega" or "custody") ||
        input.RefDiffs.ProtocolPackageHashChanged == true ||
        input.RefDiffs.CircuitOrKeyRefChanged == true ||
        input.RefDiffs.BackendVotingCriticalHashChanged == true ||
        input.RefDiffs.VerifierOrExporterHashChanged == true ||
        input.RefDiffs.DbMigrationStateChanged == true ||
        input.RefDiffs.CustodyProfileChanged == true ||
        input.SemanticChanges.BallotDefinitionChanged ||
        input.SemanticChanges.EligibilityOrCheckoffChanged ||
        input.SemanticChanges.CustodySemanticsChanged ||
        input.SemanticChanges.AcceptedBallotSemanticsChanged ||
        input.SemanticChanges.PublishedEvidenceSemanticsChanged ||
        input.SemanticChanges.TallyOrCountingLogicChanged ||
        input.SemanticChanges.VerifierOutputSemanticsChanged ||
        input.SemanticChanges.FinalPackageSchemaChanged ||
        input.SemanticChanges.ElectionCriticalDbMigrationChanged;

    private static IReadOnlyList<string> MissingNoProtocolChangeProofs(DeploymentImpactClassificationInput input)
    {
        var missing = new List<string>();
        AddMissingIfNull(input.RefDiffs.ProtocolPackageHashChanged, "protocolPackageHash", missing);
        AddMissingIfNull(input.RefDiffs.CircuitOrKeyRefChanged, "circuitOrKeyRef", missing);
        AddMissingIfNull(input.RefDiffs.BackendVotingCriticalHashChanged, "backendVotingCriticalHash", missing);
        AddMissingIfNull(input.RefDiffs.VerifierOrExporterHashChanged, "verifierOrExporterHash", missing);
        AddMissingIfNull(input.RefDiffs.DbMigrationStateChanged, "dbMigrationState", missing);
        AddMissingIfNull(input.RefDiffs.CustodyProfileChanged, "custodyProfile", missing);
        AddMissingIfNull(input.RefDiffs.DeploymentProfileChanged, "deploymentProfile", missing);

        if (input.RefDiffs.ProtocolPackageHashChanged == true ||
            input.RefDiffs.CircuitOrKeyRefChanged == true ||
            input.RefDiffs.BackendVotingCriticalHashChanged == true ||
            input.RefDiffs.VerifierOrExporterHashChanged == true ||
            input.RefDiffs.DbMigrationStateChanged == true ||
            input.RefDiffs.CustodyProfileChanged == true ||
            input.RefDiffs.DeploymentProfileChanged == true)
        {
            missing.Add("changed-voting-critical-ref");
        }

        return missing;
    }

    private static void AddMissingIfNull(bool? value, string name, List<string> missing)
    {
        if (value is null)
        {
            missing.Add(name);
        }
    }

    private static PathOwnership ResolveOwnership(
        string path,
        IReadOnlyDictionary<string, IReadOnlyList<string>> serviceOwnershipMap)
    {
        var normalizedPath = path.Replace('\\', '/');
        foreach (var (ownerId, patterns) in serviceOwnershipMap)
        {
            if (patterns.Any(pattern => MatchesPattern(normalizedPath, pattern)))
            {
                return new PathOwnership(normalizedPath, ownerId);
            }
        }

        return new PathOwnership(normalizedPath, "unknown");
    }

    private static bool MatchesPattern(string normalizedPath, string pattern)
    {
        var normalizedPattern = pattern.Replace('\\', '/');
        if (normalizedPattern.EndsWith("/**", StringComparison.Ordinal))
        {
            return normalizedPath.StartsWith(normalizedPattern[..^3], StringComparison.Ordinal);
        }

        if (normalizedPattern.Contains('*', StringComparison.Ordinal))
        {
            var expression = "^" + System.Text.RegularExpressions.Regex
                .Escape(normalizedPattern)
                .Replace("\\*\\*", ".*", StringComparison.Ordinal)
                .Replace("\\*", "[^/]*", StringComparison.Ordinal) + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(normalizedPath, expression);
        }

        return string.Equals(normalizedPath, normalizedPattern, StringComparison.Ordinal);
    }

    private static DeploymentImpactClassificationResult Unknown(
        DeploymentImpactClassificationInput input,
        string reason,
        string matchedRule) =>
        Result(
            input,
            DeploymentImpactClasses.UnknownPendingClassification,
            [matchedRule],
            reason,
            requiresManualOwnerReview: true,
            blocksAcceptedEvidence: true);

    private static DeploymentImpactClassificationResult Result(
        DeploymentImpactClassificationInput input,
        string outputClass,
        IReadOnlyList<string> matchedRules,
        string reason,
        bool requiresManualOwnerReview,
        bool blocksAcceptedEvidence) =>
        new(
            $"DIC-{input.ClassificationInputId}",
            outputClass,
            matchedRules,
            input.EvidenceRefs,
            reason,
            requiresManualOwnerReview,
            blocksAcceptedEvidence,
            input.SpecialChange.AccountabilityMarker ?? "classifier-rule-derived");

    private sealed record PathOwnership(string Path, string OwnerId);
}
