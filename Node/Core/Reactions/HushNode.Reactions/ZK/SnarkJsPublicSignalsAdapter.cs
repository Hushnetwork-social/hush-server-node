using System.Numerics;

namespace HushNode.Reactions.ZK;

public static class SnarkJsPublicSignalsAdapter
{
    public static string[] Serialize(PublicInputs inputs)
    {
        var publicInputs = new List<string>
        {
            ToUnsignedDecimal(inputs.Nullifier)
        };

        for (var i = 0; i < 6; i++)
        {
            publicInputs.Add(inputs.CiphertextC1[i].X.ToString());
            publicInputs.Add(inputs.CiphertextC1[i].Y.ToString());
        }

        for (var i = 0; i < 6; i++)
        {
            publicInputs.Add(inputs.CiphertextC2[i].X.ToString());
            publicInputs.Add(inputs.CiphertextC2[i].Y.ToString());
        }

        publicInputs.Add(ToUnsignedDecimal(inputs.MessageId));
        publicInputs.Add(ToUnsignedDecimal(inputs.FeedId));
        publicInputs.Add(inputs.FeedPk.X.ToString());
        publicInputs.Add(inputs.FeedPk.Y.ToString());
        publicInputs.Add(ToUnsignedDecimal(inputs.MembersRoot));
        publicInputs.Add(inputs.AuthorCommitment.ToString());

        return publicInputs.ToArray();
    }

    private static string ToUnsignedDecimal(byte[] bytes) =>
        new BigInteger(bytes, isUnsigned: true, isBigEndian: true).ToString();
}
