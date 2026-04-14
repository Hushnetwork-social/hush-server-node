using System.Globalization;
using System.Text.Json;
using Grpc.Core;
using Olimpo;

namespace HushNode.Elections.gRPC;

internal static class ElectionQueryRequestAuthValidator
{
    private const double ElectionQueryAuthWindowMinutes = 10;
    private const string SignatoryHeader = "x-hush-election-query-signatory";
    private const string SignedAtHeader = "x-hush-election-query-signed-at";
    private const string SignatureHeader = "x-hush-election-query-signature";

    public static void ValidateOrThrow(
        string method,
        string actorAddress,
        IReadOnlyDictionary<string, object?> request,
        ServerCallContext context)
    {
        var normalizedActorAddress = NormalizeAddress(actorAddress);
        if (string.IsNullOrWhiteSpace(normalizedActorAddress))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"Election query {method} requires a bound actor address."));
        }

        var signatory = NormalizeAddress(context.RequestHeaders.GetValue(SignatoryHeader));
        var signedAt = context.RequestHeaders.GetValue(SignedAtHeader)?.Trim() ?? string.Empty;
        var signature = context.RequestHeaders.GetValue(SignatureHeader)?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(signatory) ||
            string.IsNullOrWhiteSpace(signedAt) ||
            string.IsNullOrWhiteSpace(signature))
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                $"Election query {method} requires signed actor-bound headers."));
        }

        if (!AddressesEqual(signatory, normalizedActorAddress))
        {
            throw new RpcException(new Status(
                StatusCode.PermissionDenied,
                $"Election query {method} actor mismatch."));
        }

        if (!DateTimeOffset.TryParse(
                signedAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var signedAtValue))
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                $"Election query {method} contains an invalid signature timestamp."));
        }

        if (Math.Abs((DateTimeOffset.UtcNow - signedAtValue).TotalMinutes) > ElectionQueryAuthWindowMinutes)
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                $"Election query {method} signature is expired."));
        }

        var payload = BuildSignedPayload(method, normalizedActorAddress, signedAt, request);
        if (!DigitalSignature.VerifyCompactSignatureBase64(payload, signature, signatory))
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                $"Election query {method} signature is invalid."));
        }
    }

    public static string? ValidateOptionalOrResolveActor(
        string method,
        IReadOnlyDictionary<string, object?> request,
        ServerCallContext context)
    {
        var signatory = NormalizeAddress(context.RequestHeaders.GetValue(SignatoryHeader));
        var signedAt = context.RequestHeaders.GetValue(SignedAtHeader)?.Trim() ?? string.Empty;
        var signature = context.RequestHeaders.GetValue(SignatureHeader)?.Trim() ?? string.Empty;
        var hasAnyAuthHeader =
            !string.IsNullOrWhiteSpace(signatory) ||
            !string.IsNullOrWhiteSpace(signedAt) ||
            !string.IsNullOrWhiteSpace(signature);

        if (!hasAnyAuthHeader)
        {
            return null;
        }

        ValidateOrThrow(method, signatory, request, context);
        return signatory;
    }

    private static string BuildSignedPayload(
        string method,
        string actorAddress,
        string signedAt,
        IReadOnlyDictionary<string, object?> request)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["actorAddress"] = actorAddress,
            ["method"] = method,
            ["request"] = DeepSort(request),
            ["signedAt"] = signedAt,
        };

        return JsonSerializer.Serialize(payload);
    }

    private static object? DeepSort(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            var sortedDictionary = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var entry in readOnlyDictionary)
            {
                sortedDictionary[entry.Key] = DeepSort(entry.Value);
            }

            return sortedDictionary;
        }

        if (value is IDictionary<string, object?> dictionary)
        {
            var sortedDictionary = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var entry in dictionary)
            {
                sortedDictionary[entry.Key] = DeepSort(entry.Value);
            }

            return sortedDictionary;
        }

        if (value is IEnumerable<object?> sequence && value is not string)
        {
            return sequence.Select(DeepSort).ToArray();
        }

        return value;
    }

    private static string NormalizeAddress(string? value) =>
        value?.Trim() ?? string.Empty;

    private static bool AddressesEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
