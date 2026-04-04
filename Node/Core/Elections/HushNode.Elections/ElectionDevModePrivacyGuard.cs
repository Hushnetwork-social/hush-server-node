using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HushShared.Elections.Model;

namespace HushNode.Elections;

internal static class ElectionDevModePrivacyGuard
{
    private const string DevMode = "election-dev-mode-v1";

    public static bool TryValidateCommitmentRegistration(
        ElectionId electionId,
        string actorPublicAddress,
        string commitmentHash,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(commitmentHash))
        {
            return true;
        }

        var legacyCommitmentHash = ComputeLowerHexSha256(
            $"election-dev-commitment:v1:{electionId}:{actorPublicAddress}");
        if (string.Equals(
                commitmentHash.Trim(),
                legacyCommitmentHash,
                StringComparison.OrdinalIgnoreCase))
        {
            errorMessage =
                "Legacy dev-mode commitment material derived from the voter address is no longer accepted. Refresh the client and register a new commitment.";
            return false;
        }

        return true;
    }

    public static bool TryValidateAcceptedBallotArtifacts(
        ElectionId electionId,
        string actorPublicAddress,
        string encryptedBallotPackage,
        string proofBundle,
        string ballotNullifier,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        var legacyBallotNullifier = ComputeLowerHexSha256(
            $"election-dev-nullifier:v1:{electionId}:{actorPublicAddress}");
        if (string.Equals(
                ballotNullifier?.Trim(),
                legacyBallotNullifier,
                StringComparison.OrdinalIgnoreCase))
        {
            errorMessage =
                "Legacy dev-mode ballot nullifiers derived from the voter address are no longer accepted. Refresh the client and submit a new ballot.";
            return false;
        }

        if (IsDevModePayload(encryptedBallotPackage, out var ballotPackage))
        {
            if (HasNonEmptyStringProperty(ballotPackage, "actorPublicAddress"))
            {
                errorMessage =
                    "Dev-mode ballot packages must not embed the voter actor address.";
                return false;
            }
        }

        if (IsDevModePayload(proofBundle, out var proofPayload))
        {
            if (HasNonEmptyStringProperty(proofPayload, "actorPublicAddress") ||
                HasNonEmptyStringProperty(proofPayload, "commitmentHash") ||
                HasNonEmptyStringProperty(proofPayload, "ballotNullifier"))
            {
                errorMessage =
                    "Dev-mode proof bundles must not embed voter-linking material.";
                return false;
            }
        }

        return true;
    }

    private static bool IsDevModePayload(string payload, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            root = document.RootElement.Clone();
            return TryGetStringProperty(root, "mode", out var mode) &&
                   string.Equals(mode, DevMode, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasNonEmptyStringProperty(JsonElement root, string propertyName) =>
        TryGetStringProperty(root, propertyName, out var value) &&
        !string.IsNullOrWhiteSpace(value);

    private static bool TryGetStringProperty(
        JsonElement root,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static string ComputeLowerHexSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())))
            .ToLowerInvariant();
}
