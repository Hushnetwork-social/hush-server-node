using System.Security.Cryptography;
using System.Text;
using HushNode.Credentials;
using HushShared.Elections.Model;
using Olimpo;

namespace HushNode.Elections;

public interface ICloseCountingExecutorEnvelopeCrypto
{
    string SealAlgorithm { get; }

    string? SealedByServiceIdentity { get; }

    bool IsAvailable(out string error);

    string SealPrivateKey(string privateKey);

    string? TryUnsealPrivateKey(
        ElectionExecutorSessionKeyEnvelopeRecord envelope,
        out string error);
}

public static class CloseCountingExecutorEnvelopeCryptoConstants
{
    public const string LegacyNodeEncryptAddressSealAlgorithm = "node-encrypt-address-v1";
}

public sealed class WindowsDpapiCloseCountingExecutorEnvelopeCrypto : ICloseCountingExecutorEnvelopeCrypto
{
    private static readonly byte[] PurposeBytes =
        Encoding.UTF8.GetBytes("hush:elections:close-counting-executor-session-key:v1");

    public string SealAlgorithm => "windows-dpapi-current-user-v1";

    public string? SealedByServiceIdentity =>
        $"{Environment.UserDomainName}\\{Environment.UserName}@{Environment.MachineName}";

    public bool IsAvailable(out string error)
    {
        if (!OperatingSystem.IsWindows())
        {
            error = "Trustee close-counting custody requires an OS-protected envelope provider; Windows DPAPI is unavailable on this platform.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public string SealPrivateKey(string privateKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKey);

        if (!IsAvailable(out var error))
        {
            throw new InvalidOperationException(error);
        }

        var plaintextBytes = Encoding.UTF8.GetBytes(privateKey.Trim());
        var protectedBytes = ProtectedData.Protect(
            plaintextBytes,
            optionalEntropy: PurposeBytes,
            scope: DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public string? TryUnsealPrivateKey(
        ElectionExecutorSessionKeyEnvelopeRecord envelope,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!IsAvailable(out error))
        {
            return null;
        }

        if (!string.Equals(envelope.SealAlgorithm, SealAlgorithm, StringComparison.Ordinal))
        {
            error = $"Seal algorithm mismatch. Expected {SealAlgorithm} but found {envelope.SealAlgorithm}.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(envelope.SealedExecutorSessionPrivateKey) ||
            string.Equals(
                envelope.SealedExecutorSessionPrivateKey,
                CloseCountingExecutorKeyRegistryConstants.DestroyedEnvelopeMarker,
                StringComparison.Ordinal) ||
            string.Equals(
                envelope.SealedExecutorSessionPrivateKey,
                CloseCountingExecutorKeyRegistryConstants.MemoryOnlyEnvelopeMarker,
                StringComparison.Ordinal))
        {
            error = "The close-counting executor envelope no longer contains a sealed private key.";
            return null;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(envelope.SealedExecutorSessionPrivateKey);
            var plaintextBytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: PurposeBytes,
                scope: DataProtectionScope.CurrentUser);
            error = string.Empty;
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (Exception ex) when (
            ex is FormatException or CryptographicException or PlatformNotSupportedException)
        {
            error = "Failed to unseal the trustee close-counting executor envelope with the current Windows user context.";
            return null;
        }
    }
}

public sealed class UnavailableCloseCountingExecutorEnvelopeCrypto : ICloseCountingExecutorEnvelopeCrypto
{
    public string SealAlgorithm => "unavailable";

    public string? SealedByServiceIdentity => null;

    public bool IsAvailable(out string error)
    {
        error = "Trustee close-counting custody is unavailable in this deployment because no OS-protected envelope provider is configured.";
        return false;
    }

    public string SealPrivateKey(string privateKey) =>
        throw new InvalidOperationException("Trustee close-counting custody is unavailable in this deployment.");

    public string? TryUnsealPrivateKey(
        ElectionExecutorSessionKeyEnvelopeRecord envelope,
        out string error)
    {
        error = "Trustee close-counting custody is unavailable in this deployment.";
        return null;
    }
}

public sealed class TransparentTestCloseCountingExecutorEnvelopeCrypto : ICloseCountingExecutorEnvelopeCrypto
{
    public string SealAlgorithm => "test-transparent-envelope-v1";

    public string? SealedByServiceIdentity => "test-host";

    public bool IsAvailable(out string error)
    {
        error = string.Empty;
        return true;
    }

    public string SealPrivateKey(string privateKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKey);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(privateKey.Trim()));
    }

    public string? TryUnsealPrivateKey(
        ElectionExecutorSessionKeyEnvelopeRecord envelope,
        out string error)
    {
        if (!string.Equals(envelope.SealAlgorithm, SealAlgorithm, StringComparison.Ordinal))
        {
            error = $"Seal algorithm mismatch. Expected {SealAlgorithm} but found {envelope.SealAlgorithm}.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(envelope.SealedExecutorSessionPrivateKey) ||
            string.Equals(
                envelope.SealedExecutorSessionPrivateKey,
                CloseCountingExecutorKeyRegistryConstants.DestroyedEnvelopeMarker,
                StringComparison.Ordinal) ||
            string.Equals(
                envelope.SealedExecutorSessionPrivateKey,
                CloseCountingExecutorKeyRegistryConstants.MemoryOnlyEnvelopeMarker,
                StringComparison.Ordinal))
        {
            error = "The test executor envelope no longer contains a sealed private key.";
            return null;
        }

        try
        {
            error = string.Empty;
            return Encoding.UTF8.GetString(Convert.FromBase64String(envelope.SealedExecutorSessionPrivateKey));
        }
        catch (FormatException)
        {
            error = "The test executor envelope payload is not valid base64.";
            return null;
        }
    }
}

public static class LegacyNodeIdentityCloseCountingExecutorEnvelopeCrypto
{
    public static bool RequiresLegacyNodeKeyFallback(ElectionExecutorSessionKeyEnvelopeRecord envelope) =>
        string.IsNullOrWhiteSpace(envelope.SealAlgorithm) ||
        string.Equals(
            envelope.SealAlgorithm,
            CloseCountingExecutorEnvelopeCryptoConstants.LegacyNodeEncryptAddressSealAlgorithm,
            StringComparison.Ordinal);

    public static string SealPrivateKey(
        string privateKey,
        ICredentialsProvider credentialsProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKey);
        ArgumentNullException.ThrowIfNull(credentialsProvider);

        var credentials = credentialsProvider.GetCredentials();
        if (string.IsNullOrWhiteSpace(credentials.PublicEncryptAddress))
        {
            throw new InvalidOperationException("The local node is missing a public encryption address for sealing executor keys.");
        }

        return EncryptKeys.Encrypt(privateKey.Trim(), credentials.PublicEncryptAddress);
    }

    public static string? TryUnsealPrivateKey(
        ElectionExecutorSessionKeyEnvelopeRecord envelope,
        ICredentialsProvider? credentialsProvider,
        out string error)
    {
        if (!RequiresLegacyNodeKeyFallback(envelope))
        {
            error = $"Legacy node-key fallback only supports blank or {CloseCountingExecutorEnvelopeCryptoConstants.LegacyNodeEncryptAddressSealAlgorithm} seal algorithms.";
            return null;
        }

        if (credentialsProvider is null)
        {
            error = "Legacy node-key fallback requires node credentials.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(envelope.SealedExecutorSessionPrivateKey) ||
            string.Equals(
                envelope.SealedExecutorSessionPrivateKey,
                CloseCountingExecutorKeyRegistryConstants.DestroyedEnvelopeMarker,
                StringComparison.Ordinal) ||
            string.Equals(
                envelope.SealedExecutorSessionPrivateKey,
                CloseCountingExecutorKeyRegistryConstants.MemoryOnlyEnvelopeMarker,
                StringComparison.Ordinal))
        {
            error = "The legacy close-counting executor envelope no longer contains a sealed private key.";
            return null;
        }

        var credentials = credentialsProvider.GetCredentials();
        if (!string.IsNullOrWhiteSpace(envelope.SealedByServiceIdentity) &&
            !string.Equals(
                envelope.SealedByServiceIdentity,
                credentials.PublicSigningAddress,
                StringComparison.Ordinal))
        {
            error = "Legacy close-counting executor envelope identity does not match the current node credentials.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(credentials.PrivateEncryptKey))
        {
            error = "Legacy close-counting executor envelope requires the node private encryption key.";
            return null;
        }

        try
        {
            error = string.Empty;
            return EncryptKeys.Decrypt(
                envelope.SealedExecutorSessionPrivateKey,
                credentials.PrivateEncryptKey);
        }
        catch
        {
            error = "Legacy close-counting executor envelope could not be decrypted with the current node credentials.";
            return null;
        }
    }
}
