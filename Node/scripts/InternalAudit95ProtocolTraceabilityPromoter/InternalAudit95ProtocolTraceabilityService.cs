namespace InternalAudit95ProtocolTraceabilityPromoter;

public sealed record InternalAudit95ProtocolTraceabilityOptions(
    InternalAudit95ProtocolTraceabilityPaths Paths,
    string? Mode,
    string? SourceInput,
    string? OutputRoot,
    DateTimeOffset? GeneratedAt,
    bool ValidateOnly);

public sealed record InternalAudit95ProtocolTraceabilityResult(
    string Mode,
    string Status,
    string PackageRoot,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<string> CheckedFiles,
    InternalAudit95ProtocolTraceabilityGeneratedPackage GeneratedPackage);

public sealed class InternalAudit95ProtocolTraceabilityService
{
    public const string ModeCheckOnly = "check-only";
    public const string ModePackage = "package";
    public const string ModeValidateOnly = "validate-only";

    public InternalAudit95ProtocolTraceabilityResult Promote(InternalAudit95ProtocolTraceabilityOptions options)
    {
        ValidateSchemas(options.Paths);
        var mode = ResolveMode(options);
        var generated = InternalAudit95ProtocolTraceabilityArtifactGenerator.Generate(
            options.Paths,
            options.SourceInput,
            options.GeneratedAt);
        var packageRoot = ResolvePackageRoot(options);

        return mode switch
        {
            ModeValidateOnly => CreateResult(mode, packageRoot, [], [], generated),
            ModePackage => WritePackage(mode, packageRoot, generated),
            ModeCheckOnly => CheckPackage(mode, packageRoot, generated),
            _ => throw new InternalAudit95ProtocolTraceabilityException(
                "Unsupported FEAT-157 protocol traceability mode.",
                [$"Mode '{mode}' is not supported. Use validate-only, package, or check-only."]),
        };
    }

    private static void ValidateSchemas(InternalAudit95ProtocolTraceabilityPaths paths)
    {
        var schemaErrors = InternalAudit95ProtocolTraceabilityContracts.ValidateSchemaSet(paths.SchemasRoot);
        if (schemaErrors.Count > 0)
        {
            throw new InternalAudit95ProtocolTraceabilityException(
                "FEAT-157 protocol traceability schema validation failed.",
                schemaErrors);
        }
    }

    private static string ResolveMode(InternalAudit95ProtocolTraceabilityOptions options) =>
        options.ValidateOnly
            ? ModeValidateOnly
            : (options.Mode ?? ModeCheckOnly).Trim().ToLowerInvariant();

    private static string ResolvePackageRoot(InternalAudit95ProtocolTraceabilityOptions options)
    {
        var outputRoot = Path.GetFullPath(options.OutputRoot ?? options.Paths.OutputRoot);
        var packageRoot = Path.GetFullPath(Path.Combine(outputRoot, "package"));
        InternalAudit95ProtocolTraceabilityContracts.EnsurePathUnder(outputRoot, packageRoot, "FEAT-157 package root");
        return packageRoot;
    }

    private static InternalAudit95ProtocolTraceabilityResult WritePackage(
        string mode,
        string packageRoot,
        InternalAudit95ProtocolTraceabilityGeneratedPackage generated)
    {
        Directory.CreateDirectory(packageRoot);
        var writtenFiles = new List<string>();
        foreach (var artifact in generated.Artifacts)
        {
            var path = ResolveArtifactPath(packageRoot, artifact.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, artifact.Content);
            writtenFiles.Add(path);
        }

        return CreateResult(mode, packageRoot, writtenFiles, [], generated);
    }

    private static InternalAudit95ProtocolTraceabilityResult CheckPackage(
        string mode,
        string packageRoot,
        InternalAudit95ProtocolTraceabilityGeneratedPackage generated)
    {
        if (!Directory.Exists(packageRoot))
        {
            throw new InternalAudit95ProtocolTraceabilityException(
                "FEAT-157 protocol traceability package check failed.",
                [$"Package root does not exist: {packageRoot}"]);
        }

        var errors = new List<string>();
        var checkedFiles = new List<string>();
        foreach (var artifact in generated.Artifacts)
        {
            var path = ResolveArtifactPath(packageRoot, artifact.RelativePath);
            if (!File.Exists(path))
            {
                errors.Add($"Missing package artifact: {artifact.RelativePath}");
                continue;
            }

            checkedFiles.Add(path);
            var observedHash = InternalAudit95ProtocolTraceabilityContracts.Sha256Hex(File.ReadAllText(path));
            if (!string.Equals(observedHash, artifact.Sha256Hash, StringComparison.Ordinal))
            {
                errors.Add($"Hash mismatch for {artifact.RelativePath}: expected {artifact.Sha256Hash}, observed {observedHash}");
            }
        }

        if (errors.Count > 0)
        {
            throw new InternalAudit95ProtocolTraceabilityException(
                "FEAT-157 protocol traceability package check failed.",
                errors);
        }

        return CreateResult(mode, packageRoot, [], checkedFiles, generated);
    }

    private static string ResolveArtifactPath(string packageRoot, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(packageRoot, relativePath));
        InternalAudit95ProtocolTraceabilityContracts.EnsurePathUnder(packageRoot, path, relativePath);
        return path;
    }

    private static InternalAudit95ProtocolTraceabilityResult CreateResult(
        string mode,
        string packageRoot,
        IReadOnlyList<string> writtenFiles,
        IReadOnlyList<string> checkedFiles,
        InternalAudit95ProtocolTraceabilityGeneratedPackage generated) =>
        new(mode, generated.Status, packageRoot, writtenFiles, checkedFiles, generated);
}
