using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007/008/009 accessibility ACs -> Phase 5 UI and Phase 7 Tasks 7.1/7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class AccessibilityJourneySteps(HushVotingScenario scenario, HushVotingAccessibility accessibility,
    HushVotingIdentityJourney identity, HushVotingKeyboard keyboard)
{
    private IPage Page => scenario.Page;

    [Given("Alice checks Web accessibility across real identity creation and server activation")]
    public async Task CreationAsync()
    {
        await HushVotingArtifactClient.RequireAsync();
        await Page.GotoAsync("/");
        await accessibility.CheckAsync("creation-root");
        await keyboard.ActivateAsync(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Create User") }));
        await Expect(Page.GetByLabel("Profile name / alias", new() { Exact = true })).ToBeVisibleAsync();
        await accessibility.CheckAsync("creation-profile");
        var aliasInput = Page.GetByLabel("Profile name / alias", new() { Exact = true });
        await keyboard.ActivateAsync(Button("Continue"));
        await Expect(Page.Locator("#create-alias-error")).ToHaveAttributeAsync("role", "alert");
        await Expect(Page.Locator("#create-alias-error")).ToHaveTextAsync("Enter a profile name to continue.");
        await Expect(aliasInput).ToHaveAttributeAsync("aria-describedby", "create-alias-error");
        await accessibility.CheckAsync("creation-profile-required");
        await keyboard.EnterAsync(aliasInput, new string('a', 65));
        await keyboard.ActivateAsync(Button("Continue"));
        await Expect(Page.Locator("#create-alias-error")).ToHaveTextAsync("This profile name is too long (64 characters or 256 bytes maximum).");
        await accessibility.CheckAsync("creation-profile-length-error");
        scenario.Faults.IdentityQueryCount.Should().Be(0);
        scenario.Faults.SubmittedTransactions.Should().BeEmpty();
        await keyboard.EnterAsync(Page.GetByLabel("Profile name / alias", new() { Exact = true }), HushVotingIdentityJourney.Alias);
        await keyboard.ActivateAsync(Button("Continue"));
        await accessibility.CheckAsync("creation-generate");
        await keyboard.ActivateAsync(Button("Generate recovery words"));
        var words = await identity.ReadCandidateAsync();
        await accessibility.CheckAsync("creation-recovery");
        await keyboard.ActivateAsync(Page.GetByRole(AriaRole.Checkbox), "Space");
        await keyboard.ActivateAsync(Button("Continue"));
        var challenge = Page.Locator("input[id^=recovery-word-]");
        await Expect(challenge).ToHaveCountAsync(6);
        await accessibility.CheckAsync("creation-confirm");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var fields = await challenge.AllAsync();
            var firstId = await fields[0].GetAttributeAsync("id");
            var firstPosition = int.Parse(firstId!["recovery-word-".Length..], System.Globalization.CultureInfo.InvariantCulture);
            for (var index = 0; index < fields.Count; index++)
            {
                var position = int.Parse((await fields[index].GetAttributeAsync("id"))!["recovery-word-".Length..], System.Globalization.CultureInfo.InvariantCulture);
                await keyboard.EnterAsync(fields[index], index == 0 ? "incorrect" : words[position - 1]);
            }
            await keyboard.ActivateAsync(Button("Verify words"));
            await Expect(Page.GetByTestId("recovery-list")).ToHaveCountAsync(0);
            await Expect(Page.GetByLabel("Device password", new() { Exact = true })).ToHaveCountAsync(0);
            if (attempt < 2)
            {
                await Expect(Page.Locator("#recovery-mismatch")).ToHaveAttributeAsync("role", "alert");
                await Expect(Page.Locator("#recovery-mismatch")).ToHaveTextAsync($"Word {firstPosition} does not match. Please check your saved words.");
                await Expect(Page.Locator("input[aria-describedby=recovery-mismatch]")).ToHaveCountAsync(1);
                await Expect(fields[0]).ToHaveAttributeAsync("aria-describedby", "recovery-mismatch");
                foreach (var input in fields)
                    ((await input.InputValueAsync()).Length == 0).Should().BeTrue("failed answers must clear without echoing credentials");
                await Expect(Button("Verify words")).ToBeDisabledAsync();
                if (attempt == 0) await accessibility.CheckAsync("creation-challenge-error");
            }
        }
        await Expect(challenge).ToHaveCountAsync(0);
        await Expect(Button("Verify words")).ToHaveCountAsync(0);
        await accessibility.CheckAsync("creation-challenge-closed");
        scenario.Faults.IdentityQueryCount.Should().Be(0);
        scenario.Faults.SubmittedTransactions.Should().BeEmpty();
        await keyboard.ActivateAsync(Button("Review all words"));
        await Expect(Page.GetByTestId("recovery-list").Locator("li")).ToHaveCountAsync(24);
        var reviewed = await Page.GetByTestId("recovery-list").Locator("li > span:last-child").AllTextContentsAsync();
        reviewed.SequenceEqual(words).Should().BeTrue("explicit review must preserve the same candidate");
        await Expect(Button("Continue")).ToBeDisabledAsync();
        await accessibility.CheckAsync("creation-challenge-review");
        await keyboard.ActivateAsync(Page.GetByRole(AriaRole.Checkbox), "Space");
        await keyboard.ActivateAsync(Button("Continue"));
        await Expect(challenge).ToHaveCountAsync(6);
        foreach (var input in await challenge.AllAsync())
        {
            var index = int.Parse((await input.GetAttributeAsync("id"))!["recovery-word-".Length..], System.Globalization.CultureInfo.InvariantCulture);
            await keyboard.EnterAsync(input, words[index - 1]);
        }
        await keyboard.ActivateAsync(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Verify") }));
        await accessibility.CheckAsync("creation-protect");
        await keyboard.EnterAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await keyboard.EnterAsync(Page.GetByLabel("Confirm device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await keyboard.ActivateAsync(Button("Protect this device and continue"));
        await Expect(Button("Create HushNetwork identity")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await accessibility.CheckAsync("creation-review");
        using (var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30)))
        {
            await keyboard.ActivateAsync(Button("Create HushNetwork identity"));
            await received.WaitAsync();
        }
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await keyboard.ActivateAsync(Button("Lock"));
        await scenario.Blocks.ProduceBlockAsync();
        await Page.ReloadAsync();
        await Expect(Button("Unlock HushVoting!")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await accessibility.CheckAsync("creation-locked");
        await keyboard.EnterAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await ActivateAndIndexLicenceAsync(Button("Unlock HushVoting!"));
        keyboard.AssertExercised(29, 17);
        await accessibility.CheckAsync("creation-workspace");
    }

    // Setup registers a public fixture through real signed/indexed node transactions.
    // Every user action from the empty root onward is keyboard driven.
    private static readonly string[] PublicWords = [.. Enumerable.Repeat("abandon", 23), "art"];
    private const string BackupPassword = "keyboard backup password";

    private async Task PrepareRegisteredAsync()
    {
        await HushVotingArtifactClient.RequireAsync();
        var phrase = string.Join(' ', PublicWords);
        var keys = HushVotingTestIdentity.DeriveP01(phrase);
        await HushVotingArtifactClient.RegisterAsync(phrase, keys.SigningPrivateKey, keys.EncryptPrivateKey,
            keys.SigningPublicKey, keys.EncryptPublicKey, BackupPassword, HushVotingScenario.DevicePassword);
        await HushVotingServerIdentity.RegisterAsync(scenario, keys, HushVotingIdentityJourney.Alias);
        await Page.GotoAsync("/");
    }

    [Given("Alice checks Web accessibility across real recovery words and server activation")]
    public async Task RecoveryAsync()
    {
        await PrepareRegisteredAsync();
        await keyboard.ActivateAsync(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Restore Recovery Words") }));
        await Expect(Page.GetByTestId("word-grid")).ToBeVisibleAsync();
        await accessibility.CheckAsync("recovery-entry");
        // Native radio-group arrow navigation must switch both ways and change the real grid.
        await keyboard.ReachAsync(Page.GetByTestId("count-24"));
        await Page.Keyboard.PressAsync("ArrowLeft");
        await Expect(Page.GetByTestId("count-12")).ToBeCheckedAsync();
        await Expect(Page.GetByTestId("word-grid").Locator("input")).ToHaveCountAsync(12);
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(Page.GetByTestId("count-24")).ToBeCheckedAsync();
        await Expect(Page.GetByTestId("word-grid").Locator("input")).ToHaveCountAsync(24);
        for (var index = 0; index < PublicWords.Length; index++)
            await keyboard.EnterAsync(Page.GetByLabel($"Recovery word {index + 1} of 24", new() { Exact = true }), PublicWords[index]);
        await keyboard.ActivateAsync(Button("Show all words"));
        await Expect(Page.Locator("#rw-1")).ToHaveAttributeAsync("type", "text");
        await keyboard.ActivateAsync(Button("Hide all words"));
        await Expect(Page.Locator("#rw-1")).ToHaveAttributeAsync("type", "password");
        var queriesBeforeErrors = scenario.Faults.IdentityQueryCount;
        await keyboard.EnterAsync(Page.Locator("#rw-3"), "notabipword");
        await keyboard.ActivateAsync(Button("Verify"));
        await Expect(Page.Locator("#rw-error-UNKNOWN_WORD")).ToBeVisibleAsync();
        await Expect(Page.Locator("#rw-3")).ToBeFocusedAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Recovery word errors", Exact = true })).ToBeVisibleAsync();
        await keyboard.ActivateAsync(Page.GetByRole(AriaRole.Link, new() { Name = "Review word 3", Exact = true }));
        await Expect(Page.Locator("#rw-3")).ToBeFocusedAsync();
        scenario.Faults.IdentityQueryCount.Should().Be(queriesBeforeErrors);
        await accessibility.CheckAsync("recovery-numbered-error");
        await keyboard.EnterAsync(Page.Locator("#rw-3"), PublicWords[2]);
        // A complete vocabulary-valid but checksum-invalid phrase must remain correctable
        // by keyboard without network traffic or exposing a workspace.
        await keyboard.EnterAsync(Page.Locator("#rw-24"), "abandon");
        var queries = scenario.Faults.IdentityQueryCount;
        await keyboard.ActivateAsync(Button("Verify"));
        await Expect(Page.Locator("#rw-error-checksum")).ToBeVisibleAsync();
        await Expect(Page.Locator("#rw-error-checksum")).ToHaveAttributeAsync("role", "alert");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Recovery word errors", Exact = true })).ToBeFocusedAsync();
        scenario.Faults.IdentityQueryCount.Should().Be(queries);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await accessibility.CheckAsync("recovery-checksum-error");
        await keyboard.EnterAsync(Page.Locator("#rw-24"), PublicWords[23]);
        await keyboard.ActivateAsync(Button("Verify"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Confirm this identity", Exact = true })).ToBeVisibleAsync();
        (scenario.Faults.IdentityQueryCount - queries).Should().Be(2);
        await Expect(Page.GetByTestId("candidate-list").Locator("li")).ToHaveCountAsync(1);
        await Expect(Page.GetByTestId("safe-alias")).ToHaveTextAsync(HushVotingIdentityJourney.Alias);
        await accessibility.CheckAsync("recovery-review");
        await keyboard.ActivateAsync(Button("Continue to protect this device"));
        await Expect(Page.GetByTestId("mode-password")).ToBeCheckedAsync();
        await accessibility.CheckAsync("recovery-protect");
        await keyboard.ActivateAsync(Page.GetByTestId("recovery-no-retention-ack"), "Space");
        await keyboard.EnterAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await keyboard.EnterAsync(Page.GetByLabel("Confirm device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await ActivateAndIndexLicenceAsync(Button("Continue"));
        keyboard.AssertExercised(26, 7);
        await accessibility.CheckAsync("recovery-workspace");
    }

    [Given("Alice checks Web accessibility across real credential import and server activation")]
    public async Task FileAsync()
    {
        await PrepareRegisteredAsync();
        var keys = HushVotingTestIdentity.DeriveP01(string.Join(' ', PublicWords));
        var bytes = HushVotingCredentialFile.Create(keys, "Historical keyboard alias", BackupPassword);
        await HushVotingArtifactClient.RegisterAsync(Convert.ToBase64String(bytes));
        try
        {
            await keyboard.ActivateAsync(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Restore Credential File") }));
            await Expect(Page.GetByTestId("choose-file")).ToBeVisibleAsync();
            await accessibility.CheckAsync("file-picker");
            var structuralQueries = scenario.Faults.IdentityQueryCount;
            foreach (var defect in new[] { "incomplete", "magic", "version" })
            {
                var invalid = defect == "incomplete" ? bytes[..35] : (byte[])bytes.Clone();
                try
                {
                    if (defect == "magic") invalid[0] = 0;
                    if (defect == "version") System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(4, 4), 2);
                    await HushVotingArtifactClient.RegisterAsync(Convert.ToBase64String(invalid));
                    await keyboard.SelectFileAsync(Page.GetByTestId("choose-file"), invalid);
                    var message = defect switch
                    {
                        "incomplete" => "This credential backup is incomplete.",
                        "magic" => "This file is not a valid HUSH credential backup.",
                        _ => "This credential backup version is not supported."
                    };
                    await Expect(Page.GetByTestId("restore-error")).ToHaveAttributeAsync("role", "alert");
                    await Expect(Page.GetByTestId("restore-error")).ToHaveTextAsync(message);
                    await Expect(Page.GetByTestId("restore-error")).ToBeFocusedAsync();
                    await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
                    await Expect(Page.GetByTestId("restore-device-password")).ToHaveCountAsync(0);
                    scenario.Faults.IdentityQueryCount.Should().Be(structuralQueries);
                    await accessibility.CheckAsync("file-structural-" + defect);
                }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(invalid); }
            }
            await keyboard.SelectFileAsync(Page.GetByTestId("choose-file"), bytes);
            await Expect(Page.GetByTestId("backup-password-input")).ToBeVisibleAsync();
            await accessibility.CheckAsync("file-password");
            var queries = scenario.Faults.IdentityQueryCount;
            await HushVotingArtifactClient.RegisterAsync("incorrect keyboard password");
            for (var attempt = 0; attempt < 3; attempt++)
            {
                await keyboard.EnterAsync(Page.GetByTestId("backup-password-input"), "incorrect keyboard password");
                await keyboard.ActivateAsync(Page.GetByTestId("submit-password"));
                await Expect(Page.GetByText("The backup password is incorrect or the credential file is damaged.", new() { Exact = true })).ToBeVisibleAsync();
                await Expect(Page.GetByTestId("backup-password-input")).ToHaveValueAsync("");
                scenario.Faults.IdentityQueryCount.Should().Be(queries);
                if (attempt < 2)
                {
                    await Expect(Page.GetByTestId("restore-error")).ToBeFocusedAsync();
                    if (attempt == 0) await accessibility.CheckAsync("file-password-error");
                }
            }
            await Expect(Page.GetByTestId("backoff-countdown")).ToBeFocusedAsync();
            await Expect(Page.GetByTestId("submit-password")).ToBeDisabledAsync();
            await keyboard.ReachAsync(Page.GetByTestId("choose-different-file"));
            await Expect(Page.GetByTestId("backoff-countdown")).ToHaveCountAsync(0, new() { Timeout = 5_000 });
            await Expect(Page.GetByTestId("choose-different-file")).ToBeFocusedAsync();
            await keyboard.ActivateAsync(Page.GetByTestId("choose-different-file"));
            await Expect(Page.GetByTestId("choose-file")).ToBeVisibleAsync();
            await Expect(Page.GetByTestId("credential-file-input")).ToHaveValueAsync("");
            await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
            await keyboard.ActivateAsync(Button("Back"));
            await keyboard.ActivateAsync(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Restore Credential File") }));
            await keyboard.SelectFileAsync(Page.GetByTestId("choose-file"), bytes);
            await Expect(Page.GetByTestId("backup-password-input")).ToBeVisibleAsync();
            await keyboard.EnterAsync(Page.GetByTestId("backup-password-input"), BackupPassword);
            await keyboard.ActivateAsync(Page.GetByTestId("submit-password"));
            await Expect(Page.GetByTestId("restore-device-password")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
            await accessibility.CheckAsync("file-protect");
            await keyboard.EnterAsync(Page.GetByTestId("restore-device-password"), HushVotingScenario.DevicePassword);
            await keyboard.EnterAsync(Page.GetByTestId("restore-device-password-confirmation"), HushVotingScenario.DevicePassword);
            await ActivateAndIndexLicenceAsync(Page.GetByTestId("submit-protection"));
            keyboard.AssertExercised(3, 4);
            await accessibility.CheckAsync("file-workspace");
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }

    private async Task ActivateAndIndexLicenceAsync(ILocator submit)
    {
        var queries = scenario.Faults.IdentityQueryCount;
        using var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        await keyboard.ActivateAsync(submit);
        await received.WaitAsync();
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(queries);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await scenario.Blocks.ProduceBlockAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Button(HushVotingIdentityJourney.Alias)).ToBeVisibleAsync();
        // Exercise keyboard access to the actual account menu and return to its trigger.
        await keyboard.ActivateAsync(Button(HushVotingIdentityJourney.Alias));
        await Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "User information", Exact = true })).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "User information", Exact = true })).ToHaveCountAsync(0);
        await Expect(Button(HushVotingIdentityJourney.Alias)).ToBeFocusedAsync();
    }

    [Then("the measured Web checkpoints have no automated accessibility or layout findings")]
    public async Task AutomatedChecksAsync()
    {
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        accessibility.AssertNoAutomatedFindings();
        await HushVotingArtifactClient.CheckAsync();
    }

    private ILocator Button(string name) => Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
}
