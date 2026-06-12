using Google.Protobuf;
using HushNetwork.proto;
using HushNode.Elections.gRPC;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Olimpo.EntityFramework.Persistency;
using System.Text.Json;

var arguments = CommandLineArguments.Parse(args);
var outputRoot = Path.GetFullPath(Require(arguments, "output"));
var settingsRoot = Path.GetFullPath(arguments.GetValueOrDefault("settings-root") ??
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "HushServerNode"));
var view = ResolvePackageView(arguments.GetValueOrDefault("view"));

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((_, config) =>
    {
        config.SetBasePath(settingsRoot);
        config.AddJsonFile("ApplicationSettings.json", optional: false, reloadOnChange: false);
        config.AddJsonFile("ApplicationSettings.Development.json", optional: true, reloadOnChange: false);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        services.RegisterElectionsStorageServices(context);
    });

using var host = builder.Build();
var election = await ResolveElectionAsync(host.Services, arguments);
var electionId = election.ElectionId.ToString();
var actorPublicAddress = arguments.GetValueOrDefault("actor") ?? election.OwnerPublicAddress;
var unitOfWorkProvider = host.Services.GetRequiredService<IUnitOfWorkProvider<ElectionsDbContext>>();
var queryService = new ElectionQueryApplicationService(unitOfWorkProvider);
var response = await queryService.ExportElectionVerificationPackageAsync(
    new ElectionId(Guid.Parse(electionId)),
    actorPublicAddress,
    view);

if (!response.Success)
{
    Console.Error.WriteLine($"Export failed: {response.ResultCode} {response.ErrorMessage}");
    return 2;
}

Directory.CreateDirectory(outputRoot);
foreach (var file in response.Files)
{
    var targetPath = Path.GetFullPath(Path.Combine(outputRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
    if (!targetPath.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"Refusing to write outside output root: {file.RelativePath}");
        return 3;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
    await File.WriteAllBytesAsync(targetPath, file.Content.ToByteArray());
}

var summaryPath = Path.Combine(outputRoot, "export-summary.json");
var summary = new
{
    response.ElectionId,
    response.ActorPublicAddress,
    packageView = response.PackageView.ToString(),
    response.PackageId,
    response.PackageHash,
    response.ResultCode,
    fileCount = response.Files.Count,
    exportedAt = DateTimeOffset.UtcNow,
    files = response.Files.Select(file => new
    {
        file.RelativePath,
        file.MediaType,
        visibility = file.Visibility.ToString(),
        sizeBytes = file.Content.Length,
    }),
};
await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions
{
    WriteIndented = true,
}));

Console.WriteLine($"Exported {response.Files.Count} files to {outputRoot}");
Console.WriteLine($"ElectionId: {electionId}");
Console.WriteLine($"ElectionTitle: {election.Title}");
Console.WriteLine($"ActorPublicAddress: {actorPublicAddress}");
Console.WriteLine($"PackageId: {response.PackageId}");
Console.WriteLine($"PackageHash: {response.PackageHash}");
return 0;

static async Task<ElectionRecord> ResolveElectionAsync(
    IServiceProvider services,
    IReadOnlyDictionary<string, string> arguments)
{
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ElectionsDbContext>();

    if (arguments.TryGetValue("election-id", out var electionIdValue) &&
        !string.IsNullOrWhiteSpace(electionIdValue))
    {
        var electionId = ElectionIdHandler.CreateFromString(electionIdValue);
        return await db.Elections.AsNoTracking().SingleAsync(x => x.ElectionId == electionId);
    }

    var title = Require(arguments, "title");
    var matches = await db.Elections
        .AsNoTracking()
        .Where(x => x.Title == title)
        .OrderByDescending(x => x.LastUpdatedAt)
        .ToListAsync();

    if (matches.Count == 0)
    {
        matches = await db.Elections
            .AsNoTracking()
            .Where(x => EF.Functions.ILike(x.Title, $"%{title}%"))
            .OrderByDescending(x => x.LastUpdatedAt)
            .ToListAsync();
    }

    if (matches.Count == 0)
    {
        throw new ArgumentException($"No election found matching title '{title}'.");
    }

    if (matches.Count > 1)
    {
        Console.Error.WriteLine($"Multiple elections matched title '{title}'; exporting the most recently updated one.");
        foreach (var match in matches.Take(10))
        {
            Console.Error.WriteLine($"- {match.ElectionId} | {match.LastUpdatedAt:O} | {match.LifecycleState} | {match.Title}");
        }
    }

    return matches[0];
}

static string Require(IReadOnlyDictionary<string, string> arguments, string name)
{
    if (arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
    {
        return value;
    }

    throw new ArgumentException($"Missing required --{name} argument.");
}

static ElectionVerificationPackageViewProto ResolvePackageView(string? value) =>
    string.Equals(value, "restricted", StringComparison.OrdinalIgnoreCase)
        ? ElectionVerificationPackageViewProto.VerificationPackageRestrictedOwnerAuditor
        : ElectionVerificationPackageViewProto.VerificationPackagePublicAnonymous;

internal static class CommandLineArguments
{
    public static IReadOnlyDictionary<string, string> Parse(string[] args)
    {
        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var raw = args[index];
            if (!raw.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var name = raw[2..];
            var value = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++index]
                : "true";
            parsed[name] = value;
        }

        return parsed;
    }
}
