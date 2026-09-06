// FEAT-015 Task 6.1 — authenticated-query validator for GetMyEntitlement.
//
// Dependency-safe extraction of the established election signed-query pattern:
//   - metadata: x-hush-licence-query-signatory / -signed-at / -signature (module naming);
//   - the actor IS the canonical signatory (the request carries no selectable identity);
//   - signed UTF-8 JSON = {actorAddress, method, request, signedAt} with ordinal deep-sort;
//   - compact base64 signature (Approved FEAT-001 encoding);
//   - canonical addresses (trim + invariant lower);
//   - ten-minute absolute freshness window;
//   - authentication happens BEFORE any identity or entitlement lookup.
// FEAT-015 does not alter election RPC behavior, headers, or signed bytes.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Grpc.Core;
using static Olimpo.DigitalSignature;

namespace HushNode.HushVoting.Licence.gRPC;

/// <summary>Licence query signed-metadata validator (strictly observational).</summary>
public static class LicenceQueryRequestAuthValidator
{
    public const string SignatoryHeader = "x-hush-licence-query-signatory";
    public const string SignedAtHeader = "x-hush-licence-query-signed-at";
    public const string SignatureHeader = "x-hush-licence-query-signature";

    private const double AuthWindowMinutes = 10;

    /// <summary>Validates the signed actor-bound metadata and returns the canonical signatory actor.</summary>
    public static string ValidateOrResolveActor(
        string method,
        ServerCallContext context)
    {
        var signatory = NormalizeAddress(context.RequestHeaders.GetValue(SignatoryHeader));
        var signedAt = context.RequestHeaders.GetValue(SignedAtHeader)?.Trim() ?? string.Empty;
        var signature = context.RequestHeaders.GetValue(SignatureHeader)?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(signatory)
            || string.IsNullOrWhiteSpace(signedAt)
            || string.IsNullOrWhiteSpace(signature))
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                $"Licence query {method} requires signed actor-bound headers."));
        }

        if (!DateTimeOffset.TryParse(
                signedAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var signedAtValue))
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                $"Licence query {method} contains an invalid signature timestamp."));
        }

        if (Math.Abs((DateTimeOffset.UtcNow - signedAtValue).TotalMinutes) > AuthWindowMinutes)
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                $"Licence query {method} signature is expired."));
        }

        var payload = BuildSignedPayload(method, signatory, signedAt);
        if (!VerifyCompactSignatureBase64(payload, signature, signatory))
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                $"Licence query {method} signature is invalid."));
        }

        return signatory;
    }

    /// <summary>Canonical unsigned payload bytes = {actorAddress, method, request:{}, signedAt} ordinal deep-sort.</summary>
    public static string BuildSignedPayload(string method, string canonicalActorAddress, string signedAt)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["actorAddress"] = canonicalActorAddress,
            ["method"] = method,
            ["request"] = new SortedDictionary<string, object?>(StringComparer.Ordinal),
            ["signedAt"] = signedAt,
        };

        return JsonSerializer.Serialize(payload);
    }

    /// <summary>Canonical UTF-8 bytes of the signed payload (byte-exact, locked by contract tests).</summary>
    public static byte[] CanonicalBytes(string method, string canonicalActorAddress, string signedAt) =>
        Encoding.UTF8.GetBytes(BuildSignedPayload(method, canonicalActorAddress, signedAt));

    internal static string NormalizeAddress(string? value) =>
        value?.Trim().ToLowerInvariant() ?? string.Empty;
}
