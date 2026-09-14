using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007 AC-007-069 / FEAT-008 AC-008-077 /
// FEAT-009 AC-009-082 -> each Phase 7 Tasks 7.1/7.2.
// Only these artifact criteria use these bindings; similarly worded catalogue
// placeholders for other requirements remain unresolved.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class JourneyArtifactSteps(ArtifactPrivacySteps artifacts,
    AuthenticationSteps authentication, IdentitySubmissionSteps creation, RecoveryProfileSteps recovery)
{
    [Given("a scenario that displays recovery words or accepts a password")]
    [Given("Alice arms artifact checks before entering recovery words")]
    [Given("Alice arms artifact checks before selecting her credential source")]
    public Task ArmAsync() => artifacts.ArmAsync();

    [When("evidence is collected")]
    public async Task CreateAsync()
    {
        await creation.ReviewAsync();
        await creation.SubmitAsync();
        await creation.ConfirmAsync();
    }

    [When("Alice completes actual recovery and device protection against the node")]
    public async Task RecoverAsync()
    {
        await recovery.RegisteredAsync();
        await recovery.RestoreAsync();
        await recovery.ConfirmAsync();
        await recovery.ProtectAsync();
    }

    [When("Alice completes actual file import and separate device protection against the node")]
    public Task ImportAsync() => artifacts.ExecuteAsync();

    [Then("trace, screenshot, and video capture are disabled before exposure")]
    public void CaptureDisabled() => authentication.CaptureDisabled();

    [Then("artifact scanning finds no mnemonic-like sequences, private keys, passwords, or full transactions")]
    [Then("the recovery journey has disabled captures and clean completed artifact evidence")]
    [Then("the file journey has disabled captures and clean completed artifact evidence")]
    public Task ScanAsync() => artifacts.ScanAsync();
}
