// EPIC-001 -> FEAT-009 AC-009-041 -> Phase 3 Tasks 3.5/3.6,
// Phase 5 Tasks 5.5/5.6, Phase 6 Tasks 6.1/6.2, Phase 7 Tasks 7.1/7.2.
using FluentAssertions;
using HushNode.Identity.Storage;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Olimpo.KeyDerivation;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialHistoricalAliasSteps(HushVotingScenario scenario, CredentialFileSteps file, AuthenticationSteps authentication)
{
    private const string Historical = "<img src=x>\u202eOld\u0007 name";
    private const string Displayed = "<img src=x>\ufffdOld\ufffd name";
    private const string Password = "historical-backup-password";
    private DerivedKeys _keys = null!;
    private byte[] _backup = [];
    private IPage Page => scenario.Page;

    [Given("an indexed credential identity has historical markup and unsafe controls in its authoritative alias")]
    public async Task ArrangeAsync()
    {
        var words = MnemonicGenerator.GenerateMnemonic();
        _keys = HushVotingTestIdentity.DeriveP01(words);
        await HushVotingArtifactClient.RegisterAsync(words, _keys.SigningPrivateKey, _keys.EncryptPrivateKey,
            _keys.SigningPublicKey, _keys.EncryptPublicKey, Historical, Displayed, Password);
        await HushVotingServerIdentity.RegisterAsync(scenario, _keys, "Current valid profile");
        // A controlled historical database fixture: current registration correctly
        // rejects these controls. The restore still uses the real node lookup.
        await SetHistoricalAliasAsync(Historical);
        _backup = HushVotingCredentialFile.Create(_keys, "Old backup profile", Password);
        await HushVotingArtifactClient.RegisterAsync(Convert.ToBase64String(_backup));
        await Page.GotoAsync("/");
        await ImportAsync();
    }

    [When("credential restoration reaches the real entitlement gate and workspace with the historical profile")]
    public async Task RestoreAsync()
    {
        await Expect(Page.GetByTestId("restore-device-password")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password"), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password-confirmation"), HushVotingScenario.DevicePassword);
        using var baseline = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        await Page.GetByTestId("submit-protection").ClickAsync();
        await Expect(Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await SafeDisplayAsync(Page.GetByTestId("entitlement-gate"));
        await baseline.WaitAsync();
        await scenario.Blocks.ProduceBlockAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await SafeDisplayAsync(Page.GetByTestId("authenticated-shell"));
        (await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(Page, _keys, Historical, false)).Should().BeTrue();
        var profile = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = _keys.SigningPublicKey }, deadline: DateTime.UtcNow.AddSeconds(10));
        (profile.Successfull && profile.ProfileName == Historical && profile.PublicEncryptAddress == _keys.EncryptPublicKey).Should().BeTrue();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        scenario.Faults.RequestMethods.Should().NotContain(method => method.Contains("UpdateIdentity", StringComparison.Ordinal));
    }

    [Then("only displayed controls are replaced while a gross oversized profile cannot enable another import")]
    public async Task OversizedAsync()
    {
        await Page.Locator(".authenticated-user-trigger").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Lock", Exact = true }).ClickAsync();
        await SafeDisplayAsync(Page.Locator("[aria-label='Local identity']"));
        await authentication.RemoveAsync();
        await authentication.StorageRemovedAsync();
        // A gross 64 KiB response probe, not a newly chosen compatibility limit.
        await SetHistoricalAliasAsync(new string('x', 65_536));
        var before = scenario.Faults.IdentityQueryCount;
        await ImportAsync();
        await Expect(Page.GetByTestId("restore-panel").GetByRole(AriaRole.Alert)).ToBeVisibleAsync(new() { Timeout = 10_000 });
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(before);
        await Expect(Page.GetByTestId("restore-device-password")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("create-identity")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
    }

    private async Task ImportAsync()
    {
        await file.OpenAsync();
        await file.ChooseAsync(_backup);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("backup-password-input"), Password);
        await Page.GetByTestId("submit-password").ClickAsync();
    }

    private static async Task SafeDisplayAsync(ILocator surface)
    {
        var alias = surface.GetByTestId("safe-alias");
        await Expect(alias).ToHaveTextAsync(Displayed);
        (await alias.EvaluateAsync<string>("node => node.tagName")).Should().Be("BDI");
        await Expect(alias.Locator("img, script, a")).ToHaveCountAsync(0);
        await Expect(surface.GetByTestId("historical-alias-notice")).ToBeVisibleAsync();
    }

    private async Task SetHistoricalAliasAsync(string alias)
    {
        using var scope = scenario.Node.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await db.Profiles.Where(profile => profile.PublicSigningAddress == _keys.SigningPublicKey)
            .ExecuteUpdateAsync(setters => setters.SetProperty(profile => profile.Alias, alias))).Should().Be(1);
        await scenario.ClearCacheAsync();
    }
}
