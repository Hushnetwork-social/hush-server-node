using System.Security.Cryptography;
using System.Text;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IAdminOnlyProtectedTallyEnvelopeCrypto
{
    string SealAlgorithm { get; }

    string? SealedByServiceIdentity { get; }

    bool IsAvailable(out string error);

    string SealPrivateScalar(string privateScalar);

    string? TryUnsealPrivateScalar(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope,
        out string error);
}

public static class AdminOnlyProtectedTallyEnvelopeCryptoConstants
{
    public const string ScalarEncoding = "babyjubjub-scalar-decimal-v1";
    public const string DestroyedEnvelopeMarker = "[destroyed-admin-only-protected-tally-scalar]";
}

public sealed class WindowsDpapiAdminOnlyProtectedTallyEnvelopeCrypto : IAdminOnlyProtectedTallyEnvelopeCrypto
{
    private static readonly byte[] PurposeBytes =
        Encoding.UTF8.GetBytes("hush:elections:admin-only-protected-tally-scalar:v1");
    private const string UnavailableError =
        "Admin-only protected tally custody requires an OS-protected envelope provider; Windows DPAPI is unavailable on this platform.";

    public string SealAlgorithm => "windows-dpapi-current-user-v1";

    public string? SealedByServiceIdentity =>
        $"{Environment.UserDomainName}\\{Environment.UserName}@{Environment.MachineName}";

    public bool IsAvailable(out string error)
    {
        if (!OperatingSystem.IsWindows())
        {
            error = UnavailableError;
            return false;
        }

        error = string.Empty;
        return true;
    }

    public string SealPrivateScalar(string privateScalar)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateScalar);

        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(UnavailableError);
        }

        var plaintextBytes = Encoding.UTF8.GetBytes(privateScalar.Trim());
        var protectedBytes = ProtectedData.Protect(
            plaintextBytes,
            optionalEntropy: PurposeBytes,
            scope: DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public string? TryUnsealPrivateScalar(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!OperatingSystem.IsWindows())
        {
            error = UnavailableError;
            return null;
        }

        if (!string.Equals(envelope.SealAlgorithm, SealAlgorithm, StringComparison.Ordinal))
        {
            error = $"Seal algorithm mismatch. Expected {SealAlgorithm} but found {envelope.SealAlgorithm}.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(envelope.SealedTallyPrivateScalar) ||
            string.Equals(
                envelope.SealedTallyPrivateScalar,
                AdminOnlyProtectedTallyEnvelopeCryptoConstants.DestroyedEnvelopeMarker,
                StringComparison.Ordinal))
        {
            error = "The admin-only protected tally envelope no longer contains a sealed private scalar.";
            return null;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(envelope.SealedTallyPrivateScalar);
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
            error = "Failed to unseal the admin-only protected tally envelope with the current Windows user context.";
            return null;
        }
    }
}

public sealed class UnavailableAdminOnlyProtectedTallyEnvelopeCrypto : IAdminOnlyProtectedTallyEnvelopeCrypto
{
    public string SealAlgorithm => "unavailable";

    public string? SealedByServiceIdentity => null;

    public bool IsAvailable(out string error)
    {
        error = "Admin-only protected tally custody is unavailable in this deployment because no OS-protected envelope provider is configured.";
        return false;
    }

    public string SealPrivateScalar(string privateScalar) =>
        throw new InvalidOperationException("Admin-only protected tally custody is unavailable in this deployment.");

    public string? TryUnsealPrivateScalar(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope,
        out string error)
    {
        error = "Admin-only protected tally custody is unavailable in this deployment.";
        return null;
    }
}

public sealed class TransparentTestAdminOnlyProtectedTallyEnvelopeCrypto : IAdminOnlyProtectedTallyEnvelopeCrypto
{
    public string SealAlgorithm => "test-transparent-envelope-v1";

    public string? SealedByServiceIdentity => "test-host";

    public bool IsAvailable(out string error)
    {
        error = string.Empty;
        return true;
    }

    public string SealPrivateScalar(string privateScalar)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateScalar);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(privateScalar.Trim()));
    }

    public string? TryUnsealPrivateScalar(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope,
        out string error)
    {
        if (!string.Equals(envelope.SealAlgorithm, SealAlgorithm, StringComparison.Ordinal))
        {
            error = $"Seal algorithm mismatch. Expected {SealAlgorithm} but found {envelope.SealAlgorithm}.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(envelope.SealedTallyPrivateScalar) ||
            string.Equals(
                envelope.SealedTallyPrivateScalar,
                AdminOnlyProtectedTallyEnvelopeCryptoConstants.DestroyedEnvelopeMarker,
                StringComparison.Ordinal))
        {
            error = "The test envelope no longer contains a sealed private scalar.";
            return null;
        }

        try
        {
            error = string.Empty;
            return Encoding.UTF8.GetString(Convert.FromBase64String(envelope.SealedTallyPrivateScalar));
        }
        catch (FormatException)
        {
            error = "The test envelope payload is not valid base64.";
            return null;
        }
    }
}
