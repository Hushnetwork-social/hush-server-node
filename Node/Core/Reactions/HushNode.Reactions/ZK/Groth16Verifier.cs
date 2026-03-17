using System.Numerics;
using HushNode.Reactions.Crypto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace HushNode.Reactions.ZK;

/// <summary>
/// Groth16 ZK proof verifier for Protocol Omega reaction proofs.
/// </summary>
public class Groth16Verifier : IZkVerifier
{
    private readonly Dictionary<string, VerificationKey> _verificationKeys = new();
    private readonly HashSet<string> _deprecatedVersions = new();
    private readonly HashSet<string> _vulnerableVersions = new();
    private readonly string _currentVersion;
    private readonly bool _allowPlaceholderVerificationKeys;
    private readonly bool _allowIncompleteVerification;
    private readonly string _nodeExecutable;
    private readonly string _verifierScriptPath;
    private readonly int _processTimeoutMs;
    private readonly IBabyJubJub _curve;
    private readonly ILogger<Groth16Verifier> _logger;

    public Groth16Verifier(
        IConfiguration config,
        IBabyJubJub curve,
        ILogger<Groth16Verifier> logger)
    {
        _curve = curve;
        _logger = logger;
        _currentVersion = config["Circuits:CurrentVersion"] ?? "omega-v1.0.0";
        _allowPlaceholderVerificationKeys = config.GetValue<bool>("Circuits:AllowPlaceholderVerificationKeys", false);
        _allowIncompleteVerification = config.GetValue<bool>("Circuits:AllowIncompleteVerification", false);
        _nodeExecutable = config["Circuits:NodeExecutable"] ?? "node";
        _verifierScriptPath = config["Circuits:SnarkJsVerifierScriptPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "ZK", "verify-groth16.mjs");
        _processTimeoutMs = config.GetValue<int?>("Circuits:SnarkJsVerifyTimeoutMs") ?? 15000;

        // Load supported verification keys
        var supported = config.GetSection("Circuits:Supported").Get<string[]>() ?? new[] { _currentVersion };
        foreach (var version in supported)
        {
            if (!ReactionCircuitArtifactsManifest.IsApproved(version))
            {
                _logger.LogWarning(
                    "Skipping unsupported circuit version {Version} because it is not part of the approved FEAT-087 server artifact set.",
                    version);
                continue;
            }

            try
            {
                var vk = LoadVerificationKey(version);
                if (vk != null)
                {
                    _verificationKeys[version] = vk;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load verification key for version {Version}", version);
            }
        }

        // Load deprecated versions
        var deprecated = config.GetSection("Circuits:Deprecated").Get<string[]>() ?? Array.Empty<string>();
        foreach (var version in deprecated)
        {
            _deprecatedVersions.Add(version);
        }

        // Known vulnerable versions that should be rejected
        var vulnerable = config.GetSection("Circuits:Vulnerable").Get<string[]>() ?? Array.Empty<string>();
        foreach (var version in vulnerable)
        {
            _vulnerableVersions.Add(version);
        }

        _logger.LogInformation(
            "Groth16Verifier initialized with {Count} circuit versions. Current: {Current}. PlaceholderKeys={PlaceholderKeys}, IncompleteVerification={IncompleteVerification}, VerifyTimeoutMs={VerifyTimeoutMs}",
            _verificationKeys.Count,
            _currentVersion,
            _allowPlaceholderVerificationKeys,
            _allowIncompleteVerification,
            _processTimeoutMs);
    }

    public async Task<VerifyResult> VerifyAsync(
        byte[] proof,
        PublicInputs inputs,
        string circuitVersion)
    {
        // Check for vulnerable versions first
        if (_vulnerableVersions.Contains(circuitVersion))
        {
            return VerifyResult.Failure(
                "VULNERABLE_CIRCUIT_VERSION",
                $"Circuit version '{circuitVersion}' has known vulnerabilities and is no longer accepted.");
        }

        // Check if version is known
        if (!_verificationKeys.TryGetValue(circuitVersion, out var vk))
        {
            return VerifyResult.Failure(
                "UNKNOWN_CIRCUIT_VERSION",
                $"Circuit version '{circuitVersion}' is not supported. Use '{_currentVersion}'.");
        }

        try
        {
            // Parse the Groth16 proof
            var parsedProof = ParseProof(proof);
            if (parsedProof == null)
            {
                return VerifyResult.Failure("INVALID_PROOF_FORMAT", "Failed to parse proof bytes.");
            }

            // Prepare public inputs as field elements
            var publicInputs = PreparePublicInputs(inputs);
            var serializedSignals = SnarkJsPublicSignalsAdapter.Serialize(ToPublicInputs(publicInputs));

            // Perform Groth16 verification
            var isValid = await VerifyGroth16Async(parsedProof, publicInputs, serializedSignals, vk);

            if (!isValid)
            {
                return VerifyResult.Failure("INVALID_PROOF", "Proof verification failed.");
            }

            // Check for deprecation warning
            if (_deprecatedVersions.Contains(circuitVersion))
            {
                return VerifyResult.SuccessWithWarning(
                    $"Circuit version '{circuitVersion}' is deprecated. Please update to '{_currentVersion}'.");
            }

            return VerifyResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during proof verification");
            return VerifyResult.Failure("VERIFICATION_ERROR", $"Verification error: {ex.Message}");
        }
    }

    public string GetCurrentVersion() => _currentVersion;

    public bool IsVersionSupported(string version) => _verificationKeys.ContainsKey(version);

    public bool IsVulnerableVersion(string version) => _vulnerableVersions.Contains(version);

    private Groth16Proof? ParseProof(byte[] proofBytes)
    {
        // Groth16 proof consists of 3 curve points:
        // - A (G1): 64 bytes
        // - B (G2): 128 bytes (2 field elements)
        // - C (G1): 64 bytes
        // Total: 256 bytes

        if (proofBytes.Length < 256)
        {
            _logger.LogWarning("Proof too short: {Length} bytes (expected 256)", proofBytes.Length);
            return null;
        }

        try
        {
            var a = ECPoint.FromBytes(proofBytes[..64]);
            // B is a G2 point - for simplicity, we store it as 2 G1 points
            var b1 = ECPoint.FromBytes(proofBytes[64..128]);
            var b2 = ECPoint.FromBytes(proofBytes[128..192]);
            var c = ECPoint.FromBytes(proofBytes[192..256]);

            return new Groth16Proof
            {
                A = a,
                B = new[] { b1, b2 },
                C = c
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse proof points");
            return null;
        }
    }

    private BigInteger[] PreparePublicInputs(PublicInputs inputs)
    {
        var publicInputs = new List<BigInteger>();

        // Nullifier
        publicInputs.Add(new BigInteger(inputs.Nullifier, isUnsigned: true, isBigEndian: true));

        // Ciphertexts (6 C1 points + 6 C2 points = 24 field elements)
        for (int i = 0; i < 6; i++)
        {
            publicInputs.Add(inputs.CiphertextC1[i].X);
            publicInputs.Add(inputs.CiphertextC1[i].Y);
        }
        for (int i = 0; i < 6; i++)
        {
            publicInputs.Add(inputs.CiphertextC2[i].X);
            publicInputs.Add(inputs.CiphertextC2[i].Y);
        }

        // Remaining public inputs match the circuit's output order.
        publicInputs.Add(new BigInteger(inputs.MessageId, isUnsigned: true, isBigEndian: true));
        publicInputs.Add(new BigInteger(inputs.FeedId, isUnsigned: true, isBigEndian: true));
        publicInputs.Add(inputs.FeedPk.X);
        publicInputs.Add(inputs.FeedPk.Y);
        publicInputs.Add(new BigInteger(inputs.MembersRoot, isUnsigned: true, isBigEndian: true));
        publicInputs.Add(inputs.AuthorCommitment);

        return publicInputs.ToArray();
    }

    private async Task<bool> VerifyGroth16Async(
        Groth16Proof proof,
        BigInteger[] publicInputs,
        string[] serializedSignals,
        VerificationKey vk)
    {
        if (!_allowIncompleteVerification)
        {
            if (string.IsNullOrWhiteSpace(vk.SourcePath))
            {
                _logger.LogError("Groth16 verification rejected because no verification key source path is available.");
                await Task.CompletedTask;
                return false;
            }

            try
            {
                var verificationStopwatch = Stopwatch.StartNew();
                _logger.LogInformation(
                    "[Groth16Verifier] Prepared public signals for circuit {CircuitVersion}: {PublicSignals}",
                    vk.Version,
                    JsonSerializer.Serialize(serializedSignals));
                _logger.LogInformation(
                    "Starting snarkjs Groth16 verification. CircuitVersion={CircuitVersion}, VerificationKeyPath={VerificationKeyPath}, NodeExecutable={NodeExecutable}, VerifierScriptPath={VerifierScriptPath}, PublicSignalCount={PublicSignalCount}, ProcessTimeoutMs={ProcessTimeoutMs}",
                    vk.Version,
                    vk.SourcePath,
                    _nodeExecutable,
                    _verifierScriptPath,
                    serializedSignals.Length,
                    _processTimeoutMs);

                var isValid = await SnarkJsProcessGroth16Verifier.VerifyAsync(
                    SerializePackedProof(proof),
                    serializedSignals,
                    vk.SourcePath,
                    _nodeExecutable,
                    _verifierScriptPath,
                    _processTimeoutMs,
                    onLog: message => _logger.LogInformation("[Groth16Verifier] snarkjs: {Message}", message));
                verificationStopwatch.Stop();
                _logger.LogInformation(
                    "Completed snarkjs Groth16 verification. CircuitVersion={CircuitVersion}, Valid={Valid}, ElapsedMs={ElapsedMs}",
                    vk.Version,
                    isValid,
                    verificationStopwatch.ElapsedMilliseconds);
                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Groth16 verification failed via snarkjs process path.");
                return false;
            }
        }

        // Groth16 verification equation:
        // e(A, B) = e(α, β) · e(∑(pub_i · IC_i), γ) · e(C, δ)
        //
        // This requires pairing operations on BN254/BN128 curve.
        // For full implementation, we would need:
        // 1. BN254 pairing implementation
        // 2. G1 and G2 point arithmetic
        // 3. Final exponentiation

        // TODO: Implement actual Groth16 pairing verification
        // For now, we perform basic validation checks

        // Verify all proof points are on the curve
        if (!_curve.IsOnCurve(proof.A) || !_curve.IsOnCurve(proof.C))
        {
            _logger.LogWarning("Proof points not on curve");
            return false;
        }

        // Verify IC count matches public inputs + 1
        if (vk.IC.Length != publicInputs.Length + 1)
        {
            _logger.LogWarning(
                "IC count mismatch: expected {Expected}, got {Actual}",
                publicInputs.Length + 1,
                vk.IC.Length);
            return false;
        }

        // Compute the linear combination of IC with public inputs
        // vk_x = IC[0] + ∑(pub_i · IC[i+1])
        var vkX = vk.IC[0];
        for (int i = 0; i < publicInputs.Length; i++)
        {
            var term = _curve.ScalarMul(vk.IC[i + 1], publicInputs[i]);
            vkX = _curve.Add(vkX, term);
        }

        // In a full implementation, we would now verify:
        // e(proof.A, proof.B) == e(vk.Alpha, vk.Beta) * e(vkX, vk.Gamma) * e(proof.C, vk.Delta)

        // For explicit non-production testing, allow the legacy structural fallback.
        await Task.CompletedTask;

        _logger.LogWarning("Proof structure validated, but pairing check was skipped because incomplete verification was explicitly enabled.");
        return true;
    }

    private VerificationKey? LoadVerificationKey(string version)
    {
        if (!ReactionCircuitArtifactsManifest.IsApproved(version))
        {
            _logger.LogWarning(
                "Refusing to load verification key for {Version} because it is not part of the approved FEAT-087 server artifact set.",
                version);
            return null;
        }

        var artifacts = ReactionCircuitArtifactsManifest.GetApproved(version);

        // Load verification key from circuits directory
        var vkPath = Path.Combine(AppContext.BaseDirectory, artifacts.VerificationKeyRelativePath);

        if (!File.Exists(vkPath))
        {
            _logger.LogDebug("Verification key not found at {Path}", vkPath);
            return _allowPlaceholderVerificationKeys ? CreatePlaceholderKey(version) : null;
        }

        var json = File.ReadAllText(vkPath);
        try
        {
            return SnarkJsVerificationKeyParser.Parse(json, version, vkPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Verification key file exists at {Path}, but parsing failed. " +
                "Set Circuits:AllowPlaceholderVerificationKeys=true only for explicit non-production testing.",
                vkPath);
            return _allowPlaceholderVerificationKeys ? CreatePlaceholderKey(version) : null;
        }
    }

    private VerificationKey CreatePlaceholderKey(string version)
    {
        // Create a placeholder verification key for development/testing
        // In production, this would be loaded from the circuit build
        return new VerificationKey
        {
            Version = version,
            Alpha = _curve.Generator,
            Beta = new[] { _curve.Generator, _curve.Generator },
            Gamma = new[] { _curve.Generator, _curve.Generator },
            Delta = new[] { _curve.Generator, _curve.Generator },
            // IC array size depends on number of public inputs.
            // For omega-v1.0.0:
            // nullifier (1), ciphertexts (24), messageId (1), feedId (1),
            // feedPk (2), merkleRoot (1), authorCommitment (1) = 31 elements + 1
            IC = Enumerable.Range(0, 32)
                .Select(_ => _curve.Generator)
                .ToArray()
        };
    }

    private static byte[] SerializePackedProof(Groth16Proof proof)
    {
        var bytes = new byte[256];
        var offset = 0;

        void WriteField(BigInteger value)
        {
            var fieldBytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
            if (fieldBytes.Length > 32)
            {
                throw new InvalidOperationException("Proof field does not fit in 32 bytes.");
            }

            var start = offset + (32 - fieldBytes.Length);
            Buffer.BlockCopy(fieldBytes, 0, bytes, start, fieldBytes.Length);
            offset += 32;
        }

        WriteField(proof.A.X);
        WriteField(proof.A.Y);
        WriteField(proof.B[0].X);
        WriteField(proof.B[0].Y);
        WriteField(proof.B[1].X);
        WriteField(proof.B[1].Y);
        WriteField(proof.C.X);
        WriteField(proof.C.Y);

        return bytes;
    }

    private static PublicInputs ToPublicInputs(BigInteger[] publicInputs)
    {
        if (publicInputs.Length != 31)
        {
            throw new InvalidOperationException($"Expected 31 public inputs, got {publicInputs.Length}.");
        }

        static byte[] ToBytes32(BigInteger value)
        {
            var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
            if (bytes.Length > 32)
            {
                throw new InvalidOperationException("Public input does not fit in 32 bytes.");
            }

            var buffer = new byte[32];
            Buffer.BlockCopy(bytes, 0, buffer, 32 - bytes.Length, bytes.Length);
            return buffer;
        }

        var cursor = 0;
        var nullifier = ToBytes32(publicInputs[cursor++]);

        var c1 = new ECPoint[6];
        for (var i = 0; i < 6; i++)
        {
            c1[i] = new ECPoint(publicInputs[cursor++], publicInputs[cursor++]);
        }

        var c2 = new ECPoint[6];
        for (var i = 0; i < 6; i++)
        {
            c2[i] = new ECPoint(publicInputs[cursor++], publicInputs[cursor++]);
        }

        var messageId = ToBytes32(publicInputs[cursor++]);
        var feedId = ToBytes32(publicInputs[cursor++]);
        var feedPk = new ECPoint(publicInputs[cursor++], publicInputs[cursor++]);
        var membersRoot = ToBytes32(publicInputs[cursor++]);
        var authorCommitment = publicInputs[cursor++];

        return new PublicInputs
        {
            Nullifier = nullifier,
            MessageId = messageId,
            FeedId = feedId,
            MembersRoot = membersRoot,
            AuthorCommitment = authorCommitment,
            FeedPk = feedPk,
            CiphertextC1 = c1,
            CiphertextC2 = c2
        };
    }
}

/// <summary>
/// Parsed Groth16 proof.
/// </summary>
internal class Groth16Proof
{
    public required ECPoint A { get; set; }
    public required ECPoint[] B { get; set; }
    public required ECPoint C { get; set; }
}
