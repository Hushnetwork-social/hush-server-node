namespace DeploymentProofPackagePromoter;

public static class CommandLineArguments
{
    public static IReadOnlyDictionary<string, string?> Parse(string[] args)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument: {arg}");
            }

            var key = arg[2..];
            var hasValue = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal);
            result[key] = hasValue ? args[++index] : null;
        }

        return result;
    }

    public static bool TryGetValue(
        this IReadOnlyDictionary<string, string?> arguments,
        string name,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
    {
        if (arguments.TryGetValue(name, out value) && !string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        value = null;
        return false;
    }
}
