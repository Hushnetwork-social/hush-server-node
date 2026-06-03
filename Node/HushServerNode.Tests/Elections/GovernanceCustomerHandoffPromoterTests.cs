using System.Text.Json.Nodes;
using FluentAssertions;
using GovernanceCustomerHandoffPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class GovernanceCustomerHandoffPromoterTests
{
    private static readonly DateTimeOffset FixedGeneratedAt = DateTimeOffset.Parse("2026-06-03T12:00:00Z");

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();

        var errors = GovernanceCustomerHandoffContracts.ValidateSchemaSet(workspace.Paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in GovernanceCustomerHandoffContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(workspace.Paths.SchemasRoot, schemaFile)).Should().BeTrue(schemaFile);
        }
    }

    [Fact]
    public void SourceValidation_AcceptedReleaseBaseline_ShouldPass()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();

        var result = CreateService().Promote(new(
            workspace.Paths,
            GovernanceCustomerHandoffPromotionService.ModeValidateOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        result.Mode.Should().Be(GovernanceCustomerHandoffPromotionService.ModeValidateOnly);
        result.Status.Should().Be("accepted");
        result.WrittenFiles.Should().BeEmpty();
        Directory.Exists(workspace.DefaultPackageRoot).Should().BeFalse();
    }

    [Fact]
    public void SourceValidation_StaleReadinessBaseline_IsRejected()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var source = workspace.LoadSource();
        source["readinessBaseline"]!.AsObject()["registerVersion"] = "RDY-REG-v0.1.6";

        var errors = GovernanceCustomerHandoffContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("GCH-STALE-READINESS-BASELINE", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_WrongScoreProposal_IsRejected()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var source = workspace.LoadSource();
        source["scoreProposal"]!.AsObject()["movement"] = "9 -> 10";

        var errors = GovernanceCustomerHandoffContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("GCH-SCORE-PROPOSAL-MISMATCH", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_DirectRegisterMutation_IsRejected()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var source = workspace.LoadSource();
        source["scoreProposal"]!.AsObject()["directRegisterMutation"] = true;

        var errors = GovernanceCustomerHandoffContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("GCH-DIRECT-REGISTER-MUTATION", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_MissingUpstreamFeat165_IsRejected()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var source = workspace.LoadSource();
        var upstream = source["upstreamEvidence"]!.AsArray();
        var feat165 = FindUpstream(source, "FEAT-165");
        upstream.Remove(feat165);

        var errors = GovernanceCustomerHandoffContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("GCH-UPSTREAM-REF-STALE: required upstream evidence is missing: FEAT-165", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_Feat156MustBeAcceptedCurrent()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var source = workspace.LoadSource();
        var feat156 = FindUpstream(source, "FEAT-156");
        feat156["status"] = "accepted-input";

        var errors = GovernanceCustomerHandoffContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("GCH-UPSTREAM-REF-STALE: FEAT-156", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_PrivatePath_IsRejected()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var source = workspace.LoadSource();
        var feat140 = FindUpstream(source, "FEAT-140");
        feat140["evidenceRefs"]!.AsArray()[0]!.AsObject()["ref"] =
            "PrivateServer_ElectronicVoting/Governance-Customer-Handoff/raw-customer-material.json";

        var errors = GovernanceCustomerHandoffContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("GCH-PRIVATE-MATERIAL-PUBLISHED", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_MissingResponsibilityDomain_IsRejected()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var source = workspace.LoadSource();
        var domains = source["responsibilityDomains"]!.AsArray();
        var certification = FindResponsibilityDomain(source, "independent-certification");
        domains.Remove(certification);

        var errors = GovernanceCustomerHandoffContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("GCH-RESPONSIBILITY-DOMAIN-MISSING: required responsibility domain is missing: independent-certification", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_ResponsibilityDomainMissingEvidenceRef_IsRejected()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var source = workspace.LoadSource();
        var customerGovernance = FindResponsibilityDomain(source, "customer-governance");
        customerGovernance["evidenceRefs"] = new JsonArray();

        var errors = GovernanceCustomerHandoffContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("GCH-RESPONSIBILITY-DOMAIN-MISSING: customer-governance requires at least one evidence ref.", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_ResponsibilityPrivateOnlyVisibility_IsRejected()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var source = workspace.LoadSource();
        var externalLegal = FindResponsibilityDomain(source, "external-legal-authority");
        externalLegal["visibility"] = "private-only";

        var errors = GovernanceCustomerHandoffContracts.ValidateSource(source, publicOnly: true);

        errors.Should().Contain(error => error.Contains("GCH-PRIVATE-MATERIAL-PUBLISHED", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_LegalSufficiencyOverclaim_IsRejected()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var source = workspace.LoadSource();
        var hushTechnical = FindResponsibilityDomain(source, "hush-technical-proof");
        hushTechnical["mayClaim"]!.AsArray().Add("Hush certifies legal sufficiency.");

        var errors = GovernanceCustomerHandoffContracts.ValidateSource(source, publicOnly: true);

        errors.Should().Contain(error => error.Contains("GCH-LEGAL-SUFFICIENCY-OVERCLAIM", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_NonClaimBoundaryWrongResultCode_IsRejected()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var source = workspace.LoadSource();
        var boundaries = source["nonClaimBoundaries"]!.AsArray();
        var legalSufficiency = boundaries
            .OfType<JsonObject>()
            .First(item => GovernanceCustomerHandoffContracts.GetString(item, "category") == "legal-sufficiency");
        legalSufficiency["blockingResultCode"] = "GCH-HUSH-OVERCLAIM";

        var errors = GovernanceCustomerHandoffContracts.ValidateSource(source, publicOnly: true);

        errors.Should().Contain(error => error.Contains("GCH-NON-CLAIM-MISSING: legal-sufficiency must use blockingResultCode GCH-LEGAL-SUFFICIENCY-OVERCLAIM.", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_MissingExternalRoute_IsRejected()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var source = workspace.LoadSource();
        var routes = source["externalPrerequisiteRouting"]!.AsObject()["routes"]!.AsArray();
        routes.Remove(FindExternalRoute(source, "GCH-ROUTE-EXTERNAL-LEGAL-REVIEW"));

        var errors = GovernanceCustomerHandoffContracts.ValidateSource(source, publicOnly: true);

        errors.Should().Contain(error => error.Contains("GCH-EXTERNAL-PREREQUISITE-ROUTE-MISSING: required external prerequisite route is missing: GCH-ROUTE-EXTERNAL-LEGAL-REVIEW", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_ExternalRouteMustFailClosed()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var source = workspace.LoadSource();
        var route = FindExternalRoute(source, "GCH-ROUTE-CUSTOMER-AUTHORITY-REVIEW");
        route["missingInputBehavior"] = "continue-open";

        var errors = GovernanceCustomerHandoffContracts.ValidateSource(source, publicOnly: true);

        errors.Should().Contain(error => error.Contains("GCH-EXTERNAL-PREREQUISITE-ROUTE-MISSING: GCH-ROUTE-CUSTOMER-AUTHORITY-REVIEW must set missingInputBehavior to fail-closed.", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_ChecklistSectionAnswerPublished_IsRejected()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var source = workspace.LoadSource();
        var section = FindChecklistSection(source, "GCH-CHECKLIST-AUTHORITY");
        section["answerPublished"] = true;

        var errors = GovernanceCustomerHandoffContracts.ValidateSource(source, publicOnly: true);

        errors.Should().Contain(error => error.Contains("GCH-CUSTOMER-ANSWER-PUBLISHED", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_MissingChecklistSection_IsRejected()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var source = workspace.LoadSource();
        var sections = source["customerChecklist"]!.AsObject()["sections"]!.AsArray();
        sections.Remove(FindChecklistSection(source, "GCH-CHECKLIST-GOVERNANCE-RULES"));

        var errors = GovernanceCustomerHandoffContracts.ValidateSource(source, publicOnly: true);

        errors.Should().Contain(error => error.Contains("GCH-CUSTOMER-CHECKLIST-BOUNDARY-MISSING: required customer checklist section is missing: GCH-CHECKLIST-GOVERNANCE-RULES", StringComparison.Ordinal));
    }

    [Fact]
    public void NegativeFixtureCatalog_ReferencesKnownResultCodes()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();

        var source = GovernanceCustomerHandoffContracts.ValidateForPromotion(
            workspace.Paths,
            sourceInput: null,
            publicOnly: true);

        source["featureId"]!.GetValue<string>().Should().Be(GovernanceCustomerHandoffContracts.FeatureId);
    }

    [Fact]
    public void NegativeFixtureCatalog_CoversPhase3FailureCases()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var catalog = GovernanceCustomerHandoffContracts.ReadJsonObject(
            Path.Combine(workspace.Paths.ExamplesRoot, "negative", GovernanceCustomerHandoffPromotionPaths.NegativeFixtureCatalogFileName),
            "negative fixture catalog");
        var caseIds = catalog["fixtures"]!.AsArray()
            .OfType<JsonObject>()
            .Select(item => GovernanceCustomerHandoffContracts.GetString(item, "caseId"))
            .ToHashSet(StringComparer.Ordinal);

        caseIds.Should().Contain([
            "GCH-NEG-MISSING-UPSTREAM-REF",
            "GCH-NEG-DIRECT-REGISTER-MUTATION",
            "GCH-NEG-SCORE-PROPOSAL-MISMATCH",
            "GCH-NEG-RESTRICTED-PAYLOAD-PUBLISHED",
        ]);
    }

    [Fact]
    public void NegativeFixtureCatalog_CoversPhase5FailureCases()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var catalog = GovernanceCustomerHandoffContracts.ReadJsonObject(
            Path.Combine(workspace.Paths.ExamplesRoot, "negative", GovernanceCustomerHandoffPromotionPaths.NegativeFixtureCatalogFileName),
            "negative fixture catalog");
        var caseIds = catalog["fixtures"]!.AsArray()
            .OfType<JsonObject>()
            .Select(item => GovernanceCustomerHandoffContracts.GetString(item, "caseId"))
            .ToHashSet(StringComparer.Ordinal);

        caseIds.Should().Contain([
            "GCH-NEG-MISSING-EXTERNAL-ROUTE",
            "GCH-NEG-MISSING-CHECKLIST-SECTION",
            "GCH-NEG-CUSTOMER-ANSWER-PUBLISHED",
        ]);
    }

    [Fact]
    public void Promotion_PackageMode_WritesCurrentnessSummaries()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();

        var result = CreateService().Promote(new(
            workspace.Paths,
            GovernanceCustomerHandoffPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        result.WrittenFiles.Should().HaveCount(GovernanceCustomerHandoffArtifactGenerator.RequiredArtifactPaths.Length);

        var readiness = ReadGeneratedArtifact(workspace, GovernanceCustomerHandoffArtifactGenerator.ReadinessBaselineCurrentnessSummaryPath);
        readiness["readinessBaseline"]!.AsObject()["dimension"]!.GetValue<string>().Should().Be("RDY-DIM-010");
        readiness["scoreProposal"]!.AsObject()["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();

        var upstream = ReadGeneratedArtifact(workspace, GovernanceCustomerHandoffArtifactGenerator.UpstreamCurrentnessSummaryPath);
        upstream["requiredFeatureCount"]!.GetValue<int>().Should().Be(GovernanceCustomerHandoffContracts.RequiredUpstreamFeatures.Length);
        upstream["missingFeatureIds"]!.AsArray().Should().BeEmpty();
        upstream["staleOrBlockedFeatureIds"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public void Promotion_PackageMode_WritesPhase4Summaries()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();

        CreateService().Promote(new(
            workspace.Paths,
            GovernanceCustomerHandoffPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        var responsibility = ReadGeneratedArtifact(workspace, GovernanceCustomerHandoffArtifactGenerator.ResponsibilityMatrixSummaryPath);
        responsibility["requiredRowCount"]!.GetValue<int>().Should().Be(GovernanceCustomerHandoffContracts.RequiredResponsibilityDomains.Length);
        responsibility["observedRowCount"]!.GetValue<int>().Should().Be(GovernanceCustomerHandoffContracts.RequiredResponsibilityDomains.Length);
        responsibility["missingRows"]!.AsArray().Should().BeEmpty();
        responsibility["allRowsHaveEvidenceRefs"]!.GetValue<bool>().Should().BeTrue();
        responsibility["missingInputFailsClosed"]!.GetValue<bool>().Should().BeTrue();

        var nonClaim = ReadGeneratedArtifact(workspace, GovernanceCustomerHandoffArtifactGenerator.NonClaimBoundarySummaryPath);
        nonClaim["requiredBoundaryCount"]!.GetValue<int>().Should().Be(GovernanceCustomerHandoffContracts.RequiredNonClaimCategories.Length);
        nonClaim["missingBoundaryCategories"]!.AsArray().Should().BeEmpty();
        nonClaim["legalSufficiencyClaimed"]!.GetValue<bool>().Should().BeFalse();
        nonClaim["publicStateReadinessClaimed"]!.GetValue<bool>().Should().BeFalse();
        nonClaim["directRegisterMutationClaimed"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void Promotion_PackageMode_WritesPhase5Summaries()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();

        CreateService().Promote(new(
            workspace.Paths,
            GovernanceCustomerHandoffPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        var routing = ReadGeneratedArtifact(workspace, GovernanceCustomerHandoffArtifactGenerator.ExternalPrerequisiteRoutingSummaryPath);
        routing["requiredRouteCount"]!.GetValue<int>().Should().Be(GovernanceCustomerHandoffContracts.RequiredExternalPrerequisiteRoutes.Length);
        routing["observedRouteCount"]!.GetValue<int>().Should().Be(GovernanceCustomerHandoffContracts.RequiredExternalPrerequisiteRoutes.Length);
        routing["missingRoutes"]!.AsArray().Should().BeEmpty();
        routing["allRoutesFailClosed"]!.GetValue<bool>().Should().BeTrue();
        routing["publicStateReadinessClaimed"]!.GetValue<bool>().Should().BeFalse();

        var checklist = ReadGeneratedArtifact(workspace, GovernanceCustomerHandoffArtifactGenerator.CustomerChecklistBoundarySummaryPath);
        checklist["requiredSectionCount"]!.GetValue<int>().Should().Be(GovernanceCustomerHandoffContracts.RequiredCustomerChecklistSections.Length);
        checklist["missingSections"]!.AsArray().Should().BeEmpty();
        checklist["allSectionsFailClosed"]!.GetValue<bool>().Should().BeTrue();
        checklist["allAnswersPrivate"]!.GetValue<bool>().Should().BeTrue();
        checklist["privateAnswerPayloadPublished"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void Promotion_PackageMode_WritesPhase6PublicPackageArtifacts()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();

        var result = CreateService().Promote(new(
            workspace.Paths,
            GovernanceCustomerHandoffPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        result.WrittenFiles.Should().HaveCount(GovernanceCustomerHandoffArtifactGenerator.RequiredArtifactPaths.Length);

        var package = ReadGeneratedArtifact(workspace, GovernanceCustomerHandoffArtifactGenerator.PackageIndexPath);
        package["schemaVersion"]!.GetValue<string>().Should().Be("governance-customer-handoff-package/v1");
        package["publicOnlyValidation"]!.GetValue<bool>().Should().BeTrue();
        package["restrictedEvidence"]!.AsObject()["payloadsPublishedHere"]!.GetValue<bool>().Should().BeFalse();

        var manifest = ReadGeneratedArtifact(workspace, GovernanceCustomerHandoffArtifactGenerator.PackageManifestPath);
        manifest["schemaVersion"]!.GetValue<string>().Should().Be("governance-customer-handoff-package-manifest/v1");
        manifest["artifactRefs"]!.AsArray().Should().NotBeEmpty();
        manifest["validationRefs"]!.AsArray().Should().HaveCountGreaterThanOrEqualTo(6);
        manifest["restrictedEvidenceRefs"]!.AsArray().Should().NotBeEmpty();

        var publicOnly = ReadGeneratedArtifact(workspace, GovernanceCustomerHandoffArtifactGenerator.PublicOnlyValidationSummaryPath);
        publicOnly["privateCheckoutRequired"]!.GetValue<bool>().Should().BeFalse();
        publicOnly["credentialRequired"]!.GetValue<bool>().Should().BeFalse();

        var scan = ReadGeneratedArtifact(workspace, GovernanceCustomerHandoffArtifactGenerator.NoSecretScanResultPath);
        scan["status"]!.GetValue<string>().Should().Be("accepted");
        scan["findingCount"]!.GetValue<int>().Should().Be(0);

        File.Exists(Path.Combine(
            workspace.DefaultPackageRoot,
            GovernanceCustomerHandoffArtifactGenerator.RestrictedEvidenceIndexSchemaNotePath.Replace('/', Path.DirectorySeparatorChar))).Should().BeTrue();
    }

    [Fact]
    public void Promotion_PackageMode_WritesPrivateRestrictedIndexWhenNotPublicOnly()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var service = CreateService();

        var packageResult = service.Promote(new(
            workspace.Paths,
            GovernanceCustomerHandoffPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: false));

        packageResult.WrittenFiles.Should().HaveCount(GovernanceCustomerHandoffArtifactGenerator.RequiredArtifactPaths.Length + 1);

        var restricted = ReadRestrictedArtifact(workspace, GovernanceCustomerHandoffArtifactGenerator.PrivateRestrictedEvidenceIndexPath);
        restricted["payloadsPublished"]!.GetValue<bool>().Should().BeFalse();
        restricted["publicOnlyValidationRequiresPrivatePayload"]!.GetValue<bool>().Should().BeFalse();
        restricted["restrictedRefs"]!.AsArray().Should().NotBeEmpty();

        var checkResult = service.Promote(new(
            workspace.Paths,
            GovernanceCustomerHandoffPromotionService.ModeCheckOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: false));

        checkResult.CheckedFiles.Should().HaveCount(GovernanceCustomerHandoffArtifactGenerator.RequiredArtifactPaths.Length + 1);
    }

    [Fact]
    public void Promotion_PackageMode_WritesPhase7ReadinessProposalAndDownstreamHandoff()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();

        CreateService().Promote(new(
            workspace.Paths,
            GovernanceCustomerHandoffPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        var readiness = ReadGeneratedArtifact(workspace, GovernanceCustomerHandoffArtifactGenerator.ReadinessFragmentPath);
        readiness["schemaVersion"]!.GetValue<string>().Should().Be("governance-customer-handoff-readiness-fragment/v1");
        readiness["dimension"]!.GetValue<string>().Should().Be("RDY-DIM-010");
        readiness["currentScore"]!.GetValue<int>().Should().Be(8);
        readiness["proposedScore"]!.GetValue<int>().Should().Be(9);
        readiness["movement"]!.GetValue<string>().Should().Be("8 -> 9");
        readiness["proposalOnly"]!.GetValue<bool>().Should().BeTrue();
        readiness["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();
        readiness["nonClaims"]!.AsObject()["publicStateReadinessClaimed"]!.GetValue<bool>().Should().BeFalse();

        var score = ReadGeneratedArtifact(workspace, GovernanceCustomerHandoffArtifactGenerator.ScoreProposalPath);
        score["schemaVersion"]!.GetValue<string>().Should().Be("governance-customer-handoff-score-proposal/v1");
        score["status"]!.GetValue<string>().Should().Be("proposal_only");
        score["movement"]!.GetValue<string>().Should().Be("8 -> 9");
        score["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();
        score["canonicalRegisterMutationPerformed"]!.GetValue<bool>().Should().BeFalse();
        score["readinessFragmentRef"]!.AsObject()["path"]!.GetValue<string>().Should().Be(GovernanceCustomerHandoffArtifactGenerator.ReadinessFragmentPath);

        var handoff = ReadGeneratedArtifact(workspace, GovernanceCustomerHandoffArtifactGenerator.DownstreamHandoffPath);
        handoff["schemaVersion"]!.GetValue<string>().Should().Be("governance-customer-handoff-downstream-handoff/v1");
        handoff["status"]!.GetValue<string>().Should().Be("handoff_ready");
        handoff["downstreamTargets"]!.AsArray().Should().HaveCount(4);
        handoff["nonClaims"]!.AsObject()["externalAuditAcceptanceClaimed"]!.GetValue<bool>().Should().BeFalse();
        handoff["nonClaims"]!.AsObject()["directReadinessRegisterMutationClaimed"]!.GetValue<bool>().Should().BeFalse();

        var manifest = ReadGeneratedArtifact(workspace, GovernanceCustomerHandoffArtifactGenerator.PackageManifestPath);
        manifest["readinessProposal"]!.AsObject()["proposalRef"]!.AsObject()["path"]!.GetValue<string>()
            .Should().Be(GovernanceCustomerHandoffArtifactGenerator.ScoreProposalPath);
        manifest["handoffRefs"]!.AsArray()
            .OfType<JsonObject>()
            .Select(item => item["path"]!.GetValue<string>())
            .Should().Contain(GovernanceCustomerHandoffArtifactGenerator.DownstreamHandoffPath);
    }

    [Fact]
    public void Promotion_CheckOnly_ShouldPassAfterPackageMode()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var service = CreateService();
        service.Promote(new(
            workspace.Paths,
            GovernanceCustomerHandoffPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        var result = service.Promote(new(
            workspace.Paths,
            GovernanceCustomerHandoffPromotionService.ModeCheckOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        result.CheckedFiles.Should().HaveCount(GovernanceCustomerHandoffArtifactGenerator.RequiredArtifactPaths.Length);
    }

    [Fact]
    public void Promotion_CheckOnly_DetectsCurrentnessSummaryDrift()
    {
        using var workspace = TempGovernanceCustomerHandoffWorkspace.Create();
        var service = CreateService();
        service.Promote(new(
            workspace.Paths,
            GovernanceCustomerHandoffPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));
        File.WriteAllText(
            Path.Combine(
                workspace.DefaultPackageRoot,
                GovernanceCustomerHandoffArtifactGenerator.UpstreamCurrentnessSummaryPath.Replace('/', Path.DirectorySeparatorChar)),
            "drifted summary");

        var act = () => service.Promote(new(
            workspace.Paths,
            GovernanceCustomerHandoffPromotionService.ModeCheckOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        act.Should().Throw<GovernanceCustomerHandoffPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains(GovernanceCustomerHandoffArtifactGenerator.UpstreamCurrentnessSummaryPath, StringComparison.Ordinal));
    }

    private static GovernanceCustomerHandoffPromotionService CreateService() => new();

    private static JsonObject FindUpstream(JsonObject source, string featureId) =>
        source["upstreamEvidence"]!.AsArray()
            .OfType<JsonObject>()
            .First(item => GovernanceCustomerHandoffContracts.GetString(item, "featureId") == featureId);

    private static JsonObject FindResponsibilityDomain(JsonObject source, string domain) =>
        source["responsibilityDomains"]!.AsArray()
            .OfType<JsonObject>()
            .First(item => GovernanceCustomerHandoffContracts.GetString(item, "domain") == domain);

    private static JsonObject FindExternalRoute(JsonObject source, string routeId) =>
        source["externalPrerequisiteRouting"]!.AsObject()["routes"]!.AsArray()
            .OfType<JsonObject>()
            .First(item => GovernanceCustomerHandoffContracts.GetString(item, "routeId") == routeId);

    private static JsonObject FindChecklistSection(JsonObject source, string sectionId) =>
        source["customerChecklist"]!.AsObject()["sections"]!.AsArray()
            .OfType<JsonObject>()
            .First(item => GovernanceCustomerHandoffContracts.GetString(item, "sectionId") == sectionId);

    private static JsonObject ReadGeneratedArtifact(TempGovernanceCustomerHandoffWorkspace workspace, string relativePath) =>
        GovernanceCustomerHandoffContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultPackageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            relativePath);

    private static JsonObject ReadRestrictedArtifact(TempGovernanceCustomerHandoffWorkspace workspace, string relativePath) =>
        GovernanceCustomerHandoffContracts.ReadJsonObject(
            Path.Combine(workspace.Paths.RestrictedEvidenceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            relativePath);

    private sealed class TempGovernanceCustomerHandoffWorkspace : IDisposable
    {
        private TempGovernanceCustomerHandoffWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "feat166-governance-customer-handoff-" + Guid.NewGuid().ToString("N"));
            Paths = GovernanceCustomerHandoffPromotionPaths.FromWorkspaceRoot(Root);
            OutputRoot = Path.Combine(Root, "package-output");
            CopyDirectory(FindSourcePublicRepositoryRoot(), Paths.PublicRepositoryRoot);
        }

        public string Root { get; }

        public GovernanceCustomerHandoffPromotionPaths Paths { get; }

        public string OutputRoot { get; }

        public string DefaultPackageRoot => Path.Combine(
            OutputRoot,
            GovernanceCustomerHandoffPromotionPaths.PackageFamilyFolder,
            GovernanceCustomerHandoffPromotionPaths.DefaultHandoffRunId);

        public static TempGovernanceCustomerHandoffWorkspace Create() => new();

        public JsonObject LoadSource() => GovernanceCustomerHandoffContracts.LoadSource(Paths);

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
                var candidate = Path.Combine(directory.FullName, "Governance-Customer-Handoff");
                if (Directory.Exists(candidate) &&
                    File.Exists(Path.Combine(candidate, "examples", "release-baseline", GovernanceCustomerHandoffPromotionPaths.SourceFileName)))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Unable to locate Governance-Customer-Handoff public repository.");
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
