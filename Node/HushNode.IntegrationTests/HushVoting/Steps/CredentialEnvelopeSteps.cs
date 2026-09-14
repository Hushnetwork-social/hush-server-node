using System.Buffers.Binary;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialEnvelopeSteps(HushVotingScenario scenario, CredentialFileSteps file)
{
    private IPage Page => scenario.Page;
    private byte[] _valid = [];
    private int _rejected;

    [Given("a HUSH v1 envelope is inspected")]
    public async Task ArrangeAsync()
    {
        await Page.GotoAsync("/");
        await file.OpenAsync();
        var keys = HushVotingTestIdentity.DeriveP01(string.Join(" ", Enumerable.Repeat("abandon", 23).Append("art")));
        _valid = HushVotingCredentialFile.Create(keys, "Envelope test identity", "backup-password");
    }

    [When("the structural gate runs before password use")]
    public async Task RejectAsync()
    {
        var cases = new List<(byte[] Bytes, string Message)>();
        foreach (var length in new[] { 0, 4, 35, 36, 51 }) cases.Add((_valid[..length], "This credential backup is incomplete."));
        var magic = (byte[])_valid.Clone(); magic[0] = 0;
        cases.Add((magic, "This file is not a valid HUSH credential backup."));
        var version = (byte[])_valid.Clone(); BinaryPrimitives.WriteInt32LittleEndian(version.AsSpan(4, 4), 2);
        cases.Add((version, "This credential backup version is not supported."));
        var bigEndian = (byte[])_valid.Clone(); BinaryPrimitives.WriteInt32BigEndian(bigEndian.AsSpan(4, 4), 1);
        cases.Add((bigEndian, "This credential backup version is not supported."));
        cases.Add((new byte[1_048_577], "This credential backup exceeds the 1 MiB size limit."));
        foreach (var (bytes, message) in cases)
        {
            try { await Page.GetByTestId("credential-file-input").SetInputFilesAsync(new FilePayload { Name = "structural.dat", MimeType = "application/octet-stream", Buffer = bytes }); }
            catch (Exception error) when (error is PlaywrightException or TimeoutException) { throw new InvalidOperationException("Structural backup selection failed; file content omitted."); }
            await Expect(Page.GetByText(message, new() { Exact = true })).ToBeVisibleAsync();
            await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
            await Expect(Page.GetByTestId("restore-device-password")).ToHaveCountAsync(0);
            _rejected++;
        }
    }

    [Then("magic, little-endian version one, salt, nonce, and ciphertext bounds are validated with safe pre-password errors")]
    public async Task CorrectEnvelopeAsync()
    {
        _rejected.Should().Be(9);
        scenario.Faults.IdentityQueryCount.Should().Be(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        await file.ChooseAsync(_valid, "valid.backup");
        await Expect(Page.GetByTestId("backup-password-input")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("selected-file-name")).ToHaveTextAsync("valid.backup");
        scenario.Faults.IdentityQueryCount.Should().Be(0);
    }
}
