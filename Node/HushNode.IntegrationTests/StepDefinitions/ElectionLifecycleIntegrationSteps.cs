using FluentAssertions;
using Google.Protobuf;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode.Testing;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

[Binding]
[Scope(Feature = "FEAT-094 election lifecycle integration")]
public sealed class ElectionLifecycleIntegrationSteps
{
    private readonly ScenarioContext _scenarioContext;
    private readonly List<ElectionLifecycleStateProto> _observedStates = [];

    private HushElections.HushElectionsClient? _client;
    private TestIdentity? _owner;
    private string? _electionId;
    private ElectionCommandResponse? _lastCommandResponse;
    private GetElectionOpenReadinessResponse? _lastReadinessResponse;
    private GetElectionResponse? _lastElectionResponse;

    public ElectionLifecycleIntegrationSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"FEAT-094 election integration services are available")]
    public void GivenFeatElectionIntegrationServicesAreAvailable()
    {
        _client = GetGrpcFactory().CreateClient<HushElections.HushElectionsClient>();
        _owner = TestIdentities.Alice;
        _observedStates.Clear();
        _electionId = null;
        _lastCommandResponse = null;
        _lastReadinessResponse = null;
        _lastElectionResponse = null;
    }

    [When(@"the owner creates an admin-only election draft through gRPC")]
    public async Task WhenTheOwnerCreatesAnAdminOnlyElectionDraftThroughGrpc()
    {
        var response = await GetClient().CreateElectionDraftAsync(new CreateElectionDraftRequest
        {
            OwnerPublicAddress = GetOwner().PublicSigningAddress,
            ActorPublicAddress = GetOwner().PublicSigningAddress,
            SnapshotReason = "initial draft",
            Draft = BuildDraftInput("Board Election"),
        });

        response.Success.Should().BeTrue($"draft creation should succeed: {response.ErrorMessage}");
        _lastCommandResponse = response;
        _electionId = response.Election.ElectionId;
        RecordState(response.Election.LifecycleState);
    }

    [When(@"the owner updates the election draft title to ""(.*)""")]
    public async Task WhenTheOwnerUpdatesTheElectionDraftTitleTo(string title)
    {
        var response = await GetClient().UpdateElectionDraftAsync(new UpdateElectionDraftRequest
        {
            ElectionId = GetElectionId(),
            ActorPublicAddress = GetOwner().PublicSigningAddress,
            SnapshotReason = "owner draft update",
            Draft = BuildDraftInput(title),
        });

        response.Success.Should().BeTrue($"draft update should succeed: {response.ErrorMessage}");
        response.Election.Title.Should().Be(title);
        _lastCommandResponse = response;
        RecordState(response.Election.LifecycleState);
    }

    [When(@"the owner checks open readiness for the election")]
    public async Task WhenTheOwnerChecksOpenReadinessForTheElection()
    {
        var request = new GetElectionOpenReadinessRequest
        {
            ElectionId = GetElectionId(),
        };
        request.RequiredWarningCodes.Add(ElectionWarningCodeProto.LowAnonymitySet);

        var response = await GetClient().GetElectionOpenReadinessAsync(request);
        response.IsReadyToOpen.Should().BeTrue(
            $"open readiness should pass when required warnings are acknowledged. Errors: {string.Join("; ", response.ValidationErrors)}");

        _lastReadinessResponse = response;
    }

    [When(@"the owner opens the election through gRPC")]
    public async Task WhenTheOwnerOpensTheElectionThroughGrpc()
    {
        var request = new OpenElectionRequest
        {
            ElectionId = GetElectionId(),
            ActorPublicAddress = GetOwner().PublicSigningAddress,
            FrozenEligibleVoterSetHash = ByteString.CopyFromUtf8("frozen-eligible-voters"),
            TrusteePolicyExecutionReference = string.Empty,
            ReportingPolicyExecutionReference = "phase-one-reporting-package",
            ReviewWindowExecutionReference = string.Empty,
        };
        request.RequiredWarningCodes.Add(ElectionWarningCodeProto.LowAnonymitySet);

        var response = await GetClient().OpenElectionAsync(request);
        response.Success.Should().BeTrue($"open should succeed: {response.ErrorMessage}");
        response.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Open);

        _lastCommandResponse = response;
        RecordState(response.Election.LifecycleState);
    }

    [When(@"the owner reloads the election through gRPC")]
    public async Task WhenTheOwnerReloadsTheElectionThroughGrpc()
    {
        _lastElectionResponse = await ReloadElectionAsync();
        RecordState(_lastElectionResponse.Election.LifecycleState);
    }

    [When(@"the owner closes the election through gRPC")]
    public async Task WhenTheOwnerClosesTheElectionThroughGrpc()
    {
        var response = await GetClient().CloseElectionAsync(new CloseElectionRequest
        {
            ElectionId = GetElectionId(),
            ActorPublicAddress = GetOwner().PublicSigningAddress,
            AcceptedBallotSetHash = ByteString.CopyFromUtf8("accepted-ballot-set"),
            FinalEncryptedTallyHash = ByteString.CopyFromUtf8("final-encrypted-tally"),
        });

        response.Success.Should().BeTrue($"close should succeed: {response.ErrorMessage}");
        response.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);

        _lastCommandResponse = response;
        RecordState(response.Election.LifecycleState);
    }

    [When(@"the owner finalizes the election through gRPC")]
    public async Task WhenTheOwnerFinalizesTheElectionThroughGrpc()
    {
        var response = await GetClient().FinalizeElectionAsync(new FinalizeElectionRequest
        {
            ElectionId = GetElectionId(),
            ActorPublicAddress = GetOwner().PublicSigningAddress,
            AcceptedBallotSetHash = ByteString.CopyFromUtf8("accepted-ballot-set"),
            FinalEncryptedTallyHash = ByteString.CopyFromUtf8("final-encrypted-tally"),
        });

        response.Success.Should().BeTrue($"finalize should succeed: {response.ErrorMessage}");
        response.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Finalized);

        _lastCommandResponse = response;
        RecordState(response.Election.LifecycleState);
        _lastElectionResponse = await ReloadElectionAsync();
    }

    [Given(@"the owner has an open admin-only election through gRPC")]
    public async Task GivenTheOwnerHasAnOpenAdminOnlyElectionThroughGrpc()
    {
        await WhenTheOwnerCreatesAnAdminOnlyElectionDraftThroughGrpc();
        await WhenTheOwnerChecksOpenReadinessForTheElection();
        await WhenTheOwnerOpensTheElectionThroughGrpc();
        await WhenTheOwnerReloadsTheElectionThroughGrpc();
    }

    [When(@"the owner attempts to change the binding status after open")]
    public async Task WhenTheOwnerAttemptsToChangeTheBindingStatusAfterOpen()
    {
        var response = await GetClient().UpdateElectionDraftAsync(new UpdateElectionDraftRequest
        {
            ElectionId = GetElectionId(),
            ActorPublicAddress = GetOwner().PublicSigningAddress,
            SnapshotReason = "post-open immutable mutation",
            Draft = BuildDraftInput("Board Election", ElectionBindingStatusProto.NonBinding),
        });

        _lastCommandResponse = response;
    }

    [Then(@"the election lifecycle should progress through ""(.*)""")]
    public void ThenTheElectionLifecycleShouldProgressThrough(string expectedStates)
    {
        var actualStates = _observedStates.Distinct().Select(ToFriendlyState).ToArray();
        var expected = expectedStates
            .Replace("\", and \"", "\", \"", StringComparison.Ordinal)
            .Split("\", \"", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(state => state.Trim('"').Trim())
            .ToArray();

        actualStates.Should().Equal(expected);
    }

    [Then(@"the owner dashboard should include the election")]
    public async Task ThenTheOwnerDashboardShouldIncludeTheElection()
    {
        var response = await GetClient().GetElectionsByOwnerAsync(new GetElectionsByOwnerRequest
        {
            OwnerPublicAddress = GetOwner().PublicSigningAddress,
        });

        response.Elections.Should().Contain(summary =>
            summary.ElectionId == GetElectionId() &&
            summary.LifecycleState == ElectionLifecycleStateProto.Finalized);
    }

    [Then(@"the frozen policy and warning acknowledgement should remain visible after reload")]
    public async Task ThenTheFrozenPolicyAndWarningAcknowledgementShouldRemainVisibleAfterReload()
    {
        var response = _lastElectionResponse ?? await ReloadElectionAsync();

        response.Success.Should().BeTrue();
        response.Election.ElectionId.Should().Be(GetElectionId());
        response.Election.DisclosureMode.Should().Be(ElectionDisclosureModeProto.FinalResultsOnly);
        response.Election.BindingStatus.Should().Be(ElectionBindingStatusProto.Binding);
        response.WarningAcknowledgements.Should().Contain(ack =>
            ack.WarningCode == ElectionWarningCodeProto.LowAnonymitySet);
        response.BoundaryArtifacts.Should().Contain(artifact =>
            artifact.ArtifactType == ElectionBoundaryArtifactTypeProto.OpenArtifact &&
            artifact.Policy.DisclosureMode == ElectionDisclosureModeProto.FinalResultsOnly &&
            artifact.Policy.BindingStatus == ElectionBindingStatusProto.Binding);

        _lastElectionResponse = response;
    }

    [Then(@"the boundary artifacts should include open, close, and finalize records")]
    public async Task ThenTheBoundaryArtifactsShouldIncludeOpenCloseAndFinalizeRecords()
    {
        var response = _lastElectionResponse ?? await ReloadElectionAsync();

        response.BoundaryArtifacts.Select(artifact => artifact.ArtifactType).Should().Contain(
        [
            ElectionBoundaryArtifactTypeProto.OpenArtifact,
            ElectionBoundaryArtifactTypeProto.CloseArtifact,
            ElectionBoundaryArtifactTypeProto.FinalizeArtifact,
        ]);
    }

    [Then(@"the immutable update should be rejected through gRPC")]
    public void ThenTheImmutableUpdateShouldBeRejectedThroughGrpc()
    {
        _lastCommandResponse.Should().NotBeNull();
        _lastCommandResponse!.Success.Should().BeFalse();
        _lastCommandResponse.ErrorCode.Should().Be(ElectionCommandErrorCodeProto.InvalidState);
        _lastCommandResponse.ErrorMessage.Should().Contain("Immutable FEAT-094 policy cannot be edited after the election opens.");
    }

    [Then(@"the open-time binding status should remain ""(.*)""")]
    public async Task ThenTheOpenTimeBindingStatusShouldRemain(string expectedBindingStatus)
    {
        var expected = expectedBindingStatus switch
        {
            "Binding" => ElectionBindingStatusProto.Binding,
            "NonBinding" => ElectionBindingStatusProto.NonBinding,
            _ => throw new ArgumentOutOfRangeException(nameof(expectedBindingStatus), expectedBindingStatus, "Unsupported binding status."),
        };

        var response = await ReloadElectionAsync();

        response.Election.BindingStatus.Should().Be(expected);
        response.BoundaryArtifacts.Should().Contain(artifact =>
            artifact.ArtifactType == ElectionBoundaryArtifactTypeProto.OpenArtifact &&
            artifact.Policy.BindingStatus == expected);

        _lastElectionResponse = response;
    }

    private GrpcClientFactory GetGrpcFactory() =>
        _scenarioContext.Get<GrpcClientFactory>(ScenarioHooks.GrpcFactoryKey);

    private HushElections.HushElectionsClient GetClient() =>
        _client ?? throw new InvalidOperationException("Election gRPC client not initialized. Call the availability step first.");

    private TestIdentity GetOwner() =>
        _owner ?? throw new InvalidOperationException("Owner identity not initialized. Call the availability step first.");

    private string GetElectionId() =>
        _electionId ?? throw new InvalidOperationException("Election ID not initialized.");

    private async Task<GetElectionResponse> ReloadElectionAsync()
    {
        var response = await GetClient().GetElectionAsync(new GetElectionRequest
        {
            ElectionId = GetElectionId(),
        });

        response.Success.Should().BeTrue($"GetElection should succeed for {GetElectionId()}: {response.ErrorMessage}");
        return response;
    }

    private void RecordState(ElectionLifecycleStateProto lifecycleState)
    {
        _observedStates.Add(lifecycleState);
    }

    private static string ToFriendlyState(ElectionLifecycleStateProto lifecycleState) =>
        lifecycleState switch
        {
            ElectionLifecycleStateProto.Draft => "Draft",
            ElectionLifecycleStateProto.Open => "Open",
            ElectionLifecycleStateProto.Closed => "Closed",
            ElectionLifecycleStateProto.Finalized => "Finalized",
            _ => lifecycleState.ToString(),
        };

    private static ElectionDraftInput BuildDraftInput(
        string title,
        ElectionBindingStatusProto bindingStatus = ElectionBindingStatusProto.Binding)
    {
        var draft = new ElectionDraftInput
        {
            Title = title,
            ShortDescription = "Annual board vote",
            ExternalReferenceCode = "ORG-2026-01",
            ElectionClass = ElectionClassProto.OrganizationalRemoteVoting,
            BindingStatus = bindingStatus,
            GovernanceMode = ElectionGovernanceModeProto.AdminOnly,
            DisclosureMode = ElectionDisclosureModeProto.FinalResultsOnly,
            ParticipationPrivacyMode = ParticipationPrivacyModeProto.PublicCheckoffAnonymousBallotPrivateChoice,
            VoteUpdatePolicy = VoteUpdatePolicyProto.SingleSubmissionOnly,
            EligibilitySourceType = EligibilitySourceTypeProto.OrganizationImportedRoster,
            EligibilityMutationPolicy = EligibilityMutationPolicyProto.FrozenAtOpen,
            OutcomeRule = new OutcomeRule
            {
                Kind = OutcomeRuleKindProto.SingleWinner,
                TemplateKey = "single_winner",
                SeatCount = 1,
                BlankVoteCountsForTurnout = true,
                BlankVoteExcludedFromWinnerSelection = true,
                BlankVoteExcludedFromThresholdDenominator = false,
                TieResolutionRule = "tie_unresolved",
                CalculationBasis = "highest_non_blank_votes",
            },
            ProtocolOmegaVersion = "omega-v1.0.0",
            ReportingPolicy = ReportingPolicyProto.DefaultPhaseOnePackage,
            ReviewWindowPolicy = ReviewWindowPolicyProto.NoReviewWindow,
        };

        draft.ApprovedClientApplications.Add(new ApprovedClientApplication
        {
            ApplicationId = "hushsocial",
            Version = "1.0.0",
        });
        draft.OwnerOptions.Add(new ElectionOption
        {
            OptionId = "option-a",
            DisplayLabel = "Alice",
            ShortDescription = "Candidate Alice",
            BallotOrder = 1,
            IsBlankOption = false,
        });
        draft.OwnerOptions.Add(new ElectionOption
        {
            OptionId = "option-b",
            DisplayLabel = "Bob",
            ShortDescription = "Candidate Bob",
            BallotOrder = 2,
            IsBlankOption = false,
        });
        draft.AcknowledgedWarningCodes.Add(ElectionWarningCodeProto.LowAnonymitySet);

        return draft;
    }
}
