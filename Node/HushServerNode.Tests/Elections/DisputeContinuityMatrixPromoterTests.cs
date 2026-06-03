using System.Text.Json.Nodes;
using DisputeContinuityMatrixPromoter;
using FluentAssertions;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class DisputeContinuityMatrixPromoterTests
{
    private static readonly DateTimeOffset FixedGeneratedAt = DateTimeOffset.Parse("2026-06-03T12:00:00Z");

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();

        var errors = DisputeContinuityMatrixContracts.ValidateSchemaSet(workspace.Paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in DisputeContinuityMatrixContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(workspace.Paths.SchemasRoot, schemaFile)).Should().BeTrue(schemaFile);
        }
    }

    [Fact]
    public void SourceValidation_AcceptedReleaseBaseline_ShouldPass()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();

        var result = CreateService().Promote(new(
            workspace.Paths,
            DisputeContinuityMatrixPromotionService.ModeValidateOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        result.Mode.Should().Be(DisputeContinuityMatrixPromotionService.ModeValidateOnly);
        result.Status.Should().Be("accepted");
        result.WrittenFiles.Should().BeEmpty();
        Directory.Exists(workspace.DefaultPackageRoot).Should().BeFalse();
    }

    [Fact]
    public void SourceValidation_StaleReadinessBaseline_IsRejected()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();
        var source = workspace.LoadSource();
        source["readinessBaseline"]!.AsObject()["registerVersion"] = "RDY-REG-v0.1.6";

        var errors = DisputeContinuityMatrixContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("DCM165_STALE_READINESS_BASELINE", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_WrongScoreProposal_IsRejected()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();
        var source = workspace.LoadSource();
        source["scoreProposal"]!.AsObject()["movement"] = "8 -> 10";

        var errors = DisputeContinuityMatrixContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("DCM165_SCORE_PROPOSAL_OVERCLAIM", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_DirectRegisterMutation_IsRejected()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();
        var source = workspace.LoadSource();
        source["scoreProposal"]!.AsObject()["directRegisterMutation"] = true;

        var errors = DisputeContinuityMatrixContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("DCM165_DIRECT_REGISTER_MUTATION_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_StaleFeat139AcceptedAsCurrent_IsRejected()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();
        var source = workspace.LoadSource();
        var feat139 = FindUpstream(source, "FEAT-139");
        feat139["status"] = "accepted-current";
        feat139["freshness"] = "accepted current evidence";

        var errors = DisputeContinuityMatrixContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("DCM165_STALE_FEAT139_ACCEPTED_AS_CURRENT", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_MissingAcceptedFeat155_IsRejected()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();
        var source = workspace.LoadSource();
        var upstream = source["upstreamEvidence"]!.AsArray();
        var feat155 = upstream.OfType<JsonObject>()
            .First(item => DisputeContinuityMatrixContracts.GetString(item, "featureId") == "FEAT-155");
        upstream.Remove(feat155);

        var errors = DisputeContinuityMatrixContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("DCM165_UPSTREAM_CURRENTNESS_BLOCKED: FEAT-155", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_MissingScenarioFamily_IsRejected()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();
        var source = workspace.LoadSource();
        var families = source["scenarioFamilies"]!.AsArray();
        families.Remove(FindScenarioFamily(source, "void"));

        var errors = DisputeContinuityMatrixContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("DCM165_SCENARIO_FAMILY_MISSING", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_PrivatePath_IsRejected()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();
        var source = workspace.LoadSource();
        var feat138 = FindUpstream(source, "FEAT-138");
        feat138["evidenceRefs"]!.AsArray()[0]!.AsObject()["ref"] =
            "PrivateServer_ElectronicVoting/Dispute-Continuity-Scenario-Matrix/raw-dispute.json";

        var errors = DisputeContinuityMatrixContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("DCM165_PRIVATE_PATH_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_MissingReplacementSupersessionGate_IsRejected()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();
        var source = workspace.LoadSource();
        var replacement = FindScenarioFamily(source, "replacement-publication");
        RemoveStringArrayValue(replacement, "expectedResultCodes", "superseded_package_not_current");

        var errors = DisputeContinuityMatrixContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("DCM165_RESULT_CODE_MISSING", StringComparison.Ordinal));
        errors.Should().Contain(error => error.Contains("DCM165_REPLACEMENT_PUBLICATION_INVALID", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_VerifierChallengeUnknownResultCode_IsRejected()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();
        var source = workspace.LoadSource();
        var challenge = FindScenarioFamily(source, "verifier-challenge");
        challenge["expectedResultCodes"]!.AsArray().Add("verifier_challenge_future_unknown");

        var errors = DisputeContinuityMatrixContracts.ValidateSource(
            source,
            publicOnly: true,
            DisputeContinuityMatrixContracts.LoadResultCodeSeverities(workspace.Paths));

        errors.Should().Contain(error => error.Contains("DCM165_RESULT_CODE_UNKNOWN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_CustomerRemedyOverclaim_IsRejected()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();
        var source = workspace.LoadSource();
        var boundary = FindScenarioFamily(source, "customer-remedy-boundary");
        boundary["publicSafeSummary"] =
            "Hush technical proof remains separate from customer-owned legal remedy decisions. AGM management is accepted.";

        var errors = DisputeContinuityMatrixContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("DCM165_OVERCLAIM_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void ResultCodeCatalog_MissingWarningSeverity_IsRejected()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();
        var catalogPath = Path.Combine(
            workspace.Paths.ScenariosRoot,
            DisputeContinuityMatrixPromotionPaths.ResultCodesFileName);
        var catalog = DisputeContinuityMatrixContracts.ReadJsonObject(catalogPath, "result-code catalog");
        var warningCode = catalog["resultCodes"]!.AsArray()
            .OfType<JsonObject>()
            .First(item => DisputeContinuityMatrixContracts.GetString(item, "code") == "scenario_matrix_warning");
        warningCode["severity"] = "unknown-state";
        File.WriteAllText(catalogPath, DisputeContinuityMatrixContracts.CanonicalJson(catalog));

        var act = () => DisputeContinuityMatrixContracts.ValidateForPromotion(
            workspace.Paths,
            sourceInput: null,
            publicOnly: true);

        act.Should().Throw<DisputeContinuityMatrixPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("DCM165_RESULT_CODE_SEVERITY_MISSING", StringComparison.Ordinal));
    }

    [Fact]
    public void NegativeFixtureCatalog_ReferencesKnownResultCodes()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();

        var errors = DisputeContinuityMatrixContracts.ValidateForPromotion(
            workspace.Paths,
            sourceInput: null,
            publicOnly: true);

        errors["featureId"]!.GetValue<string>().Should().Be(DisputeContinuityMatrixContracts.FeatureId);
    }

    [Fact]
    public void NegativeFixtureCatalog_CoversPhase4FailureCases()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();
        var catalog = DisputeContinuityMatrixContracts.ReadJsonObject(
            Path.Combine(workspace.Paths.ExamplesRoot, "negative", DisputeContinuityMatrixPromotionPaths.NegativeFixtureCatalogFileName),
            "negative fixture catalog");
        var caseIds = catalog["cases"]!.AsArray()
            .OfType<JsonObject>()
            .Select(item => DisputeContinuityMatrixContracts.GetString(item, "id"))
            .ToHashSet(StringComparer.Ordinal);

        caseIds.Should().Contain(["missing-void-evidence", "replay-binding-mismatch", "legal-sufficiency-overclaim", "direct-register-mutation-attempt"]);
    }

    [Fact]
    public void Promotion_PackageMode_WritesCurrentnessSummary()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();

        var result = CreateService().Promote(new(
            workspace.Paths,
            DisputeContinuityMatrixPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        result.WrittenFiles.Should().HaveCount(RequiredGeneratedArtifactCount);
        var summaryPath = Path.Combine(
            workspace.DefaultPackageRoot,
            DisputeContinuityMatrixArtifactGenerator.UpstreamCurrentnessSummaryPath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(summaryPath).Should().BeTrue();

        var summary = DisputeContinuityMatrixContracts.ReadJsonObject(summaryPath, "currentness summary");
        summary["readinessBaseline"]!.AsObject()["dimension"]!.GetValue<string>().Should().Be("RDY-DIM-009");
        summary["scoreProposal"]!.AsObject()["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void Promotion_PackageMode_WritesPhase4Summaries()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();

        CreateService().Promote(new(
            workspace.Paths,
            DisputeContinuityMatrixPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        var scenarioSummary = ReadGeneratedArtifact(workspace, DisputeContinuityMatrixArtifactGenerator.ScenarioCoverageSummaryPath);
        scenarioSummary["coveredFamilyCount"]!.GetValue<int>().Should().Be(DisputeContinuityMatrixContracts.RequiredScenarioFamilies.Length);
        scenarioSummary["missingFamilies"]!.AsArray().Should().BeEmpty();

        var verifierSummary = ReadGeneratedArtifact(workspace, DisputeContinuityMatrixArtifactGenerator.VerifierChallengeSummaryPath);
        verifierSummary["unknownResultFailsClosed"]!.GetValue<bool>().Should().BeTrue();
        verifierSummary["replayMismatchFailsClosed"]!.GetValue<bool>().Should().BeTrue();

        var replacementSummary = ReadGeneratedArtifact(workspace, DisputeContinuityMatrixArtifactGenerator.ReplacementPublicationSummaryPath);
        replacementSummary["supersededPackageNotCurrent"]!.GetValue<bool>().Should().BeTrue();
        replacementSummary["replayMismatchFailsClosed"]!.GetValue<bool>().Should().BeTrue();

        var customerSummary = ReadGeneratedArtifact(workspace, DisputeContinuityMatrixArtifactGenerator.CustomerRemedyBoundarySummaryPath);
        customerSummary["legalSufficiencyNotClaimed"]!.GetValue<bool>().Should().BeTrue();
        customerSummary["restrictedLegalContentRequired"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void Promotion_PackageMode_WritesPhase5PackageAndRestrictedIndex()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();

        CreateService().Promote(new(
            workspace.Paths,
            DisputeContinuityMatrixPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        var manifest = ReadGeneratedArtifact(workspace, DisputeContinuityMatrixArtifactGenerator.ManifestPath);
        manifest["schemaVersion"]!.GetValue<string>().Should().Be("dispute-continuity-matrix-package-manifest/v1");
        manifest["artifactRefs"]!.AsArray()
            .OfType<JsonObject>()
            .Select(item => DisputeContinuityMatrixContracts.GetString(item, "path"))
            .Should()
            .Contain([
                DisputeContinuityMatrixArtifactGenerator.PackageIndexPath,
                DisputeContinuityMatrixArtifactGenerator.NoSecretScanResultPath,
                DisputeContinuityMatrixArtifactGenerator.RestrictedEvidenceIndexNotePath,
            ]);

        var package = ReadGeneratedArtifact(workspace, DisputeContinuityMatrixArtifactGenerator.PackageIndexPath);
        package["proposalOnly"]!.AsObject()["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();
        package["restrictedEvidenceRefs"]!.AsArray().Should().NotBeEmpty();

        var scan = ReadGeneratedArtifact(workspace, DisputeContinuityMatrixArtifactGenerator.NoSecretScanResultPath);
        scan["status"]!.GetValue<string>().Should().Be("pass");
        scan["unexpectedFindingCount"]!.GetValue<int>().Should().Be(0);

        var publicOnly = ReadGeneratedArtifact(workspace, DisputeContinuityMatrixArtifactGenerator.PublicOnlyValidationSummaryPath);
        publicOnly["privateCheckoutRequired"]!.GetValue<bool>().Should().BeFalse();
        publicOnly["credentialsRequired"]!.GetValue<bool>().Should().BeFalse();

        var restricted = ReadRestrictedArtifact(workspace, DisputeContinuityMatrixArtifactGenerator.PrivateRestrictedEvidenceIndexPath);
        restricted["payloadsPublishedHere"]!.GetValue<bool>().Should().BeFalse();
        restricted["restrictedRefs"]!.AsArray()
            .OfType<JsonObject>()
            .Should()
            .OnlyContain(item => item["payloadPublished"]!.GetValue<bool>() == false);
    }

    [Fact]
    public void Promotion_PackageMode_WritesPhase6ReadinessAndHandoffArtifacts()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();

        CreateService().Promote(new(
            workspace.Paths,
            DisputeContinuityMatrixPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        var readiness = ReadGeneratedArtifact(workspace, DisputeContinuityMatrixArtifactGenerator.ReadinessFragmentPath);
        readiness["dimension"]!.GetValue<string>().Should().Be(DisputeContinuityMatrixContracts.TargetDimensionId);
        readiness["proposedScore"]!.GetValue<int>().Should().Be(9);
        readiness["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();

        var scoreProposal = ReadGeneratedArtifact(workspace, DisputeContinuityMatrixArtifactGenerator.ScoreProposalPath);
        scoreProposal["movement"]!.GetValue<string>().Should().Be(DisputeContinuityMatrixContracts.AllowedScoreMovement);
        scoreProposal["notReplayedScoreMovements"]!.AsArray()
            .Select(item => item!.GetValue<string>())
            .Should()
            .Contain(["FEAT-138 owner void 4 -> 6", "FEAT-155 failed-finalize continuity 6 -> 8"]);
        scoreProposal["forbiddenMovements"]!.AsArray()
            .Select(item => item!.GetValue<string>())
            .Should()
            .Contain("8 -> 10");

        var handoff = ReadGeneratedArtifact(workspace, DisputeContinuityMatrixArtifactGenerator.DownstreamHandoffPath);
        handoff["targetFeature"]!.GetValue<string>().Should().Be("FEAT-166");
        handoff["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();
        handoff["restrictedNoPayloadRefs"]!.AsArray().Should().NotBeEmpty();

        var claimBoundary = ReadGeneratedArtifact(workspace, DisputeContinuityMatrixArtifactGenerator.ClaimBoundaryReviewPath);
        claimBoundary["publicStateElectionReadinessClaimed"]!.GetValue<bool>().Should().BeFalse();
        claimBoundary["productionRolloutApprovalClaimed"]!.GetValue<bool>().Should().BeFalse();
        claimBoundary["legalSufficiencyClaimed"]!.GetValue<bool>().Should().BeFalse();
        claimBoundary["agmManagementClaimed"]!.GetValue<bool>().Should().BeFalse();
        claimBoundary["externalAuditAcceptanceClaimed"]!.GetValue<bool>().Should().BeFalse();
        claimBoundary["certificationClaimed"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void Promotion_CheckOnly_ShouldPassAfterPackageMode()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();
        var service = CreateService();
        service.Promote(new(
            workspace.Paths,
            DisputeContinuityMatrixPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        var result = service.Promote(new(
            workspace.Paths,
            DisputeContinuityMatrixPromotionService.ModeCheckOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        result.CheckedFiles.Should().HaveCount(RequiredGeneratedArtifactCount);
    }

    [Fact]
    public void Promotion_CheckOnly_DetectsCurrentnessSummaryDrift()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();
        var service = CreateService();
        service.Promote(new(
            workspace.Paths,
            DisputeContinuityMatrixPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));
        File.WriteAllText(
            Path.Combine(
                workspace.DefaultPackageRoot,
                DisputeContinuityMatrixArtifactGenerator.UpstreamCurrentnessSummaryPath.Replace('/', Path.DirectorySeparatorChar)),
            "drifted summary");

        var act = () => service.Promote(new(
            workspace.Paths,
            DisputeContinuityMatrixPromotionService.ModeCheckOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        act.Should().Throw<DisputeContinuityMatrixPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains(DisputeContinuityMatrixArtifactGenerator.UpstreamCurrentnessSummaryPath, StringComparison.Ordinal));
    }

    [Fact]
    public void Promotion_CheckOnly_DetectsManifestDrift()
    {
        using var workspace = TempDisputeContinuityMatrixWorkspace.Create();
        var service = CreateService();
        service.Promote(new(
            workspace.Paths,
            DisputeContinuityMatrixPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));
        File.WriteAllText(
            Path.Combine(
                workspace.DefaultPackageRoot,
                DisputeContinuityMatrixArtifactGenerator.ManifestPath.Replace('/', Path.DirectorySeparatorChar)),
            "drifted manifest");

        var act = () => service.Promote(new(
            workspace.Paths,
            DisputeContinuityMatrixPromotionService.ModeCheckOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        act.Should().Throw<DisputeContinuityMatrixPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains(DisputeContinuityMatrixArtifactGenerator.ManifestPath, StringComparison.Ordinal));
    }

    private static int RequiredGeneratedArtifactCount =>
        DisputeContinuityMatrixArtifactGenerator.RequiredArtifactPaths.Length +
        DisputeContinuityMatrixArtifactGenerator.RequiredRestrictedArtifactPaths.Length;

    private static DisputeContinuityMatrixPromotionService CreateService() => new();

    private static JsonObject FindUpstream(JsonObject source, string featureId) =>
        source["upstreamEvidence"]!.AsArray()
            .OfType<JsonObject>()
            .First(item => DisputeContinuityMatrixContracts.GetString(item, "featureId") == featureId);

    private static JsonObject FindScenarioFamily(JsonObject source, string familyName) =>
        source["scenarioFamilies"]!.AsArray()
            .OfType<JsonObject>()
            .First(item => DisputeContinuityMatrixContracts.GetString(item, "family") == familyName);

    private static void RemoveStringArrayValue(JsonObject obj, string propertyName, string value)
    {
        var array = obj[propertyName]!.AsArray();
        var match = array.First(item => item?.GetValue<string>() == value);
        array.Remove(match);
    }

    private static JsonObject ReadGeneratedArtifact(TempDisputeContinuityMatrixWorkspace workspace, string relativePath) =>
        DisputeContinuityMatrixContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultPackageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            relativePath);

    private static JsonObject ReadRestrictedArtifact(TempDisputeContinuityMatrixWorkspace workspace, string relativePath) =>
        DisputeContinuityMatrixContracts.ReadJsonObject(
            Path.Combine(workspace.RestrictedIndexRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            relativePath);

    private sealed class TempDisputeContinuityMatrixWorkspace : IDisposable
    {
        private TempDisputeContinuityMatrixWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "feat165-dispute-continuity-matrix-" + Guid.NewGuid().ToString("N"));
            Paths = DisputeContinuityMatrixPromotionPaths.FromWorkspaceRoot(Root);
            OutputRoot = Path.Combine(Root, "package-output");
            CopyDirectory(FindSourcePublicRepositoryRoot(), Paths.PublicRepositoryRoot);
        }

        public string Root { get; }

        public DisputeContinuityMatrixPromotionPaths Paths { get; }

        public string OutputRoot { get; }

        public string DefaultPackageRoot => Path.Combine(
            OutputRoot,
            DisputeContinuityMatrixPromotionPaths.PackageFamilyFolder,
            DisputeContinuityMatrixPromotionPaths.DefaultMatrixRunId);

        public string RestrictedIndexRoot => Paths.RestrictedEvidenceIndexRoot;

        public static TempDisputeContinuityMatrixWorkspace Create() => new();

        public JsonObject LoadSource() => DisputeContinuityMatrixContracts.LoadSource(Paths);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string FindSourcePublicRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "Dispute-Continuity-Matrix");
                if (Directory.Exists(candidate) &&
                    File.Exists(Path.Combine(candidate, "examples", "release-baseline", DisputeContinuityMatrixPromotionPaths.SourceFileName)))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Unable to locate Dispute-Continuity-Matrix public repository.");
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(source, file);
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }
    }
}
