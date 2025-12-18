using System.Text.Json;
using Olimpo.KeyDerivation;
using Olimpo.CredentialsManager;

namespace HushKeyGenerator;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            ShowUsage();
            return 1;
        }

        var command = args[0].ToLowerInvariant();

        try
        {
            return command switch
            {
                "generate" => GenerateKeys(),
                "validate" => ValidateMnemonic(args.Length > 1 ? string.Join(" ", args.Skip(1)) : null),
                "derive" => DeriveKeys(args.Length > 1 ? string.Join(" ", args.Skip(1)) : null),
                "export" => ExportCredentials(args.Skip(1).ToArray()),
                "help" or "--help" or "-h" => ShowHelp(),
                _ => ShowUsage()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static int GenerateKeys()
    {
        Console.Error.WriteLine("Generating new 24-word mnemonic...");
        Console.Error.WriteLine();

        var mnemonic = MnemonicGenerator.GenerateMnemonic();
        var keys = DeterministicKeyGenerator.DeriveKeys(mnemonic);

        var output = new
        {
            mnemonic,
            signingPublicKey = keys.SigningPublicKey,
            signingPrivateKey = keys.SigningPrivateKey,
            encryptPublicKey = keys.EncryptPublicKey,
            encryptPrivateKey = keys.EncryptPrivateKey
        };

        var json = JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        Console.WriteLine(json);

        Console.Error.WriteLine();
        Console.Error.WriteLine("=".PadRight(60, '='));
        Console.Error.WriteLine("IMPORTANT: Save your mnemonic words securely!");
        Console.Error.WriteLine("They are the ONLY way to recover your keys.");
        Console.Error.WriteLine("=".PadRight(60, '='));
        Console.Error.WriteLine();
        Console.Error.WriteLine("Your 24-word recovery phrase:");
        Console.Error.WriteLine();

        var words = mnemonic.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            Console.Error.WriteLine($"  {i + 1,2}. {words[i]}");
        }

        return 0;
    }

    static int ValidateMnemonic(string? mnemonic)
    {
        if (string.IsNullOrWhiteSpace(mnemonic))
        {
            Console.Error.WriteLine("Enter your 24-word mnemonic (paste all words, press Enter twice when done):");
            mnemonic = ReadMultilineMnemonic();
        }

        if (string.IsNullOrWhiteSpace(mnemonic))
        {
            Console.Error.WriteLine("Error: No mnemonic provided");
            return 1;
        }

        var isValid = MnemonicGenerator.ValidateMnemonic(mnemonic);

        if (isValid)
        {
            Console.WriteLine("Valid mnemonic");
            return 0;
        }
        else
        {
            Console.Error.WriteLine("Invalid mnemonic");
            Console.Error.WriteLine("Please check:");
            Console.Error.WriteLine("  - You have exactly 24 words");
            Console.Error.WriteLine("  - All words are from the BIP-39 English wordlist");
            Console.Error.WriteLine("  - Words are spelled correctly");
            return 1;
        }
    }

    static int DeriveKeys(string? mnemonic)
    {
        if (string.IsNullOrWhiteSpace(mnemonic))
        {
            Console.Error.WriteLine("Enter your 24-word mnemonic (paste all words, press Enter twice when done):");
            mnemonic = ReadMultilineMnemonic();
        }

        if (string.IsNullOrWhiteSpace(mnemonic))
        {
            Console.Error.WriteLine("Error: No mnemonic provided");
            return 1;
        }

        if (!MnemonicGenerator.ValidateMnemonic(mnemonic))
        {
            Console.Error.WriteLine("Error: Invalid mnemonic");
            return 1;
        }

        Console.Error.WriteLine("Deriving keys from mnemonic...");
        var keys = DeterministicKeyGenerator.DeriveKeys(mnemonic);

        var output = new
        {
            mnemonic = NormalizeMnemonic(mnemonic),
            signingPublicKey = keys.SigningPublicKey,
            signingPrivateKey = keys.SigningPrivateKey,
            encryptPublicKey = keys.EncryptPublicKey,
            encryptPrivateKey = keys.EncryptPrivateKey
        };

        var json = JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        Console.WriteLine(json);
        return 0;
    }

    static int ExportCredentials(string[] args)
    {
        string? mnemonic = null;
        string? password = null;
        string? outputFile = null;
        string profileName = "Stacker";

        // Parse arguments
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--mnemonic" or "-m":
                    if (i + 1 < args.Length)
                        mnemonic = args[++i];
                    break;
                case "--password" or "-p":
                    if (i + 1 < args.Length)
                        password = args[++i];
                    break;
                case "--output" or "-o":
                    if (i + 1 < args.Length)
                        outputFile = args[++i];
                    break;
                case "--name" or "-n":
                    if (i + 1 < args.Length)
                        profileName = args[++i];
                    break;
            }
        }

        // Validate required arguments
        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("Error: --password is required");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Usage: HushKeyGenerator export --password <password> --output <file> [--mnemonic <words>] [--name <profile>]");
            return 1;
        }

        if (string.IsNullOrEmpty(outputFile))
        {
            Console.Error.WriteLine("Error: --output is required");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Usage: HushKeyGenerator export --password <password> --output <file> [--mnemonic <words>] [--name <profile>]");
            return 1;
        }

        // Generate or derive keys
        DerivedKeys keys;
        if (string.IsNullOrEmpty(mnemonic))
        {
            Console.Error.WriteLine("Generating new 24-word mnemonic...");
            mnemonic = MnemonicGenerator.GenerateMnemonic();
            keys = DeterministicKeyGenerator.DeriveKeys(mnemonic);
        }
        else
        {
            if (!MnemonicGenerator.ValidateMnemonic(mnemonic))
            {
                Console.Error.WriteLine("Error: Invalid mnemonic");
                return 1;
            }
            Console.Error.WriteLine("Deriving keys from provided mnemonic...");
            mnemonic = NormalizeMnemonic(mnemonic);
            keys = DeterministicKeyGenerator.DeriveKeys(mnemonic);
        }

        // Create portable credentials
        var credentials = new PortableCredentials
        {
            ProfileName = profileName,
            PublicSigningAddress = keys.SigningPublicKey,
            PrivateSigningKey = keys.SigningPrivateKey,
            PublicEncryptAddress = keys.EncryptPublicKey,
            PrivateEncryptKey = keys.EncryptPrivateKey,
            IsPublic = true,
            Mnemonic = mnemonic
        };

        // Export to file
        var fileService = new CredentialsFileService();
        fileService.ExportToFile(credentials, outputFile, password);

        Console.Error.WriteLine();
        Console.Error.WriteLine($"Credentials exported to: {outputFile}");
        Console.Error.WriteLine($"Profile name: {profileName}");
        Console.Error.WriteLine();

        // Output JSON to stdout for reference
        var output = new
        {
            mnemonic,
            signingPublicKey = keys.SigningPublicKey,
            signingPrivateKey = keys.SigningPrivateKey,
            encryptPublicKey = keys.EncryptPublicKey,
            encryptPrivateKey = keys.EncryptPrivateKey,
            exportedTo = outputFile
        };

        var json = JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        Console.WriteLine(json);

        Console.Error.WriteLine();
        Console.Error.WriteLine("=".PadRight(60, '='));
        Console.Error.WriteLine("IMPORTANT: Save your mnemonic words securely!");
        Console.Error.WriteLine("They are the ONLY way to recover your keys.");
        Console.Error.WriteLine("=".PadRight(60, '='));
        Console.Error.WriteLine();
        Console.Error.WriteLine("Your 24-word recovery phrase:");
        Console.Error.WriteLine();

        var words = mnemonic.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            Console.Error.WriteLine($"  {i + 1,2}. {words[i]}");
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine($"Password used for .dat file: {password}");
        Console.Error.WriteLine("Store this password securely - you'll need it to unlock the credentials.");

        return 0;
    }

    static string ReadMultilineMnemonic()
    {
        var lines = new List<string>();
        string? line;

        while ((line = Console.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                break;
            lines.Add(line.Trim());
        }

        return string.Join(" ", lines);
    }

    static string NormalizeMnemonic(string mnemonic)
    {
        var words = mnemonic.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words);
    }

    static int ShowUsage()
    {
        Console.Error.WriteLine("Usage: HushKeyGenerator <command> [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Commands:");
        Console.Error.WriteLine("  generate              Generate new 24-word mnemonic and derive keys");
        Console.Error.WriteLine("  validate [mnemonic]   Validate a mnemonic phrase");
        Console.Error.WriteLine("  derive [mnemonic]     Derive keys from an existing mnemonic");
        Console.Error.WriteLine("  export [options]      Export credentials to encrypted .dat file");
        Console.Error.WriteLine("  help                  Show detailed help");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Run 'HushKeyGenerator help' for more information.");
        return 1;
    }

    static int ShowHelp()
    {
        Console.WriteLine("HushKeyGenerator - BIP-39 Mnemonic Key Generation Tool");
        Console.WriteLine();
        Console.WriteLine("This tool generates deterministic cryptographic keys from a 24-word");
        Console.WriteLine("BIP-39 mnemonic phrase. The same mnemonic will always produce the");
        Console.WriteLine("same keys, allowing for easy backup and recovery.");
        Console.WriteLine();
        Console.WriteLine("COMMANDS:");
        Console.WriteLine();
        Console.WriteLine("  generate");
        Console.WriteLine("    Generates a new random 24-word mnemonic and derives all keys.");
        Console.WriteLine("    Output is JSON to stdout, helpful messages to stderr.");
        Console.WriteLine();
        Console.WriteLine("    Example:");
        Console.WriteLine("      HushKeyGenerator generate > keys.json");
        Console.WriteLine();
        Console.WriteLine("  validate [mnemonic]");
        Console.WriteLine("    Validates a mnemonic phrase (word count, wordlist, checksum).");
        Console.WriteLine("    If no mnemonic is provided, reads from stdin.");
        Console.WriteLine();
        Console.WriteLine("    Example:");
        Console.WriteLine("      HushKeyGenerator validate word1 word2 ... word24");
        Console.WriteLine();
        Console.WriteLine("  derive [mnemonic]");
        Console.WriteLine("    Derives keys from an existing mnemonic.");
        Console.WriteLine("    If no mnemonic is provided, reads from stdin.");
        Console.WriteLine();
        Console.WriteLine("    Example:");
        Console.WriteLine("      HushKeyGenerator derive word1 word2 ... word24");
        Console.WriteLine();
        Console.WriteLine("  export --password <password> --output <file> [options]");
        Console.WriteLine("    Creates an encrypted .dat credentials file for Node deployment.");
        Console.WriteLine("    If no mnemonic is provided, generates a new one.");
        Console.WriteLine();
        Console.WriteLine("    Required:");
        Console.WriteLine("      --password, -p <password>  Password to encrypt the .dat file");
        Console.WriteLine("      --output, -o <file>        Output file path (e.g., stacker.dat)");
        Console.WriteLine();
        Console.WriteLine("    Optional:");
        Console.WriteLine("      --mnemonic, -m <words>     Use existing 24-word mnemonic");
        Console.WriteLine("      --name, -n <profile>       Profile name (default: Stacker)");
        Console.WriteLine();
        Console.WriteLine("    Examples:");
        Console.WriteLine("      # Generate new credentials and export:");
        Console.WriteLine("      HushKeyGenerator export -p MySecurePassword -o stacker.dat");
        Console.WriteLine();
        Console.WriteLine("      # Export from existing mnemonic:");
        Console.WriteLine("      HushKeyGenerator export -p MySecurePassword -o stacker.dat -m \"word1 word2 ... word24\"");
        Console.WriteLine();
        Console.WriteLine("OUTPUT FORMAT:");
        Console.WriteLine();
        Console.WriteLine("  The generate, derive, and export commands output JSON with these fields:");
        Console.WriteLine("    - mnemonic: The 24-word recovery phrase");
        Console.WriteLine("    - signingPublicKey: ECDSA secp256k1 public key (hex)");
        Console.WriteLine("    - signingPrivateKey: ECDSA secp256k1 private key (hex)");
        Console.WriteLine("    - encryptPublicKey: ECIES secp256k1 public key (hex)");
        Console.WriteLine("    - encryptPrivateKey: ECIES secp256k1 private key (hex)");
        Console.WriteLine();
        Console.WriteLine("SECURITY:");
        Console.WriteLine();
        Console.WriteLine("  - Your 24-word mnemonic is the master key to all your keys");
        Console.WriteLine("  - Store it securely offline (paper, metal backup, etc.)");
        Console.WriteLine("  - Never share your mnemonic or private keys");
        Console.WriteLine("  - Anyone with your mnemonic can derive all your keys");
        Console.WriteLine();
        return 0;
    }
}
