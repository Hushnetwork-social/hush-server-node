using System.Text.Json;
using HushShared.Elections.Verification.Model;

if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
    args.Contains("-h", StringComparer.OrdinalIgnoreCase))
{
    PrintUsage();
    return 0;
}

var inputPath = GetOption(args, "--input");
var outputPath = GetOption(args, "--output");
var hashOutputPath = GetOption(args, "--hash-output");

if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputPath))
{
    PrintUsage();
    return 2;
}

try
{
    var inputJson = await File.ReadAllTextAsync(inputPath);
    var input = JsonSerializer.Deserialize<ElectionSp08ReleaseManifestArtifactRecord>(
            inputJson,
            VerificationJson.Options)
        ?? throw new JsonException("Input release manifest JSON is empty.");
    var canonicalManifest = ElectionSp08ReleaseManifestGenerator.Generate(input);
    var canonicalJson = JsonSerializer.Serialize(canonicalManifest, VerificationJson.Options);
    var manifestHash = ElectionSp08ReleaseManifestHasher.ComputeReleaseManifestHash(canonicalManifest);

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
    await File.WriteAllTextAsync(outputPath, canonicalJson);
    if (!string.IsNullOrWhiteSpace(hashOutputPath))
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(hashOutputPath))!);
        await File.WriteAllTextAsync(hashOutputPath, $"{manifestHash}{Environment.NewLine}");
    }

    Console.WriteLine($"release_manifest={Path.GetFullPath(outputPath)}");
    Console.WriteLine($"release_manifest_hash={manifestHash}");
    return 0;
}
catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

static string? GetOption(string[] args, string name)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }

    return null;
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage: HushVotingReleaseManifest --input <manifest.json> --output <HushVotingReleaseManifest-v1.json> [--hash-output <hash.txt>]");
}
