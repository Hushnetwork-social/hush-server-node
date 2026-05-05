using System.Security.Cryptography;
using System.Text.Json;

namespace HushShared.Elections.Model;

public static class ElectionBallotDefinitionCanonicalizer
{
    public const int CurrentVersion = 1;
    public const string PayloadKind = "hushvoting.ballot-definition.v1";
    public const string HashAlgorithm = "SHA-256";

    public static byte[] ComputeHash(ElectionRecord election)
    {
        ArgumentNullException.ThrowIfNull(election);

        return ComputeHash(
            election.Title,
            election.ShortDescription,
            election.ElectionClass,
            election.BindingStatus,
            election.VoteUpdatePolicy,
            election.OutcomeRule,
            election.Options);
    }

    public static byte[] ComputeHash(
        string title,
        string? shortDescription,
        ElectionClass electionClass,
        ElectionBindingStatus bindingStatus,
        VoteUpdatePolicy voteUpdatePolicy,
        OutcomeRuleDefinition outcomeRule,
        IReadOnlyList<ElectionOptionDefinition> options) =>
        SHA256.HashData(SerializeCanonicalPayload(
            title,
            shortDescription,
            electionClass,
            bindingStatus,
            voteUpdatePolicy,
            outcomeRule,
            options));

    public static byte[] SerializeCanonicalPayload(
        string title,
        string? shortDescription,
        ElectionClass electionClass,
        ElectionBindingStatus bindingStatus,
        VoteUpdatePolicy voteUpdatePolicy,
        OutcomeRuleDefinition outcomeRule,
        IReadOnlyList<ElectionOptionDefinition> options)
    {
        ArgumentNullException.ThrowIfNull(outcomeRule);
        ArgumentNullException.ThrowIfNull(options);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            stream,
            new JsonWriterOptions
            {
                Indented = false,
                SkipValidation = false,
            }))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", PayloadKind);
            writer.WriteNumber("version", CurrentVersion);
            writer.WriteString("title", NormalizeRequiredText(title, nameof(title)));
            WriteOptionalString(writer, "shortDescription", NormalizeOptionalText(shortDescription));
            writer.WriteString("electionClass", electionClass.ToString());
            writer.WriteString("bindingStatus", bindingStatus.ToString());
            writer.WriteString("voteUpdatePolicy", voteUpdatePolicy.ToString());

            writer.WritePropertyName("outcomeRule");
            writer.WriteStartObject();
            writer.WriteString("kind", outcomeRule.Kind.ToString());
            writer.WriteString("templateKey", NormalizeRequiredText(outcomeRule.TemplateKey, nameof(outcomeRule)));
            writer.WriteNumber("seatCount", outcomeRule.SeatCount);
            writer.WriteBoolean("blankVoteCountsForTurnout", outcomeRule.BlankVoteCountsForTurnout);
            writer.WriteBoolean("blankVoteExcludedFromWinnerSelection", outcomeRule.BlankVoteExcludedFromWinnerSelection);
            writer.WriteBoolean("blankVoteExcludedFromThresholdDenominator", outcomeRule.BlankVoteExcludedFromThresholdDenominator);
            writer.WriteString("tieResolutionRule", NormalizeRequiredText(outcomeRule.TieResolutionRule, nameof(outcomeRule)));
            writer.WriteString("calculationBasis", NormalizeRequiredText(outcomeRule.CalculationBasis, nameof(outcomeRule)));
            writer.WriteEndObject();

            writer.WritePropertyName("options");
            writer.WriteStartArray();
            foreach (var option in options.OrderBy(x => x.BallotOrder).ThenBy(x => x.OptionId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("optionId", NormalizeRequiredText(option.OptionId, nameof(options)));
                writer.WriteString("displayLabel", NormalizeRequiredText(option.DisplayLabel, nameof(options)));
                WriteOptionalString(writer, "shortDescription", NormalizeOptionalText(option.ShortDescription));
                writer.WriteNumber("ballotOrder", option.BallotOrder);
                writer.WriteBoolean("isBlankOption", option.IsBlankOption);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteString(propertyName, value);
    }

    private static string NormalizeRequiredText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
