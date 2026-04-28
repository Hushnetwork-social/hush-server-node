using Amazon;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;
using System.Security.Cryptography;
using System.Text;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IAdminOnlyProtectedTallyEnvelopeCrypto
{
    string SealAlgorithm { get; }

    string? SealedByServiceIdentity { get; }

    bool IsAvailable(out string error);

    string SealPrivateScalar(
        string privateScalar,
        ElectionId electionId,
        string selectedProfileId);

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

    public string SealPrivateScalar(
        string privateScalar,
        ElectionId electionId,
        string selectedProfileId)
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

public sealed record AdminOnlyProtectedTallyEnvelopeCryptoOptions(
    string Provider,
    string? AwsKmsKeyId = null,
    string? AwsKmsRegion = null,
    string? AwsKmsServiceUrl = null,
    string? AwsKmsServiceIdentityLabel = null)
{
    public const string ProviderAuto = "auto";
    public const string ProviderUnavailable = "unavailable";
    public const string ProviderWindowsDpapi = "windows-dpapi";
    public const string ProviderAwsKms = "aws-kms";

    public static AdminOnlyProtectedTallyEnvelopeCryptoOptions Default { get; } =
        new(ProviderAuto);

    public string NormalizedProvider =>
        string.IsNullOrWhiteSpace(Provider)
            ? ProviderAuto
            : Provider.Trim().ToLowerInvariant();
}

public static class AdminOnlyProtectedTallyEnvelopeCryptoFactory
{
    public static IAdminOnlyProtectedTallyEnvelopeCrypto Create(
        AdminOnlyProtectedTallyEnvelopeCryptoOptions? options = null)
    {
        var resolvedOptions = options ?? AdminOnlyProtectedTallyEnvelopeCryptoOptions.Default;
        return resolvedOptions.NormalizedProvider switch
        {
            AdminOnlyProtectedTallyEnvelopeCryptoOptions.ProviderAwsKms =>
                CreateAwsKmsProvider(resolvedOptions),
            AdminOnlyProtectedTallyEnvelopeCryptoOptions.ProviderWindowsDpapi =>
                OperatingSystem.IsWindows()
                    ? new WindowsDpapiAdminOnlyProtectedTallyEnvelopeCrypto()
                    : new UnavailableAdminOnlyProtectedTallyEnvelopeCrypto(
                        "Admin-only protected tally custody was configured for Windows DPAPI, but this deployment is not running on Windows."),
            AdminOnlyProtectedTallyEnvelopeCryptoOptions.ProviderUnavailable =>
                new UnavailableAdminOnlyProtectedTallyEnvelopeCrypto(),
            AdminOnlyProtectedTallyEnvelopeCryptoOptions.ProviderAuto =>
                CreateAutoProvider(resolvedOptions),
            _ => new UnavailableAdminOnlyProtectedTallyEnvelopeCrypto(
                $"Unknown admin-only protected tally custody provider '{resolvedOptions.Provider}'."),
        };
    }

    private static IAdminOnlyProtectedTallyEnvelopeCrypto CreateAutoProvider(
        AdminOnlyProtectedTallyEnvelopeCryptoOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.AwsKmsKeyId))
        {
            return CreateAwsKmsProvider(options);
        }

        return OperatingSystem.IsWindows()
            ? new WindowsDpapiAdminOnlyProtectedTallyEnvelopeCrypto()
            : new UnavailableAdminOnlyProtectedTallyEnvelopeCrypto();
    }

    private static IAdminOnlyProtectedTallyEnvelopeCrypto CreateAwsKmsProvider(
        AdminOnlyProtectedTallyEnvelopeCryptoOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AwsKmsKeyId))
        {
            return new UnavailableAdminOnlyProtectedTallyEnvelopeCrypto(
                "Admin-only protected tally custody was configured for AWS KMS, but no KMS key id or alias was configured.");
        }

        return new AwsKmsAdminOnlyProtectedTallyEnvelopeCrypto(
            options,
            CreateAwsKmsClient(options),
            disposeClient: true);
    }

    private static IAmazonKeyManagementService CreateAwsKmsClient(
        AdminOnlyProtectedTallyEnvelopeCryptoOptions options)
    {
        var serviceUrl = options.AwsKmsServiceUrl?.Trim();
        var region = options.AwsKmsRegion?.Trim();

        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            return new AmazonKeyManagementServiceClient(
                new AmazonKeyManagementServiceConfig
                {
                    ServiceURL = serviceUrl,
                    AuthenticationRegion = string.IsNullOrWhiteSpace(region) ? "us-east-1" : region,
                });
        }

        if (!string.IsNullOrWhiteSpace(region))
        {
            return new AmazonKeyManagementServiceClient(
                new AmazonKeyManagementServiceConfig
                {
                    RegionEndpoint = RegionEndpoint.GetBySystemName(region),
                });
        }

        return new AmazonKeyManagementServiceClient();
    }
}

public sealed class AwsKmsAdminOnlyProtectedTallyEnvelopeCrypto : IAdminOnlyProtectedTallyEnvelopeCrypto, IDisposable
{
    private const string ContextPurpose = "hush:elections:admin-only-protected-tally-scalar:v1";
    private readonly AdminOnlyProtectedTallyEnvelopeCryptoOptions _options;
    private readonly IAmazonKeyManagementService _kmsClient;
    private readonly bool _disposeClient;

    public AwsKmsAdminOnlyProtectedTallyEnvelopeCrypto(
        AdminOnlyProtectedTallyEnvelopeCryptoOptions options,
        IAmazonKeyManagementService kmsClient,
        bool disposeClient = false)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _kmsClient = kmsClient ?? throw new ArgumentNullException(nameof(kmsClient));
        _disposeClient = disposeClient;
    }

    public string SealAlgorithm => "aws-kms-v1";

    public string? SealedByServiceIdentity =>
        _options.AwsKmsServiceIdentityLabel?.Trim()
        ?? BuildServiceIdentityLabel(_options.AwsKmsKeyId);

    public bool IsAvailable(out string error)
    {
        if (string.IsNullOrWhiteSpace(_options.AwsKmsKeyId))
        {
            error = "Admin-only protected tally custody is configured for AWS KMS, but no KMS key id or alias is configured.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public string SealPrivateScalar(
        string privateScalar,
        ElectionId electionId,
        string selectedProfileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateScalar);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedProfileId);

        if (!IsAvailable(out var availabilityError))
        {
            throw new InvalidOperationException(availabilityError);
        }

        try
        {
            var request = new EncryptRequest
            {
                KeyId = _options.AwsKmsKeyId!.Trim(),
                Plaintext = new MemoryStream(Encoding.UTF8.GetBytes(privateScalar.Trim())),
                EncryptionContext = BuildEncryptionContext(electionId, selectedProfileId),
            };

            var response = _kmsClient.EncryptAsync(request).GetAwaiter().GetResult();
            if (response.CiphertextBlob is null || response.CiphertextBlob.Length == 0)
            {
                throw new InvalidOperationException("AWS KMS returned an empty encrypted tally envelope.");
            }

            return Convert.ToBase64String(response.CiphertextBlob.ToArray());
        }
        catch (Exception ex) when (ex is AmazonKeyManagementServiceException or AmazonServiceException)
        {
            throw new InvalidOperationException(
                $"AWS KMS failed to seal the admin-only protected tally envelope: {ex.Message}",
                ex);
        }
    }

    public string? TryUnsealPrivateScalar(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(envelope);

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
            error = "The AWS KMS admin-only protected tally envelope no longer contains a sealed private scalar.";
            return null;
        }

        if (!IsAvailable(out error))
        {
            return null;
        }

        try
        {
            var ciphertext = Convert.FromBase64String(envelope.SealedTallyPrivateScalar);
            var request = new DecryptRequest
            {
                CiphertextBlob = new MemoryStream(ciphertext),
                EncryptionContext = BuildEncryptionContext(envelope.ElectionId, envelope.SelectedProfileId),
            };

            var response = _kmsClient.DecryptAsync(request).GetAwaiter().GetResult();
            if (response.Plaintext is null || response.Plaintext.Length == 0)
            {
                error = "AWS KMS returned an empty decrypted tally envelope.";
                return null;
            }

            error = string.Empty;
            return Encoding.UTF8.GetString(response.Plaintext.ToArray());
        }
        catch (FormatException)
        {
            error = "The AWS KMS admin-only protected tally envelope payload is not valid base64.";
            return null;
        }
        catch (Exception ex) when (ex is AmazonKeyManagementServiceException or AmazonServiceException)
        {
            error = $"AWS KMS failed to unseal the admin-only protected tally envelope: {ex.Message}";
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposeClient)
        {
            _kmsClient.Dispose();
        }
    }

    private static Dictionary<string, string> BuildEncryptionContext(
        ElectionId electionId,
        string selectedProfileId) =>
        new(StringComparer.Ordinal)
        {
            ["hush-purpose"] = ContextPurpose,
            ["election-id"] = electionId.ToString(),
            ["selected-profile-id"] = selectedProfileId.Trim(),
            ["scalar-encoding"] = AdminOnlyProtectedTallyEnvelopeCryptoConstants.ScalarEncoding,
        };

    private static string? BuildServiceIdentityLabel(string? keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId))
        {
            return null;
        }

        var label = keyId.Trim();
        var slashIndex = label.LastIndexOf('/');
        if (label.StartsWith("arn:", StringComparison.OrdinalIgnoreCase) && slashIndex >= 0)
        {
            label = label[(slashIndex + 1)..];
        }

        var serviceIdentity = $"aws-kms:{label}";
        return serviceIdentity.Length <= 160
            ? serviceIdentity
            : serviceIdentity[..160];
    }
}

public sealed class UnavailableAdminOnlyProtectedTallyEnvelopeCrypto : IAdminOnlyProtectedTallyEnvelopeCrypto
{
    private readonly string _error;

    public UnavailableAdminOnlyProtectedTallyEnvelopeCrypto(
        string? error = null)
    {
        _error = string.IsNullOrWhiteSpace(error)
            ? "Admin-only protected tally custody is unavailable in this deployment because no OS-protected envelope provider is configured."
            : error.Trim();
    }

    public string SealAlgorithm => "unavailable";

    public string? SealedByServiceIdentity => null;

    public bool IsAvailable(out string error)
    {
        error = _error;
        return false;
    }

    public string SealPrivateScalar(
        string privateScalar,
        ElectionId electionId,
        string selectedProfileId) =>
        throw new InvalidOperationException(_error);

    public string? TryUnsealPrivateScalar(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope,
        out string error)
    {
        error = _error;
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

    public string SealPrivateScalar(
        string privateScalar,
        ElectionId electionId,
        string selectedProfileId)
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
