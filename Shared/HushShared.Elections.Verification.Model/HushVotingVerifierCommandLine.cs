namespace HushShared.Elections.Verification.Model;

public static class HushVotingVerifierCommandLine
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var parsed = Parse(args);
        if (parsed.Error is not null)
        {
            Console.Error.WriteLine(parsed.Error);
            return VerificationExitCodes.ProfileOrPackageMismatch;
        }

        var verifier = new HushVotingPackageVerifier();
        var result = await verifier.VerifyAsync(
            new HushVotingPackageVerificationRequest(
                parsed.PackagePath!,
                parsed.ProfileId ?? VerificationProfileIds.DevelopmentCurrentV1,
                parsed.OutputPath),
            cancellationToken);

        return result.ExitCode;
    }

    private static ParsedArguments Parse(IReadOnlyList<string> args)
    {
        string? packagePath = null;
        string? profile = null;
        string? output = null;

        for (var index = 0; index < args.Count; index++)
        {
            var token = args[index];
            if (string.Equals(token, "--package", StringComparison.Ordinal))
            {
                packagePath = ReadValue(args, ref index, token);
                if (packagePath is null)
                {
                    return new ParsedArguments(null, null, null, "The --package argument requires a value.");
                }

                continue;
            }

            if (string.Equals(token, "--profile", StringComparison.Ordinal))
            {
                profile = ReadValue(args, ref index, token);
                if (profile is null)
                {
                    return new ParsedArguments(null, null, null, "The --profile argument requires a value.");
                }

                continue;
            }

            if (string.Equals(token, "--output", StringComparison.Ordinal))
            {
                output = ReadValue(args, ref index, token);
                if (output is null)
                {
                    return new ParsedArguments(null, null, null, "The --output argument requires a value.");
                }

                continue;
            }

            return new ParsedArguments(null, null, null, $"Unsupported argument '{token}'.");
        }

        if (string.IsNullOrWhiteSpace(packagePath))
        {
            return new ParsedArguments(null, null, null, "The --package argument is required.");
        }

        if (!string.IsNullOrWhiteSpace(profile) && !VerificationProfileIds.All.Contains(profile))
        {
            return new ParsedArguments(null, null, null, $"Unsupported verifier profile '{profile}'.");
        }

        if (HushVotingPackageVerifier.IsLiveDependency(packagePath))
        {
            return new ParsedArguments(packagePath, profile, output, null);
        }

        return new ParsedArguments(packagePath, profile, output, null);
    }

    private static string? ReadValue(IReadOnlyList<string> args, ref int index, string argumentName)
    {
        if (index + 1 >= args.Count)
        {
            return null;
        }

        index++;
        var value = args[index];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value;
    }

    private sealed record ParsedArguments(
        string? PackagePath,
        string? ProfileId,
        string? OutputPath,
        string? Error);
}
