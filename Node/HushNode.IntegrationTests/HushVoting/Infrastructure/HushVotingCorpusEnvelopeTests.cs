using System.Diagnostics;
using System.Security.Cryptography;
using HushVoting.IntegrationTests.Infrastructure;
using Olimpo.KeyDerivation;
using Xunit;

namespace HushVoting.IntegrationTests.ToolingTests;

// FEAT-009 Phase 6 Task 6.10. Public fixtures only; no controlled acceptance claim.
[Trait("Category", "HushVoting")]
[Trait("Category", "HV-CORPUS-TOOLING")]
public sealed class HushVotingCorpusEnvelopeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ActualV1Envelope_PreservesConcreteKeysAndMnemonicShape(bool historical, bool mnemonic)
    {
        var words = string.Join(' ', Enumerable.Repeat("abandon", historical ? 23 : 11).Append(historical ? "art" : "about"));
        var keys = historical ? DeterministicKeyGenerator.DeriveKeys(words) : HushVotingTestIdentity.DeriveP01(words);
        var bytes = HushVotingCredentialFile.Create(keys, "Public fixture", "public-password", mnemonic: mnemonic ? words : null);
        try
        {
            using var oracle = HushVotingCorpusEnvelope.Open(bytes, "public-password");
            Assert.NotNull(oracle);
            Assert.True(oracle.Keys.SigningPublicKey == keys.SigningPublicKey && oracle.Keys.SigningPrivateKey == keys.SigningPrivateKey
                && oracle.Keys.EncryptPublicKey == keys.EncryptPublicKey && oracle.Keys.EncryptPrivateKey == keys.EncryptPrivateKey);
            Assert.Equal(mnemonic, oracle.HasMnemonic);
            oracle.Dispose();
            Assert.Null(oracle.Keys);
            Assert.False(oracle.HasMnemonic);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    [Theory]
    [InlineData("password")]
    [InlineData("tag")]
    [InlineData("magic")]
    [InlineData("version")]
    [InlineData("truncated")]
    [InlineData("oversized")]
    [InlineData("password-size")]
    public void RejectedOracleInput_ProducesNoCredentialResult(string fault)
    {
        var keys = HushVotingTestIdentity.DeriveP01("abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about");
        var bytes = HushVotingCredentialFile.Create(keys, "Public fixture", "public-password");
        if (fault == "tag") bytes[^1] ^= 1;
        if (fault == "magic") bytes[0] ^= 1;
        if (fault == "version") bytes[4] = 2;
        var selected = fault == "oversized" ? new byte[HushVotingCorpusSource.MaximumBytes + 1]
            : fault == "truncated" ? bytes[..40] : bytes;
        try
        {
            var password = fault == "password" ? "incorrect-password" : fault == "password-size" ? new string('x', 4097) : "public-password";
            Assert.Null(HushVotingCorpusEnvelope.Open(selected, password));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); CryptographicOperations.ZeroMemory(selected); }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"PublicSigningAddress\":\"a\",\"PublicSigningAddress\":\"b\"}")]
    [InlineData("{\"PublicSigningAddress\":7}")]
    public void AmbiguousOrIncompletePayload_ProducesNoCredentialResult(string json)
    {
        var bytes = HushVotingCredentialFile.CreateJson(json, "public-password");
        try { Assert.Null(HushVotingCorpusEnvelope.Open(bytes, "public-password")); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    [Theory]
    [InlineData("CI", "true")]
    [InlineData("GITHUB_ACTIONS", "true")]
    [InlineData("CODESPACES", "true")]
    [InlineData("DOCKER_HOST", "tcp://example.invalid:2375")]
    [InlineData("DOCKER_CONTEXT", "shared")]
    public void ControlledPolicy_RefusesCiCloudAndRemoteDocker(string name, string value)
        => Assert.False(HushVotingCorpusInputs.IsLocalOperatorEnvironment(true, key => key == name ? value : null));

    [Fact]
    public void ControlledPolicy_RequiresLinuxAndAllowsTheLocalDefault()
    {
        Assert.False(HushVotingCorpusInputs.IsLocalOperatorEnvironment(false, _ => null));
        Assert.True(HushVotingCorpusInputs.IsLocalOperatorEnvironment(true, _ => null));
    }

    [Fact]
    public async Task ChildProcess_DoesNotReceiveCorpusInputsOrMode()
    {
        var start = new ProcessStartInfo("bash") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var name in HushVotingCorpusInputs.Names) start.Environment[name] = "public-sentinel";
        start.Environment["HUSHVOTING_CONTROLLED_CORPUS"] = "1";
        start.Environment["HV_PUBLIC_CHILD_CHECK"] = "kept";
        HushVotingCorpusInputs.RemoveFromChildEnvironment(start.Environment);
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("test -z \"${HUSH_TEST_KEYS_DIR+x}${HUSH_TEST_DAT_PASSWORD+x}${HUSH_TEST_CORPUS_INVENTORY+x}${HUSHVOTING_CONTROLLED_CORPUS+x}\" && test \"$HV_PUBLIC_CHILD_CHECK\" = kept");
        using var child = Process.Start(start)!;
        try
        {
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, child.ExitCode);
            Assert.Empty(await child.StandardOutput.ReadToEndAsync());
            Assert.Empty(await child.StandardError.ReadToEndAsync());
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
        }
    }
}
