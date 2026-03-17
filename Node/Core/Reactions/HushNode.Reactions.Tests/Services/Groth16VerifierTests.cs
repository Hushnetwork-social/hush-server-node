using FluentAssertions;
using HushNode.Reactions.Crypto;
using HushNode.Reactions.ZK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Numerics;
using Xunit;

namespace HushNode.Reactions.Tests.Services;

public class Groth16VerifierTests
{
    [Fact]
    public void Constructor_WithoutPlaceholderKeys_DoesNotPretendVersionIsSupported()
    {
        var verifier = CreateVerifier(new Dictionary<string, string?>
        {
            ["Circuits:CurrentVersion"] = "omega-v1.0.0",
            ["Circuits:Supported:0"] = "omega-v1.0.0",
            ["Circuits:AllowPlaceholderVerificationKeys"] = "false",
            ["Circuits:AllowIncompleteVerification"] = "false",
        });

        verifier.IsVersionSupported("omega-v1.0.0").Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithUnapprovedConfiguredVersion_DoesNotTreatItAsSupportedEvenWithPlaceholderKeys()
    {
        var verifier = CreateVerifier(new Dictionary<string, string?>
        {
            ["Circuits:CurrentVersion"] = "omega-v1.0.0",
            ["Circuits:Supported:0"] = "omega-v9.9.9",
            ["Circuits:AllowPlaceholderVerificationKeys"] = "true",
            ["Circuits:AllowIncompleteVerification"] = "false",
        });

        verifier.IsVersionSupported("omega-v9.9.9").Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithApprovedVersionAndPlaceholderKeys_CanLoadApprovedServerArtifactSet()
    {
        var verifier = CreateVerifier(new Dictionary<string, string?>
        {
            ["Circuits:CurrentVersion"] = "omega-v1.0.0",
            ["Circuits:Supported:0"] = "omega-v1.0.0",
            ["Circuits:AllowPlaceholderVerificationKeys"] = "true",
            ["Circuits:AllowIncompleteVerification"] = "false",
        });

        verifier.IsVersionSupported("omega-v1.0.0").Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithApprovedVersionAndRealVerificationKey_LoadsWithoutPlaceholderMode()
    {
        var vkPath = Path.Combine(
            AppContext.BaseDirectory,
            "circuits",
            "omega-v1.0.0",
            "verification_key.json");

        Directory.CreateDirectory(Path.GetDirectoryName(vkPath)!);
        File.WriteAllText(vkPath, CreateMinimalSnarkJsVerificationKeyJson(icCount: 31));

        try
        {
            var verifier = CreateVerifier(new Dictionary<string, string?>
            {
                ["Circuits:CurrentVersion"] = "omega-v1.0.0",
                ["Circuits:Supported:0"] = "omega-v1.0.0",
                ["Circuits:AllowPlaceholderVerificationKeys"] = "false",
                ["Circuits:AllowIncompleteVerification"] = "false",
            });

            verifier.IsVersionSupported("omega-v1.0.0").Should().BeTrue();
        }
        finally
        {
            if (File.Exists(vkPath))
            {
                File.Delete(vkPath);
            }
        }
    }

    [Fact]
    public async Task VerifyAsync_WithPlaceholderKeysButIncompleteVerificationDisabled_FailsClosed()
    {
        var verifier = CreateVerifier(new Dictionary<string, string?>
        {
            ["Circuits:CurrentVersion"] = "omega-v1.0.0",
            ["Circuits:Supported:0"] = "omega-v1.0.0",
            ["Circuits:AllowPlaceholderVerificationKeys"] = "true",
            ["Circuits:AllowIncompleteVerification"] = "false",
        });

        var result = await verifier.VerifyAsync(
            CreateStructurallyValidProof(),
            CreatePublicInputs(),
            "omega-v1.0.0");

        result.Valid.Should().BeFalse();
        result.Error.Should().Be("INVALID_PROOF");
    }

    [Fact]
    public async Task VerifyAsync_WithExplicitLegacyFlags_CanUseLegacyStructuralFallback()
    {
        var verifier = CreateVerifier(new Dictionary<string, string?>
        {
            ["Circuits:CurrentVersion"] = "omega-v1.0.0",
            ["Circuits:Supported:0"] = "omega-v1.0.0",
            ["Circuits:AllowPlaceholderVerificationKeys"] = "true",
            ["Circuits:AllowIncompleteVerification"] = "true",
        });

        var result = await verifier.VerifyAsync(
            CreateStructurallyValidProof(),
            CreatePublicInputs(),
            "omega-v1.0.0");

        result.Valid.Should().BeTrue();
    }

    private static Groth16Verifier CreateVerifier(IDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new Groth16Verifier(
            configuration,
            new BabyJubJubCurve(),
            Mock.Of<ILogger<Groth16Verifier>>());
    }

    private static byte[] CreateStructurallyValidProof()
    {
        var curve = new BabyJubJubCurve();
        var pointBytes = curve.Generator.ToBytes();
        var proofBytes = new byte[256];

        Buffer.BlockCopy(pointBytes, 0, proofBytes, 0, 64);
        Buffer.BlockCopy(pointBytes, 0, proofBytes, 64, 64);
        Buffer.BlockCopy(pointBytes, 0, proofBytes, 128, 64);
        Buffer.BlockCopy(pointBytes, 0, proofBytes, 192, 64);

        return proofBytes;
    }

    private static PublicInputs CreatePublicInputs()
    {
        var curve = new BabyJubJubCurve();
        var point = curve.Generator;

        return new PublicInputs
        {
            Nullifier = Bytes32(1),
            CiphertextC1 = Enumerable.Range(0, 6).Select(_ => point).ToArray(),
            CiphertextC2 = Enumerable.Range(0, 6).Select(_ => point).ToArray(),
            MessageId = Bytes32(2),
            FeedId = Bytes32(5),
            FeedPk = point,
            MembersRoot = Bytes32(3),
            AuthorCommitment = new BigInteger(4),
        };
    }

    private static byte[] Bytes32(byte value)
    {
        var bytes = new byte[32];
        bytes[31] = value;
        return bytes;
    }

    private static string CreateMinimalSnarkJsVerificationKeyJson(int icCount)
    {
        static string G1() => """["1","2","1"]""";
        static string G2() => """[["1","2"],["3","4"],["1","0"]]""";

        var ic = string.Join(",", Enumerable.Range(0, icCount).Select(_ => G1()));

        return $$"""
        {
          "protocol": "groth16",
          "curve": "bn128",
          "vk_alpha_1": {{G1()}},
          "vk_beta_2": {{G2()}},
          "vk_gamma_2": {{G2()}},
          "vk_delta_2": {{G2()}},
          "IC": [{{ic}}]
        }
        """;
    }
}
