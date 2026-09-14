using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialLookupSteps(HushVotingScenario scenario, CredentialFileSteps file)
{
    private IPage Page => scenario.Page;
    private const string Password = "lookup-test-backup";
    private byte[] _backup = [];
    private int _before;
    private int _transactions;

    [Given("the node has the backup signing address registered with a different valid encryption address")]
    public async Task MismatchedProfileAsync()
    {
        var keys = HushVotingTestIdentity.DeriveP01(string.Join(" ", Enumerable.Repeat("abandon", 23).Append("art")));
        var other = HushVotingTestIdentity.DeriveP01(string.Join(" ", Enumerable.Repeat("abandon", 11).Append("about")));
        await HushVotingServerIdentity.RegisterAsync(scenario, keys, "Registered pair", encryptionAddress: other.EncryptPublicKey);
        _backup = HushVotingCredentialFile.Create(keys, "Backup pair", Password);
        _before = scenario.Faults.IdentityQueryCount;
        _transactions = scenario.Faults.SubmittedTransactions.Count;
        await Page.GotoAsync("/");
        await file.OpenAsync();
    }

    [Given("the credential identity lookup service is temporarily unavailable")]
    public async Task UnavailableAsync()
    {
        var keys = HushVotingTestIdentity.DeriveP01(string.Join(" ", Enumerable.Repeat("abandon", 23).Append("art")));
        _backup = HushVotingCredentialFile.Create(keys, "Offline backup", Password);
        scenario.Faults.IdentityUnavailable = true;
        await Page.GotoAsync("/");
        await file.OpenAsync();
    }

    [When("Alice decrypts the backup and performs its real public identity lookup")]
    public async Task LookupAsync()
    {
        await file.ChooseAsync(_backup);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("backup-password-input"), Password);
        await Page.GetByTestId("submit-password").ClickAsync();
        await Expect(Page.GetByTestId("restore-panel").GetByRole(AriaRole.Alert)).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Then("a signing-only match cannot authorize protection, registration, or authentication")]
    public async Task MismatchRejectedAsync()
    {
        scenario.Faults.IdentityQueryCount.Should().Be(_before + 1);
        await NoCreationAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_transactions);
    }

    private async Task NoCreationAsync()
    {
        await Expect(Page.GetByTestId("create-identity")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("restore-device-password")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
    }

    [Then("transport failure offers no profile creation and only a later authoritative absence enables review")]
    public async Task OutageIsNotAbsenceAsync()
    {
        scenario.Faults.RejectedIdentityQueries.Should().Be(1);
        await NoCreationAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        scenario.Faults.IdentityUnavailable = false;
        await Page.GoBackAsync();
        // Validated candidate cleanup returns to a fresh picker (AC-009-067).
        await Expect(Page.GetByTestId("choose-file")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("credential-file-input")).ToHaveValueAsync("");
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        await file.ChooseAsync(_backup);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("backup-password-input"), Password);
        await Page.GetByTestId("submit-password").ClickAsync();
        await Expect(Page.GetByTestId("create-identity")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.IdentityQueryCount.Should().Be(2);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }
}
