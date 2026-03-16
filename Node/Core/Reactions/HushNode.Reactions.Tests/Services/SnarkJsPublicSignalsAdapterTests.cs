using FluentAssertions;
using HushNode.Reactions.Crypto;
using HushNode.Reactions.ZK;
using System.Numerics;
using Xunit;

namespace HushNode.Reactions.Tests.Services;

public sealed class SnarkJsPublicSignalsAdapterTests
{
    [Fact]
    public void Serialize_WithProtocolOmegaInputs_UsesExpectedSignalOrder()
    {
        var point = new BabyJubJubCurve().Generator;
        var inputs = new PublicInputs
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

        var signals = SnarkJsPublicSignalsAdapter.Serialize(inputs);

        signals.Should().HaveCount(31);
        signals[0].Should().Be("1");
        signals[1].Should().Be(point.X.ToString());
        signals[24].Should().Be(point.Y.ToString());
        signals[25].Should().Be("2");
        signals[26].Should().Be("5");
        signals[27].Should().Be(point.X.ToString());
        signals[28].Should().Be(point.Y.ToString());
        signals[29].Should().Be("3");
        signals[30].Should().Be("4");
    }

    private static byte[] Bytes32(byte value)
    {
        var bytes = new byte[32];
        bytes[31] = value;
        return bytes;
    }
}
