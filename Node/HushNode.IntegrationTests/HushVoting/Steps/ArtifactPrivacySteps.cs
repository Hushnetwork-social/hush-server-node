using HushVoting.IntegrationTests.Infrastructure;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-010 AC-010-089/097 -> Phase 7 Task 7.3;
// FEAT-011 Phase 7 Task 7.M3 owns final runner evidence validation.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class ArtifactPrivacySteps(HushVotingScenario scenario, CredentialFileSteps file, AuthenticationSteps authentication)
{
    [Given("a secret-bearing journey is about to run")]
    public async Task ArmAsync()
    {
        await HushVotingArtifactClient.RequireAsync();
        await HushVotingArtifactClient.RegisterAsync(HushVotingIdentityJourney.Alias, HushVotingScenario.DevicePassword,
            scenario.BaseUrl, $"localhost:{scenario.Node.GrpcPort}");
        authentication.CaptureDisabled();
    }

    [When("the scenario executes")]
    public async Task ExecuteAsync()
    {
        await file.SourceWithWordsAsync();
        await file.DecryptAsync();
        await file.ImportedAsync();
        await file.ProtectAsync();
        await file.SourceUnchangedAsync();
    }

    [Then("artifact scans find no credential, identity, endpoint, or file material")]
    public async Task ScanAsync()
    {
        authentication.NoBrowserArtifacts();
        foreach (var transaction in scenario.Faults.SubmittedTransactions)
            await HushVotingArtifactClient.RegisterAsync(transaction);
        // The supervisor repeats this check against completed TRX/stdout after
        // dotnet exits; a late leak makes the dedicated runner fail.
        await HushVotingArtifactClient.CheckAsync();
    }
}
