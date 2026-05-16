using System.Text.Json;
using FluentAssertions;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Focused browser coverage for FEAT-129 report-package anomaly projections.
/// </summary>
[Binding]
internal sealed class HushVotingAnomalyReportE2ESteps : BrowserStepsBase
{
    private const string ElectionId = "feat-129-e2e-election";
    private const string ElectionTitle = "FEAT-129 anomaly report package";
    private const string ReportPackageId = "report-package-feat-129";
    private const string RestrictedManifestArtifactId = "restricted-anomaly-artifact-feat-129";
    private const string RestrictedManifestHash = "sha256:restricted-manifest-feat-129";
    private const string RestrictedPayloadReference = "encrypted://payload/secret-feat-129";
    private const string RestrictedSubmitterReference = "submitter-public-key-feat-129";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    public HushVotingAnomalyReportE2ESteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    [Given(@"the FEAT-129 anomaly report package query responses are seeded for the browser")]
    public async Task GivenTheFeat129AnomalyReportPackageQueryResponsesAreSeededForTheBrowser()
    {
        var page = await GetOrCreatePageAsync();

        await page.RouteAsync("**/api/elections/query", async route =>
        {
            using var document = JsonDocument.Parse(route.Request.PostData ?? "{}");
            var method = GetMethod(document.RootElement);
            var actorPublicAddress = GetActorPublicAddress(document.RootElement);
            var response = BuildElectionQueryResponse(method, actorPublicAddress);

            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = response.Status,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(response.Body, JsonOptions),
            });
        });

        Console.WriteLine("[E2E HushVoting FEAT-129] Browser query responses seeded");
    }

    [When(@"the user opens the FEAT-129 anomaly result package election")]
    public async Task WhenTheUserOpensTheFeat129AnomalyResultPackageElection()
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine("[E2E HushVoting FEAT-129] Opening result package election...");
        await NavigateToAsync(page, $"/elections/{ElectionId}");

        await Assertions.Expect(page).ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex($@"/elections/{ElectionId}"),
            new PageAssertionsToHaveURLOptions { Timeout = 15000 });

        await WaitForTestIdAsync(page, "hush-voting-section-artifacts", 30000);
        await EnsureArtifactsExpandedAsync(page);

        Console.WriteLine("[E2E HushVoting FEAT-129] Result package election opened");
    }

    [Then(@"the FEAT-129 anomaly report package summary should be visible")]
    public async Task ThenTheFeat129AnomalyReportPackageSummaryShouldBeVisible()
    {
        var page = await GetOrCreatePageAsync();

        await EnsureArtifactsExpandedAsync(page);
        await Assertions.Expect(page.GetByTestId("report-package-summary")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        await Assertions.Expect(page.GetByTestId("public-anomaly-summary-panel")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        await ExpectVisibleTextAsync(page, "Privacy-safe anomaly reporting");
        await ExpectVisibleTextAsync(page, "Security Or Integrity Concern");
        await ExpectVisibleTextAsync(page, "Suppressed");

        Console.WriteLine("[E2E HushVoting FEAT-129] Public anomaly summary is visible");
    }

    [Then(@"the FEAT-129 anomaly readiness strip should show review blockers")]
    public async Task ThenTheFeat129AnomalyReadinessStripShouldShowReviewBlockers()
    {
        var page = await GetOrCreatePageAsync();
        var readinessStrip = page.GetByTestId("anomaly-report-readiness-strip");

        await Assertions.Expect(readinessStrip).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        await Assertions.Expect(readinessStrip).ToContainTextAsync(
            "Readiness blockers",
            new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        await Assertions.Expect(readinessStrip).ToContainTextAsync(
            "Payload Missing",
            new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        await Assertions.Expect(page.GetByTestId("anomaly-retention-status")).ToContainTextAsync(
            "Open anomaly cases require policy review.",
            new LocatorAssertionsToContainTextOptions { Timeout = 15000 });

        Console.WriteLine("[E2E HushVoting FEAT-129] Anomaly readiness blockers are visible");
    }

    [Then(@"the FEAT-129 restricted anomaly artifact row should be visible")]
    public async Task ThenTheFeat129RestrictedAnomalyArtifactRowShouldBeVisible()
    {
        var page = await GetOrCreatePageAsync();
        var restrictedRow = page.GetByTestId("restricted-anomaly-artifact-row");

        await Assertions.Expect(restrictedRow).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        await Assertions.Expect(restrictedRow).ToContainTextAsync(
            "Restricted anomaly intake manifest",
            new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        await Assertions.Expect(restrictedRow).ToContainTextAsync(
            "restricted-anomaly-intake-manifest.json",
            new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        await Assertions.Expect(page.GetByTestId("restricted-anomaly-artifact-download")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        Console.WriteLine("[E2E HushVoting FEAT-129] Restricted anomaly artifact row is visible");
    }

    [Then(@"the FEAT-129 report package UI should not leak restricted anomaly payload references")]
    public async Task ThenTheFeat129ReportPackageUiShouldNotLeakRestrictedAnomalyPayloadReferences()
    {
        var page = await GetOrCreatePageAsync();
        var bodyText = await page.Locator("body").InnerTextAsync();

        bodyText.Should().NotContain(RestrictedPayloadReference);
        bodyText.Should().NotContain(RestrictedSubmitterReference);
        bodyText.Should().NotContain("encryptedPayloadReference");
        bodyText.Should().NotContain("submitterActorPublicAddress");

        Console.WriteLine("[E2E HushVoting FEAT-129] Restricted anomaly payload details are not visible");
    }

    private static async Task EnsureArtifactsExpandedAsync(IPage page)
    {
        var summary = page.GetByTestId("report-package-summary");
        if (await summary.CountAsync() > 0 && await summary.First.IsVisibleAsync())
        {
            return;
        }

        var toggle = page.GetByTestId("hush-voting-artifacts-toggle");
        await Assertions.Expect(toggle).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });

        var expanded = await toggle.GetAttributeAsync("aria-expanded");
        if (!string.Equals(expanded, "true", StringComparison.OrdinalIgnoreCase))
        {
            await toggle.ClickAsync();
        }
    }

    private static async Task ExpectVisibleTextAsync(IPage page, string text)
    {
        await Assertions.Expect(page.GetByText(text, new PageGetByTextOptions { Exact = false }).First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
    }

    private static (int Status, object Body) BuildElectionQueryResponse(
        string method,
        string actorPublicAddress)
    {
        return method switch
        {
            "GetElectionHubView" => (200, BuildElectionHubView(actorPublicAddress)),
            "GetElection" => (200, BuildGetElectionResponse(actorPublicAddress)),
            "GetElectionReportAccessGrants" => (200, BuildReportAccessGrants(actorPublicAddress)),
            "GetElectionResultView" => (200, BuildElectionResultView(actorPublicAddress)),
            _ => (500, new
            {
                Success = false,
                ErrorMessage = $"Unexpected FEAT-129 E2E election query method: {method}",
            }),
        };
    }

    private static string GetMethod(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("method", out var method) &&
            method.ValueKind == JsonValueKind.String)
        {
            return method.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string GetActorPublicAddress(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("request", out var request) &&
            request.ValueKind == JsonValueKind.Object &&
            request.TryGetProperty("ActorPublicAddress", out var actor) &&
            actor.ValueKind == JsonValueKind.String)
        {
            return actor.GetString() ?? "actor-public-key";
        }

        return "actor-public-key";
    }

    private static object BuildElectionHubView(string actorPublicAddress) => new
    {
        Success = true,
        ErrorMessage = string.Empty,
        ActorPublicAddress = actorPublicAddress,
        Elections = new[]
        {
            BuildHubEntry(),
        },
        HasAnyElectionRoles = true,
        EmptyStateReason = string.Empty,
    };

    private static object BuildGetElectionResponse(string actorPublicAddress) => new
    {
        Success = true,
        ErrorMessage = string.Empty,
        Election = BuildElectionRecord(actorPublicAddress),
        WarningAcknowledgements = Array.Empty<object>(),
        TrusteeInvitations = Array.Empty<object>(),
        BoundaryArtifacts = Array.Empty<object>(),
        GovernedProposals = Array.Empty<object>(),
        GovernedProposalApprovals = Array.Empty<object>(),
        CeremonyProfiles = Array.Empty<object>(),
        CeremonyVersions = Array.Empty<object>(),
        CeremonyTranscriptEvents = Array.Empty<object>(),
        ActiveCeremonyTrusteeStates = Array.Empty<object>(),
        FinalizationSessions = Array.Empty<object>(),
        FinalizationShares = Array.Empty<object>(),
        FinalizationReleaseEvidenceRecords = Array.Empty<object>(),
        ResultArtifacts = new[]
        {
            BuildOfficialResult(actorPublicAddress),
        },
    };

    private static object BuildReportAccessGrants(string actorPublicAddress) => new
    {
        Success = true,
        ErrorMessage = string.Empty,
        ActorPublicAddress = actorPublicAddress,
        CanManageGrants = false,
        DeniedReason = string.Empty,
        Grants = Array.Empty<object>(),
    };

    private static object BuildElectionResultView(string actorPublicAddress) => new
    {
        Success = true,
        ErrorMessage = string.Empty,
        ActorPublicAddress = actorPublicAddress,
        CanViewParticipantEncryptedResults = false,
        OfficialResultVisibilityPolicy = 1,
        ClosedProgressStatus = 0,
        OfficialResult = BuildOfficialResult(actorPublicAddress),
        CanViewReportPackage = true,
        CanRetryFailedPackageFinalization = false,
        LatestReportPackage = BuildReportPackage(actorPublicAddress),
        VisibleReportArtifacts = new[]
        {
            BuildHumanManifestArtifact(),
            BuildRestrictedAnomalyArtifact(),
        },
        PublicAnomalySummary = BuildPublicAnomalySummary(),
        AnomalyReportReadiness = BuildAnomalyReportReadiness(),
    };

    private static object BuildHubEntry() => new
    {
        Election = BuildElectionSummary(),
        ActorRoles = new
        {
            IsOwnerAdmin = false,
            IsTrustee = false,
            IsVoter = false,
            IsDesignatedAuditor = false,
        },
        SuggestedAction = 10,
        SuggestedActionReason = "Review the sealed report package.",
        CanClaimIdentity = false,
        CanViewNamedParticipationRoster = false,
        CanViewReportPackage = true,
        CanViewParticipantResults = true,
        ClosedProgressStatus = 0,
        HasUnofficialResult = true,
        HasOfficialResult = true,
    };

    private static object BuildElectionSummary() => new
    {
        ElectionId,
        Title = ElectionTitle,
        OwnerPublicAddress = "owner-public-key-feat-129",
        LifecycleState = 3,
        BindingStatus = 0,
        GovernanceMode = 0,
        CurrentDraftRevision = 2,
        LastUpdatedAt = Timestamp(),
    };

    private static object BuildElectionRecord(string actorPublicAddress) => new
    {
        ElectionId,
        Title = ElectionTitle,
        ShortDescription = "Sealed result report package with privacy-safe anomaly reporting.",
        OwnerPublicAddress = actorPublicAddress,
        ExternalReferenceCode = "FEAT-129-E2E",
        LifecycleState = 3,
        ElectionClass = 0,
        BindingStatus = 0,
        SelectedProfileId = "admin-prod-1of1",
        SelectedProfileDevOnly = false,
        GovernanceMode = 0,
        DisclosureMode = 0,
        ParticipationPrivacyMode = 0,
        VoteUpdatePolicy = 0,
        EligibilitySourceType = 0,
        EligibilityMutationPolicy = 0,
        OutcomeRule = new
        {
            Kind = 0,
            TemplateKey = "single_winner",
            SeatCount = 1,
            BlankVoteCountsForTurnout = true,
            BlankVoteExcludedFromWinnerSelection = true,
            BlankVoteExcludedFromThresholdDenominator = false,
            TieResolutionRule = "tie_unresolved",
            CalculationBasis = "highest_non_blank_votes",
        },
        ApprovedClientApplications = new[]
        {
            new { ApplicationId = "hushsocial", Version = "1.0.0" },
        },
        ProtocolOmegaVersion = "omega-v1.0.0",
        ReportingPolicy = 0,
        ReviewWindowPolicy = 0,
        CurrentDraftRevision = 2,
        Options = new[]
        {
            new
            {
                OptionId = "candidate-alice",
                DisplayLabel = "Alice",
                ShortDescription = "Board candidate",
                BallotOrder = 1,
                IsBlankOption = false,
            },
            new
            {
                OptionId = "candidate-bob",
                DisplayLabel = "Bob",
                ShortDescription = "Board candidate",
                BallotOrder = 2,
                IsBlankOption = false,
            },
        },
        AcknowledgedWarningCodes = Array.Empty<int>(),
        RequiredApprovalCount = 0,
        CreatedAt = Timestamp(),
        LastUpdatedAt = Timestamp(),
        OpenedAt = Timestamp(),
        ClosedAt = Timestamp(),
        FinalizedAt = Timestamp(),
        OpenArtifactId = "open-artifact-feat-129",
        CloseArtifactId = "close-artifact-feat-129",
        FinalizeArtifactId = "finalize-artifact-feat-129",
        TallyReadyAt = Timestamp(),
        VoteAcceptanceLockedAt = Timestamp(),
        TallyReadyArtifactId = "tally-ready-feat-129",
        OfficialResultVisibilityPolicy = 1,
        ClosedProgressStatus = 0,
        UnofficialResultArtifactId = "unofficial-result-feat-129",
        OfficialResultArtifactId = "official-result-feat-129",
    };

    private static object BuildOfficialResult(string actorPublicAddress) => new
    {
        Id = "official-result-feat-129",
        ElectionId,
        ArtifactKind = 1,
        Visibility = 1,
        Title = "Official FEAT-129 result",
        NamedOptionResults = new[]
        {
            new
            {
                OptionId = "candidate-alice",
                DisplayLabel = "Alice",
                ShortDescription = "Board candidate",
                BallotOrder = 1,
                Rank = 1,
                VoteCount = 12,
            },
            new
            {
                OptionId = "candidate-bob",
                DisplayLabel = "Bob",
                ShortDescription = "Board candidate",
                BallotOrder = 2,
                Rank = 2,
                VoteCount = 5,
            },
        },
        BlankCount = 1,
        TotalVotedCount = 18,
        EligibleToVoteCount = 24,
        DidNotVoteCount = 6,
        DenominatorEvidence = new
        {
            SnapshotType = 1,
            EligibilitySnapshotId = "eligibility-close-feat-129",
            BoundaryArtifactId = "close-artifact-feat-129",
            ActiveDenominatorSetHash = "active-denominator-feat-129",
        },
        TallyReadyArtifactId = "tally-ready-feat-129",
        SourceResultArtifactId = "unofficial-result-feat-129",
        EncryptedPayload = string.Empty,
        PublicPayload = "{\"winner\":\"Alice\"}",
        RecordedAt = Timestamp(),
        RecordedByPublicAddress = actorPublicAddress,
    };

    private static object BuildReportPackage(string actorPublicAddress) => new
    {
        Id = ReportPackageId,
        Status = 1,
        AttemptNumber = 2,
        PreviousAttemptId = "report-package-feat-129-attempt-1",
        FinalizationSessionId = "finalization-session-feat-129",
        TallyReadyArtifactId = "tally-ready-feat-129",
        UnofficialResultArtifactId = "unofficial-result-feat-129",
        OfficialResultArtifactId = "official-result-feat-129",
        FinalizeArtifactId = "finalize-artifact-feat-129",
        CloseBoundaryArtifactId = "close-artifact-feat-129",
        CloseEligibilitySnapshotId = "eligibility-close-feat-129",
        FinalizationReleaseEvidenceId = "release-evidence-feat-129",
        FrozenEvidenceHash = new[] { 0xaa, 0xbb, 0xcc, 0xdd },
        FrozenEvidenceFingerprint = "close=close-artifact-feat-129|tally=tally-ready-feat-129",
        PackageHash = new[] { 0x10, 0x20, 0x30, 0x40 },
        ArtifactCount = 14,
        FailureCode = string.Empty,
        FailureReason = string.Empty,
        AttemptedAt = Timestamp(),
        SealedAt = Timestamp(),
        HasSealedAt = true,
        AttemptedByPublicAddress = actorPublicAddress,
    };

    private static object BuildHumanManifestArtifact() => new
    {
        Id = "human-manifest-feat-129",
        ReportPackageId,
        ElectionId,
        ArtifactKind = 0,
        Format = 0,
        AccessScope = 1,
        SortOrder = 1,
        Title = "Final manifest",
        FileName = "final-manifest.md",
        MediaType = "text/markdown;charset=utf-8",
        ContentHash = new[] { 0x01, 0x02, 0x03, 0x04 },
        Content = "# Final manifest",
        PairedArtifactId = string.Empty,
        RecordedAt = Timestamp(),
    };

    private static object BuildRestrictedAnomalyArtifact() => new
    {
        Id = RestrictedManifestArtifactId,
        ReportPackageId,
        ElectionId,
        ArtifactKind = 13,
        Format = 1,
        AccessScope = 0,
        SortOrder = 14,
        Title = "Restricted anomaly intake manifest",
        FileName = "restricted-anomaly-intake-manifest.json",
        MediaType = "application/json",
        ContentHash = new[] { 0x99, 0x88, 0x77, 0x66 },
        Content = JsonSerializer.Serialize(new
        {
            artifactSchemaId = "restricted-anomaly-intake-manifest-artifact-v1",
            encryptedPayloadReference = RestrictedPayloadReference,
            submitterActorPublicAddress = RestrictedSubmitterReference,
        }, JsonOptions),
        PairedArtifactId = string.Empty,
        RecordedAt = Timestamp(),
    };

    private static object BuildPublicAnomalySummary() => new
    {
        SchemaId = "public-anomaly-summary-v1",
        SuppressionPolicyId = "anomaly-public-summary-v1",
        ElectionId,
        SourceManifestHash = RestrictedManifestHash,
        HasSourceManifestHash = true,
        TotalThreadCount = 0,
        HasTotalThreadCount = false,
        TotalThreadCountMode = "suppressed",
        VisibleBuckets = new[]
        {
            new
            {
                CategoryId = "security_or_integrity_concern",
                CountMode = "suppressed",
                PublicCount = 0,
                HasPublicCount = false,
                SuppressionReasonIds = new[] { "restricted_evidence_only" },
                SourceCategoryIds = new[] { "security_or_integrity_concern" },
            },
        },
        AggregatedBucketCount = 0,
        SuppressedThreadCount = 2,
        SuppressionReasonIds = new[] { "restricted_evidence_only" },
        RestrictedManifestArtifactId,
        HasRestrictedManifestArtifactId = true,
        RestrictedManifestHash,
        HasRestrictedManifestHash = true,
        GeneratedAt = Timestamp(),
    };

    private static object BuildAnomalyReportReadiness() => new
    {
        PublicSummarySchemaId = "public-anomaly-summary-v1",
        SuppressionPolicyId = "anomaly-public-summary-v1",
        ForbiddenFieldScanStatusId = "passed",
        RestrictedManifestArtifactId,
        HasRestrictedManifestArtifactId = true,
        RestrictedManifestHash,
        HasRestrictedManifestHash = true,
        PackageReadinessStatusId = "blocked",
        PackageReadinessBlockerIds = new[] { "payload_missing" },
        OpenCaseCount = 1,
        EscalatedCaseCount = 0,
        RetentionEvidenceStatusId = "open_case_requires_policy_review",
        RetentionEvidenceStatus = new
        {
            StatusId = "open_case_requires_policy_review",
            GovernedDecisionRefs = Array.Empty<string>(),
            RedactionHoldReferenceCount = 0,
            OpenCaseCount = 1,
            EscalatedCaseCount = 0,
            ReadinessBlocksValidationClaims = true,
            Message = "Open anomaly cases require policy review.",
        },
        HasGovernedLifecycleEvidence = false,
        ReportGenerationReadOnlyStatusId = "validated",
    };

    private static object Timestamp() => new
    {
        seconds = 1_774_120_000,
        nanos = 0,
    };
}
