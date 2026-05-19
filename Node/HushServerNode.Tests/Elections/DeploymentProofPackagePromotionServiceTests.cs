using System.Text.Json.Nodes;
using DeploymentProofPackagePromoter;
using FluentAssertions;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class DeploymentProofPackagePromotionServiceTests
{
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
