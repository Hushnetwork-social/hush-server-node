using System.Numerics;
using System.Text.Json;

namespace HushNode.Reactions.Crypto;

public class PoseidonHash : IPoseidonHash
{
    private static readonly BigInteger P = BigInteger.Parse(
        "21888242871839275222246405745257275088548364400416034343698204186575808495617");
    private const int FullRounds = 8;
    private static readonly int[] PartialRounds = [56, 57, 56, 60];
    private static readonly PoseidonParameters[] Parameters = LoadParameters();

    public PoseidonHash() { }

    public BigInteger Hash(params BigInteger[] inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.Length is < 1 or > 4)
        {
            throw new ArgumentException($"Unsupported input count: {inputs.Length}. Expected 1-4 inputs.");
        }

        return HashInternal(inputs);
    }

    public BigInteger Hash2(BigInteger a, BigInteger b)
    {
        return HashInternal([a, b]);
    }

    public BigInteger Hash4(BigInteger a, BigInteger b, BigInteger c, BigInteger d)
    {
        return HashInternal([a, b, c, d]);
    }

    private static BigInteger HashInternal(ReadOnlySpan<BigInteger> inputs)
    {
        var parameters = Parameters[inputs.Length - 1];
        var state = new BigInteger[parameters.StateWidth];
        state[0] = BigInteger.Zero;

        for (var i = 0; i < inputs.Length; i++)
        {
            state[i + 1] = Mod(inputs[i]);
        }

        Permute(state, parameters);
        return state[0];
    }

    private static void Permute(BigInteger[] state, PoseidonParameters parameters)
    {
        var t = state.Length;
        var halfFullRounds = FullRounds / 2;
        var c = parameters.RoundConstants;

        for (var i = 0; i < t; i++)
        {
            state[i] = Mod(state[i] + c[i]);
        }

        for (var round = 0; round < halfFullRounds - 1; round++)
        {
            ApplySBoxToAll(state);
            for (var i = 0; i < t; i++)
            {
                state[i] = Mod(state[i] + c[(round + 1) * t + i]);
            }
            MultiplyByMatrix(state, parameters.MMatrix);
        }

        ApplySBoxToAll(state);
        for (var i = 0; i < t; i++)
        {
            state[i] = Mod(state[i] + c[halfFullRounds * t + i]);
        }
        MultiplyByMatrix(state, parameters.PMatrix);

        for (var round = 0; round < parameters.PartialRounds; round++)
        {
            state[0] = SBox(state[0]);
            state[0] = Mod(state[0] + c[(halfFullRounds + 1) * t + round]);

            var s0 = BigInteger.Zero;
            for (var j = 0; j < t; j++)
            {
                s0 = Mod(s0 + Mod(parameters.SVector[(t * 2 - 1) * round + j] * state[j]));
            }

            var state0 = state[0];
            for (var k = 1; k < t; k++)
            {
                state[k] = Mod(state[k] + Mod(state0 * parameters.SVector[(t * 2 - 1) * round + t + k - 1]));
            }
            state[0] = s0;
        }

        for (var round = 0; round < halfFullRounds - 1; round++)
        {
            ApplySBoxToAll(state);
            for (var i = 0; i < t; i++)
            {
                state[i] = Mod(state[i] + c[(halfFullRounds + 1) * t + parameters.PartialRounds + round * t + i]);
            }
            MultiplyByMatrix(state, parameters.MMatrix);
        }

        ApplySBoxToAll(state);
        MultiplyByMatrix(state, parameters.MMatrix);
    }

    private static BigInteger SBox(BigInteger x)
    {
        var x2 = Mod(x * x);
        var x4 = Mod(x2 * x2);
        return Mod(x4 * x);
    }

    private static void ApplySBoxToAll(BigInteger[] state)
    {
        for (var i = 0; i < state.Length; i++)
        {
            state[i] = SBox(state[i]);
        }
    }

    private static void MultiplyByMatrix(BigInteger[] state, BigInteger[][] matrix)
    {
        var t = state.Length;
        var newState = new BigInteger[t];

        for (var i = 0; i < t; i++)
        {
            var acc = BigInteger.Zero;
            for (var j = 0; j < t; j++)
            {
                acc = Mod(acc + Mod(matrix[j][i] * state[j]));
            }
            newState[i] = acc;
        }

        Array.Copy(newState, state, t);
    }

    private static BigInteger Mod(BigInteger a)
    {
        var result = a % P;
        return result < 0 ? result + P : result;
    }

    private static PoseidonParameters[] LoadParameters()
    {
        using var stream = typeof(PoseidonHash).Assembly.GetManifestResourceStream(
            "HushNode.Reactions.Crypto.poseidon_constants.circomlibjs.json");

        if (stream is null)
        {
            throw new InvalidOperationException("Embedded Poseidon constants resource was not found.");
        }

        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("C", out var cElement) ||
            !document.RootElement.TryGetProperty("M", out var mElement) ||
            !document.RootElement.TryGetProperty("P", out var pElement) ||
            !document.RootElement.TryGetProperty("S", out var sElement) ||
            cElement.GetArrayLength() < 4 ||
            mElement.GetArrayLength() < 4 ||
            pElement.GetArrayLength() < 4 ||
            sElement.GetArrayLength() < 4)
        {
            throw new InvalidOperationException("Embedded Poseidon constants resource is invalid.");
        }

        var parameters = new PoseidonParameters[4];
        for (var i = 0; i < parameters.Length; i++)
        {
            var roundConstants = cElement[i].EnumerateArray()
                .Select(item => ParseFieldElement(item.GetString() ?? throw new InvalidOperationException("Missing Poseidon round constant.")))
                .ToArray();
            var mMatrix = mElement[i].EnumerateArray()
                .Select(row => row.EnumerateArray()
                    .Select(item => ParseFieldElement(item.GetString() ?? throw new InvalidOperationException("Missing Poseidon M value.")))
                    .ToArray())
                .ToArray();
            var pMatrix = pElement[i].EnumerateArray()
                .Select(row => row.EnumerateArray()
                    .Select(item => ParseFieldElement(item.GetString() ?? throw new InvalidOperationException("Missing Poseidon P value.")))
                    .ToArray())
                .ToArray();
            var sVector = sElement[i].EnumerateArray()
                .Select(item => ParseFieldElement(item.GetString() ?? throw new InvalidOperationException("Missing Poseidon S value.")))
                .ToArray();

            parameters[i] = new PoseidonParameters(
                i + 2,
                PartialRounds[i],
                roundConstants,
                mMatrix,
                pMatrix,
                sVector);
        }

        return parameters;
    }

    private static BigInteger ParseFieldElement(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var hex = value[2..];
            if (hex.Length % 2 != 0)
            {
                hex = "0" + hex;
            }
            return new BigInteger(Convert.FromHexString(hex), isUnsigned: true, isBigEndian: true);
        }

        return BigInteger.Parse(value);
    }

    private sealed record PoseidonParameters(
        int StateWidth,
        int PartialRounds,
        BigInteger[] RoundConstants,
        BigInteger[][] MMatrix,
        BigInteger[][] PMatrix,
        BigInteger[] SVector);
}
