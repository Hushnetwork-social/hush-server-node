using System.Numerics;

namespace HushNode.Reactions.ZK;

public static class PackedGroth16ProofAdapter
{
    public static SnarkJsGroth16Proof Unpack(byte[] proofBytes)
    {
        if (proofBytes.Length < 256)
        {
            throw new InvalidOperationException($"Packed Groth16 proof must be 256 bytes, got {proofBytes.Length}.");
        }

        var offset = 0;
        string ReadField()
        {
            var value = new BigInteger(proofBytes.AsSpan(offset, 32), isUnsigned: true, isBigEndian: true);
            offset += 32;
            return value.ToString();
        }

        return new SnarkJsGroth16Proof(
            PiA: [ReadField(), ReadField(), "1"],
            PiB:
            [
                [ReadField(), ReadField()],
                [ReadField(), ReadField()],
                ["1", "0"]
            ],
            PiC: [ReadField(), ReadField(), "1"],
            Protocol: "groth16",
            Curve: "bn128");
    }
}
