using System.Text.Json.Nodes;
using FluentAssertions;
using SecondProductionLikeOperationalRunPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class SecondProductionLikeOperationalRunPromoterTests
{
    private static readonly DateTimeOffset FixedGeneratedAt = DateTimeOffset.Parse("2026-06-03T12:00:00Z");

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        using var workspace = TempSecondRunWorkspace.Create();

        var errors = SecondProductionLikeOperationalRunContracts.ValidateSchemaSet(workspace.Paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in SecondProductionLikeOperationalRunContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(workspace.Paths.SchemasRoot, schemaFile)).Should().BeTrue(schemaFile);
        }
    }

    [Fact]
    public void Promotion_ValidateOnly_ShouldNotWritePackageRoot()
    {
        using var workspace = TempSecondRunWorkspace.Create();

        var result = CreateService().Promote(new(
            workspace.Paths,
            SecondProductionLikeOperationalRunPromotionService.ModeValidateOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        result.Mode.Should().Be(SecondProductionLikeOperationalRunPromotionService.ModeValidateOnly);
        result.Status.Should().Be("draft");
        result.WrittenFiles.Should().BeEmpty();
        result.CheckedFiles.Should().BeEmpty();
        result.GeneratedPackage.Artifacts.Should().HaveCount(SecondProductionLikeOperationalRunArtifactGenerator.RequiredArtifactPaths.Length);
        Directory.Exists(workspace.DefaultPackageRoot).Should().BeFalse();
    }

    [Fact]
    public void Promotion_PublicOnlyValidateOnly_ShouldNotRequirePrivateContextRepos()
    {
        using var workspace = TempSecondRunWorkspace.Create();
        Directory.Delete(Path.Combine(workspace.Root, "hush-memory-bank"), recursive: true);
        Directory.Delete(Path.Combine(workspace.Root, "Kms-Custody-Rehearsal"), recursive: true);
        Directory.Delete(Path.Combine(workspace.Root, "Deployment-Rollback-Rehearsal"), recursive: true);

        var result = CreateService().Promote(new(
            workspace.Paths,
            SecondProductionLikeOperationalRunPromotionService.ModeValidateOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        result.Mode.Should().Be(SecondProductionLikeOperationalRunPromotionService.ModeValidateOnly);
        result.WrittenFiles.Should().BeEmpty();
    }

    [Fact]
    public void SourceValidation_StaleReadinessBaseline_IsRejected()
    {
        using var workspace = TempSecondRunWorkspace.Create();
        var source = workspace.LoadSource();
        source["readinessBaseline"]!.AsObject()["registerId"] = "RDY-REG-v0.1.6";

        var errors = SecondProductionLikeOperationalRunContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT163_STALE_READINESS_BASELINE", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_DirectRegisterMutation_IsRejected()
    {
        using var workspace = TempSecondRunWorkspace.Create();
        var source = workspace.LoadSource();
        source["scoreProposal"]!.AsObject()["directRegisterMutation"] = true;

        var errors = SecondProductionLikeOperationalRunContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT163_DIRECT_REGISTER_MUTATION_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_Feat154ReuseAsSecondRun_IsRejected()
    {
        using var workspace = TempSecondRunWorkspace.Create();
        var source = workspace.LoadSource();
        source["secondRunProfile"]!.AsObject()["distinctFromFirstRun"] = false;

        var errors = SecondProductionLikeOperationalRunContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT163_FEAT154_REUSE_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_MissingMonitoringWindow_IsRejected()
    {
        using var workspace = TempSecondRunWorkspace.Create();
        var source = workspace.LoadSource();
        source["operationalEvidence"]!.AsObject()["monitoringAlerting"]!.AsObject().Remove("windowStart");

        var errors = SecondProductionLikeOperationalRunContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT163_OPERATIONAL_TIME_INVALID", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_PrivateSupportIdentity_IsRejected()
    {
        using var workspace = TempSecondRunWorkspace.Create();
        var source = workspace.LoadSource();
        source["operationalEvidence"]!.AsObject()["supportOperatorHandoff"]!.AsObject()["privateIdentityPublished"] = true;

        var errors = SecondProductionLikeOperationalRunContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT163_SUPPORT_OPERATOR_HANDOFF_BLOCKED", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_MissingIncidentResponseWalkthrough_IsRejected()
    {
        using var workspace = TempSecondRunWorkspace.Create();
        var source = workspace.LoadSource();
        source["operationalEvidence"]!.AsObject()["incidentResponseWalkthrough"]!.AsObject().Remove("simulatedIncident");

        var errors = SecondProductionLikeOperationalRunContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT163_INCIDENT_RESPONSE_BLOCKED", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_StaleFeat134Freshness_IsRejected()
    {
        using var workspace = TempSecondRunWorkspace.Create();
        var source = workspace.LoadSource();
        source["operationalEvidence"]!.AsObject()["securitySupportFreshness"]!.AsObject()["feat134Currentness"] = "stale";

        var errors = SecondProductionLikeOperationalRunContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT163_SECURITY_SUPPORT_FRESHNESS_BLOCKED", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_MissingBackupRestoreEvidence_IsRejected()
    {
        using var workspace = TempSecondRunWorkspace.Create();
        var source = workspace.LoadSource();
        source["operationalEvidence"]!.AsObject()["backupRestore"]!.AsObject().Remove("restoreEvidenceMode");

        var errors = SecondProductionLikeOperationalRunContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT163_BACKUP_RESTORE_BLOCKED", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_MissingPostmortemEvidence_IsRejected()
    {
        using var workspace = TempSecondRunWorkspace.Create();
        var source = workspace.LoadSource();
        source["operationalEvidence"]!.AsObject()["postmortem"]!.AsObject().Remove("followUpRefs");

        var errors = SecondProductionLikeOperationalRunContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT163_POSTMORTEM_BLOCKED", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_PrivateMaterialMarker_IsRejected()
    {
        using var workspace = TempSecondRunWorkspace.Create();
        var source = workspace.LoadSource();
        source["secondRunProfile"]!.AsObject()["publicClaimBoundary"] = "credential=leaked";

        var errors = SecondProductionLikeOperationalRunContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT163_PRIVATE_MATERIAL_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_ScoreOverclaimWrongRange_IsRejected()
    {
        using var workspace = TempSecondRunWorkspace.Create();
        var source = workspace.LoadSource();
        source["scoreProposal"]!.AsObject()["fromScore"] = 6;

        var errors = SecondProductionLikeOperationalRunContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT163_SCORE_PROPOSAL_INVALID", StringComparison.Ordinal));
    }

    [Fact]
    public void CurrentnessValidation_StaleFeat143Hash_BlocksPromotion()
    {
        using var workspace = TempSecondRunWorkspace.Create();
        var source = workspace.LoadSource();
        source["upstreamRefs"]!.AsObject()["feat143"]!.AsObject()["sha256Hash"] = HashFor("wrong-feat143");

        var errors = SecondProductionLikeOperationalRunContracts.ValidateCurrentRefs(workspace.Paths, source);

        errors.Should().Contain(error => error.Contains("upstreamRefs.feat143.sha256Hash mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public void Promotion_PackageAndCheckOnly_AreDeterministic()
    {
        using var workspace = TempSecondRunWorkspace.Create();
        var service = CreateService();

        var package = service.Promote(new(
            workspace.Paths,
            SecondProductionLikeOperationalRunPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));
        var check = service.Promote(new(
            workspace.Paths,
            SecondProductionLikeOperationalRunPromotionService.ModeCheckOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        package.WrittenFiles.Should().HaveCount(SecondProductionLikeOperationalRunArtifactGenerator.RequiredArtifactPaths.Length);
        check.CheckedFiles.Should().HaveCount(SecondProductionLikeOperationalRunArtifactGenerator.RequiredArtifactPaths.Length);
        package.GeneratedPackage.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content))
            .Should()
            .Equal(check.GeneratedPackage.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content)));
    }

    [Fact]
    public void Promotion_PublicOnlyCheckOnly_ShouldNotRequirePrivateContextRepos()
    {
        using var workspace = TempSecondRunWorkspace.Create();
        var service = CreateService();
        service.Promote(new(
            workspace.Paths,
            SecondProductionLikeOperationalRunPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));
        Directory.Delete(Path.Combine(workspace.Root, "hush-memory-bank"), recursive: true);
        Directory.Delete(Path.Combine(workspace.Root, "Kms-Custody-Rehearsal"), recursive: true);
        Directory.Delete(Path.Combine(workspace.Root, "Deployment-Rollback-Rehearsal"), recursive: true);

        var result = service.Promote(new(
            workspace.Paths,
            SecondProductionLikeOperationalRunPromotionService.ModeCheckOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        result.Mode.Should().Be(SecondProductionLikeOperationalRunPromotionService.ModeCheckOnly);
        result.CheckedFiles.Should().HaveCount(SecondProductionLikeOperationalRunArtifactGenerator.RequiredArtifactPaths.Length);
    }

    [Fact]
    public void Promotion_PackageMode_WritesPackageIndexManifestAndRestrictedIndexRefs()
    {
        using var workspace = TempSecondRunWorkspace.Create();

        var result = CreateService().Promote(new(
            workspace.Paths,
            SecondProductionLikeOperationalRunPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        result.WrittenFiles.Should().HaveCount(SecondProductionLikeOperationalRunArtifactGenerator.RequiredArtifactPaths.Length);
        File.Exists(Path.Combine(workspace.DefaultPackageRoot, SecondProductionLikeOperationalRunArtifactGenerator.PackageIndexPath)).Should().BeTrue();
        File.Exists(Path.Combine(workspace.DefaultPackageRoot, SecondProductionLikeOperationalRunArtifactGenerator.ManifestPath)).Should().BeTrue();
        File.Exists(Path.Combine(workspace.DefaultPackageRoot, SecondProductionLikeOperationalRunArtifactGenerator.RestrictedEvidenceIndexPath.Replace('/', Path.DirectorySeparatorChar))).Should().BeTrue();

        var manifest = SecondProductionLikeOperationalRunContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultPackageRoot, SecondProductionLikeOperationalRunArtifactGenerator.ManifestPath),
            "generated manifest");
        var artifactPaths = manifest["artifacts"]!.AsArray()
            .OfType<JsonObject>()
            .Select(item => SecondProductionLikeOperationalRunContracts.GetString(item, "path"))
            .ToArray();
        artifactPaths.Should().Contain(SecondProductionLikeOperationalRunArtifactGenerator.PackageIndexPath);
        artifactPaths.Should().Contain(SecondProductionLikeOperationalRunArtifactGenerator.RestrictedEvidenceIndexPath);

        var validationSummary = manifest["validationSummary"]!.AsObject();
        validationSummary["allRequiredGatesAccepted"]!.GetValue<bool>().Should().BeTrue();
        validationSummary["publicOnlyReplayStatus"]!.GetValue<string>().Should().Be("accepted");

        var restricted = manifest["restrictedEvidence"]!.AsObject();
        restricted["payloadPublished"]!.GetValue<bool>().Should().BeFalse();
        restricted["publicManifestIncludesPayload"]!.GetValue<bool>().Should().BeFalse();
        restricted["privatePathRef"]!.GetValue<string>().Should().Be("PrivateServer_ElectronicVoting/Operational-Evidence-Second-Run/FEAT163-SECOND-RUN-20260603-001/");
    }

    [Fact]
    public void Promotion_PackageMode_WritesReadinessFragmentScoreProposalAndFeat166Handoff()
    {
        using var workspace = TempSecondRunWorkspace.Create();

        CreateService().Promote(new(
            workspace.Paths,
            SecondProductionLikeOperationalRunPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        var readiness = SecondProductionLikeOperationalRunContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultPackageRoot, SecondProductionLikeOperationalRunArtifactGenerator.ReadinessFragmentPath.Replace('/', Path.DirectorySeparatorChar)),
            "readiness fragment");
        readiness["dimensionId"]!.GetValue<string>().Should().Be(SecondProductionLikeOperationalRunContracts.TargetDimensionId);
        readiness["targetBlockerId"]!.GetValue<string>().Should().Be(SecondProductionLikeOperationalRunContracts.TargetBlockerId);
        readiness["scoreEffect"]!.AsObject()["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();

        var scoreProposal = SecondProductionLikeOperationalRunContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultPackageRoot, SecondProductionLikeOperationalRunArtifactGenerator.ScoreProposalPath.Replace('/', Path.DirectorySeparatorChar)),
            "score proposal");
        scoreProposal["proposedScoreFrom"]!.GetValue<int>().Should().Be(8);
        scoreProposal["proposedScoreTo"]!.GetValue<int>().Should().Be(10);
        scoreProposal["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();
        scoreProposal["nonClaims"]!.AsArray().Select(item => item!.GetValue<string>()).Should().Contain("external_audit_acceptance");

        var handoff = SecondProductionLikeOperationalRunContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultPackageRoot, SecondProductionLikeOperationalRunArtifactGenerator.DownstreamHandoffPath.Replace('/', Path.DirectorySeparatorChar)),
            "downstream handoff");
        handoff["consumers"]!.AsArray()
            .OfType<JsonObject>()
            .Select(item => SecondProductionLikeOperationalRunContracts.GetString(item, "consumerId"))
            .Should()
            .Contain(new[] { "FEAT-166", "internal_audit_95_promotion_owner" });
        handoff["nonClaims"]!.AsArray().Select(item => item!.GetValue<string>())
            .Should()
            .Contain(new[] { "production_rollout_approval", "public_state_certification", "legal_sufficiency", "external_audit_acceptance" });
    }

    [Fact]
    public void Promotion_CheckOnly_DetectsManifestDrift()
    {
        using var workspace = TempSecondRunWorkspace.Create();
        var service = CreateService();
        service.Promote(new(
            workspace.Paths,
            SecondProductionLikeOperationalRunPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));
        File.WriteAllText(
            Path.Combine(workspace.DefaultPackageRoot, SecondProductionLikeOperationalRunArtifactGenerator.ManifestPath),
            "drifted manifest");

        var act = () => service.Promote(new(
            workspace.Paths,
            SecondProductionLikeOperationalRunPromotionService.ModeCheckOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<SecondProductionLikeOperationalRunPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains(SecondProductionLikeOperationalRunArtifactGenerator.ManifestPath, StringComparison.Ordinal));
    }

    private static SecondProductionLikeOperationalRunPromotionService CreateService() => new();

    private static string HashFor(string value) => SecondProductionLikeOperationalRunContracts.Sha256Hex(value + "\n");

    private sealed class TempSecondRunWorkspace : IDisposable
    {
        private const string KmsCommit = "96c845d9ebda9bc359bd2f44477d64c374ce844e";
        private const string RollbackCommit = "7fb8ad27c70be8ba58616df574d89b0197f783a8";

        private TempSecondRunWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "feat163-second-run-" + Guid.NewGuid().ToString("N"));
            Paths = SecondProductionLikeOperationalRunPromotionPaths.FromWorkspaceRoot(Root);
            OutputRoot = Path.Combine(Root, "package-output");
            Directory.CreateDirectory(Paths.SchemasRoot);
            Directory.CreateDirectory(Path.Combine(Paths.ExamplesRoot, "release-baseline"));
            Directory.CreateDirectory(Paths.PackagesRoot);
            WriteSchemas();
            var refs = WriteCurrentnessFiles();
            WriteGitHead("Kms-Custody-Rehearsal", KmsCommit);
            WriteGitHead("Deployment-Rollback-Rehearsal", RollbackCommit);
            WriteJson(Path.Combine(Paths.ExamplesRoot, "release-baseline", SecondProductionLikeOperationalRunPromotionPaths.SourceFileName), BuildSource(refs));
        }

        public string Root { get; }

        public SecondProductionLikeOperationalRunPromotionPaths Paths { get; }

        public string OutputRoot { get; }

        public string DefaultPackageRoot => Path.Combine(OutputRoot, SecondProductionLikeOperationalRunPromotionPaths.PackageFamilyFolder, "FEAT163-SECOND-RUN-20260603-001");

        public static TempSecondRunWorkspace Create() => new();

        public JsonObject LoadSource() => SecondProductionLikeOperationalRunContracts.LoadSource(Paths);

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
                Path.Combine(Paths.SchemasRoot, SecondProductionLikeOperationalRunPromotionPaths.SourceSchemaFileName),
                new JsonObject
                {
                    ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                    ["required"] = new JsonArray("schemaVersion"),
                });
            WriteJson(
                Path.Combine(Paths.SchemasRoot, SecondProductionLikeOperationalRunPromotionPaths.PackageManifestSchemaFileName),
                new JsonObject
                {
                    ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                    ["required"] = new JsonArray("schemaVersion"),
                });
        }

        private CurrentRefs WriteCurrentnessFiles()
        {
            var feat134 = WriteWorkspaceFile("hush-memory-bank/Features/04_COMPLETED/FEAT-134-security-dependency-support-readiness/feature-completion-report.md", "feat134 security support current\n");
            var feat143 = WriteWorkspaceFile("hush-memory-bank/Features/04_COMPLETED/FEAT-143-runtime-deployment-proof-binding-ledger/readiness-handoff-20260526.md", "feat143 runtime binding current\n");
            var feat144 = WriteWorkspaceFile("hush-memory-bank/Features/04_COMPLETED/FEAT-144-hushwebclient-deployment-proof-exposure-handshake/FeatureDescription.md", "feat144 webclient proof current\n");
            var feat161 = WriteWorkspaceFile("hush-memory-bank/Features/04_COMPLETED/FEAT-161-kms-custody-drift-rotation-recovery-rehearsal/feature-completion-report.md", "feat161 custody current\n");
            var feat162 = WriteWorkspaceFile("hush-memory-bank/Features/04_COMPLETED/FEAT-162-trusted-deployment-rollback-emergency-change-rehearsal/feature-completion-report.md", "feat162 rollback current\n");

            return new CurrentRefs(
                SecondProductionLikeOperationalRunContracts.FileSha256Hex(feat134),
                SecondProductionLikeOperationalRunContracts.FileSha256Hex(feat143),
                SecondProductionLikeOperationalRunContracts.FileSha256Hex(feat144),
                SecondProductionLikeOperationalRunContracts.FileSha256Hex(feat161),
                SecondProductionLikeOperationalRunContracts.FileSha256Hex(feat162));
        }

        private string WriteWorkspaceFile(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        private void WriteGitHead(string repoName, string commit)
        {
            var gitRoot = Path.Combine(Root, repoName, ".git");
            Directory.CreateDirectory(gitRoot);
            File.WriteAllText(Path.Combine(gitRoot, "HEAD"), commit + "\n");
        }

        private static JsonObject BuildSource(CurrentRefs refs) =>
            new()
            {
                ["schemaVersion"] = SecondProductionLikeOperationalRunContracts.SourceSchemaVersion,
                ["featureId"] = SecondProductionLikeOperationalRunContracts.FeatureId,
                ["sourceId"] = "FEAT163-SECOND-PRODUCTION-LIKE-RUN-20260603-BASELINE",
                ["generatedAt"] = "2026-06-03T00:00:00Z",
                ["readinessBaseline"] = new JsonObject
                {
                    ["registerId"] = SecondProductionLikeOperationalRunContracts.CurrentRegisterId,
                    ["totalScore"] = 80,
                    ["registerManifestSha256Hash"] = HashFor("register-manifest"),
                    ["scorecardSha256Hash"] = HashFor("scorecard"),
                    ["dimension"] = new JsonObject
                    {
                        ["dimensionId"] = SecondProductionLikeOperationalRunContracts.TargetDimensionId,
                        ["currentScore"] = 8,
                        ["targetScore"] = 10,
                        ["blockerId"] = SecondProductionLikeOperationalRunContracts.TargetBlockerId,
                    },
                },
                ["scoreProposal"] = new JsonObject
                {
                    ["dimensionId"] = SecondProductionLikeOperationalRunContracts.TargetDimensionId,
                    ["fromScore"] = 8,
                    ["toScore"] = 10,
                    ["proposalOnly"] = true,
                    ["directRegisterMutation"] = false,
                    ["blockedUnlessAllGatesPass"] = true,
                },
                ["firstRunBaseline"] = new JsonObject
                {
                    ["featureId"] = "FEAT-154",
                    ["manifestId"] = "FEAT154-PRODUCTION-LIKE-OPERATIONAL-RUN-MANIFEST",
                    ["sourceId"] = "FEAT154-PRODUCTION-LIKE-RUN-20260528-BASELINE",
                    ["status"] = "accepted",
                    ["generatedAt"] = "2026-05-28T18:37:53Z",
                    ["artifactCount"] = 18,
                    ["manifestSha256Hash"] = SecondProductionLikeOperationalRunContracts.AcceptedFeat154ManifestHash,
                    ["baselineUse"] = "baseline_currentness_only",
                },
                ["secondRunProfile"] = new JsonObject
                {
                    ["runId"] = "FEAT163-SECOND-RUN-20260603-001",
                    ["runWindow"] = new JsonObject
                    {
                        ["plannedStart"] = "2026-06-03T00:00:00Z",
                        ["plannedEnd"] = "2026-06-03T04:00:00Z",
                        ["timeBasis"] = "planned_release_baseline_fixture",
                    },
                    ["environmentProfile"] = "production_like_hush_managed_release_rehearsal",
                    ["dataScope"] = "synthetic_or_non_confidential",
                    ["evidenceMode"] = "sanitized_public_fixture_with_restricted_refs",
                    ["distinctFromFirstRun"] = true,
                    ["publicClaimBoundary"] = "Public-safe refs only.",
                },
                ["upstreamRefs"] = new JsonObject
                {
                    ["feat134"] = Upstream("FEAT-134", "requires_currentness_check_at_package_time", "security-support-readiness-currentness-ref", refs.Feat134Hash, required: true),
                    ["feat143"] = Upstream("FEAT-143", "requires_current_runtime_binding_ref", "runtime-deployment-proof-binding-ledger-ref", refs.Feat143Hash, required: true),
                    ["feat144"] = Upstream("FEAT-144", "requires_current_webclient_observed_proof_ref", "hushwebclient-observed-proof-ref", refs.Feat144Hash, required: true),
                    ["feat154"] = Upstream("FEAT-154", "accepted_first_run_baseline_only", "FEAT154-PRODUCTION-LIKE-OPERATIONAL-RUN-MANIFEST", SecondProductionLikeOperationalRunContracts.AcceptedFeat154ManifestHash, required: true),
                    ["feat161"] = Upstream("FEAT-161", "consume_when_custody_profile_is_in_scope", "Kms-Custody-Rehearsal public source currentness ref", refs.Feat161Hash, required: false, KmsCommit),
                    ["feat162"] = Upstream("FEAT-162", "consume_when_deployment_variance_is_claimed", "Deployment-Rollback-Rehearsal public source currentness ref", refs.Feat162Hash, required: false, RollbackCommit),
                },
                ["evidenceGates"] = BuildGates(),
                ["operationalEvidence"] = BuildOperationalEvidence(),
                ["restrictedEvidencePolicy"] = new JsonObject
                {
                    ["payloadPublished"] = false,
                    ["refId"] = "FEAT163-RESTRICTED-EVIDENCE-INDEX-REF",
                    ["allowedPublicFields"] = new JsonArray("ids", "hashes", "result_codes"),
                    ["restrictedMaterialClasses"] = new JsonArray("secret"),
                },
                ["publicSafety"] = new JsonObject
                {
                    ["noSecretScanRequired"] = true,
                    ["allowPrivatePathStrings"] = false,
                    ["forbiddenMaterialClasses"] = new JsonArray("secret"),
                },
            };

        private static JsonObject Upstream(
            string featureId,
            string status,
            string publicRef,
            string sha256Hash,
            bool required,
            string? commitHash = null)
        {
            var value = new JsonObject
            {
                ["featureId"] = featureId,
                ["status"] = status,
                ["publicRef"] = publicRef,
                ["sha256Hash"] = sha256Hash,
                ["requiredForScoreMovement"] = required,
            };
            if (commitHash is not null)
            {
                value["commitHash"] = commitHash;
            }

            return value;
        }

        private static JsonObject BuildGates() =>
            new()
            {
                ["feat154BaselineCurrentness"] = Gate("FEAT163-GATE-FEAT154-BASELINE-CURRENTNESS", "FEAT163_BASELINE_CURRENTNESS_PENDING"),
                ["runtimeProofBinding"] = Gate("FEAT163-GATE-RUNTIME-PROOF-BINDING", "FEAT163_RUNTIME_PROOF_PENDING"),
                ["monitoringAlerting"] = Gate("FEAT163-GATE-MONITORING-ALERTING", "FEAT163_MONITORING_ALERTING_PENDING"),
                ["backupRestore"] = Gate("FEAT163-GATE-BACKUP-RESTORE", "FEAT163_BACKUP_RESTORE_PENDING"),
                ["supportOperatorHandoff"] = Gate("FEAT163-GATE-SUPPORT-OPERATOR-HANDOFF", "FEAT163_SUPPORT_OPERATOR_HANDOFF_PENDING"),
                ["securitySupportFreshness"] = Gate("FEAT163-GATE-SECURITY-SUPPORT-FRESHNESS", "FEAT163_SECURITY_SUPPORT_FRESHNESS_PENDING"),
                ["incidentResponseWalkthrough"] = Gate("FEAT163-GATE-INCIDENT-RESPONSE-WALKTHROUGH", "FEAT163_INCIDENT_RESPONSE_PENDING"),
                ["postmortem"] = Gate("FEAT163-GATE-POSTMORTEM", "FEAT163_POSTMORTEM_PENDING"),
                ["noSecretScan"] = Gate("FEAT163-GATE-NO-SECRET-SCAN", "FEAT163_NO_SECRET_SCAN_PENDING"),
            };

        private static JsonObject BuildOperationalEvidence() =>
            new()
            {
                ["monitoringAlerting"] = new JsonObject
                {
                    ["status"] = "accepted",
                    ["resultCode"] = "FEAT163_MONITORING_ALERTING_ACCEPTED",
                    ["publicSummaryRef"] = "validation/monitoring-alerting-summary.json",
                    ["windowStart"] = "2026-06-03T00:00:00Z",
                    ["windowEnd"] = "2026-06-03T04:00:00Z",
                    ["alertResultCodes"] = new JsonArray("FEAT163_MONITORING_ALERTING_ACCEPTED"),
                    ["restrictedEvidenceRefs"] = new JsonArray(RestrictedRef("FEAT163-MONITORING-WINDOW-REF")),
                    ["signoffRoles"] = new JsonArray("operations_owner", "monitoring_reviewer"),
                },
                ["backupRestore"] = new JsonObject
                {
                    ["status"] = "accepted",
                    ["resultCode"] = "FEAT163_BACKUP_RESTORE_ACCEPTED",
                    ["publicSummaryRef"] = "validation/backup-restore-summary.json",
                    ["restoreEvidenceMode"] = "same_profile_current",
                    ["profileCompatibility"] = "same_profile_current",
                    ["restrictedEvidenceRefs"] = new JsonArray(RestrictedRef("FEAT163-BACKUP-RESTORE-REF")),
                    ["signoffRoles"] = new JsonArray("operations_owner", "restore_reviewer"),
                },
                ["supportOperatorHandoff"] = new JsonObject
                {
                    ["status"] = "accepted",
                    ["resultCode"] = "FEAT163_SUPPORT_OPERATOR_HANDOFF_ACCEPTED",
                    ["publicSummaryRef"] = "validation/support-operator-handoff-summary.json",
                    ["supportCategories"] = new JsonArray("platform_operations", "election_support", "security_response"),
                    ["escalationPathRefs"] = new JsonArray("FEAT163-ESCALATION-PATH-PUBLIC-REF"),
                    ["privateIdentityPublished"] = false,
                    ["restrictedEvidenceRefs"] = new JsonArray(RestrictedRef("FEAT163-SUPPORT-HANDOFF-REF")),
                    ["signoffRoles"] = new JsonArray("support_owner", "operator_on_call_role"),
                },
                ["securitySupportFreshness"] = new JsonObject
                {
                    ["status"] = "accepted",
                    ["resultCode"] = "FEAT163_SECURITY_SUPPORT_FRESHNESS_ACCEPTED",
                    ["publicSummaryRef"] = "validation/security-support-freshness-summary.json",
                    ["feat134Currentness"] = "current",
                    ["freshnessCheckedAt"] = "2026-06-03T00:00:00Z",
                    ["maxAgeDays"] = 30,
                    ["restrictedEvidenceRefs"] = new JsonArray(),
                    ["signoffRoles"] = new JsonArray("security_owner", "dependency_reviewer"),
                },
                ["incidentResponseWalkthrough"] = new JsonObject
                {
                    ["status"] = "accepted",
                    ["resultCode"] = "FEAT163_INCIDENT_RESPONSE_ACCEPTED",
                    ["publicSummaryRef"] = "validation/incident-response-walkthrough-summary.json",
                    ["noIncidentDeclaration"] = new JsonObject
                    {
                        ["monitoringWindowRef"] = "FEAT163-MONITORING-WINDOW-REF",
                        ["incidentRegisterRef"] = "FEAT163-NO-INCIDENT-REGISTER-PUBLIC-REF",
                        ["resultCode"] = "FEAT163_INCIDENT_RESPONSE_ACCEPTED",
                    },
                    ["simulatedIncident"] = new JsonObject
                    {
                        ["reasonCode"] = "controlled_second_run_response_walkthrough",
                        ["accountabilityRole"] = "incident_commander_role",
                        ["timelineSummary"] = "public_safe_timeline_summary_ref",
                        ["resultCode"] = "FEAT163_INCIDENT_RESPONSE_ACCEPTED",
                        ["status"] = "accepted",
                    },
                    ["restrictedEvidenceRefs"] = new JsonArray(RestrictedRef("FEAT163-INCIDENT-WALKTHROUGH-REF")),
                    ["signoffRoles"] = new JsonArray("incident_commander_role", "operations_owner"),
                },
                ["postmortem"] = new JsonObject
                {
                    ["status"] = "accepted",
                    ["resultCode"] = "FEAT163_POSTMORTEM_ACCEPTED",
                    ["publicSummaryRef"] = "validation/postmortem-summary.json",
                    ["findingsCategories"] = new JsonArray("no_live_incident", "walkthrough_actions_recorded"),
                    ["followUpRefs"] = new JsonArray("FEAT163-POSTMORTEM-FOLLOWUP-PUBLIC-REF"),
                    ["restrictedEvidenceRefs"] = new JsonArray(RestrictedRef("FEAT163-POSTMORTEM-REF")),
                    ["signoffRoles"] = new JsonArray("postmortem_owner", "operations_owner"),
                },
                ["restrictedBoundary"] = new JsonObject
                {
                    ["status"] = "accepted",
                    ["resultCode"] = "FEAT163_NO_SECRET_SCAN_ACCEPTED",
                    ["publicSummaryRef"] = "validation/no-secret-scan-result.json",
                    ["payloadPublished"] = false,
                    ["restrictedRefsOnly"] = true,
                    ["scannerFamilies"] = new JsonArray("credential_marker", "private_url_marker", "raw_log_marker"),
                    ["restrictedEvidenceRefs"] = new JsonArray(),
                    ["signoffRoles"] = new JsonArray("public_safety_reviewer"),
                },
            };

        private static JsonObject RestrictedRef(string refId) =>
            new()
            {
                ["refId"] = refId,
                ["sha256Hash"] = HashFor(refId),
                ["payloadPublished"] = false,
            };

        private static JsonObject Gate(string gateId, string resultCode) =>
            new()
            {
                ["gateId"] = gateId,
                ["required"] = true,
                ["status"] = "pending",
                ["resultCode"] = resultCode,
                ["claimImpact"] = "Blocks score movement until accepted.",
                ["scoreMovementEffect"] = "Blocks RDY-DIM-007 8 -> 10 until accepted.",
                ["publicSummaryRef"] = "validation/" + gateId.ToLowerInvariant() + ".json",
                ["restrictedEvidenceRefId"] = "none",
            };

        private static void WriteJson(string path, JsonObject json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, SecondProductionLikeOperationalRunContracts.CanonicalJson(json));
        }

        private sealed record CurrentRefs(
            string Feat134Hash,
            string Feat143Hash,
            string Feat144Hash,
            string Feat161Hash,
            string Feat162Hash);
    }
}
