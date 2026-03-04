using System.Text.RegularExpressions;

namespace HushNode.Feeds;

internal static partial class CustomCircleNameRules
{
    private const int MinLength = 3;
    private const int MaxLength = 40;

    [GeneratedRegex("^[A-Za-z0-9 _-]+$")]
    private static partial Regex AllowedNameRegex();

    public static bool TryNormalize(string? rawName, out string trimmedName, out string normalizedName)
    {
        trimmedName = string.Empty;
        normalizedName = string.Empty;

        if (string.IsNullOrWhiteSpace(rawName))
        {
            return false;
        }

        trimmedName = rawName.Trim();
        if (trimmedName.Length < MinLength || trimmedName.Length > MaxLength)
        {
            return false;
        }

        if (!AllowedNameRegex().IsMatch(trimmedName))
        {
            return false;
        }

        normalizedName = trimmedName.ToLowerInvariant();
        return true;
    }
}
