using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using VerifierCorpusPromoter;

try
{
    var arguments = CommandLineArguments.Parse(args);
    if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine("VerifierCorpusPromoter validates and generates the HushVoting public verifier corpus.");
        Console.WriteLine("Options: --workspace-root, --public-output-root, --corpus-version, --generated-at, --public-repository-ref, --verifier-source-ref, --verifier-hash, --windows-reviewer-replay-validated, --linux-reviewer-replay-validated, --validate-only, --check-only.");
        Console.WriteLine("CI replay: --replay-ci, --corpus-root, --corpus-repository-ref, --workflow-name, --workflow-path, --workflow-run-id, --workflow-run-attempt.");
        Console.WriteLine("The promoter writes no commits and performs no git push.");
        return 0;
    }

    if (arguments.ContainsKey("replay-ci"))
    {
        var ciCorpusVersion = CommandLineArguments.TryGetValue(arguments, "corpus-version", out var configuredCiCorpusVersion)
            ? configuredCiCorpusVersion
            : "v0.3.0";
        var ciGeneratedAt = CommandLineArguments.TryGetValue(arguments, "generated-at", out var configuredCiGeneratedAt)
            ? DateTimeOffset.Parse(configuredCiGeneratedAt, null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            : DateTimeOffset.UtcNow;
        var ciVerifierSourceRef = CommandLineArguments.TryGetValue(arguments, "verifier-source-ref", out var configuredCiVerifierSourceRef)
            ? configuredCiVerifierSourceRef
            : "0000000000000000000000000000000000000000";
        var ciVerifierHash = CommandLineArguments.TryGetValue(arguments, "verifier-hash", out var configuredCiVerifierHash)
            ? configuredCiVerifierHash
            : "sha256:0000000000000000000000000000000000000000000000000000000000000000";
        var replayOptions = new VerifierCorpusCiReplayOptions(
            CommandLineArguments.TryGetValue(arguments, "corpus-root", out var ciCorpusRoot)
                ? ciCorpusRoot
                : Path.Combine(Directory.GetCurrentDirectory(), VerifierCorpusGenerator.DefaultCorpusFamily, ciCorpusVersion),
            ciGeneratedAt,
            CorpusRepository: CommandLineArguments.TryGetValue(arguments, "corpus-repository", out var ciCorpusRepository)
                ? ciCorpusRepository
                : "https://github.com/Hushnetwork-social/HushVoting-Verifier-Corpus",
            CorpusRepositoryRef: CommandLineArguments.TryGetValue(arguments, "corpus-repository-ref", out var ciCorpusRepositoryRef)
                ? ciCorpusRepositoryRef
                : "0000000000000000000000000000000000000000",
            CorpusVersion: ciCorpusVersion,
            VerifierRepository: CommandLineArguments.TryGetValue(arguments, "verifier-repository", out var ciVerifierRepository)
                ? ciVerifierRepository
                : "https://github.com/Hushnetwork-social/hush-server-node",
            VerifierSourceRef: ciVerifierSourceRef,
            VerifierHash: ciVerifierHash,
            WorkflowName: CommandLineArguments.TryGetValue(arguments, "workflow-name", out var ciWorkflowName)
                ? ciWorkflowName
                : "local-verifier-corpus-ci",
            WorkflowPath: CommandLineArguments.TryGetValue(arguments, "workflow-path", out var ciWorkflowPath)
                ? ciWorkflowPath
                : ".github/workflows/verifier-corpus-ci.yml",
            WorkflowRunId: CommandLineArguments.TryGetValue(arguments, "workflow-run-id", out var ciWorkflowRunId)
                ? ciWorkflowRunId
                : "local-replay",
            WorkflowRunAttempt: TryGetPositiveInt(arguments, "workflow-run-attempt", out var ciWorkflowRunAttempt)
                ? ciWorkflowRunAttempt
                : 1);

        var replay = await new VerifierCorpusCiReplayRunner().ReplayAsync(replayOptions);
        Console.WriteLine($"Replayed verifier corpus {replayOptions.CorpusVersion}");
        Console.WriteLine($"Corpus root: {replay.CorpusRoot}");
        Console.WriteLine($"Run status: {replay.RunStatus}");
        Console.WriteLine($"Public safety: {replay.PublicSafetyStatus}");
        Console.WriteLine($"Fixtures: {replay.MatchedFixtureCount}/{replay.FixtureCount} matched");
        Console.WriteLine($"Mismatches: {replay.MismatchCount}");
        Console.WriteLine($"Unexpected public findings: {replay.UnexpectedPublicFindingCount}");
        foreach (var error in replay.ContractErrors)
        {
            Console.Error.WriteLine($"- {error}");
        }

        return replay.Passed ? 0 : 2;
    }

    var workspaceRoot = CommandLineArguments.TryGetValue(arguments, "workspace-root", out var configuredWorkspaceRoot)
        ? configuredWorkspaceRoot
        : WorkspaceRootFinder.Find(Directory.GetCurrentDirectory());
    var paths = VerifierCorpusPromotionPaths.FromWorkspaceRoot(workspaceRoot);
    var serverRoot = Path.Combine(paths.WorkspaceRoot, "hush-server-node");
    var verifierProjectPath = Path.Combine(serverRoot, "Tools", "HushVotingVerifier", "HushVotingVerifier.csproj");

    var options = new VerifierCorpusPromotionOptions(
        paths,
        CommandLineArguments.TryGetValue(arguments, "public-output-root", out var publicOutputRoot)
            ? publicOutputRoot
            : paths.PublicOutputRoot,
        CommandLineArguments.TryGetValue(arguments, "corpus-version", out var corpusVersion)
            ? corpusVersion
            : "v0.1.0",
        CommandLineArguments.TryGetValue(arguments, "generated-at", out var generatedAt)
            ? DateTimeOffset.Parse(generatedAt, null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            : DateTimeOffset.UtcNow,
        arguments.ContainsKey("validate-only"),
        arguments.ContainsKey("check-only"),
        CommandLineArguments.TryGetValue(arguments, "public-repository-ref", out var publicRepositoryRef)
            ? publicRepositoryRef
            : "local-generated",
        CommandLineArguments.TryGetValue(arguments, "verifier-source-ref", out var verifierSourceRef)
            ? verifierSourceRef
            : ResolveGitHead(serverRoot),
        CommandLineArguments.TryGetValue(arguments, "verifier-hash", out var verifierHash)
            ? verifierHash
            : Sha256File(verifierProjectPath),
        arguments.ContainsKey("windows-reviewer-replay-validated") ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
        arguments.ContainsKey("linux-reviewer-replay-validated") ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux));

    var result = await new VerifierCorpusPromotionService().PromoteAsync(options);
    Console.WriteLine(result.Mode switch
    {
        VerifierCorpusPromotionService.ModeValidateOnly => $"Validated HushVoting verifier corpus {result.CorpusVersion}",
        VerifierCorpusPromotionService.ModeCheckOnly => $"Checked HushVoting verifier corpus {result.CorpusVersion}",
        _ => $"Generated HushVoting verifier corpus {result.CorpusVersion}",
    });
    Console.WriteLine($"Output root: {result.OutputRoot}");
    Console.WriteLine($"Generated at: {result.GeneratedAt:O}");
    Console.WriteLine($"Manifest hash: {result.ManifestHash}");
    Console.WriteLine($"Fixture index hash: {result.FixtureIndexHash}");
    Console.WriteLine($"Fixtures: {result.FixtureCount}");
    Console.WriteLine($"No-secret scan: {result.NoSecretScanStatus}");
    Console.WriteLine($"Scan findings: {result.ScanFindingCount}");
    Console.WriteLine($"Unexpected scan findings: {result.UnexpectedScanFindingCount}");
    Console.WriteLine($"Written files: {result.WrittenFiles.Count}");
    return result.NoSecretScanStatus == "pass" ? 0 : 2;
}
catch (VerifierCorpusPromotionException ex)
{
    Console.Error.WriteLine(ex.Message);
    foreach (var detail in ex.Details)
    {
        Console.Error.WriteLine($"- {detail}");
    }

    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static string ResolveGitHead(string repositoryRoot)
{
    try
    {
        var gitPath = Path.Combine(repositoryRoot, ".git");
        var gitDirectory = Directory.Exists(gitPath)
            ? gitPath
            : ResolveGitDirectoryFromFile(gitPath, repositoryRoot);
        if (gitDirectory is null)
        {
            return "local-working-tree";
        }

        var head = File.ReadAllText(Path.Combine(gitDirectory, "HEAD")).Trim();
        if (!head.StartsWith("ref:", StringComparison.Ordinal))
        {
            return head;
        }

        var refPath = head["ref:".Length..].Trim().Replace('/', Path.DirectorySeparatorChar);
        var resolvedRefPath = Path.Combine(gitDirectory, refPath);
        return File.Exists(resolvedRefPath)
            ? File.ReadAllText(resolvedRefPath).Trim()
            : "local-working-tree";
    }
    catch
    {
        return "local-working-tree";
    }
}

static string? ResolveGitDirectoryFromFile(string gitFilePath, string repositoryRoot)
{
    if (!File.Exists(gitFilePath))
    {
        return null;
    }

    var content = File.ReadAllText(gitFilePath).Trim();
    if (!content.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    var path = content["gitdir:".Length..].Trim();
    return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(repositoryRoot, path));
}

static string Sha256File(string path)
{
    if (!File.Exists(path))
    {
        return "sha256:not-computed";
    }

    var hash = SHA256.HashData(File.ReadAllBytes(path));
    return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
}

static bool TryGetPositiveInt(
    IReadOnlyDictionary<string, string?> arguments,
    string name,
    out int value)
{
    value = 0;
    return CommandLineArguments.TryGetValue(arguments, name, out var text) &&
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
        value > 0;
}
