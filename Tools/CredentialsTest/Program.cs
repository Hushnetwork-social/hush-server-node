using Olimpo.CredentialsManager;

var filePath = args.Length > 0 ? args[0] : @"c:\myWork\stacker_keys.dat";
var password = args.Length > 1 ? args[1] : "paulo97";

Console.WriteLine($"Testing decryption of: {filePath}");
Console.WriteLine($"With password: {password}");

var service = new CredentialsFileService();

// First check if it's a valid file
if (!service.IsValidCredentialsFile(filePath))
{
    Console.WriteLine("ERROR: File is not a valid credentials file (wrong magic number or version)");
    return 1;
}

Console.WriteLine("File format is valid (HUSH magic number found)");

// Try to decrypt
var credentials = service.ImportFromFile(filePath, password);

if (credentials == null)
{
    Console.WriteLine("ERROR: Failed to decrypt - wrong password or corrupted file");
    return 1;
}

Console.WriteLine("SUCCESS! Decryption worked!");
Console.WriteLine($"  Profile Name: {credentials.ProfileName}");
Console.WriteLine($"  Public Signing Address: {credentials.PublicSigningAddress?[..20]}...");
Console.WriteLine($"  Is Public: {credentials.IsPublic}");

return 0;
