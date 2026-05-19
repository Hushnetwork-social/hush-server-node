using System.Text.Json.Nodes;
using DeploymentProofPackagePromoter;
using FluentAssertions;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class DeploymentProofPackagePromotionServiceTests
{
    private static readonly DateTimeOffset FixedGeneratedAt = new(2026, 5, 19, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        var paths = CreatePaths();

        var errors = DeploymentProofPackageContracts.ValidateSchemaSet(paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in DeploymentProofPackageContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(paths.SchemasRoot, schemaFile)).Should().BeTrue();
        }
    }

    [Fact]
    public void ComponentProofFixtures_WebClientAndServerNode_AreAcceptedIndependently()
    {
        var paths = CreatePaths();
        var webClient = LoadExample(paths, "component-proofs", "hush-web-client-component-proof.json");
        var serverNode = LoadExample(paths, "component-proofs", "hush-server-node-component-proof.json");

        DeploymentProofPackageContracts.ValidateComponentProof(webClient).Should().BeEmpty();
        DeploymentProofPackageContracts.ValidateComponentProof(serverNode).Should().BeEmpty();

        webClient.ContainsKey("rehearsalElectionId").Should().BeFalse();
        serverNode.ContainsKey("rehearsalElectionId").Should().BeFalse();
        webClient["artifactRefs"]!.AsObject().ContainsKey("webArtifactHash").Should().BeTrue();
        serverNode["artifactRefs"]!.AsObject().ContainsKey("backendImageDigest").Should().BeTrue();
    }

    [Fact]
    public void BindingAndCeremonyFixtures_ReferenceBothComponentProofsAndRequiredStages()
    {
        var paths = CreatePaths();
        var proofSet = LoadExample(paths, "bindings", "deployment-proof-set.json");
        var ledger = LoadExample(paths, "bindings", "per-election-deployment-binding-ledger.json");
        var ceremony = LoadExample(paths, "ceremonies", "deployment-ceremony.json");

        DeploymentProofPackageContracts.ValidateProofSet(proofSet).Should().BeEmpty();
        DeploymentProofPackageContracts.ValidateBindingLedger(ledger).Should().BeEmpty();
        DeploymentProofPackageContracts.ValidateCeremony(ceremony).Should().BeEmpty();

        var stageIds = ceremony["ceremonyStages"]!.AsArray()
            .Select(stage => stage!["stageId"]!.GetValue<string>())
            .ToArray();
        stageIds.Should().Contain(DeploymentProofPackageContracts.RequiredCeremonyStageIds);
    }

    [Fact]
    public void ComponentProof_MissingMandatoryArtifactHash_FailsClosed()
    {
        var paths = CreatePaths();
        var webClient = LoadExample(paths, "component-proofs", "hush-web-client-component-proof.json");
        webClient["artifactRefs"]!.AsObject().Remove("webArtifactHash");

        var errors = DeploymentProofPackageContracts.ValidateComponentProof(webClient);

        errors.Should().Contain(error => error.Contains("webArtifactHash", StringComparison.Ordinal));
    }

    [Fact]
    public void ComponentProof_MutableSourceRef_FailsClosed()
    {
        var paths = CreatePaths();
        var webClient = LoadExample(paths, "component-proofs", "hush-web-client-component-proof.json");
        var sourceRef = webClient["sourceRef"]!.AsObject();
        sourceRef["refType"] = "branch";
        sourceRef["value"] = "main";
        sourceRef["immutable"] = false;

        var errors = DeploymentProofPackageContracts.ValidateComponentProof(webClient);

        errors.Should().Contain(error => error.Contains("immutable", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("mutable branch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ComponentProof_PublicKmsAndProviderIdentifiers_FailClosed()
    {
        var paths = CreatePaths();
        var serverNode = LoadExample(paths, "component-proofs", "hush-server-node-component-proof.json");
        serverNode["publicLeakTest"] = "arn:aws:kms:eu-west-1:123456789012:key/kms-key-public-leak";

        var errors = DeploymentProofPackageContracts.ValidateComponentProof(serverNode);

        errors.Should().Contain(error => error.Contains("arn:aws:kms", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("direct provider account identifier", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("hush-web-client", "hush-web-client-component-proof.json")]
    [InlineData("hush-server-node", "hush-server-node-component-proof.json")]
    public void ComponentProof_CdGeneratedAcceptedEvidence_IsAccepted(string componentId, string fileName)
    {
        var paths = CreatePaths();
        var proof = LoadExample(paths, "component-proofs", fileName);

        proof["componentId"]!.GetValue<string>().Should().Be(componentId);
        proof["status"]!.GetValue<string>().Should().Be("accepted");
        proof["deploymentExecutionKind"]!.GetValue<string>().Should().Be("cd_deployment");
        proof["cdProvider"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        proof["cdRunId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        DeploymentProofPackageContracts.ValidateComponentProof(proof).Should().BeEmpty();
    }

    [Fact]
    public void Classifier_UnknownPath_FailsClosedAndBlocksAcceptedEvidence()
    {
        var input = CreateWebsiteOnlyInput() with
        {
            ChangedPaths = ["unknown-service/path/changed.txt"],
        };

        var result = DeploymentImpactClassifier.Classify(input);

        result.OutputClass.Should().Be(DeploymentImpactClasses.UnknownPendingClassification);
        result.RequiresManualOwnerReview.Should().BeTrue();
        result.BlocksAcceptedEvidence.Should().BeTrue();
        result.MatchedRules.Should().Contain("unknown-path");
    }

    [Fact]
    public void Classifier_SupportsAllEightOutputClasses()
    {
        var outputs = new[]
        {
            DeploymentImpactClassifier.Classify(CreateProtocolChangeInput()).OutputClass,
            DeploymentImpactClassifier.Classify(CreateServerNoProtocolChangeInput()).OutputClass,
            DeploymentImpactClassifier.Classify(CreateWebsiteOnlyInput()).OutputClass,
            DeploymentImpactClassifier.Classify(CreateNonVotingServiceInput()).OutputClass,
            DeploymentImpactClassifier.Classify(CreateOperationalConfigInput()).OutputClass,
            DeploymentImpactClassifier.Classify(CreateEmergencyInput()).OutputClass,
            DeploymentImpactClassifier.Classify(CreateRollbackInput()).OutputClass,
            DeploymentImpactClassifier.Classify(CreateWebsiteOnlyInput() with
            {
                RefDiffs = SafeNoProtocolRefDiffs() with { VerifierOrExporterHashChanged = null },
            }).OutputClass,
        };

        outputs.Should().BeEquivalentTo(
        [
            DeploymentImpactClasses.VotingProtocolChange,
            DeploymentImpactClasses.VotingProtocolNoChange,
            DeploymentImpactClasses.WebsiteOnlyNoProtocolChange,
            DeploymentImpactClasses.NonVotingServiceNoProtocolChange,
            DeploymentImpactClasses.OperationalConfigChange,
            DeploymentImpactClasses.EmergencyChange,
            DeploymentImpactClasses.Rollback,
            DeploymentImpactClasses.UnknownPendingClassification,
        ]);
    }

    [Theory]
    [InlineData("ballot")]
    [InlineData("eligibility")]
    [InlineData("custody")]
    [InlineData("accepted-ballot")]
    [InlineData("published-evidence")]
    [InlineData("tally")]
    [InlineData("verifier-output")]
    [InlineData("final-package")]
    [InlineData("election-critical-migration")]
    public void Classifier_VotingCriticalSemanticChanges_AreVotingProtocolChanges(string semanticChange)
    {
        var input = CreateWebsiteOnlyInput() with
        {
            SemanticChanges = CreateSemanticChange(semanticChange),
        };

        var result = DeploymentImpactClassifier.Classify(input);

        result.OutputClass.Should().Be(DeploymentImpactClasses.VotingProtocolChange);
    }

    [Fact]
    public void Classifier_WebsiteOnlyDeployment_RequiresAllVotingRefsUnchanged()
    {
        var accepted = DeploymentImpactClassifier.Classify(CreateWebsiteOnlyInput());
        var incomplete = DeploymentImpactClassifier.Classify(CreateWebsiteOnlyInput() with
        {
            RefDiffs = SafeNoProtocolRefDiffs() with { CustodyProfileChanged = null },
        });

        accepted.OutputClass.Should().Be(DeploymentImpactClasses.WebsiteOnlyNoProtocolChange);
        incomplete.OutputClass.Should().Be(DeploymentImpactClasses.UnknownPendingClassification);
        incomplete.Reason.Should().Contain("custodyProfile");
    }

    [Fact]
    public void Classifier_NonVotingService_RequiresMappedNonVotingPathAndUnchangedVotingArtifacts()
    {
        var result = DeploymentImpactClassifier.Classify(CreateNonVotingServiceInput());

        result.OutputClass.Should().Be(DeploymentImpactClasses.NonVotingServiceNoProtocolChange);
        result.BlocksAcceptedEvidence.Should().BeFalse();
    }

    [Fact]
    public void Classifier_OperationalConfigChange_RequiresRecordedConfigHash()
    {
        var accepted = DeploymentImpactClassifier.Classify(CreateOperationalConfigInput());
        var incomplete = DeploymentImpactClassifier.Classify(CreateOperationalConfigInput() with
        {
            RefDiffs = SafeNoProtocolRefDiffs() with
            {
                ConfigProfileHashChanged = true,
                ConfigProfileHashRecorded = false,
            },
        });

        accepted.OutputClass.Should().Be(DeploymentImpactClasses.OperationalConfigChange);
        incomplete.OutputClass.Should().Be(DeploymentImpactClasses.UnknownPendingClassification);
    }

    [Fact]
    public void Classifier_EmergencyChange_RequiresReasonRerunChecksAndAccountability()
    {
        var accepted = DeploymentImpactClassifier.Classify(CreateEmergencyInput());
        var incomplete = DeploymentImpactClassifier.Classify(CreateEmergencyInput() with
        {
            SpecialChange = new DeploymentSpecialChangeEvidence
            {
                IsEmergencyChange = true,
                IsNonStateBreakingFix = true,
                RerunChecks = ["runtime-verification"],
                AccountabilityMarker = "aboimpinto-hushvoting-owner",
            },
        });

        accepted.OutputClass.Should().Be(DeploymentImpactClasses.EmergencyChange);
        incomplete.OutputClass.Should().Be(DeploymentImpactClasses.UnknownPendingClassification);
        incomplete.Reason.Should().Contain("reason");
    }

    [Fact]
    public void Classifier_Rollback_RequiresApprovedArtifactSetStateCompatibilityAndRerunChecks()
    {
        var accepted = DeploymentImpactClassifier.Classify(CreateRollbackInput());
        var incomplete = DeploymentImpactClassifier.Classify(CreateRollbackInput() with
        {
            SpecialChange = new DeploymentSpecialChangeEvidence
            {
                IsRollback = true,
                RollbackToLastCeremonyApprovedArtifactSet = true,
                RerunChecks = ["runtime-verification"],
                AccountabilityMarker = "aboimpinto-hushvoting-owner",
            },
        });

        accepted.OutputClass.Should().Be(DeploymentImpactClasses.Rollback);
        incomplete.OutputClass.Should().Be(DeploymentImpactClasses.UnknownPendingClassification);
        incomplete.Reason.Should().Contain("state compatibility");
    }

    [Fact]
    public void ComponentProof_CiOnlyAcceptedEvidence_FailsClosed()
    {
        var paths = CreatePaths();
        var webClient = LoadExample(paths, "component-proofs", "hush-web-client-component-proof.json");
        webClient["deploymentExecutionKind"] = "ci_preflight_only";

        var errors = DeploymentProofPackageContracts.ValidateComponentProof(webClient);

        errors.Should().Contain(error => error.Contains("CI-only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BindingLedger_AcceptedUnknownClassification_FailsClosed()
    {
        var paths = CreatePaths();
        var ledger = LoadExample(paths, "bindings", "per-election-deployment-binding-ledger.json");
        ledger["deploymentEvents"]!.AsArray()[0]!["classification"] = DeploymentImpactClasses.UnknownPendingClassification;

        var errors = DeploymentProofPackageContracts.ValidateBindingLedger(ledger);

        errors.Should().Contain(error => error.Contains("unknown_pending_classification", StringComparison.Ordinal));
    }

    [Fact]
    public void BindingLedger_MissingActiveComponentProof_FailsClosed()
    {
        var paths = CreatePaths();
        var ledger = LoadExample(paths, "bindings", "per-election-deployment-binding-ledger.json");
        ledger["activeProofSetAtOpen"]!.AsObject().Remove("hushServerNodeDeploymentProofId");

        var errors = DeploymentProofPackageContracts.ValidateBindingLedger(ledger);

        errors.Should().Contain(error => error.Contains("hushServerNodeDeploymentProofId", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Draft -> Open")]
    [InlineData("Open -> Close")]
    [InlineData("Close -> Finalize")]
    [InlineData("Open -> Void")]
    [InlineData("Close -> Void")]
    [InlineData("final_package_export")]
    public void BindingLedger_ReconciliationCoverage_RequiresEveryLifecycleCheckpoint(string missingCheckpoint)
    {
        var paths = CreatePaths();
        var ledger = LoadExample(paths, "bindings", "per-election-deployment-binding-ledger.json");
        var reconciliation = ledger["catalogReconciliation"]!.AsObject();
        var checkpoints = reconciliation["checkpointsCovered"]!.AsArray();
        var filtered = new JsonArray();
        foreach (var checkpoint in checkpoints.Select(node => node!.GetValue<string>()))
        {
            if (!string.Equals(checkpoint, missingCheckpoint, StringComparison.Ordinal))
            {
                filtered.Add(checkpoint);
            }
        }

        reconciliation["checkpointsCovered"] = filtered;

        var errors = DeploymentProofPackageContracts.ValidateBindingLedger(ledger);

        errors.Should().Contain(error => error.Contains(missingCheckpoint, StringComparison.Ordinal));
    }

    [Fact]
    public void Ceremony_MissingCustodyRefsOrFakeCustody_FailClosed()
    {
        var paths = CreatePaths();
        var missingCustodyRefs = LoadExample(paths, "ceremonies", "deployment-ceremony.json");
        missingCustodyRefs["electionCustodyEvidenceRefs"] = new JsonArray();
        var fakeCustody = LoadExample(paths, "ceremonies", "deployment-ceremony.json");
        fakeCustody["custodyProfile"]!.AsObject()["custodyMode"] = "fake_dev_local_custody";

        var missingRefErrors = DeploymentProofPackageContracts.ValidateCeremony(missingCustodyRefs);
        var fakeCustodyErrors = DeploymentProofPackageContracts.ValidateCeremony(fakeCustody);

        missingRefErrors.Should().Contain(error => error.Contains("electionCustodyEvidenceRefs", StringComparison.Ordinal));
        fakeCustodyErrors.Should().Contain(error => error.Contains("fake", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PublicComponentSummary_IncludesVerifierUsefulFieldsAndScansClean()
    {
        var paths = CreatePaths();
        var webClient = LoadExample(paths, "component-proofs", "hush-web-client-component-proof.json");

        var markdown = DeploymentProofPackageViewRenderer.GetPublicComponentSummary(webClient);
        var scanErrors = DeploymentProofPackagePublicRedactionScanner.ScanPublicMarkdown("public-safe-deployment-summary.md", markdown);

        markdown.Should().Contain("DPP-WEB-20260519-001");
        markdown.Should().Contain("hush-web-client");
        markdown.Should().Contain("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        markdown.Should().Contain("webArtifactHash");
        markdown.Should().Contain("Runtime Verification");
        markdown.Should().Contain("website_only_no_protocol_change");
        markdown.Should().Contain("not_component_scoped");
        markdown.Should().Contain("REST-DEPLOY-WEB-20260519-001");
        scanErrors.Should().BeEmpty();
    }

    [Fact]
    public void PublicBindingSummary_IncludesActiveProofsCatalogAndDeploymentEvents()
    {
        var paths = CreatePaths();
        var proofSet = LoadExample(paths, "bindings", "deployment-proof-set.json");
        var ledger = LoadExample(paths, "bindings", "per-election-deployment-binding-ledger.json");

        var markdown = DeploymentProofPackageViewRenderer.GetPublicBindingSummary(proofSet, ledger);
        var scanErrors = DeploymentProofPackagePublicRedactionScanner.ScanPublicMarkdown("public-safe-binding-summary.md", markdown);

        markdown.Should().Contain("HV-REHEARSAL-PUBLIC-20260519-001");
        markdown.Should().Contain("Draft -> Open");
        markdown.Should().Contain("DPP-WEB-20260519-001");
        markdown.Should().Contain("DPP-SERVER-20260519-001");
        markdown.Should().Contain("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee");
        markdown.Should().Contain("DPE-OPEN-20260519-001");
        markdown.Should().Contain("voting_protocol_no_change");
        scanErrors.Should().BeEmpty();
    }

    [Fact]
    public void RestrictedIndexes_IncludePrivateEvidenceRefsWithoutChangingPublicProofRefs()
    {
        var paths = CreatePaths();
        var ceremony = LoadExample(paths, "ceremonies", "deployment-ceremony.json");
        var webClient = LoadExample(paths, "component-proofs", "hush-web-client-component-proof.json");
        var serverNode = LoadExample(paths, "component-proofs", "hush-server-node-component-proof.json");

        var ceremonyIndex = DeploymentProofPackageViewRenderer.GetRestrictedCeremonyIndex(ceremony);
        var deploymentIndex = DeploymentProofPackageViewRenderer.GetRestrictedDeploymentEvidenceIndex(
            ceremony,
            [webClient, serverNode]);

        ceremonyIndex.Should().Contain("REST-ENV-BOUNDARY-20260519-001");
        ceremonyIndex.Should().Contain("REST-CUSTODY-HANDOFF-20260519-001");
        ceremonyIndex.Should().Contain("FINAL-PACKAGE-REHEARSAL-20260519-001");
        ceremonyIndex.Should().Contain("VERIFIER-OUTPUT-REHEARSAL-20260519-001");
        deploymentIndex.Should().Contain("gh-run-web-20260519-001");
        deploymentIndex.Should().Contain("gh-run-server-20260519-001");
        deploymentIndex.Should().Contain("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff");
        deploymentIndex.Should().Contain("Public Forbidden Material Scan");
    }

    [Fact]
    public void PublicRedactionScanner_RejectsForbiddenMaterialBeforeCatalogUpdate()
    {
        const string publicMarkdown = """
                                      raw log contains arn:aws:kms:eu-west-1:123456789012:key/kms-key-public-leak
                                      kmsKeyId=kms-key-public-leak
                                      alias/hush-voting-election-key
                                      decrypt authority role
                                      aws_access_key_id=AKIAEXAMPLEPUBLICLEAK
                                      secret=not-public
                                      token=secret-value
                                      https://service.internal/private
                                      voter data row
                                      voterEmail: voter@example.com
                                      voteChoice=approved
                                      raw support log entry
                                      raw anomaly log entry
                                      operator contact owner@example.com
                                      legal digital signature claim
                                      """;

        var errors = DeploymentProofPackagePublicRedactionScanner.ScanPublicMarkdown(
            "public-safe-deployment-summary.md",
            publicMarkdown);

        errors.Should().Contain(error => error.Contains("arn:aws:kms", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("direct provider account identifier", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("kmsKeyId", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("alias/", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("decrypt authority", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("aws_access_key_id", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("secret=", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("token=", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("private URL", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("voter data", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("voteChoice", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("raw support log", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("raw anomaly log", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("contact detail", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("legal digital signature", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Promote_PublicForbiddenMaterial_LeavesCatalogUnchanged()
    {
        using var workspace = TempPromotionWorkspace.Create(mutableSource: true);
        var service = new DeploymentProofPackagePromotionService();
        service.Promote(CreatePromotionOptions(
            workspace.Paths,
            DeploymentProofPackagePromotionService.ModeComponentProof,
            componentId: "hush-web-client",
            deploymentProofId: "DPP-WEB-20260519-001"));
        var catalogBefore = File.ReadAllText(workspace.Paths.CatalogPath);

        var componentProofPath = Path.Combine(
            workspace.Paths.ComponentProofExamplesRoot,
            "hush-web-client-component-proof.json");
        var componentProof = DeploymentProofPackageContracts.ReadJsonObject(componentProofPath, "hush-web-client-component-proof.json");
        componentProof["runtimeVerification"]!.AsObject()["rawLogLeak"] = "raw log token=not-public";
        File.WriteAllText(componentProofPath, componentProof.ToJsonString());

        var act = () => service.Promote(CreatePromotionOptions(
            workspace.Paths,
            DeploymentProofPackagePromotionService.ModeComponentProof,
            componentId: "hush-web-client",
            deploymentProofId: "DPP-WEB-20260519-001"));

        act.Should().Throw<DeploymentProofPackagePromotionException>()
            .Where(ex => ex.Details.Any(detail => detail.Contains("raw log", StringComparison.OrdinalIgnoreCase)));
        File.ReadAllText(workspace.Paths.CatalogPath).Should().Be(catalogBefore);
    }

    [Fact]
    public void PublicViews_RenderInformalAccountabilityWithoutSignatureOrCertificationClaims()
    {
        var paths = CreatePaths();
        var serverNode = LoadExample(paths, "component-proofs", "hush-server-node-component-proof.json");

        var markdown = DeploymentProofPackageViewRenderer.GetPublicComponentSummary(serverNode);

        markdown.Should().Contain(DeploymentProofPackageViewRenderer.InformalAccountabilityLabel);
        markdown.Should().NotContain("certification");
        markdown.Should().NotContain("external validation");
        markdown.Should().NotContain("legal digital signature");
        markdown.Should().NotContain("cryptographic signature");
        markdown.Should().NotContain("wet signature");
        markdown.Should().NotContain("external witness");
    }

    [Fact]
    public void PromoterCliArtifacts_ExistAndArgumentsParseExpectedModes()
    {
        var paths = CreatePaths();
        var wrapperPath = Path.Combine(paths.WorkspaceRoot, "hush-server-node", "Node", "scripts", "promote-deployment-proof-package.ps1");

        File.Exists(wrapperPath).Should().BeTrue();
        var arguments = CommandLineArguments.Parse(
        [
            "--mode",
            DeploymentProofPackagePromotionService.ModeComponentProof,
            "--workspace-root",
            paths.WorkspaceRoot,
            "--component-id",
            "hush-web-client",
            "--deployment-proof-id",
            "DPP-WEB-20260519-001",
            "--validate-only",
        ]);

        arguments["mode"].Should().Be(DeploymentProofPackagePromotionService.ModeComponentProof);
        arguments.ContainsKey("validate-only").Should().BeTrue();
    }

    [Fact]
    public void Promote_WithValidateOnlyAndNoMode_ValidatesFixturesWithoutWriting()
    {
        using var workspace = TempPromotionWorkspace.Create();

        var result = new DeploymentProofPackagePromotionService().Promote(CreatePromotionOptions(
            workspace.Paths,
            mode: null,
            validateOnly: true));

        result.Mode.Should().Be("validate_all");
        Directory.Exists(workspace.Paths.PublicOutputRoot).Should().BeFalse();
        Directory.Exists(workspace.Paths.RestrictedOutputRoot).Should().BeFalse();
    }

    [Theory]
    [InlineData(DeploymentProofPackagePromotionService.ModeComponentProof, "hush-web-client", "DPP-WEB-20260519-001")]
    [InlineData(DeploymentProofPackagePromotionService.ModeBindingLedger, null, null)]
    [InlineData(DeploymentProofPackagePromotionService.ModeRehearsalCeremony, null, null)]
    public void Promote_WithValidateOnlyMode_DoesNotWritePromotedArtifacts(
        string mode,
        string? componentId,
        string? deploymentProofId)
    {
        using var workspace = TempPromotionWorkspace.Create();

        var result = new DeploymentProofPackagePromotionService().Promote(CreatePromotionOptions(
            workspace.Paths,
            mode,
            componentId,
            deploymentProofId,
            validateOnly: true));

        result.Mode.Should().Be(mode);
        result.WrittenFiles.Should().BeEmpty();
        Directory.Exists(workspace.Paths.PublicOutputRoot).Should().BeFalse();
        Directory.Exists(workspace.Paths.RestrictedOutputRoot).Should().BeFalse();
    }

    [Fact]
    public void Promote_WithOutputRootOutsideWorkspace_FailsClosed()
    {
        var paths = CreatePaths() with
        {
            PublicOutputRoot = Path.Combine(Path.GetTempPath(), $"feat132-outside-{Guid.NewGuid():N}"),
        };

        var act = () => new DeploymentProofPackagePromotionService().Promote(CreatePromotionOptions(
            paths,
            DeploymentProofPackagePromotionService.ModeComponentProof,
            componentId: "hush-web-client",
            deploymentProofId: "DPP-WEB-20260519-001"));

        act.Should().Throw<DeploymentProofPackagePromotionException>()
            .WithMessage("*escapes the workspace root*");
    }

    [Fact]
    public void Promote_MissingComponentProofSource_FailsClosed()
    {
        using var workspace = TempPromotionWorkspace.Create();

        var act = () => new DeploymentProofPackagePromotionService().Promote(CreatePromotionOptions(
            workspace.Paths,
            DeploymentProofPackagePromotionService.ModeComponentProof,
            componentId: "hush-web-client",
            deploymentProofId: "DPP-WEB-MISSING"));

        act.Should().Throw<DeploymentProofPackagePromotionException>()
            .WithMessage("*Component proof source was not found*");
    }

    [Fact]
    public void Promote_ComponentProof_WritesPublicPackageManifestArchiveAndCatalog()
    {
        using var workspace = TempPromotionWorkspace.Create();

        var result = new DeploymentProofPackagePromotionService().Promote(CreatePromotionOptions(
            workspace.Paths,
            DeploymentProofPackagePromotionService.ModeComponentProof,
            componentId: "hush-web-client",
            deploymentProofId: "DPP-WEB-20260519-001"));

        result.PackageId.Should().Be("DPP-WEB-20260519-001");
        File.Exists(Path.Combine(workspace.Paths.PublicOutputRoot, "packages", "hush-web-client", "DPP-WEB-20260519-001", "deployment-proof-package.json")).Should().BeTrue();
        File.Exists(Path.Combine(workspace.Paths.PublicOutputRoot, "packages", "hush-web-client", "DPP-WEB-20260519-001", "deployment-proof-manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(workspace.Paths.PublicOutputRoot, "packages", "hush-web-client", "DPP-WEB-20260519-001", "public-safe-deployment-summary.md")).Should().BeTrue();
        File.Exists(Path.Combine(workspace.Paths.PublicOutputRoot, "packages", "hush-web-client", "DPP-WEB-20260519-001", "deployment-proof-package.zip")).Should().BeTrue();
        File.ReadAllText(workspace.Paths.CatalogPath).Should().Contain("DPP-WEB-20260519-001");
    }

    [Fact]
    public void Promote_ServerNodeComponentProof_WritesIndependentPublicPackageAndCatalogEntry()
    {
        using var workspace = TempPromotionWorkspace.Create();

        var result = new DeploymentProofPackagePromotionService().Promote(CreatePromotionOptions(
            workspace.Paths,
            DeploymentProofPackagePromotionService.ModeComponentProof,
            componentId: "hush-server-node",
            deploymentProofId: "DPP-SERVER-20260519-001"));

        result.PackageId.Should().Be("DPP-SERVER-20260519-001");
        var outputRoot = Path.Combine(workspace.Paths.PublicOutputRoot, "packages", "hush-server-node", "DPP-SERVER-20260519-001");
        File.Exists(Path.Combine(outputRoot, "deployment-proof-package.json")).Should().BeTrue();
        File.Exists(Path.Combine(outputRoot, "deployment-proof-manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(outputRoot, "public-safe-deployment-summary.md")).Should().BeTrue();
        File.Exists(Path.Combine(outputRoot, "deployment-proof-package.zip")).Should().BeTrue();
        var catalog = File.ReadAllText(workspace.Paths.CatalogPath);
        catalog.Should().Contain("DPP-SERVER-20260519-001");
        catalog.Should().Contain("hush-server-node");
    }

    [Fact]
    public void Promote_BindingLedger_WritesElectionBindingPackage()
    {
        using var workspace = TempPromotionWorkspace.Create();

        var result = new DeploymentProofPackagePromotionService().Promote(CreatePromotionOptions(
            workspace.Paths,
            DeploymentProofPackagePromotionService.ModeBindingLedger));

        result.PackageId.Should().Be("DPBL-REHEARSAL-20260519-001");
        var outputRoot = Path.Combine(workspace.Paths.PublicOutputRoot, "election-bindings", "HV-REHEARSAL-PUBLIC-20260519-001", "DPBL-REHEARSAL-20260519-001");
        File.Exists(Path.Combine(outputRoot, "deployment-proof-set.json")).Should().BeTrue();
        File.Exists(Path.Combine(outputRoot, "per-election-deployment-binding-ledger.json")).Should().BeTrue();
        File.Exists(Path.Combine(outputRoot, "per-election-deployment-binding-ledger-manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(outputRoot, "public-safe-binding-summary.md")).Should().BeTrue();
        var proofSet = File.ReadAllText(Path.Combine(outputRoot, "deployment-proof-set.json"));
        var ledger = File.ReadAllText(Path.Combine(outputRoot, "per-election-deployment-binding-ledger.json"));
        proofSet.Should().Contain("DPP-WEB-20260519-001");
        proofSet.Should().Contain("DPP-SERVER-20260519-001");
        ledger.Should().Contain("DPP-WEB-20260519-001");
        ledger.Should().Contain("DPP-SERVER-20260519-001");
    }

    [Fact]
    public void Promote_RehearsalCeremony_WritesPublicRefsRestrictedIndexesAndReadinessFragment()
    {
        using var workspace = TempPromotionWorkspace.Create();

        var result = new DeploymentProofPackagePromotionService().Promote(CreatePromotionOptions(
            workspace.Paths,
            DeploymentProofPackagePromotionService.ModeRehearsalCeremony));

        result.PackageId.Should().Be("DPC-REHEARSAL-20260519-001");
        var publicRoot = Path.Combine(workspace.Paths.PublicOutputRoot, "ceremonies", "DPC-REHEARSAL-20260519-001");
        File.Exists(Path.Combine(publicRoot, "deployment-ceremony.json")).Should().BeTrue();
        File.Exists(Path.Combine(publicRoot, "public-safe-binding-summary.md")).Should().BeTrue();
        File.ReadAllText(Path.Combine(publicRoot, "readiness-fragment.json")).Should().Contain("\"acceptedScore\": 8");
        File.ReadAllText(Path.Combine(publicRoot, "downstream-handoff.json")).Should().Contain("\"consumerFeature\": \"FEAT-133\"");
        File.Exists(Path.Combine(publicRoot, "deployment-ceremony-manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(publicRoot, "deployment-ceremony.zip")).Should().BeTrue();
        File.ReadAllText(workspace.Paths.CatalogPath).Should().Contain("DPC-REHEARSAL-20260519-001");
        var restrictedRoot = Path.Combine(workspace.Paths.RestrictedOutputRoot, "DPC-REHEARSAL-20260519-001");
        File.Exists(Path.Combine(restrictedRoot, "restricted-ceremony-evidence-index.md")).Should().BeTrue();
        File.Exists(Path.Combine(restrictedRoot, "restricted-deployment-evidence-index.md")).Should().BeTrue();
    }

    [Fact]
    public void Promote_RehearsalCeremony_ProducesSyntheticNonConfidentialPackage()
    {
        using var workspace = TempPromotionWorkspace.Create();

        var result = new DeploymentProofPackagePromotionService().Promote(CreatePromotionOptions(
            workspace.Paths,
            DeploymentProofPackagePromotionService.ModeRehearsalCeremony));

        result.WrittenFiles.Should().Contain(path => path.EndsWith("readiness-fragment.json", StringComparison.Ordinal));
        result.WrittenFiles.Should().Contain(path => path.EndsWith("restricted-ceremony-evidence-index.md", StringComparison.Ordinal));
        result.WrittenFiles.Should().Contain(path => path.EndsWith("restricted-deployment-evidence-index.md", StringComparison.Ordinal));
        var publicTextFiles = result.WrittenFiles
            .Where(path => path.StartsWith(workspace.Paths.PublicOutputRoot, StringComparison.OrdinalIgnoreCase))
            .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        foreach (var publicTextFile in publicTextFiles)
        {
            var content = File.ReadAllText(publicTextFile);
            DeploymentProofPackagePublicRedactionScanner.ScanPublicMarkdown(Path.GetFileName(publicTextFile), content)
                .Should().BeEmpty(publicTextFile);
            content.Contains("customer roster", StringComparison.OrdinalIgnoreCase).Should().BeFalse(publicTextFile);
            content.Contains("legally binding ballot", StringComparison.OrdinalIgnoreCase).Should().BeFalse(publicTextFile);
            content.Contains("sensitive personal data", StringComparison.OrdinalIgnoreCase).Should().BeFalse(publicTextFile);
        }
    }

    [Fact]
    public void Promote_SameSourceAndTimestamp_ProducesDeterministicManifestAndArchiveHashes()
    {
        using var first = TempPromotionWorkspace.Create();
        using var second = TempPromotionWorkspace.Create();

        var firstResult = new DeploymentProofPackagePromotionService().Promote(CreatePromotionOptions(
            first.Paths,
            DeploymentProofPackagePromotionService.ModeComponentProof,
            componentId: "hush-web-client",
            deploymentProofId: "DPP-WEB-20260519-001"));
        var secondResult = new DeploymentProofPackagePromotionService().Promote(CreatePromotionOptions(
            second.Paths,
            DeploymentProofPackagePromotionService.ModeComponentProof,
            componentId: "hush-web-client",
            deploymentProofId: "DPP-WEB-20260519-001"));

        secondResult.ManifestHash.Should().Be(firstResult.ManifestHash);
        secondResult.ArchiveHash.Should().Be(firstResult.ArchiveHash);
        File.ReadAllText(second.Paths.CatalogPath).Should().Be(File.ReadAllText(first.Paths.CatalogPath));
    }

    [Fact]
    public void Promote_CatalogConflictWithDifferentHash_FailsClosed()
    {
        using var workspace = TempPromotionWorkspace.Create();
        new DeploymentProofPackagePromotionService().Promote(CreatePromotionOptions(
            workspace.Paths,
            DeploymentProofPackagePromotionService.ModeComponentProof,
            componentId: "hush-web-client",
            deploymentProofId: "DPP-WEB-20260519-001"));

        var catalog = DeploymentProofPackageContracts.ReadJsonObject(workspace.Paths.CatalogPath, "catalog");
        catalog["componentProofs"]!.AsArray()[0]!.AsObject()["manifestHash"] = new string('0', 64);
        File.WriteAllText(workspace.Paths.CatalogPath, catalog.ToJsonString());

        var act = () => new DeploymentProofPackagePromotionService().Promote(CreatePromotionOptions(
            workspace.Paths,
            DeploymentProofPackagePromotionService.ModeComponentProof,
            componentId: "hush-web-client",
            deploymentProofId: "DPP-WEB-20260519-001"));

        act.Should().Throw<DeploymentProofPackagePromotionException>()
            .WithMessage("*Catalog entry conflict*");
    }

    [Fact]
    public void DownstreamHandoff_ReadinessFragment_RecordsScoreMovementAndBlockerResolution()
    {
        var paths = CreatePaths();
        var handoff = LoadExample(paths, "handoffs", "downstream-handoff.json");
        var readiness = handoff["readinessRegisterHandoff"]!.AsObject();
        var scoreChange = readiness["dimensionScoreChange"]!.AsObject();

        handoff["sourceGap"]!.GetValue<string>().Should().Be("Trusted deployment ceremony");
        handoff["acceptanceGate"]!.GetValue<string>().Should().Be("AT-RDY-005");
        readiness["readinessFragmentId"]!.GetValue<string>().Should().Be("RDY-FRAG-AT-RDY-005-FEAT-132-001");
        scoreChange["dimensionId"]!.GetValue<string>().Should().Be("RDY-DIM-006");
        scoreChange["previousScore"]!.GetValue<int>().Should().Be(4);
        scoreChange["acceptedScore"]!.GetValue<int>().Should().Be(8);
        readiness["resolvedBlockers"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .Should().Contain("RDY-BLOCK-FRIENDLY_ORGANIZATION_PILOT-002");
    }

    [Fact]
    public void DownstreamHandoff_CustodyConsumption_RemainsPublicSafe()
    {
        var paths = CreatePaths();
        var handoffPath = Path.Combine(paths.ExamplesRoot, "handoffs", "downstream-handoff.json");
        var text = File.ReadAllText(handoffPath);
        var handoff = DeploymentProofPackageContracts.ReadJsonObject(handoffPath, "downstream-handoff.json");
        var custody = handoff["custodyHandoffConsumption"]!.AsObject();

        custody["acceptedBindingRequirement"]!.GetValue<string>().Should().Contain("live_aws_kms_custody_evidence");
        custody["fakeDevCustodyPolicy"]!.GetValue<string>().Should().Be("tests_and_dry_runs_only");
        custody["publicComponentProofCustodyScope"]!.GetValue<string>().Should().Be("custody_profile_status_only");
        text.Should().NotContain("arn:aws:kms");
        text.Should().NotContain("alias/");
        text.Should().NotContain("decrypt authority");
        DeploymentProofPackagePublicRedactionScanner.ScanPublicMarkdown("downstream-handoff.json", text).Should().BeEmpty();
    }

    [Fact]
    public void DownstreamHandoff_OperationalAndPilotConsumersReceiveStableRefs()
    {
        var paths = CreatePaths();
        var handoff = LoadExample(paths, "handoffs", "downstream-handoff.json");
        var operational = handoff["operationalEvidenceHandoff"]!.AsObject();
        var pilot = handoff["pilotRehearsalHandoff"]!.AsObject();

        operational["consumerFeature"]!.GetValue<string>().Should().Be("FEAT-133");
        operational["webClientProof"]!.AsObject()["deploymentProofId"]!.GetValue<string>().Should().Be("DPP-WEB-20260519-001");
        operational["hushServerNodeProof"]!.AsObject()["deploymentProofId"]!.GetValue<string>().Should().Be("DPP-SERVER-20260519-001");
        operational["proofSetId"]!.GetValue<string>().Should().Be("DPS-REHEARSAL-20260519-001");
        operational["bindingLedgerId"]!.GetValue<string>().Should().Be("DPBL-REHEARSAL-20260519-001");
        operational["catalogRef"]!.AsObject()["repository"]!.GetValue<string>().Should().Be("https://github.com/Hushnetwork-social/Deployment-Proof-Packages");
        pilot["consumerFeature"]!.GetValue<string>().Should().Be("FEAT-141");
        pilot["publicPackageDoesNotRequireRawCustodyOrProviderData"]!.GetValue<bool>().Should().BeTrue();
        pilot["publicRefs"]!.AsArray().Should().HaveCount(3);
        pilot["restrictedRefs"]!.AsArray().Should().HaveCount(2);
    }

    [Fact]
    public void DownstreamHandoff_RuntimeVisibilityContract_PreservesEventModelWithoutUiScope()
    {
        var paths = CreatePaths();
        var handoff = LoadExample(paths, "handoffs", "downstream-handoff.json");
        var runtime = handoff["runtimeVisibilityContract"]!.AsObject();

        runtime["implementedInFeat132"]!.GetValue<bool>().Should().BeFalse();
        runtime["productionRuntimeUiScope"]!.GetValue<string>().Should().Be("not_implemented_by_FEAT-132");
        runtime["eventModelFields"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .Should().Contain(["timestamp", "classification", "reason", "checksRerun", "accountabilityMarker"]);
        runtime["reconciliationCheckpoints"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .Should().Contain(["Draft -> Open", "Open -> Close", "Close -> Finalize", "Open -> Void", "Close -> Void", "final_package_export"]);
    }

    private static DeploymentProofPackagePromotionPaths CreatePaths()
    {
        var workspaceRoot = WorkspaceRootFinder.Find(AppContext.BaseDirectory);
        return DeploymentProofPackagePromotionPaths.FromWorkspaceRoot(workspaceRoot);
    }

    private static JsonObject LoadExample(
        DeploymentProofPackagePromotionPaths paths,
        string folder,
        string fileName) =>
        DeploymentProofPackageContracts.ReadJsonObject(
            Path.Combine(paths.ExamplesRoot, folder, fileName),
            fileName);

    private static DeploymentProofPackagePromotionOptions CreatePromotionOptions(
        DeploymentProofPackagePromotionPaths paths,
        string? mode,
        string? componentId = null,
        string? deploymentProofId = null,
        bool validateOnly = false) =>
        new(
            paths,
            mode,
            componentId,
            deploymentProofId,
            CeremonyId: null,
            ClassificationInput: null,
            CdProvider: null,
            CdRunId: null,
            FixedGeneratedAt,
            validateOnly,
            Scaffold: false,
            CaptureLiveEvidence: false);

    private sealed class TempPromotionWorkspace : IDisposable
    {
        private TempPromotionWorkspace(string root, DeploymentProofPackagePromotionPaths paths)
        {
            Root = root;
            Paths = paths;
        }

        public string Root { get; }

        public DeploymentProofPackagePromotionPaths Paths { get; }

        public static TempPromotionWorkspace Create(bool mutableSource = false)
        {
            var basePaths = CreatePaths();
            var root = Path.Combine(basePaths.WorkspaceRoot, ".tmp-feat132-tests", Guid.NewGuid().ToString("N"));
            var sourceRoot = mutableSource
                ? Path.Combine(root, "source")
                : basePaths.SourceRoot;
            if (mutableSource)
            {
                CopyDirectory(basePaths.SourceRoot, sourceRoot);
            }

            var paths = basePaths with
            {
                SourceRoot = sourceRoot,
                PublicOutputRoot = Path.Combine(root, "public"),
                RestrictedOutputRoot = Path.Combine(root, "restricted"),
            };

            return new TempPromotionWorkspace(root, paths);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, file);
                var destinationPath = Path.Combine(destinationDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(file, destinationPath, overwrite: true);
            }
        }
    }

    private static DeploymentImpactClassificationInput CreateWebsiteOnlyInput() =>
        new()
        {
            ClassificationInputId = "WEB-20260519-001",
            ChangedPaths = ["hush-web-client/src/hush-voting/components/StatusBanner.tsx"],
            AffectedServices = ["hush-web-client"],
            EvidenceRefs = ["DPP-WEB-20260519-001"],
            RefDiffs = SafeNoProtocolRefDiffs() with
            {
                WebArtifactHashChanged = true,
            },
        };

    private static DeploymentImpactClassificationInput CreateProtocolChangeInput() =>
        CreateWebsiteOnlyInput() with
        {
            RefDiffs = SafeNoProtocolRefDiffs() with
            {
                ProtocolPackageHashChanged = true,
            },
        };

    private static DeploymentImpactClassificationInput CreateServerNoProtocolChangeInput() =>
        new()
        {
            ClassificationInputId = "SERVER-20260519-001",
            ChangedPaths = ["deployment/hushvoting/server-container.json"],
            AffectedServices = ["hush-server-node"],
            EvidenceRefs = ["DPP-SERVER-20260519-001"],
            RefDiffs = SafeNoProtocolRefDiffs() with
            {
                BackendImageDigestChanged = true,
            },
        };

    private static DeploymentImpactClassificationInput CreateNonVotingServiceInput() =>
        new()
        {
            ClassificationInputId = "FEEDS-20260519-001",
            ChangedPaths = ["hush-server-node/Node/Core/Feeds/FeedProjection.cs"],
            AffectedServices = ["hush-feeds"],
            EvidenceRefs = ["DPP-SERVER-20260519-001"],
            RefDiffs = SafeNoProtocolRefDiffs(),
        };

    private static DeploymentImpactClassificationInput CreateOperationalConfigInput() =>
        new()
        {
            ClassificationInputId = "CONFIG-20260519-001",
            ChangedPaths = ["deployment/hushvoting/runtime-profile.json"],
            AffectedServices = ["hushvoting-deployment"],
            EvidenceRefs = ["DPP-SERVER-20260519-001"],
            RefDiffs = SafeNoProtocolRefDiffs() with
            {
                ConfigProfileHashChanged = true,
                ConfigProfileHashRecorded = true,
            },
        };

    private static DeploymentImpactClassificationInput CreateEmergencyInput() =>
        CreateNonVotingServiceInput() with
        {
            ClassificationInputId = "EMERGENCY-20260519-001",
            SpecialChange = new DeploymentSpecialChangeEvidence
            {
                IsEmergencyChange = true,
                IsNonStateBreakingFix = true,
                Reason = "Synthetic non-state-breaking bug fix during an open rehearsal.",
                RerunChecks = ["runtime-verification", "public-redaction-scan"],
                AccountabilityMarker = "aboimpinto-hushvoting-owner",
            },
        };

    private static DeploymentImpactClassificationInput CreateRollbackInput() =>
        CreateServerNoProtocolChangeInput() with
        {
            ClassificationInputId = "ROLLBACK-20260519-001",
            SpecialChange = new DeploymentSpecialChangeEvidence
            {
                IsRollback = true,
                RollbackToLastCeremonyApprovedArtifactSet = true,
                StateCompatibilityEvidenceAvailable = true,
                RerunChecks = ["runtime-verification", "public-redaction-scan"],
                AccountabilityMarker = "aboimpinto-hushvoting-owner",
            },
        };

    private static DeploymentRefDiffEvidence SafeNoProtocolRefDiffs() =>
        new()
        {
            ProtocolPackageHashChanged = false,
            CircuitOrKeyRefChanged = false,
            BackendVotingCriticalHashChanged = false,
            BackendImageDigestChanged = false,
            WebArtifactHashChanged = false,
            VerifierOrExporterHashChanged = false,
            DbMigrationStateChanged = false,
            CustodyProfileChanged = false,
            DeploymentProfileChanged = false,
            ConfigProfileHashChanged = false,
            ConfigProfileHashRecorded = true,
        };

    private static DeploymentSemanticChangeEvidence CreateSemanticChange(string semanticChange) =>
        semanticChange switch
        {
            "ballot" => new DeploymentSemanticChangeEvidence { BallotDefinitionChanged = true },
            "eligibility" => new DeploymentSemanticChangeEvidence { EligibilityOrCheckoffChanged = true },
            "custody" => new DeploymentSemanticChangeEvidence { CustodySemanticsChanged = true },
            "accepted-ballot" => new DeploymentSemanticChangeEvidence { AcceptedBallotSemanticsChanged = true },
            "published-evidence" => new DeploymentSemanticChangeEvidence { PublishedEvidenceSemanticsChanged = true },
            "tally" => new DeploymentSemanticChangeEvidence { TallyOrCountingLogicChanged = true },
            "verifier-output" => new DeploymentSemanticChangeEvidence { VerifierOutputSemanticsChanged = true },
            "final-package" => new DeploymentSemanticChangeEvidence { FinalPackageSchemaChanged = true },
            "election-critical-migration" => new DeploymentSemanticChangeEvidence { ElectionCriticalDbMigrationChanged = true },
            _ => throw new ArgumentOutOfRangeException(nameof(semanticChange), semanticChange, "Unknown semantic change case."),
        };
}
