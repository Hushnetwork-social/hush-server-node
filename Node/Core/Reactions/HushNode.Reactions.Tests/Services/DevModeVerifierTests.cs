using FluentAssertions;
using HushNode.Reactions.Crypto;
using HushNode.Reactions.ZK;
using Microsoft.Extensions.Logging;
using Moq;
using System.Numerics;
using Xunit;

namespace HushNode.Reactions.Tests.Services;

public class DevModeVerifierTests
{
    [Fact]
    public async Task VerifyAsync_WithExpectedDevVersion_AcceptsProof()
    {
        var verifier = new DevModeVerifier(Mock.Of<ILogger<DevModeVerifier>>());

        var result = await verifier.VerifyAsync(new byte[32], CreatePublicInputs(), "dev-mode-v1");

        result.Valid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_WithUnexpectedVersion_FailsClosed()
    {
        var verifier = new DevModeVerifier(Mock.Of<ILogger<DevModeVerifier>>());

        var result = await verifier.VerifyAsync(new byte[32], CreatePublicInputs(), "omega-v1.0.0");

        result.Valid.Should().BeFalse();
        result.Error.Should().Be("UNSUPPORTED_DEV_CIRCUIT_VERSION");
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
}
