using System.Text.Json;
using FluentAssertions;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Focused browser coverage for FEAT-138 VOID publication replacement surfaces.
/// </summary>
[Binding]
internal sealed class HushVotingVoidDecisionE2ESteps : BrowserStepsBase
{
    private const string ElectionId = "feat-138-e2e-void-election";
    private const string ElectionTitle = "FEAT-138 VOID publication replacement";
    private const string OwnerPublicAddress = "owner-public-key-feat-138";
    private const string ReportPackageId = "void-report-package-feat-138";
    private const string VoidDecisionId = "void-decision-feat-138";
    private const string VoidPublicationAttemptId = "void-publication-attempt-feat-138";
    private const string PublicJustification = "ElectionOwner accepted a dispute and voided the election before final publication.";
    private const string HiddenResultMarker = "obsolete-final-result-claim-feat-138";
    private const string RestrictedEvidenceMarker = "restricted-historical-unofficial-result-feat-138";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    public HushVotingVoidDecisionE2ESteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    [Given(@"the FEAT-138 voided election query responses are seeded for the browser")]
    public async Task GivenTheFeat138VoidedElectionQueryResponsesAreSeededForTheBrowser()
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

        Console.WriteLine("[E2E HushVoting FEAT-138] Browser query responses seeded");
    }

    [When(@"the user opens the FEAT-138 voided election")]
    public async Task WhenTheUserOpensTheFeat138VoidedElection()
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine("[E2E HushVoting FEAT-138] Opening voided election...");
        await NavigateToAsync(page, $"/elections/{ElectionId}");

        await Assertions.Expect(page).ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex($@"/elections/{ElectionId}"),
            new PageAssertionsToHaveURLOptions { Timeout = 15000 });

        await WaitForTestIdAsync(page, "public-void-status-panel", 30000);
        await EnsureArtifactsExpandedAsync(page);

        Console.WriteLine("[E2E HushVoting FEAT-138] Voided election opened");
    }

    [Then(@"the FEAT-138 VOID package status should be visible")]
    public async Task ThenTheFeat138VoidPackageStatusShouldBeVisible()
    {
        var page = await GetOrCreatePageAsync();

        await EnsureArtifactsExpandedAsync(page);
        var publicVoidStatusPanel = page.GetByTestId("public-void-status-panel");
        await Assertions.Expect(publicVoidStatusPanel).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        await Assertions.Expect(publicVoidStatusPanel.GetByTestId("void-publication-status-details")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        await Assertions.Expect(publicVoidStatusPanel.GetByTestId("public-void-justification")).ToContainTextAsync(
            "accepted a dispute",
            new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        await ExpectVisibleTextAsync(page, "This election is VOID");
        await ExpectVisibleTextAsync(page, "VOID publication sealed");
        await ExpectVisibleTextAsync(page, "election_voided");

        Console.WriteLine("[E2E HushVoting FEAT-138] VOID publication status is visible");
    }

    [Then(@"the FEAT-138 VOID report package notice should be visible")]
    public async Task ThenTheFeat138VoidReportPackageNoticeShouldBeVisible()
    {
        var page = await GetOrCreatePageAsync();

        await EnsureArtifactsExpandedAsync(page);
        await Assertions.Expect(page.GetByTestId("void-report-package-notice")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        await Assertions.Expect(page.GetByTestId("void-report-package-notice")).ToContainTextAsync(
            "does not contain a current result claim",
            new LocatorAssertionsToContainTextOptions { Timeout = 15000 });

        Console.WriteLine("[E2E HushVoting FEAT-138] VOID package notice is visible");
    }

    [Then(@"the FEAT-138 voided election screen should not expose current result or restricted evidence")]
    public async Task ThenTheFeat138VoidedElectionScreenShouldNotExposeCurrentResultOrRestrictedEvidence()
    {
        var page = await GetOrCreatePageAsync();
        var bodyText = await page.Locator("body").InnerTextAsync();

        bodyText.Should().NotContain(HiddenResultMarker);
        bodyText.Should().NotContain("Obsolete final result claim");
        bodyText.Should().NotContain("Alice: 17");
        bodyText.Should().NotContain(RestrictedEvidenceMarker);
        bodyText.Should().NotContain("restricted/historical-unofficial-result.json");

        Console.WriteLine("[E2E HushVoting FEAT-138] Current result and restricted VOID evidence markers are not visible");
    }

    private static async Task EnsureArtifactsExpandedAsync(IPage page)
    {
        var panel = page.GetByTestId("public-void-status-panel");
        if (await panel.CountAsync() > 0 && await panel.First.IsVisibleAsync())
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
            "GetElectionVerificationPackageStatus" => (200, BuildVerificationPackageStatusResponse(actorPublicAddress)),
            "GetElectionAnomalyEvidenceManifest" => (200, BuildAnomalyEvidenceManifestResponse(actorPublicAddress)),
            _ => (500, new
            {
                Success = false,
                ErrorMessage = $"Unexpected FEAT-138 E2E election query method: {method}",
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
            BuildHistoricalOfficialResult(actorPublicAddress),
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
        CanViewParticipantEncryptedResults = true,
        OfficialResultVisibilityPolicy = 1,
        ClosedProgressStatus = 0,
        OfficialResult = BuildHistoricalOfficialResult(actorPublicAddress),
        CanViewReportPackage = true,
        CanRetryFailedPackageFinalization = false,
        LatestReportPackage = BuildVoidReportPackage(actorPublicAddress),
        VisibleReportArtifacts = new[]
        {
            BuildVoidSummaryArtifact(),
            BuildVoidPublicStatusArtifact(),
            BuildVoidPackageManifestArtifact(),
        },
        VerificationPackageStatus = BuildVerificationPackageStatus(actorPublicAddress),
    };

    private static object BuildAnomalyEvidenceManifestResponse(string actorPublicAddress) => new
    {
        Success = true,
        ErrorMessage = string.Empty,
        ActorPublicAddress = actorPublicAddress,
        HasManifest = true,
        Manifest = new
        {
            ElectionId,
            ScopeId = "package",
            CanonicalizationId = "anomaly-intake-manifest-v1",
            ManifestHash = "sha256:feat-138-void-empty-anomaly-manifest",
            PackageReadinessStatusId = "ready",
            PackageReadinessBlockerIds = Array.Empty<string>(),
            TotalThreadCount = 0,
            AttachmentManifestCount = 0,
            RedactionCount = 0,
            Threads = Array.Empty<object>(),
        },
    };

    private static object BuildVerificationPackageStatusResponse(string actorPublicAddress) => new
    {
        Success = true,
        ErrorMessage = string.Empty,
        Status = BuildVerificationPackageStatus(actorPublicAddress),
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
        SuggestedActionReason = "Review the public VOID status.",
        CanClaimIdentity = false,
        CanViewNamedParticipationRoster = false,
        CanViewReportPackage = true,
        CanViewParticipantResults = false,
        ClosedProgressStatus = 0,
        HasUnofficialResult = false,
        HasOfficialResult = false,
    };

    private static object BuildElectionSummary() => new
    {
        ElectionId,
        Title = ElectionTitle,
        OwnerPublicAddress,
        LifecycleState = 4,
        BindingStatus = 0,
        GovernanceMode = 0,
        CurrentDraftRevision = 2,
        LastUpdatedAt = Timestamp(),
    };

    private static object BuildElectionRecord(string actorPublicAddress) => new
    {
        ElectionId,
        Title = ElectionTitle,
        ShortDescription = "Voided election with public VOID publication evidence.",
        OwnerPublicAddress = actorPublicAddress,
        ExternalReferenceCode = "FEAT-138-E2E",
        LifecycleState = 4,
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
        FinalizedAt = new { seconds = 0, nanos = 0 },
        OpenArtifactId = "open-artifact-feat-138",
        CloseArtifactId = "close-artifact-feat-138",
        FinalizeArtifactId = string.Empty,
        TallyReadyAt = Timestamp(),
        VoteAcceptanceLockedAt = Timestamp(),
        TallyReadyArtifactId = "tally-ready-feat-138",
        OfficialResultVisibilityPolicy = 1,
        ClosedProgressStatus = 0,
        UnofficialResultArtifactId = "unofficial-result-feat-138",
        OfficialResultArtifactId = string.Empty,
    };

    private static object BuildHistoricalOfficialResult(string actorPublicAddress) => new
    {
        Id = "historical-result-feat-138",
        ElectionId,
        ArtifactKind = 1,
        Visibility = 1,
        Title = "Obsolete final result claim",
        NamedOptionResults = new[]
        {
            new
            {
                OptionId = "candidate-alice",
                DisplayLabel = "Alice",
                ShortDescription = "Board candidate",
                BallotOrder = 1,
                Rank = 1,
                VoteCount = 17,
            },
        },
        BlankCount = 0,
        TotalVotedCount = 17,
        EligibleToVoteCount = 20,
        DidNotVoteCount = 3,
        DenominatorEvidence = new
        {
            SnapshotType = 1,
            EligibilitySnapshotId = "eligibility-close-feat-138",
            BoundaryArtifactId = "close-artifact-feat-138",
            ActiveDenominatorSetHash = "active-denominator-feat-138",
        },
        TallyReadyArtifactId = "tally-ready-feat-138",
        SourceResultArtifactId = "unofficial-result-feat-138",
        EncryptedPayload = string.Empty,
        PublicPayload = HiddenResultMarker,
        RecordedAt = Timestamp(),
        RecordedByPublicAddress = actorPublicAddress,
    };

    private static object BuildVoidReportPackage(string actorPublicAddress) => new
    {
        Id = ReportPackageId,
        Status = 1,
        AttemptNumber = 1,
        PreviousAttemptId = string.Empty,
        FinalizationSessionId = string.Empty,
        TallyReadyArtifactId = string.Empty,
        UnofficialResultArtifactId = string.Empty,
        OfficialResultArtifactId = string.Empty,
        FinalizeArtifactId = string.Empty,
        CloseBoundaryArtifactId = "close-artifact-feat-138",
        CloseEligibilitySnapshotId = "eligibility-close-feat-138",
        FinalizationReleaseEvidenceId = string.Empty,
        FrozenEvidenceHash = new[] { 0xaa, 0xbb, 0xcc, 0xdd },
        FrozenEvidenceFingerprint = "void=void-decision-feat-138|status=VOID",
        PackageHash = new[] { 0x10, 0x20, 0x30, 0x40 },
        ArtifactCount = 9,
        FailureCode = string.Empty,
        FailureReason = string.Empty,
        AttemptedAt = Timestamp(),
        SealedAt = Timestamp(),
        HasSealedAt = true,
        AttemptedByPublicAddress = actorPublicAddress,
        PackageKind = 1,
        VoidDecisionId,
        VoidPublicationAttemptId,
        SupersededByVoidDecisionId = string.Empty,
    };

    private static object BuildVerificationPackageStatus(string actorPublicAddress) => new
    {
        ElectionId,
        ActorPublicAddress = actorPublicAddress,
        IsVisible = true,
        Status = 6,
        StatusMessage = "Election VOID package is sealed.",
        PublicPackage = BuildPackageAvailability(actorPublicAddress, 0),
        RestrictedPackage = BuildPackageAvailability(actorPublicAddress, 1),
        LastVerifierResult = new
        {
            OverallStatus = 1,
            VerifierVersion = "hushvoting-local-verifier-v1",
            PackageHash = "10203040",
            PassedCount = 3,
            WarningCount = 1,
            FailedCount = 0,
            NotApplicableCount = 0,
            Message = "Election is VOID.",
            VerifiedAt = Timestamp(),
            HasVerifiedAt = true,
            ResultCode = "election_voided",
        },
        VoidPublicationStatus = new
        {
            VoidDecisionId,
            PublicationAttemptId = VoidPublicationAttemptId,
            Status = 2,
            AttemptNumber = 1,
            PublicStatusArtifactRef = $"{ReportPackageId}:void-public-status.json",
            VoidPackageArtifactRef = $"{ReportPackageId}:void-package.zip",
            PackageHash = "10203040",
            FailureCode = string.Empty,
            FailureReason = string.Empty,
            AttemptedAt = Timestamp(),
            SealedAt = Timestamp(),
            HasSealedAt = true,
            AttemptedByPublicAddress = actorPublicAddress,
            CanRetry = false,
            PublicJustification,
            PublicJustificationHash = "aabbccdd",
            PreviousLifecycleState = 2,
            ResultingLifecycleState = 4,
            ActorPublicAddress = actorPublicAddress,
            ActorRole = "ElectionOwner",
            SourceTransactionId = "void-source-tx-feat-138",
            SourceBlockHeight = 42,
            SourceBlockId = "void-source-block-feat-138",
            DecidedAt = Timestamp(),
            HasDecidedAt = true,
        },
    };

    private static object BuildPackageAvailability(string actorPublicAddress, int packageView) => new
    {
        PackageView = packageView,
        VerifierProfileId = packageView == 0 ? "public-anonymous-v1" : "restricted-owner-auditor-v1",
        IsAvailable = true,
        Blocker = 0,
        BlockerCode = string.Empty,
        Message = "VOID package available.",
        PackageId = ReportPackageId,
        PackageHash = "10203040",
        CanRetry = false,
        PackageKind = 1,
        VoidDecisionId,
        VoidPublicationAttemptId,
        PublicStatusArtifactRef = $"{ReportPackageId}:void-public-status.json",
        VoidPackageArtifactRef = $"{ReportPackageId}:void-package.zip",
        ActorPublicAddress = actorPublicAddress,
    };

    private static object BuildVoidSummaryArtifact() => new
    {
        Id = "void-summary-feat-138",
        ReportPackageId,
        ElectionId,
        ArtifactKind = 15,
        Format = 0,
        AccessScope = 1,
        SortOrder = 2,
        Title = "Public VOID summary",
        FileName = "public-void-summary.md",
        MediaType = "text/markdown;charset=utf-8",
        ContentHash = new[] { 0x01, 0x02, 0x03, 0x04 },
        Content = "# VOID summary\n\nNo final result is claimed for this election.",
        PairedArtifactId = string.Empty,
        RecordedAt = Timestamp(),
    };

    private static object BuildVoidPublicStatusArtifact() => new
    {
        Id = "void-public-status-feat-138",
        ReportPackageId,
        ElectionId,
        ArtifactKind = 16,
        Format = 1,
        AccessScope = 1,
        SortOrder = 3,
        Title = "Public VOID status",
        FileName = "void-public-status.json",
        MediaType = "application/json",
        ContentHash = new[] { 0x05, 0x06, 0x07, 0x08 },
        Content = JsonSerializer.Serialize(new
        {
            status = "VOID",
            voidDecisionId = VoidDecisionId,
            verifierResultCode = "election_voided",
            resultClaim = "No final result is claimed for this election.",
        }, JsonOptions),
        PairedArtifactId = string.Empty,
        RecordedAt = Timestamp(),
    };

    private static object BuildVoidPackageManifestArtifact() => new
    {
        Id = "void-package-manifest-feat-138",
        ReportPackageId,
        ElectionId,
        ArtifactKind = 21,
        Format = 1,
        AccessScope = 1,
        SortOrder = 8,
        Title = "VOID package manifest",
        FileName = "void-package-manifest.json",
        MediaType = "application/json",
        ContentHash = new[] { 0x09, 0x0a, 0x0b, 0x0c },
        Content = JsonSerializer.Serialize(new
        {
            voidDecisionId = VoidDecisionId,
            restrictedEvidenceRef = RestrictedEvidenceMarker,
        }, JsonOptions),
        PairedArtifactId = string.Empty,
        RecordedAt = Timestamp(),
    };

    private static object Timestamp() => new
    {
        seconds = 1_774_120_000,
        nanos = 0,
    };
}
