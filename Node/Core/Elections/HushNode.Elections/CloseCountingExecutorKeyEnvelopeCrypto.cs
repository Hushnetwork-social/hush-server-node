using Amazon;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;
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

    string SealPrivateKey(
        string privateKey,
        Guid closeCountingJobId,
        string keyAlgorithm);

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
    private const string UnavailableError =
        "Trustee close-counting custody requires an OS-protected envelope provider; Windows DPAPI is unavailable on this platform.";

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

    public string SealPrivateKey(
        string privateKey,
        Guid closeCountingJobId,
        string keyAlgorithm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKey);

        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(UnavailableError);
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

    public string SealPrivateKey(
        string privateKey,
        Guid closeCountingJobId,
        string keyAlgorithm) =>
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

    public string SealPrivateKey(
        string privateKey,
        Guid closeCountingJobId,
        string keyAlgorithm)
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

public sealed record CloseCountingExecutorEnvelopeCryptoOptions(
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

    public static CloseCountingExecutorEnvelopeCryptoOptions Default { get; } =
        new(ProviderAuto);

    public string NormalizedProvider =>
        string.IsNullOrWhiteSpace(Provider)
            ? ProviderAuto
            : Provider.Trim().ToLowerInvariant();
}

public static class CloseCountingExecutorEnvelopeCryptoFactory
{
    public static ICloseCountingExecutorEnvelopeCrypto Create(
        CloseCountingExecutorEnvelopeCryptoOptions? options = null)
    {
        var resolvedOptions = options ?? CloseCountingExecutorEnvelopeCryptoOptions.Default;
        return resolvedOptions.NormalizedProvider switch
        {
            CloseCountingExecutorEnvelopeCryptoOptions.ProviderAwsKms =>
                CreateAwsKmsProvider(resolvedOptions),
            CloseCountingExecutorEnvelopeCryptoOptions.ProviderWindowsDpapi =>
                OperatingSystem.IsWindows()
                    ? new WindowsDpapiCloseCountingExecutorEnvelopeCrypto()
                    : new UnavailableCloseCountingExecutorEnvelopeCrypto(),
            CloseCountingExecutorEnvelopeCryptoOptions.ProviderUnavailable =>
                new UnavailableCloseCountingExecutorEnvelopeCrypto(),
            CloseCountingExecutorEnvelopeCryptoOptions.ProviderAuto =>
                CreateAutoProvider(resolvedOptions),
            _ => new UnavailableCloseCountingExecutorEnvelopeCrypto(),
        };
    }

    private static ICloseCountingExecutorEnvelopeCrypto CreateAutoProvider(
        CloseCountingExecutorEnvelopeCryptoOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.AwsKmsKeyId))
        {
            return CreateAwsKmsProvider(options);
        }

        return OperatingSystem.IsWindows()
            ? new WindowsDpapiCloseCountingExecutorEnvelopeCrypto()
            : new UnavailableCloseCountingExecutorEnvelopeCrypto();
    }

    private static ICloseCountingExecutorEnvelopeCrypto CreateAwsKmsProvider(
        CloseCountingExecutorEnvelopeCryptoOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AwsKmsKeyId))
        {
            return new UnavailableCloseCountingExecutorEnvelopeCrypto();
        }

        return new AwsKmsCloseCountingExecutorEnvelopeCrypto(
            options,
            CreateAwsKmsClient(options),
            disposeClient: true);
    }

    private static IAmazonKeyManagementService CreateAwsKmsClient(
        CloseCountingExecutorEnvelopeCryptoOptions options)
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

public sealed class AwsKmsCloseCountingExecutorEnvelopeCrypto : ICloseCountingExecutorEnvelopeCrypto, IDisposable
{
    private const string ContextPurpose = "hush:elections:close-counting-executor-session-key:v1";
    private readonly CloseCountingExecutorEnvelopeCryptoOptions _options;
    private readonly IAmazonKeyManagementService _kmsClient;
    private readonly bool _disposeClient;

    public AwsKmsCloseCountingExecutorEnvelopeCrypto(
        CloseCountingExecutorEnvelopeCryptoOptions options,
        IAmazonKeyManagementService kmsClient,
        bool disposeClient = false)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _kmsClient = kmsClient ?? throw new ArgumentNullException(nameof(kmsClient));
        _disposeClient = disposeClient;
    }

    public string SealAlgorithm => "aws-kms-close-counting-executor-v1";

    public string? SealedByServiceIdentity =>
        _options.AwsKmsServiceIdentityLabel?.Trim()
        ?? BuildServiceIdentityLabel(_options.AwsKmsKeyId);

    public bool IsAvailable(out string error)
    {
        if (string.IsNullOrWhiteSpace(_options.AwsKmsKeyId))
        {
            error = "Trustee close-counting custody is configured for AWS KMS, but no KMS key id or alias is configured.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public string SealPrivateKey(
        string privateKey,
        Guid closeCountingJobId,
        string keyAlgorithm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyAlgorithm);

        if (!IsAvailable(out var availabilityError))
        {
            throw new InvalidOperationException(availabilityError);
        }

        try
        {
            var request = new EncryptRequest
            {
                KeyId = _options.AwsKmsKeyId!.Trim(),
                Plaintext = new MemoryStream(Encoding.UTF8.GetBytes(privateKey.Trim())),
                EncryptionContext = BuildEncryptionContext(closeCountingJobId, keyAlgorithm),
            };

            var response = _kmsClient.EncryptAsync(request).GetAwaiter().GetResult();
            if (response.CiphertextBlob is null || response.CiphertextBlob.Length == 0)
            {
                throw new InvalidOperationException("AWS KMS returned an empty encrypted close-counting executor envelope.");
            }

            return Convert.ToBase64String(response.CiphertextBlob.ToArray());
        }
        catch (Exception ex) when (ex is AmazonKeyManagementServiceException or AmazonServiceException)
        {
            throw new InvalidOperationException(
                $"AWS KMS failed to seal the trustee close-counting executor envelope: {ex.Message}",
                ex);
        }
    }

    public string? TryUnsealPrivateKey(
        ElectionExecutorSessionKeyEnvelopeRecord envelope,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(envelope);

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
            error = "The AWS KMS close-counting executor envelope no longer contains a sealed private key.";
            return null;
        }

        if (!IsAvailable(out error))
        {
            return null;
        }

        try
        {
            var ciphertext = Convert.FromBase64String(envelope.SealedExecutorSessionPrivateKey);
            var request = new DecryptRequest
            {
                CiphertextBlob = new MemoryStream(ciphertext),
                EncryptionContext = BuildEncryptionContext(envelope.CloseCountingJobId, envelope.KeyAlgorithm),
            };

            var response = _kmsClient.DecryptAsync(request).GetAwaiter().GetResult();
            if (response.Plaintext is null || response.Plaintext.Length == 0)
            {
                error = "AWS KMS returned an empty decrypted close-counting executor envelope.";
                return null;
            }

            error = string.Empty;
            return Encoding.UTF8.GetString(response.Plaintext.ToArray());
        }
        catch (FormatException)
        {
            error = "The AWS KMS close-counting executor envelope payload is not valid base64.";
            return null;
        }
        catch (Exception ex) when (ex is AmazonKeyManagementServiceException or AmazonServiceException)
        {
            error = $"AWS KMS failed to unseal the trustee close-counting executor envelope: {ex.Message}";
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
        Guid closeCountingJobId,
        string keyAlgorithm) =>
        new(StringComparer.Ordinal)
        {
            ["hush-purpose"] = ContextPurpose,
            ["close-counting-job-id"] = closeCountingJobId.ToString("D"),
            ["key-algorithm"] = keyAlgorithm.Trim(),
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
