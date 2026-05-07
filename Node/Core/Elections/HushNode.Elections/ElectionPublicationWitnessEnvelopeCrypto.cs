using Amazon;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;
using System.Security.Cryptography;
using System.Text;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IElectionPublicationWitnessEnvelopeCrypto
{
    string SealAlgorithm { get; }

    string? SealedByServiceIdentity { get; }

    bool IsAvailable(out string error);

    string SealWitnessMaterial(
        string witnessMaterial,
        ElectionId electionId,
        Guid witnessId);

    string UnsealWitnessMaterial(
        string sealedWitnessMaterial,
        ElectionId electionId,
        Guid witnessId);
}

public sealed class WindowsDpapiElectionPublicationWitnessEnvelopeCrypto : IElectionPublicationWitnessEnvelopeCrypto
{
    private static readonly byte[] PurposeBytes =
        Encoding.UTF8.GetBytes("hush:elections:sp07-publication-witness:v1");
    private const string UnavailableError =
        "SP-07 publication witness custody requires an OS-protected envelope provider; Windows DPAPI is unavailable on this platform.";

    public string SealAlgorithm => "windows-dpapi-current-user-sp07-publication-witness-v1";

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

    public string SealWitnessMaterial(
        string witnessMaterial,
        ElectionId electionId,
        Guid witnessId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(witnessMaterial);

        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(UnavailableError);
        }

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(witnessMaterial.Trim()),
            optionalEntropy: PurposeBytes,
            scope: DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public string UnsealWitnessMaterial(
        string sealedWitnessMaterial,
        ElectionId electionId,
        Guid witnessId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sealedWitnessMaterial);

        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(UnavailableError);
        }

        var protectedBytes = Convert.FromBase64String(sealedWitnessMaterial.Trim());
        var clearBytes = ProtectedData.Unprotect(
            protectedBytes,
            optionalEntropy: PurposeBytes,
            scope: DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(clearBytes);
    }
}

public sealed class UnavailableElectionPublicationWitnessEnvelopeCrypto : IElectionPublicationWitnessEnvelopeCrypto
{
    public string SealAlgorithm => "unavailable";

    public string? SealedByServiceIdentity => null;

    public bool IsAvailable(out string error)
    {
        error = "SP-07 publication witness custody is unavailable in this deployment because no OS-protected envelope provider is configured.";
        return false;
    }

    public string SealWitnessMaterial(
        string witnessMaterial,
        ElectionId electionId,
        Guid witnessId) =>
        throw new InvalidOperationException("SP-07 publication witness custody is unavailable in this deployment.");

    public string UnsealWitnessMaterial(
        string sealedWitnessMaterial,
        ElectionId electionId,
        Guid witnessId) =>
        throw new InvalidOperationException("SP-07 publication witness custody is unavailable in this deployment.");
}

public sealed record ElectionPublicationWitnessEnvelopeCryptoOptions(
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

    public static ElectionPublicationWitnessEnvelopeCryptoOptions Default { get; } =
        new(ProviderAuto);

    public string NormalizedProvider =>
        string.IsNullOrWhiteSpace(Provider)
            ? ProviderAuto
            : Provider.Trim().ToLowerInvariant();
}

public static class ElectionPublicationWitnessEnvelopeCryptoFactory
{
    public static IElectionPublicationWitnessEnvelopeCrypto Create(
        ElectionPublicationWitnessEnvelopeCryptoOptions? options = null)
    {
        var resolvedOptions = options ?? ElectionPublicationWitnessEnvelopeCryptoOptions.Default;
        return resolvedOptions.NormalizedProvider switch
        {
            ElectionPublicationWitnessEnvelopeCryptoOptions.ProviderAwsKms =>
                CreateAwsKmsProvider(resolvedOptions),
            ElectionPublicationWitnessEnvelopeCryptoOptions.ProviderWindowsDpapi =>
                OperatingSystem.IsWindows()
                    ? new WindowsDpapiElectionPublicationWitnessEnvelopeCrypto()
                    : new UnavailableElectionPublicationWitnessEnvelopeCrypto(),
            ElectionPublicationWitnessEnvelopeCryptoOptions.ProviderUnavailable =>
                new UnavailableElectionPublicationWitnessEnvelopeCrypto(),
            ElectionPublicationWitnessEnvelopeCryptoOptions.ProviderAuto =>
                CreateAutoProvider(resolvedOptions),
            _ => new UnavailableElectionPublicationWitnessEnvelopeCrypto(),
        };
    }

    private static IElectionPublicationWitnessEnvelopeCrypto CreateAutoProvider(
        ElectionPublicationWitnessEnvelopeCryptoOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.AwsKmsKeyId))
        {
            return CreateAwsKmsProvider(options);
        }

        return OperatingSystem.IsWindows()
            ? new WindowsDpapiElectionPublicationWitnessEnvelopeCrypto()
            : new UnavailableElectionPublicationWitnessEnvelopeCrypto();
    }

    private static IElectionPublicationWitnessEnvelopeCrypto CreateAwsKmsProvider(
        ElectionPublicationWitnessEnvelopeCryptoOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AwsKmsKeyId))
        {
            return new UnavailableElectionPublicationWitnessEnvelopeCrypto();
        }

        return new AwsKmsElectionPublicationWitnessEnvelopeCrypto(
            options,
            CreateAwsKmsClient(options),
            disposeClient: true);
    }

    private static IAmazonKeyManagementService CreateAwsKmsClient(
        ElectionPublicationWitnessEnvelopeCryptoOptions options)
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

public sealed class AwsKmsElectionPublicationWitnessEnvelopeCrypto : IElectionPublicationWitnessEnvelopeCrypto, IDisposable
{
    private const string ContextPurpose = "hush:elections:sp07-publication-witness:v1";
    private readonly ElectionPublicationWitnessEnvelopeCryptoOptions _options;
    private readonly IAmazonKeyManagementService _kmsClient;
    private readonly bool _disposeClient;

    public AwsKmsElectionPublicationWitnessEnvelopeCrypto(
        ElectionPublicationWitnessEnvelopeCryptoOptions options,
        IAmazonKeyManagementService kmsClient,
        bool disposeClient = false)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _kmsClient = kmsClient ?? throw new ArgumentNullException(nameof(kmsClient));
        _disposeClient = disposeClient;
    }

    public string SealAlgorithm => "aws-kms-sp07-publication-witness-v1";

    public string? SealedByServiceIdentity =>
        _options.AwsKmsServiceIdentityLabel?.Trim()
        ?? BuildServiceIdentityLabel(_options.AwsKmsKeyId);

    public bool IsAvailable(out string error)
    {
        if (string.IsNullOrWhiteSpace(_options.AwsKmsKeyId))
        {
            error = "SP-07 publication witness custody is configured for AWS KMS, but no KMS key id or alias is configured.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public string SealWitnessMaterial(
        string witnessMaterial,
        ElectionId electionId,
        Guid witnessId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(witnessMaterial);

        if (!IsAvailable(out var availabilityError))
        {
            throw new InvalidOperationException(availabilityError);
        }

        try
        {
            var request = new EncryptRequest
            {
                KeyId = _options.AwsKmsKeyId!.Trim(),
                Plaintext = new MemoryStream(Encoding.UTF8.GetBytes(witnessMaterial.Trim())),
                EncryptionContext = BuildEncryptionContext(electionId, witnessId),
            };

            var response = _kmsClient.EncryptAsync(request).GetAwaiter().GetResult();
            if (response.CiphertextBlob is null || response.CiphertextBlob.Length == 0)
            {
                throw new InvalidOperationException("AWS KMS returned an empty encrypted SP-07 publication witness envelope.");
            }

            return Convert.ToBase64String(response.CiphertextBlob.ToArray());
        }
        catch (Exception ex) when (ex is AmazonKeyManagementServiceException or AmazonServiceException)
        {
            throw new InvalidOperationException(
                $"AWS KMS failed to seal the SP-07 publication witness envelope: {ex.Message}",
            ex);
        }
    }

    public string UnsealWitnessMaterial(
        string sealedWitnessMaterial,
        ElectionId electionId,
        Guid witnessId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sealedWitnessMaterial);

        if (!IsAvailable(out var availabilityError))
        {
            throw new InvalidOperationException(availabilityError);
        }

        try
        {
            var request = new DecryptRequest
            {
                CiphertextBlob = new MemoryStream(Convert.FromBase64String(sealedWitnessMaterial.Trim())),
                EncryptionContext = BuildEncryptionContext(electionId, witnessId),
            };

            var response = _kmsClient.DecryptAsync(request).GetAwaiter().GetResult();
            if (response.Plaintext is null || response.Plaintext.Length == 0)
            {
                throw new InvalidOperationException("AWS KMS returned an empty decrypted SP-07 publication witness envelope.");
            }

            return Encoding.UTF8.GetString(response.Plaintext.ToArray());
        }
        catch (Exception ex) when (ex is AmazonKeyManagementServiceException or AmazonServiceException or FormatException)
        {
            throw new InvalidOperationException(
                $"AWS KMS failed to unseal the SP-07 publication witness envelope: {ex.Message}",
                ex);
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
        Guid witnessId) =>
        new(StringComparer.Ordinal)
        {
            ["hush-purpose"] = ContextPurpose,
            ["election-id"] = electionId.ToString(),
            ["publication-witness-id"] = witnessId.ToString("D"),
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

        return $"aws-kms:{label}";
    }
}

public sealed class TransparentTestElectionPublicationWitnessEnvelopeCrypto : IElectionPublicationWitnessEnvelopeCrypto
{
    public string SealAlgorithm => "test-transparent-sp07-publication-witness-v1";

    public string? SealedByServiceIdentity => "test-host";

    public bool IsAvailable(out string error)
    {
        error = string.Empty;
        return true;
    }

    public string SealWitnessMaterial(
        string witnessMaterial,
        ElectionId electionId,
        Guid witnessId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(witnessMaterial);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(witnessMaterial.Trim()));
    }

    public string UnsealWitnessMaterial(
        string sealedWitnessMaterial,
        ElectionId electionId,
        Guid witnessId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sealedWitnessMaterial);
        return Encoding.UTF8.GetString(Convert.FromBase64String(sealedWitnessMaterial.Trim()));
    }
}
