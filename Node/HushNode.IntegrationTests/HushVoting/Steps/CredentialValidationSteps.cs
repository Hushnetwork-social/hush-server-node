using System.Text.Json;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialValidationSteps(HushVotingScenario scenario, CredentialFileSteps file)
{
    private IPage Page => scenario.Page;
    private const string Password = "independent-backup";
    private const string Inconsistent = "This credential file contains invalid or inconsistent identity keys and cannot be restored.";
    private const string AuthenticationError = "The backup password is incorrect or the credential file is damaged.";
    private static readonly string Words = string.Join(" ", Enumerable.Repeat("abandon", 23).Append("art"));
    private int _rejected;
    private int _expected;
    private string? _positiveMnemonic;
    private readonly HashSet<string> _codes = [];
    private bool _secretEcho;
    private static IEnumerable<string> PrivateProbes()
    {
        var keys = HushVotingTestIdentity.DeriveP01(Words);
        return [Words, keys.SigningPrivateKey, keys.EncryptPrivateKey, Password, "Non-disclosed credential probe", "private-probe"];
    }

    private static Dictionary<string, object?> Payload()
    {
        var keys = HushVotingTestIdentity.DeriveP01(Words);
        return new() { ["ProfileName"] = "Non-disclosed credential probe", ["PublicSigningAddress"] = keys.SigningPublicKey,
            ["PrivateSigningKey"] = keys.SigningPrivateKey, ["PublicEncryptAddress"] = keys.EncryptPublicKey,
            ["PrivateEncryptKey"] = keys.EncryptPrivateKey, ["IsPublic"] = false, ["Mnemonic"] = null };
    }

    [Given("independently authenticated credential payloads are ready for the real browser worker")]
    public async Task ArrangeAsync()
    {
        await Page.GotoAsync("/");
        await file.OpenAsync();
        var probes = PrivateProbes().ToArray();
        Page.Console += (_, message) =>
        {
            if (probes.Any(probe => message.Text.Contains(probe, StringComparison.Ordinal))) _secretEcho = true;
            var match = System.Text.RegularExpressions.Regex.Match(message.Text, @"^\[HushVoting\]\[credential-file-restore\] stage=import outcome=[A-Z_]+ code=(DAT_[A-Z_]+)$");
            if (match.Success) _codes.Add(match.Groups[1].Value);
        };
    }

    private async Task SubmitAsync(byte[] bytes, string password = Password)
    {
        await file.ChooseAsync(bytes);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("backup-password-input"), password);
        await Page.GetByTestId("submit-password").ClickAsync();
    }

    private async Task RejectedAsync(byte[] bytes, string error = Inconsistent, string password = Password)
    {
        await SubmitAsync(bytes, password);
        await Expect(Page.GetByText(error, new() { Exact = true })).ToBeVisibleAsync();
        if (error == AuthenticationError)
        {
            await Expect(Page.GetByTestId("backup-password-input")).ToHaveValueAsync("");
            await Expect(Page.GetByTestId("backup-password-input")).ToHaveAttributeAsync("type", "password");
        }
        else await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("create-identity")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("restore-device-password")).ToHaveCountAsync(0);
        var visible = await Page.Locator("body").InnerTextAsync();
        if (PrivateProbes().Any(probe => visible.Contains(probe, StringComparison.Ordinal))) _secretEcho = true;
        _secretEcho.Should().BeFalse("semantic and authentication errors must not echo decrypted data or credentials");
        scenario.Faults.IdentityQueryCount.Should().Be(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        _rejected++;
        if (error == AuthenticationError)
        {
            await Page.GetByTestId("choose-different-file").ClickAsync();
            await Expect(Page.GetByTestId("choose-file")).ToBeVisibleAsync();
        }
    }

    [When("Alice imports authenticated JSON with duplicate, unknown, missing, invalid-type, and out-of-bounds fields")]
    public async Task SchemaAsync()
    {
        var cases = new List<string> { "null", "[]", "true", "12", "\"scalar\"" };
        var valid = JsonSerializer.Serialize(Payload());
        cases.Add(valid[..^1] + ",\"ProfileName\":\"Duplicate\"}");
        cases.Add(valid[..^1] + ",\"\\u0050rofileName\":\"Escaped duplicate\"}");
        var unknown = Payload(); unknown["UnknownSecretField"] = "private-probe"; cases.Add(JsonSerializer.Serialize(unknown));
        var missing = Payload(); missing.Remove("Mnemonic"); cases.Add(JsonSerializer.Serialize(missing));
        foreach (var (key, value) in new (string, object?)[] { ("IsPublic", "false"), ("Mnemonic", 1), ("ProfileName", ""), ("ProfileName", new string('x', 65)), ("ProfileName", "control\u0001"), ("PrivateSigningKey", 4) })
        { var payload = Payload(); payload[key] = value; cases.Add(JsonSerializer.Serialize(payload)); }
        _expected = cases.Count;
        foreach (var payload in cases) await RejectedAsync(HushVotingCredentialFile.CreateJson(payload, Password));
    }

    [When("Alice imports a backup whose signing private key disagrees with its public address")]
    public async Task SigningAsync() => await MismatchAsync("PrivateSigningKey");

    [When("Alice imports a backup whose encryption private key disagrees with its public address")]
    public async Task EncryptionAsync() => await MismatchAsync("PrivateEncryptKey");

    private async Task MismatchAsync(string field)
    {
        var payload = Payload(); payload[field] = new string('0', 63) + "1";
        _expected = 1;
        await RejectedAsync(HushVotingCredentialFile.CreateJson(JsonSerializer.Serialize(payload), Password));
    }

    [When("Alice imports backups with malformed or unsupported key encodings")]
    public async Task EncodingAsync()
    {
        var cases = new (string, string)[] { ("PrivateSigningKey", "not-hex"), ("PrivateEncryptKey", new string('0', 64)),
            ("PublicSigningAddress", "05" + new string('1', 64)), ("PublicEncryptAddress", "rsa:unsupported"), ("PrivateSigningKey", new string('f', 64)) };
        _expected = cases.Length;
        foreach (var (key, value) in cases)
        { var payload = Payload(); payload[key] = value; await RejectedAsync(HushVotingCredentialFile.CreateJson(JsonSerializer.Serialize(payload), Password)); }
    }

    [When("Alice imports a backup with a valid phrase that derives different concrete keys")]
    public async Task MnemonicMismatchAsync()
    {
        var payload = Payload(); payload["Mnemonic"] = string.Join(" ", Enumerable.Repeat("abandon", 11).Append("about"));
        _expected = 1;
        await RejectedAsync(HushVotingCredentialFile.CreateJson(JsonSerializer.Serialize(payload), Password));
        _positiveMnemonic = Words;
    }

    [Then("invalid credential data is rejected before lookup and a consistent backup reaches the live node")]
    public async Task RejectionsVerifiedAsync()
    {
        _expected.Should().BeGreaterThan(0);
        _rejected.Should().Be(_expected);
        scenario.Faults.IdentityQueryCount.Should().Be(0);
        await ConsistentAsync();
    }

    [When("Alice decrypts a valid internally consistent backup with a null mnemonic")]
    public async Task ConsistentAsync()
    {
        await SubmitAsync(HushVotingCredentialFile.Create(HushVotingTestIdentity.DeriveP01(Words), "Validated voting profile", Password, mnemonic: _positiveMnemonic));
        await Expect(Page.GetByTestId("create-identity")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.IdentityQueryCount.Should().Be(1);
        await CandidateIsNotSuccessAsync();
    }

    [Then("the decrypted candidate still requires explicit profile confirmation and separate protection")]
    public async Task CandidateIsNotSuccessAsync()
    {
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("entitlement-gate")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("restore-device-password")).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [When("Alice attempts wrong-password, damaged-ciphertext, and damaged-tag imports")]
    public async Task AuthenticationFailuresAsync()
    {
        var valid = HushVotingCredentialFile.Create(HushVotingTestIdentity.DeriveP01(Words), "Non-disclosed credential probe", Password);
        await RejectedAsync(valid, AuthenticationError, "incorrect-test-password");
        var ciphertext = (byte[])valid.Clone(); ciphertext[40] ^= 1;
        await RejectedAsync(ciphertext, AuthenticationError);
        var tag = (byte[])valid.Clone(); tag[^1] ^= 1;
        await RejectedAsync(tag, AuthenticationError);
        _expected = 3;
    }

    [Then("every authentication failure uses the same ambiguous message and never queries or stages an identity")]
    public async Task AuthenticationVerifiedAsync()
    {
        _rejected.Should().Be(3);
        scenario.Faults.IdentityQueryCount.Should().Be(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
    }

    [When("Alice imports schema, key-pair, and mnemonic inconsistencies")]
    public async Task CombinedErrorsAsync()
    {
        await SigningAsync();
        await MnemonicMismatchAsync();
        var payload = Payload(); payload["IsPublic"] = "false";
        await RejectedAsync(HushVotingCredentialFile.CreateJson(JsonSerializer.Serialize(payload), Password));
        _expected = 3;
    }

    [Then("the visible inconsistency message is shared while safe internal failure codes remain distinct")]
    public void CodesVerified()
    {
        _rejected.Should().Be(3);
        _codes.Should().Contain(["DAT_KEY_MISMATCH", "DAT_MNEMONIC_KEY_MISMATCH", "DAT_INVALID_FIELD"]);
        scenario.Faults.IdentityQueryCount.Should().Be(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [Then("semantic failures expose no decrypted values and a fresh valid import uses only its own identity")]
    public async Task SemanticCleanupAsync()
    {
        _secretEcho.Should().BeFalse();
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("selected-file-name")).ToHaveCountAsync(0);
        (await Page.EvaluateAsync<bool>("() => localStorage.length === 0 && sessionStorage.length === 0")).Should().BeTrue();
        await RejectionsVerifiedAsync();
        var expected = HushVotingTestIdentity.DeriveP01(Words).SigningPublicKey;
        scenario.Faults.IdentityLookups.All(lookup => lookup.SigningAddress == expected).Should().BeTrue();
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
    }
}
