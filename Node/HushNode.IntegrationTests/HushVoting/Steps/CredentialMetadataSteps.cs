using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;
using HushNode.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialMetadataSteps(HushVotingScenario scenario, CredentialFileSteps file, HushVotingIdentityJourney identity)
{
    private HushVotingVaultInspection.Facts? _stored;

    [Given("the encrypted backup contains an old alias and Public visibility for Alice's Private blockchain identity")]
    public async Task BackupAsync() => await file.BackupAsync();

    [When("Alice restores the backup and the independently decrypted vault is inspected in test memory")]
    public async Task InspectAsync()
    {
        await file.DecryptAsync();
        await file.ImportedAsync();
        await file.ProtectAsync();
        _stored = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, isPublic: false);
    }

    [When("Alice restores authoritative backup metadata while real Redis rejects cache fills")]
    public async Task InspectWithRejectedCacheWriteAsync()
    {
        await scenario.ClearCacheAsync();
        var cache = (IdentityCacheService)scenario.Node.Services.GetRequiredService<IIdentityCacheService>();
        var errors = cache.WriteErrors;
        await scenario.WithRedisWritesRejectedAsync(async () =>
        {
            await file.DecryptAsync();
            await file.ImportedAsync();
            cache.WriteErrors.Should().BeGreaterThan(errors, "real browser lookup reached PostgreSQL despite the rejected Redis fill");
        });
        await file.ProtectAsync();
        _stored = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, isPublic: false);
    }

    [Then("the encrypted local metadata uses the blockchain alias and visibility without changing its profile")]
    public async Task MetadataAsync()
    {
        _stored.Should().NotBeNull();
        _stored!.MetadataMatches.Should().BeTrue();
        _stored.KeysMatch.Should().BeTrue();
        _stored.ConcreteKeysOnly.Should().BeTrue();
        _stored.Active.Should().BeTrue();
        _stored.NetworkMatches.Should().BeTrue();
        var profile = await scenario.Identities.GetIdentityAsync(new HushNetwork.proto.GetIdentityRequest { PublicSigningAddress = identity.Keys.SigningPublicKey }, deadline: DateTime.UtcNow.AddSeconds(10));
        if (!profile.Successfull || profile.ProfileName != HushVotingIdentityJourney.Alias || profile.IsPublic
            || profile.PublicSigningAddress != identity.Keys.SigningPublicKey || profile.PublicEncryptAddress != identity.Keys.EncryptPublicKey)
            throw new InvalidOperationException("Restoration changed the indexed identity profile.");
        await Expect(scenario.Page.GetByRole(AriaRole.Button, new() { Name = HushVotingIdentityJourney.Alias, Exact = true })).ToBeVisibleAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2, "only the fixture identity and root licence bootstrap are submitted");
        scenario.Faults.RequestMethods.Should().NotContain(method => method.Contains("UpdateIdentity", StringComparison.Ordinal));
    }
}
