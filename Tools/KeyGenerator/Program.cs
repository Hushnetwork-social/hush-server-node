using Olimpo.KeyDerivation;

Console.WriteLine("=== HushNetwork Key Generator ===\n");

// Generate a new mnemonic
var mnemonic = MnemonicGenerator.GenerateMnemonic();
Console.WriteLine("Mnemonic (SAVE THIS!):");
Console.WriteLine(mnemonic);
Console.WriteLine();

// Derive all keys
var keys = DeterministicKeyGenerator.DeriveKeys(mnemonic);

Console.WriteLine("=== Signing Keys (ECDSA secp256k1) ===");
Console.WriteLine($"PublicSigningAddress: {keys.SigningPublicKey}");
Console.WriteLine($"PrivateSigningKey: {keys.SigningPrivateKey}");
Console.WriteLine();

Console.WriteLine("=== Encryption Keys (ECIES secp256k1) ===");
Console.WriteLine($"PublicEncryptAddress: {keys.EncryptPublicKey}");
Console.WriteLine($"PrivateEncryptKey: {keys.EncryptPrivateKey}");
Console.WriteLine();

Console.WriteLine("=== JSON Format (for ApplicationSettings.json) ===");
Console.WriteLine($@"
""StackerInfo"": {{
    ""PublicSigningAddress"":  ""{keys.SigningPublicKey}"",
    ""PrivateSigningKey"":     ""{keys.SigningPrivateKey}"",
    ""PublicEncryptAddress"":  ""{keys.EncryptPublicKey}"",
    ""PrivateEncryptKey"":     ""{keys.EncryptPrivateKey}""
}},

""CredentialsProfile"": {{
    ""ProfileName"": ""Stacker"",
    ""PublicSigningAddress"":  ""{keys.SigningPublicKey}"",
    ""PrivateSigningKey"":     ""{keys.SigningPrivateKey}"",
    ""PublicEncryptAddress"":  ""{keys.EncryptPublicKey}"",
    ""PrivateEncryptKey"":     ""{keys.EncryptPrivateKey}"",
    ""IsPublic"": true,
    ""Mnemonic"": ""{mnemonic}""
}}
");
