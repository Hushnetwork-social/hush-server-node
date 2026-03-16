using FluentAssertions;
using HushNode.Reactions.ZK;
using Xunit;

namespace HushNode.Reactions.Tests.Services;

public sealed class PackedGroth16ProofAdapterTests
{
    [Fact]
    public void Unpack_WithPackedProof_MapsSnarkJsShape()
    {
        var proofBytes = BuildProofBytes(1, 2, 3, 4, 5, 6, 7, 8);

        var proof = PackedGroth16ProofAdapter.Unpack(proofBytes);

        proof.PiA.Should().Equal("1", "2", "1");
        proof.PiB[0].Should().Equal("3", "4");
        proof.PiB[1].Should().Equal("5", "6");
        proof.PiB[2].Should().Equal("1", "0");
        proof.PiC.Should().Equal("7", "8", "1");
        proof.Protocol.Should().Be("groth16");
        proof.Curve.Should().Be("bn128");
    }

    private static byte[] BuildProofBytes(params byte[] values)
    {
        var buffer = new byte[256];
        for (var i = 0; i < values.Length; i++)
        {
            buffer[(i * 32) + 31] = values[i];
        }

        return buffer;
    }
}
