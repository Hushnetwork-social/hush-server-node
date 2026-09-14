using System.Text.Json;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialProfileSteps(HushVotingScenario scenario, CredentialFileSteps file)
{
    private IPage Page => scenario.Page;
    private const string Password = "profile-backup-password";
    private const string ImportedAlias = "Imported voting profile";
    private const string ReviewedAlias = "Café restored";
    private static readonly string Words = string.Join(" ", Enumerable.Repeat("abandon", 23).Append("art"));

    [Given("Alice decrypts a public backup whose identity is absent from the live blockchain")]
    public async Task MissingAsync()
    {
        var keys = HushVotingTestIdentity.DeriveP01(Words);
        await HushVotingArtifactClient.RegisterAsync(Words, Password, HushVotingScenario.DevicePassword,
            keys.SigningPrivateKey, keys.EncryptPrivateKey);
        await Page.GotoAsync("/");
        await file.OpenAsync();
        await file.ChooseAsync(HushVotingCredentialFile.Create(keys, ImportedAlias, Password, isPublic: true));
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("backup-password-input"), Password);
        await Page.GetByTestId("submit-password").ClickAsync();
        await Expect(Page.GetByTestId("create-identity")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.IdentityQueryCount.Should().Be(1);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [When("Alice reviews the missing-profile explanation and both recovered public addresses")]
    public async Task ReviewAsync()
    {
        await Expect(Page.GetByText("This may happen after a blockchain reset or if the identity was never registered. Creating its profile uses the same recovered keys.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("hushnetwork-devnet", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("file-full-signing-address")).ToHaveCountAsync(0);
        await Page.GetByTestId("reveal-addresses").ClickAsync();
        var keys = HushVotingTestIdentity.DeriveP01(Words);
        if (await Page.GetByTestId("file-full-signing-address").InnerTextAsync() != keys.SigningPublicKey || await Page.GetByTestId("file-full-encryption-address").InnerTextAsync() != keys.EncryptPublicKey)
            throw new InvalidOperationException("Credential review changed the imported public key pair.");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Hide full addresses", Exact = true }).ClickAsync();
        await Expect(Page.GetByTestId("file-full-signing-address")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("file-full-encryption-address")).ToHaveCountAsync(0);
    }

    [Then("the review preserves the same keys and bound network while waiting for explicit profile consent")]
    public async Task ConsentPendingAsync()
    {
        await Expect(Page.GetByTestId("file-profile-alias")).ToHaveValueAsync(ImportedAlias);
        await Expect(Page.GetByRole(AriaRole.Radio, new() { Name = "Public", Exact = true })).ToBeCheckedAsync();
        await Expect(Page.GetByTestId("file-profile-public-ack")).Not.ToBeCheckedAsync();
        await Expect(Page.GetByTestId("create-identity")).ToBeDisabledAsync();
        await Expect(Page.GetByTestId("restore-device-password")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [When("Alice corrects the imported profile name and explicitly acknowledges Public visibility")]
    public async Task ConsentAsync()
    {
        await ConsentPendingAsync();
        await Page.GetByTestId("file-profile-public-ack").CheckAsync();
        foreach (var invalid in new[] { "   ", new string('x', 65), "unsafe\u202ealias" })
        {
            await Page.GetByTestId("file-profile-alias").FillAsync(invalid);
            await Expect(Page.GetByTestId("create-identity")).ToBeDisabledAsync();
        }
        await Page.GetByTestId("file-profile-alias").FillAsync("  Cafe\u0301 restored  ");
        await Page.GetByRole(AriaRole.Radio, new() { Name = "Private", Exact = true }).CheckAsync();
        await Expect(Page.GetByTestId("file-profile-public-ack")).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Radio, new() { Name = "Public", Exact = true }).CheckAsync();
        await Expect(Page.GetByTestId("file-profile-public-ack")).Not.ToBeCheckedAsync();
        await Expect(Page.GetByTestId("create-identity")).ToBeDisabledAsync();
        await Page.GetByTestId("file-profile-public-ack").CheckAsync();
        await Page.GetByTestId("create-identity").ClickAsync();
        await Expect(Page.GetByTestId("restore-device-password")).ToBeVisibleAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [Then("separate protection registers only the reviewed profile using the exact imported keys")]
    public async Task RegisterAsync()
    {
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password"), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password-confirmation"), HushVotingScenario.DevicePassword);
        using (var submitted = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30)))
        {
            await Page.GetByTestId("submit-protection").ClickAsync();
            await submitted.WaitAsync();
        }
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Waiting for blockchain final approval", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("entitlement-gate")).ToHaveCountAsync(0);
        var keys = HushVotingTestIdentity.DeriveP01(Words);
        using (var signed = JsonDocument.Parse(scenario.Faults.SubmittedTransactions.Single()))
        {
            var root = signed.RootElement;
            root.GetProperty("PayloadKind").GetString().Should().Be("351cd60b-3fdf-48d4-b608-e93c0100f7d0");
            var payload = root.GetProperty("Payload");
            if (payload.GetProperty("PublicSigningAddress").GetString() != keys.SigningPublicKey || payload.GetProperty("PublicEncryptAddress").GetString() != keys.EncryptPublicKey)
                throw new InvalidOperationException("Credential profile registration changed the imported keys.");
            payload.GetProperty("IdentityAlias").GetString().Should().Be(ReviewedAlias);
            payload.GetProperty("IsPublic").GetBoolean().Should().BeTrue();
        }
        using var baseline = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        await scenario.Blocks.ProduceBlockAsync();
        await baseline.WaitAsync();
        await Expect(Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await scenario.Blocks.ProduceBlockAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = ReviewedAlias, Exact = true })).ToBeVisibleAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        scenario.Faults.RequestMethods.Should().NotContain(method => method.Contains("Feed", StringComparison.OrdinalIgnoreCase) || method.Contains("Social", StringComparison.OrdinalIgnoreCase));
    }

    [When("the node receives the explicitly confirmed imported profile with a damaged signature")]
    public async Task DamagedProofAsync()
    {
        await ConsentAsync();
        scenario.Faults.CorruptNextTransactionSignature = true;
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password"), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password-confirmation"), HushVotingScenario.DevicePassword);
        await Page.GetByTestId("submit-protection").ClickAsync();
    }

    [Then("unsigned lookup absence remains distinct from the server's typed invalid-signature rejection")]
    public async Task ProofRejectedAsync()
    {
        await Expect(Page.GetByTestId("restore-panel").GetByRole(AriaRole.Alert)).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.IdentityLookups.Count.Should().Be(1);
        scenario.Faults.IdentityLookups.Single().Reply.Successfull.Should().BeFalse();
        scenario.Faults.Submissions.Should().ContainSingle();
        var rejection = scenario.Faults.Submissions.Single();
        rejection.Status.Should().Be(HushNetwork.proto.TransactionStatus.Rejected);
        rejection.ValidationCode.Should().Be("FULL_IDENTITY_INVALID_SIGNATURE");
        await Expect(Page.GetByTestId("restore-panel").GetByRole(AriaRole.Alert)).ToHaveTextAsync("HushServerNode rejected the identity proof.");
        await Expect(Page.GetByTestId("create-identity")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("entitlement-gate")).ToHaveCountAsync(0);
        await scenario.Blocks.ProduceBlockAsync();
        var keys = HushVotingTestIdentity.DeriveP01(Words);
        var profile = await scenario.Identities.GetIdentityAsync(new HushNetwork.proto.GetIdentityRequest { PublicSigningAddress = keys.SigningPublicKey });
        profile.Successfull.Should().BeFalse();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
    }
}
