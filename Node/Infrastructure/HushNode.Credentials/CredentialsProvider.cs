using Microsoft.Extensions.Options;
using HushShared.Blockchain;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using Olimpo.CredentialsManager;

namespace HushNode.Credentials;

public class CredentialsProvider : ICredentialsProvider
{
    private static CredentialsProvider? _instance;
    private readonly CredentialsProfile _credentialsProfile;

    internal static CredentialsProvider Instance { get => _instance ?? throw new InvalidOperationException("CredentialsProvider has not been initialized."); }

    public CredentialsProvider(IOptions<CredentialsProfile> credentials)
    {
        var config = credentials.Value;

        if (config.UseFileBasedCredentials)
        {
            this._credentialsProfile = LoadFromFile(config);
        }
        else
        {
            this._credentialsProfile = config;
        }

        _instance ??= this;
    }

    public CredentialsProfile GetCredentials() => this._credentialsProfile;

    private static CredentialsProfile LoadFromFile(CredentialsProfile config)
    {
        var filePath = config.CredentialsFile!;

        // Resolve relative paths
        if (!Path.IsPathRooted(filePath))
        {
            filePath = Path.Combine(AppContext.BaseDirectory, filePath);
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                $"Credentials file not found: {filePath}. " +
                "Please ensure the .dat file exists or use inline credentials.");
        }

        // Resolve password
        var password = ResolvePassword(config);

        // Load and decrypt
        var fileService = new CredentialsFileService();
        var portable = fileService.ImportFromFile(filePath, password);

        if (portable == null)
        {
            throw new InvalidOperationException(
                "Failed to decrypt credentials file. Check password and file integrity.");
        }

        Console.WriteLine($"[Credentials] Loaded credentials from file: {Path.GetFileName(filePath)}");
        Console.WriteLine($"[Credentials] Profile: {portable.ProfileName}");

        // Map to CredentialsProfile
        return new CredentialsProfile
        {
            ProfileName = portable.ProfileName,
            PublicSigningAddress = portable.PublicSigningAddress,
            PrivateSigningKey = portable.PrivateSigningKey,
            PublicEncryptAddress = portable.PublicEncryptAddress,
            PrivateEncryptKey = portable.PrivateEncryptKey,
            IsPublic = portable.IsPublic
        };
    }

    private static string ResolvePassword(CredentialsProfile config)
    {
        // Option 1: Environment variable name specified (recommended for Docker/production)
        if (!string.IsNullOrEmpty(config.CredentialsPasswordEnvVar))
        {
            var envValue = Environment.GetEnvironmentVariable(config.CredentialsPasswordEnvVar);
            if (string.IsNullOrEmpty(envValue))
            {
                throw new InvalidOperationException(
                    $"Environment variable '{config.CredentialsPasswordEnvVar}' is not set. " +
                    "Set this variable with the .dat file password.");
            }
            Console.WriteLine($"[Credentials] Using password from environment variable: {config.CredentialsPasswordEnvVar}");
            return envValue;
        }

        // Option 2: Password with environment variable syntax: ${ENV_VAR_NAME}
        if (!string.IsNullOrEmpty(config.CredentialsPassword))
        {
            var password = config.CredentialsPassword;

            if (password.StartsWith("${") && password.EndsWith("}"))
            {
                var envVarName = password.Substring(2, password.Length - 3);
                var envValue = Environment.GetEnvironmentVariable(envVarName);
                if (string.IsNullOrEmpty(envValue))
                {
                    throw new InvalidOperationException(
                        $"Environment variable '{envVarName}' is not set. " +
                        "Set this variable with the .dat file password.");
                }
                Console.WriteLine($"[Credentials] Using password from environment variable: {envVarName}");
                return envValue;
            }

            // Direct password in config (development only - not recommended)
            Console.WriteLine("[Credentials] WARNING: Using password from config file. Use environment variables in production!");
            return password;
        }

        // No password configured - fail with clear instructions
        throw new InvalidOperationException(
            "No password configured for credentials file. " +
            "Configure one of the following in ApplicationSettings.json:\n" +
            "  1. \"CredentialsPasswordEnvVar\": \"HUSH_CREDENTIALS_PASSWORD\" (recommended)\n" +
            "  2. \"CredentialsPassword\": \"${HUSH_CREDENTIALS_PASSWORD}\"\n" +
            "Then set the environment variable with the .dat file password.");
    }
}

public static class SignerValidatorTransactionHandler 
{
    public static SignedTransaction<T> SignTransactionWithLocalUser<T>(this UnsignedTransaction<T> transaction)
        where T : ITransactionPayloadKind
    {
        var localCredendials = CredentialsProvider.Instance.GetCredentials();

        return transaction.SignByUser(localCredendials.PublicSigningAddress, localCredendials.PrivateSigningKey);
    }

    public static ValidatedTransaction<T> ValidateTransactionWithLocalUser<T>(this SignedTransaction<T> transaction)
        where T : ITransactionPayloadKind
    {
        var localCredendials = CredentialsProvider.Instance.GetCredentials();

        return transaction.SignByValidator(localCredendials.PublicSigningAddress, localCredendials.PrivateSigningKey);
    }
}