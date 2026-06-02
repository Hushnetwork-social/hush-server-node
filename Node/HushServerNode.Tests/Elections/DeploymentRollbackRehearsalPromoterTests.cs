using System.Text.Json.Nodes;
using DeploymentRollbackRehearsalPromoter;
using FluentAssertions;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class DeploymentRollbackRehearsalPromoterTests
{
    private static readonly DateTimeOffset FixedGeneratedAt = DateTimeOffset.Parse("2026-06-02T12:00:00Z");

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        using var workspace = TempDeploymentRollbackWorkspace.Create();

        var errors = DeploymentRollbackRehearsalContracts.ValidateSchemaSet(workspace.Paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in DeploymentRollbackRehearsalContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(workspace.Paths.SchemasRoot, schemaFile)).Should().BeTrue(schemaFile);
        }
    }

    [Fact]
    public void ReleaseBaseline_SourceAndCurrentRefs_AreValid()
    {
        using var workspace = TempDeploymentRollbackWorkspace.Create();
        var source = workspace.LoadSource();

        var errors = DeploymentRollbackRehearsalContracts.ValidateSource(source)
            .Concat(DeploymentRollbackRehearsalContracts.ValidateCurrentRefs(workspace.Paths, source))
            .ToArray();

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Promotion_ValidateOnly_ShouldNotWritePackageRoot()
    {
        using var workspace = TempDeploymentRollbackWorkspace.Create();

        var result = CreateService().Promote(new(
            workspace.Paths,
            DeploymentRollbackRehearsalPromotionService.ModeValidateOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        result.Mode.Should().Be(DeploymentRollbackRehearsalPromotionService.ModeValidateOnly);
        result.WrittenFiles.Should().BeEmpty();
        result.CheckedFiles.Should().BeEmpty();
        result.GeneratedPackage.Artifacts.Should().HaveCount(DeploymentRollbackRehearsalArtifactGenerator.RequiredArtifactPaths.Length);
        result.GeneratedPackage.SecondCeremonyArtifacts.Should().HaveCount(DeploymentRollbackRehearsalArtifactGenerator.RequiredSecondCeremonyArtifactPaths.Length);
        Directory.Exists(workspace.DefaultOutputPackageRoot).Should().BeFalse();
        Directory.Exists(workspace.DefaultOutputSecondCeremonyRoot).Should().BeFalse();
    }

    [Fact]
    public void Promotion_PublicOnlyValidateOnly_ShouldNotRequirePrivateContextRepos()
    {
        using var workspace = TempDeploymentRollbackWorkspace.Create();
        Directory.Delete(Path.Combine(workspace.Root, "hush-memory-bank"), recursive: true);
        Directory.Delete(Path.Combine(workspace.Root, "hush-documents"), recursive: true);

        var result = CreateService().Promote(new(
            workspace.Paths,
            DeploymentRollbackRehearsalPromotionService.ModeValidateOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        result.Mode.Should().Be(DeploymentRollbackRehearsalPromotionService.ModeValidateOnly);
        result.WrittenFiles.Should().BeEmpty();
        result.GeneratedPackage.Artifacts.Should().HaveCount(DeploymentRollbackRehearsalArtifactGenerator.RequiredArtifactPaths.Length);
        result.GeneratedPackage.SecondCeremonyArtifacts.Should().HaveCount(DeploymentRollbackRehearsalArtifactGenerator.RequiredSecondCeremonyArtifactPaths.Length);
    }

    [Fact]
    public void Promotion_PackageMode_WritesDeterministicDeploymentRollbackArtifacts()
    {
        using var workspace = TempDeploymentRollbackWorkspace.Create();
        var service = CreateService();

        var first = service.Promote(new(
            workspace.Paths,
            DeploymentRollbackRehearsalPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));
        var second = service.Promote(new(
            workspace.Paths,
            DeploymentRollbackRehearsalPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        first.Status.Should().Be("candidate");
        first.WrittenFiles.Should().HaveCount(
            DeploymentRollbackRehearsalArtifactGenerator.RequiredArtifactPaths.Length +
            DeploymentRollbackRehearsalArtifactGenerator.RequiredSecondCeremonyArtifactPaths.Length);
        first.GeneratedPackage.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content))
            .Should()
            .Equal(second.GeneratedPackage.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content)));
        first.GeneratedPackage.SecondCeremonyArtifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content))
            .Should()
            .Equal(second.GeneratedPackage.SecondCeremonyArtifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content)));

        foreach (var relativePath in DeploymentRollbackRehearsalArtifactGenerator.RequiredArtifactPaths)
        {
            File.Exists(Path.Combine(workspace.DefaultOutputPackageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Should()
                .BeTrue(relativePath);
        }

        foreach (var relativePath in DeploymentRollbackRehearsalArtifactGenerator.RequiredSecondCeremonyArtifactPaths)
        {
            File.Exists(Path.Combine(workspace.DefaultOutputSecondCeremonyRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Should()
                .BeTrue(relativePath);
        }

        var manifest = DeploymentRollbackRehearsalContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultOutputPackageRoot, DeploymentRollbackRehearsalArtifactGenerator.ManifestPath),
            "generated manifest");
        manifest["baselineRegister"]!.AsObject()["proposedScoreTo"]!.GetValue<int>().Should().Be(9);
        manifest["publicSafety"]!.AsObject()["unexpectedFindingCount"]!.GetValue<int>().Should().Be(0);
        manifest["readinessProposal"]!.AsObject()["doesNotMutateRegister"]!.GetValue<bool>().Should().BeTrue();
        manifest["secondCeremony"]!.AsObject()["ceremonyId"]!.GetValue<string>()
            .Should()
            .Be(DeploymentRollbackRehearsalPromotionPaths.SecondCeremonyId);

        var webClientSummary = DeploymentRollbackRehearsalContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultOutputPackageRoot, DeploymentRollbackRehearsalArtifactGenerator.WebClientObservedProofSummaryPath),
            "webclient proof summary");
        webClientSummary["cases"]!.AsArray()
            .OfType<JsonObject>()
            .Single()["expectedResult"]!
            .GetValue<string>()
            .Should()
            .Be("degraded");

        var rollbackSummary = DeploymentRollbackRehearsalContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultOutputPackageRoot, DeploymentRollbackRehearsalArtifactGenerator.RollbackBindingSummaryPath),
            "rollback summary");
        rollbackSummary["cases"]!.AsArray()
            .OfType<JsonObject>()
            .Select(item => DeploymentRollbackRehearsalContracts.GetString(item, "scenarioId"))
            .Should()
            .Contain("DEPLOY-ROLLBACK-TO-LAST-ACCEPTED");
    }

    [Fact]
    public void Promotion_CheckOnly_DetectsExistingPackageDrift()
    {
        using var workspace = TempDeploymentRollbackWorkspace.Create();
        var service = CreateService();
        service.Promote(new(
            workspace.Paths,
            DeploymentRollbackRehearsalPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));
        File.WriteAllText(
            Path.Combine(workspace.DefaultOutputPackageRoot, DeploymentRollbackRehearsalArtifactGenerator.ReadmePath),
            "drifted package");

        var act = () => service.Promote(new(
            workspace.Paths,
            DeploymentRollbackRehearsalPromotionService.ModeCheckOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<DeploymentRollbackRehearsalPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains(DeploymentRollbackRehearsalArtifactGenerator.ReadmePath, StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_StaleReadinessBaseline_IsRejected()
    {
        using var workspace = TempDeploymentRollbackWorkspace.Create();
        var source = workspace.LoadSource();
        source["baselineRegister"]!.AsObject()["registerVersionId"] = "RDY-REG-v0.1.6";

        var errors = DeploymentRollbackRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT162_STALE_READINESS_BASELINE", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_ScoreOverclaimTo10_IsRejected()
    {
        using var workspace = TempDeploymentRollbackWorkspace.Create();
        var source = workspace.LoadSource();
        source["scorePolicy"]!.AsObject()["proposedScoreTo"] = 10;

        var errors = DeploymentRollbackRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT162_SCORE_OVERCLAIM_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_MissingWebClientObservedProofScenario_BlocksPromotion()
    {
        using var workspace = TempDeploymentRollbackWorkspace.Create();
        var source = workspace.LoadSource();
        RemoveScenario(source["rehearsalMatrix"]!.AsObject()["scenarios"]!.AsArray(), "DEPLOY-ROLLBACK-WEBCLIENT-OBSERVED-PROOF");

        var errors = DeploymentRollbackRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT162_REQUIRED_SCENARIO_MISSING", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_EmergencyChangePolicyMismatch_IsRejected()
    {
        using var workspace = TempDeploymentRollbackWorkspace.Create();
        var source = workspace.LoadSource();
        ScenarioById(source, "DEPLOY-ROLLBACK-EMERGENCY-OPEN-ELECTION")["expectedResult"] = "blocked";

        var errors = DeploymentRollbackRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT162_EMERGENCY_POLICY_INVALID", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_PrivateMaterialMarker_IsRejected()
    {
        using var workspace = TempDeploymentRollbackWorkspace.Create();
        var source = workspace.LoadSource();
        ScenarioById(source, "DEPLOY-ROLLBACK-TO-LAST-ACCEPTED")["description"] = "leaked arn:aws value";

        var errors = DeploymentRollbackRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT162_PRIVATE_MATERIAL_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void CurrentnessValidation_StaleFeat144Ref_BlocksPromotion()
    {
        using var workspace = TempDeploymentRollbackWorkspace.Create();
        var source = workspace.LoadSource();
        source["upstreamBaselines"]!.AsObject()["feat144"]!.AsObject()["referenceHash"] = HashFor("wrong-feat144");

        var errors = DeploymentRollbackRehearsalContracts.ValidateCurrentRefs(workspace.Paths, source);

        errors.Should().Contain(error => error.Contains("upstreamBaselines.feat144.referenceHash mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public void PublicSafetyScanner_RejectsGeneratedCredentialMarkers()
    {
        var artifacts = new[]
        {
            new DeploymentRollbackRehearsalArtifact("validation/example.json", "{\"value\":\"credential=leaked\"}\n"),
        };

        var findings = DeploymentRollbackRehearsalArtifactGenerator.ScanGeneratedArtifacts(artifacts);

        findings.Should().Contain(finding => finding.Contains("credential=", StringComparison.Ordinal));
    }

    private static DeploymentRollbackRehearsalPromotionService CreateService() => new();

    private static JsonObject ScenarioById(JsonObject source, string scenarioId) =>
        source["rehearsalMatrix"]!.AsObject()["scenarios"]!.AsArray()
            .OfType<JsonObject>()
            .Single(item => DeploymentRollbackRehearsalContracts.GetString(item, "scenarioId") == scenarioId);

    private static void RemoveScenario(JsonArray scenarios, string scenarioId)
    {
        for (var index = scenarios.Count - 1; index >= 0; index--)
        {
            if (scenarios[index] is JsonObject scenario &&
                DeploymentRollbackRehearsalContracts.GetString(scenario, "scenarioId") == scenarioId)
            {
                scenarios.RemoveAt(index);
            }
        }
    }

    private sealed class TempDeploymentRollbackWorkspace : IDisposable
    {
        private TempDeploymentRollbackWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "feat162-deployment-rollback-" + Guid.NewGuid().ToString("N"));
            Paths = DeploymentRollbackRehearsalPromotionPaths.FromWorkspaceRoot(Root);
            OutputRoot = Path.Combine(Root, "package-output");
            Directory.CreateDirectory(Paths.SchemasRoot);
            Directory.CreateDirectory(Path.Combine(Paths.ExamplesRoot, "release-baseline"));
            Directory.CreateDirectory(Paths.PublicProofPackagesRoot);
            WriteSchemas();
            var currentRefs = WriteCurrentnessFiles();
            WriteJson(Path.Combine(Paths.ExamplesRoot, "release-baseline", DeploymentRollbackRehearsalPromotionPaths.SourceFileName), BuildSource(currentRefs));
        }

        public string Root { get; }

        public DeploymentRollbackRehearsalPromotionPaths Paths { get; }

        public string OutputRoot { get; }

        public string DefaultOutputPackageRoot => Path.Combine(OutputRoot, DeploymentRollbackRehearsalPromotionPaths.PackageRelativeRoot);

        public string DefaultOutputSecondCeremonyRoot => Path.Combine(OutputRoot, DeploymentRollbackRehearsalPromotionPaths.SecondCeremonyRelativeRoot);

        public static TempDeploymentRollbackWorkspace Create() => new();

        public JsonObject LoadSource() => DeploymentRollbackRehearsalContracts.LoadSource(Paths);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private void WriteSchemas()
        {
            WriteJson(
                Path.Combine(Paths.SchemasRoot, DeploymentRollbackRehearsalPromotionPaths.SourceSchemaFileName),
                new JsonObject
                {
                    ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                    ["required"] = new JsonArray("schemaVersion"),
                });
            WriteJson(
                Path.Combine(Paths.SchemasRoot, DeploymentRollbackRehearsalPromotionPaths.PackageManifestSchemaFileName),
                new JsonObject
                {
                    ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                    ["required"] = new JsonArray("schemaVersion"),
                });
        }

        private CurrentRefs WriteCurrentnessFiles()
        {
            var readinessPath = Path.Combine(Paths.PublicProofPackagesRoot, "ceremonies", "DPC-REHEARSAL-20260519-001", "readiness-fragment.json");
            Directory.CreateDirectory(Path.GetDirectoryName(readinessPath)!);
            File.WriteAllText(readinessPath, "{\"id\":\"RDY-FRAG-AT-RDY-005-FEAT-132-001\"}\n");

            File.WriteAllText(
                Path.Combine(Paths.PublicProofPackagesRoot, "deployment-proof-catalog.json"),
                "{\"refs\":[\"DPC-REHEARSAL-20260519-001\",\"DPP-WEB-20260519-001\",\"DPP-SERVER-20260519-001\",\"DPS-REHEARSAL-20260519-001\",\"DPBL-REHEARSAL-20260519-001\"]}\n");

            var feat143Path = WriteWorkspaceFile(
                "hush-memory-bank/Features/04_COMPLETED/FEAT-143-runtime-deployment-proof-binding-ledger/readiness-handoff-20260526.md",
                "feat143 runtime binding handoff\n");
            var feat144Path = WriteWorkspaceFile(
                "hush-memory-bank/Features/04_COMPLETED/FEAT-144-hushwebclient-deployment-proof-exposure-handshake/FeatureDescription.md",
                "feat144 observed webclient proof\n");
            var feat154Path = WriteWorkspaceFile(
                "hush-memory-bank/Features/04_COMPLETED/FEAT-154-production-like-operational-run-evidence/feature-completion-report.md",
                "feat154 production-like context\n");
            var feat156Path = WriteWorkspaceFile(
                "hush-documents/PrivateServer_ElectronicVoting/Production-Rollout-Promotion-Register/package/feat156-package-manifest.json",
                "feat156 promotion owner\n");
            var feat161Path = WriteWorkspaceFile(
                "hush-memory-bank/Features/04_COMPLETED/FEAT-161-kms-custody-drift-rotation-recovery-rehearsal/feature-completion-report.md",
                "feat161 custody handoff\n");

            return new CurrentRefs(
                DeploymentRollbackRehearsalContracts.FileSha256Hex(readinessPath),
                DeploymentRollbackRehearsalContracts.FileSha256Hex(feat143Path),
                DeploymentRollbackRehearsalContracts.FileSha256Hex(feat144Path),
                DeploymentRollbackRehearsalContracts.FileSha256Hex(feat154Path),
                DeploymentRollbackRehearsalContracts.FileSha256Hex(feat156Path),
                DeploymentRollbackRehearsalContracts.FileSha256Hex(feat161Path));
        }

        private string WriteWorkspaceFile(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }
    }

    private static JsonObject BuildSource(CurrentRefs refs) =>
        new()
        {
            ["schemaVersion"] = DeploymentRollbackRehearsalContracts.SourceSchemaVersion,
            ["sourceId"] = "DEPLOYMENT-ROLLBACK-REHEARSAL-v0.1.0",
            ["producerFeature"] = DeploymentRollbackRehearsalContracts.FeatureId,
            ["status"] = "accepted",
            ["generatedAt"] = "2026-06-02T00:00:00Z",
            ["baselineRegister"] = new JsonObject
            {
                ["registerVersionId"] = DeploymentRollbackRehearsalContracts.CurrentRegisterId,
                ["registerVersion"] = DeploymentRollbackRehearsalContracts.CurrentRegisterVersion,
                ["status"] = "AcceptedInternal",
                ["totalScore"] = 80,
                ["internalAuditTargetScore"] = 95,
                ["dimensionId"] = DeploymentRollbackRehearsalContracts.TargetDimensionId,
                ["dimensionName"] = DeploymentRollbackRehearsalContracts.TargetDimensionName,
                ["currentScore"] = 8,
                ["proposedScore"] = 9,
                ["targetBlockerId"] = DeploymentRollbackRehearsalContracts.TargetBlockerId,
                ["blockerOwnerFeatureId"] = DeploymentRollbackRehearsalContracts.FeatureId,
            },
            ["upstreamBaselines"] = new JsonObject
            {
                ["feat132"] = new JsonObject
                {
                    ["producerFeature"] = "FEAT-132",
                    ["evidenceId"] = "RDY-EVID-AT-RDY-005-FEAT-132-001",
                    ["dimensionId"] = DeploymentRollbackRehearsalContracts.TargetDimensionId,
                    ["publicRepository"] = "https://github.com/Hushnetwork-social/Deployment-Proof-Packages",
                    ["publicBranch"] = "main",
                    ["publicCommit"] = "5ec9f5ee2418ddc5f72e5953089ac158d13105db",
                    ["ceremonyId"] = "DPC-REHEARSAL-20260519-001",
                    ["webClientProofId"] = "DPP-WEB-20260519-001",
                    ["serverProofId"] = "DPP-SERVER-20260519-001",
                    ["proofSetId"] = "DPS-REHEARSAL-20260519-001",
                    ["ledgerId"] = "DPBL-REHEARSAL-20260519-001",
                    ["readinessFragmentHash"] = refs.Feat132ReadinessHash,
                },
                ["feat143"] = Upstream("FEAT-143", "accepted_with_limitations", "runtime binding", refs.Feat143Hash),
                ["feat144"] = Upstream("FEAT-144", "completed", "observed webclient proof", refs.Feat144Hash),
                ["feat154"] = Upstream("FEAT-154", "completed", "production-like context", refs.Feat154Hash),
                ["feat156"] = Upstream("FEAT-156", "completed", "promotion owner", refs.Feat156Hash),
                ["feat161"] = Upstream("FEAT-161", "completed", "custody handoff", refs.Feat161Hash),
            },
            ["scorePolicy"] = new JsonObject
            {
                ["dimensionId"] = DeploymentRollbackRehearsalContracts.TargetDimensionId,
                ["proposedScoreFrom"] = 8,
                ["proposedScoreTo"] = 9,
                ["directRegisterMutation"] = false,
                ["doesNotMutateRegister"] = true,
                ["canonicalRegisterMutationOwner"] = "later_internal_audit_95_promotion_pass",
                ["scoreMovementBlockedUnlessAllCasesPass"] = true,
            },
            ["rehearsalMatrix"] = new JsonObject
            {
                ["defaultValidationMode"] = "deterministic_rehearsal_fixture",
                ["liveDeploymentRequiredForDefaultValidation"] = false,
                ["scenarios"] = new JsonArray(Scenario("DEPLOY-ROLLBACK-ACCEPTED-FEAT132-BASELINE", "accepted_baseline", "pass", "accepted_feat132_baseline_verified", "supports_score_proposal", ["DPC-REHEARSAL-20260519-001"]), Scenario("DEPLOY-ROLLBACK-SECOND-CEREMONY", "second_ceremony", "pass", "second_ceremony_hash_bound", "supports_score_proposal", ["DPC-REHEARSAL-20260602-002"]), Scenario("DEPLOY-ROLLBACK-NO-CHANGE-FREEZE", "no_change", "pass", "no_change_freeze_verified", "supports_score_proposal", []), Scenario("DEPLOY-ROLLBACK-NON-VOTING-CHANGE", "non_voting_change", "pass", "non_voting_change_classified", "supports_score_proposal", []), Scenario("DEPLOY-ROLLBACK-OPERATIONAL-CONFIG-CHANGE", "operational_config_change", "pass", "operational_config_rerun_checks_passed", "supports_score_proposal", []), Scenario("DEPLOY-ROLLBACK-TO-LAST-ACCEPTED", "rollback", "pass", "rollback_to_accepted_artifact_set_verified", "supports_score_proposal", ["DPC-REHEARSAL-20260519-001", "DPC-REHEARSAL-20260602-002"]), Scenario("DEPLOY-ROLLBACK-EMERGENCY-OPEN-ELECTION", "emergency_change", "pass", "emergency_change_rerun_checks_passed", "supports_score_proposal", []), Scenario("DEPLOY-ROLLBACK-WEBCLIENT-OBSERVED-PROOF", "webclient_observed_proof", "degraded", "webclient_observed_proof_limitation_preserved", "records_residual_risk", ["FEAT-144-observed-webclient-proof-handshake"]), Scenario("DEPLOY-ROLLBACK-CUSTODY-IMPACT-CHECK", "custody_impact", "pass", "custody_impact_handoff_current", "supports_score_proposal", ["kms-custody-rehearsal/v0.1.0"]), Scenario("DEPLOY-ROLLBACK-RESTRICTED-BOUNDARY", "restricted_boundary", "restricted_only", "restricted_boundary_preserved", "preserves_private_boundary", [])),
            },
            ["negativeMatrix"] = new JsonArray(DeploymentRollbackRehearsalContracts.RequiredNegativeCaseIds.Select(id => new JsonObject
            {
                ["caseId"] = id,
                ["mutation"] = id,
                ["expectedDiagnostic"] = id.Replace("NEG-", "FEAT162_", StringComparison.Ordinal),
            }).ToArray<JsonNode?>()),
            ["packageLayout"] = new JsonObject
            {
                ["targetPackagePath"] = DeploymentRollbackRehearsalContracts.ExpectedTargetPackagePath,
                ["expectedArtifacts"] = new JsonArray(DeploymentRollbackRehearsalArtifactGenerator.RequiredArtifactPaths.Select(item => JsonValue.Create(item)).ToArray<JsonNode?>()),
                ["expectedSecondCeremonyArtifacts"] = new JsonArray(DeploymentRollbackRehearsalArtifactGenerator.RequiredSecondCeremonyArtifactPaths.Select(item => JsonValue.Create(item)).ToArray<JsonNode?>()),
            },
            ["publicSafety"] = new JsonObject
            {
                ["publicOnlyValidation"] = true,
                ["forbiddenMaterialClasses"] = new JsonArray("deployment_credential", "private_runtime_marker"),
                ["liveCredentialRequiredForDefaultValidation"] = false,
                ["directPrivateRepoDependency"] = false,
            },
            ["restrictedEvidenceBoundary"] = new JsonObject
            {
                ["payloadPublished"] = false,
                ["restrictedIndexPath"] = "hush-documents/PrivateServer_ElectronicVoting/Deployment-Rollback-Rehearsal/package/restricted-evidence-index.json",
                ["allowedPublicFields"] = new JsonArray("refId", "visibility", "hash", "payloadPublished"),
            },
            ["readinessOutput"] = new JsonObject
            {
                ["readinessFragment"] = DeploymentRollbackRehearsalArtifactGenerator.ReadinessFragmentPath,
                ["scoreProposal"] = DeploymentRollbackRehearsalArtifactGenerator.ScoreProposalPath,
                ["directRegisterMutation"] = false,
            },
            ["downstreamConsumers"] = new JsonArray(new JsonObject { ["consumerId"] = "FEAT-163", ["role"] = "second run input" }, new JsonObject { ["consumerId"] = "FEAT-166", ["role"] = "governance handoff input" }),
            ["residualRisks"] = new JsonArray("webclient observed proof remains limited"),
        };

    private static JsonObject Upstream(string producerFeature, string status, string role, string hash) =>
        new()
        {
            ["producerFeature"] = producerFeature,
            ["status"] = status,
            ["role"] = role,
            ["referenceHash"] = hash,
        };

    private static JsonObject Scenario(
        string scenarioId,
        string category,
        string expectedResult,
        string safeResultCode,
        string readinessImpact,
        string[] proofRefs) =>
        new()
        {
            ["scenarioId"] = scenarioId,
            ["category"] = category,
            ["description"] = $"{scenarioId} public-safe description",
            ["gateIds"] = new JsonArray("AT-RDY-005"),
            ["expectedResult"] = expectedResult,
            ["safeResultCodes"] = new JsonArray(safeResultCode),
            ["readinessImpact"] = readinessImpact,
            ["proofRefs"] = new JsonArray(proofRefs.Select(item => JsonValue.Create(item)).ToArray<JsonNode?>()),
            ["restrictedEvidenceRefs"] = new JsonArray(RestrictedRef("RESTRICTED-" + scenarioId)),
            ["requiredForScore"] = true,
        };

    private static JsonObject RestrictedRef(string refId) =>
        new()
        {
            ["refId"] = refId,
            ["visibility"] = "restricted_reviewer",
            ["hash"] = HashFor(refId),
            ["payloadPublished"] = false,
        };

    private static void WriteJson(string path, JsonObject json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, DeploymentRollbackRehearsalContracts.CanonicalJson(json));
    }

    private static string HashFor(string value) => DeploymentRollbackRehearsalContracts.Sha256Hex(value + "\n");

    private sealed record CurrentRefs(
        string Feat132ReadinessHash,
        string Feat143Hash,
        string Feat144Hash,
        string Feat154Hash,
        string Feat156Hash,
        string Feat161Hash);
}
