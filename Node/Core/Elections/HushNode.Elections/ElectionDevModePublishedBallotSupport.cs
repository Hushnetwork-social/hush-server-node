using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HushShared.Elections.Model;

namespace HushNode.Elections;

internal static class ElectionDevModePublishedBallotSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool TryBuildPublishedBallotTally(
        ElectionRecord election,
        IReadOnlyList<ElectionPublishedBallotRecord> publishedBallots,
        out ElectionDevModePublishedBallotTally? tally)
    {
        tally = null;
        if (publishedBallots.Count == 0)
        {
            return false;
        }

        var optionById = election.Options.ToDictionary(
            x => x.OptionId,
            StringComparer.OrdinalIgnoreCase);
        var countsByOptionId = optionById.Keys.ToDictionary(
            x => x,
            _ => 0,
            StringComparer.OrdinalIgnoreCase);

        foreach (var publishedBallot in publishedBallots.OrderBy(x => x.PublicationSequence))
        {
            ElectionDevModePublishedBallotPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<ElectionDevModePublishedBallotPayload>(
                    publishedBallot.EncryptedBallotPackage,
                    JsonOptions);
            }
            catch (JsonException)
            {
                return false;
            }

            if (payload is null ||
                !string.Equals(payload.Mode, "election-dev-mode-v1", StringComparison.OrdinalIgnoreCase) ||
                !IsSupportedPackageType(payload.PackageType) ||
                string.IsNullOrWhiteSpace(payload.OptionId) ||
                !string.Equals(payload.ElectionId, election.ElectionId.ToString(), StringComparison.OrdinalIgnoreCase) ||
                !optionById.TryGetValue(payload.OptionId, out var option) ||
                option.IsBlankOption != payload.IsBlankOption ||
                option.BallotOrder != payload.BallotOrder)
            {
                return false;
            }

            countsByOptionId[option.OptionId] = countsByOptionId[option.OptionId] + 1;
        }

        var blankCount = election.Options
            .Where(x => x.IsBlankOption)
            .Sum(x => countsByOptionId.GetValueOrDefault(x.OptionId, 0));
        var namedOptionResults = election.Options
            .Where(x => !x.IsBlankOption)
            .Select(x => new
            {
                Option = x,
                VoteCount = countsByOptionId.GetValueOrDefault(x.OptionId, 0),
            })
            .OrderByDescending(x => x.VoteCount)
            .ThenBy(x => x.Option.BallotOrder)
            .Select((x, index) => new ElectionResultOptionCount(
                x.Option.OptionId,
                x.Option.DisplayLabel,
                x.Option.ShortDescription,
                x.Option.BallotOrder,
                index + 1,
                x.VoteCount))
            .ToArray();
        var totalVotedCount = countsByOptionId.Values.Sum();
        var tallyPayload = string.Join(
            '\n',
            election.Options
                .OrderBy(x => x.BallotOrder)
                .Select(x => $"{x.OptionId}|{countsByOptionId.GetValueOrDefault(x.OptionId, 0)}"));

        // Keep the existing dev-mode tally hash formula stable until FEAT-105 replaces it.
        var finalEncryptedTallyHash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"admin-only-dev-tally:v1|{election.ElectionId}|{tallyPayload}"));

        tally = new ElectionDevModePublishedBallotTally(
            namedOptionResults,
            blankCount,
            totalVotedCount,
            finalEncryptedTallyHash);
        return true;
    }

    private static bool IsSupportedPackageType(string? packageType) =>
        string.Equals(packageType, "dev-published-ballot", StringComparison.OrdinalIgnoreCase);

    private sealed record ElectionDevModePublishedBallotPayload(
        string? Mode,
        string? PackageType,
        string? ElectionId,
        string? OptionId,
        string? OptionLabel,
        string? OptionDescription,
        int BallotOrder,
        bool IsBlankOption,
        string? PublicationNonce);
}

internal sealed record ElectionDevModePublishedBallotTally(
    IReadOnlyList<ElectionResultOptionCount> NamedOptionResults,
    int BlankCount,
    int TotalVotedCount,
    byte[] FinalEncryptedTallyHash);
