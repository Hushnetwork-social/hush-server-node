namespace HushShared.Elections.Model;

internal static class ProtocolPackageRecordValidation
{
    public static string NormalizeRequiredValue(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }

    public static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string NormalizeSha256Hash(string value, string paramName)
    {
        var normalized = NormalizeRequiredValue(value, paramName);
        if (normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["sha256:".Length..];
        }

        normalized = normalized.Trim().ToLowerInvariant();

        if (normalized.Length != 64 || normalized.Any(x => !Uri.IsHexDigit(x)))
        {
            throw new ArgumentException("Value must be a full SHA-256 hash.", paramName);
        }

        return normalized;
    }

    public static string? NormalizeOptionalSha256Hash(string? value, string paramName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeSha256Hash(value, paramName);

    public static IReadOnlyList<string> NormalizeRequiredStringList(
        IReadOnlyList<string>? values,
        string paramName)
    {
        if (values is null || values.Count == 0)
        {
            throw new ArgumentException("At least one value is required.", paramName);
        }

        return values
            .Select(x => NormalizeRequiredValue(x, paramName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<T> NormalizeRequiredRecordList<T>(
        IReadOnlyList<T>? values,
        string paramName)
    {
        if (values is null || values.Count == 0)
        {
            throw new ArgumentException("At least one value is required.", paramName);
        }

        if (values.Any(x => x is null))
        {
            throw new ArgumentException("Null values are not allowed.", paramName);
        }

        return values.ToArray();
    }
}
