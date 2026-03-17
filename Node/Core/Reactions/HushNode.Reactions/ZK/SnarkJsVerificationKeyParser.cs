using System.Numerics;
using System.Text.Json;

namespace HushNode.Reactions.ZK;

public static class SnarkJsVerificationKeyParser
{
    public static VerificationKey Parse(string json, string version, string? sourcePath = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new VerificationKey
        {
            Version = version,
            Alpha = ParseG1(root, "vk_alpha_1"),
            Beta = ParseG2(root, "vk_beta_2"),
            Gamma = ParseG2(root, "vk_gamma_2"),
            Delta = ParseG2(root, "vk_delta_2"),
            IC = ParseIc(root),
            SourcePath = sourcePath
        };
    }

    private static ECPoint ParseG1(JsonElement root, string propertyName)
    {
        var point = root.GetProperty(propertyName);
        if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() < 2)
        {
            throw new InvalidOperationException($"'{propertyName}' must be a snarkjs G1 array.");
        }

        return new ECPoint(ParseBigInteger(point[0]), ParseBigInteger(point[1]));
    }

    private static ECPoint[] ParseG2(JsonElement root, string propertyName)
    {
        var point = root.GetProperty(propertyName);
        if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() < 2)
        {
            throw new InvalidOperationException($"'{propertyName}' must be a snarkjs G2 array.");
        }

        return new[]
        {
            ParseG2Limb(point[0], $"{propertyName}[0]"),
            ParseG2Limb(point[1], $"{propertyName}[1]")
        };
    }

    private static ECPoint[] ParseIc(JsonElement root)
    {
        var ic = root.GetProperty("IC");
        if (ic.ValueKind != JsonValueKind.Array || ic.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("'IC' must contain at least one G1 point.");
        }

        return ic.EnumerateArray()
            .Select((point, index) =>
            {
                if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() < 2)
                {
                    throw new InvalidOperationException($"'IC[{index}]' must be a snarkjs G1 array.");
                }

                return new ECPoint(ParseBigInteger(point[0]), ParseBigInteger(point[1]));
            })
            .ToArray();
    }

    private static ECPoint ParseG2Limb(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() < 2)
        {
            throw new InvalidOperationException($"'{path}' must be a snarkjs Fq2 limb array.");
        }

        return new ECPoint(ParseBigInteger(element[0]), ParseBigInteger(element[1]));
    }

    private static BigInteger ParseBigInteger(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => BigInteger.Parse(element.GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.Number => BigInteger.Parse(element.GetRawText(), System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Expected numeric JSON value but got {element.ValueKind}.")
        };
    }
}
