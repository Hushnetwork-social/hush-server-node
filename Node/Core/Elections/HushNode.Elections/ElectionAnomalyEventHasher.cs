using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public static class ElectionAnomalyEventHasher
{
    public const string EventCanonicalizationVersion = "hush-election-anomaly-event-v1";
    public const string ThreadCanonicalizationVersion = "hush-election-anomaly-thread-v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    public static string ComputeEventHash(ElectionAnomalyThreadEventRecord threadEvent)
    {
        var input = new EventHashInput(
            EventCanonicalizationVersion,
            threadEvent.ElectionId.ToString(),
            threadEvent.AnomalyThreadId,
            threadEvent.Sequence,
            threadEvent.EventTypeId,
            CanonicalizePayloadJson(threadEvent.EventPayloadJson),
            threadEvent.PreviousEventHash,
            threadEvent.ActionNonce,
            threadEvent.SourceTransactionId,
            threadEvent.ActorPublicAddress,
            FormatUtc(threadEvent.OccurredAt));

        return ComputeSha256Reference(JsonSerializer.SerializeToUtf8Bytes(input, JsonOptions));
    }

    public static string ComputeThreadHash(IEnumerable<ElectionAnomalyThreadEventRecord> threadEvents)
    {
        var eventHashes = threadEvents
            .OrderBy(x => x.Sequence)
            .ThenBy(x => x.OccurredAt)
            .Select(x => x.EventHash)
            .ToArray();

        var input = new ThreadHashInput(ThreadCanonicalizationVersion, eventHashes);

        return ComputeSha256Reference(JsonSerializer.SerializeToUtf8Bytes(input, JsonOptions));
    }

    public static string CanonicalizePayloadJson(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return "{}";
        }

        using var document = JsonDocument.Parse(payloadJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonicalElement(document.RootElement, writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonicalElement(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalElement(property.Value, writer);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalElement(item, writer);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string FormatUtc(DateTime value)
    {
        var utcValue = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

        return utcValue.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string ComputeSha256Reference(byte[] bytes) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";

    private sealed record EventHashInput(
        string CanonicalizationVersion,
        string ElectionId,
        Guid AnomalyThreadId,
        int Sequence,
        string EventTypeId,
        string EventPayloadJson,
        string? PreviousEventHash,
        Guid ActionNonce,
        Guid SourceTransactionId,
        string ActorPublicAddress,
        string OccurredAtUtc);

    private sealed record ThreadHashInput(
        string CanonicalizationVersion,
        IReadOnlyList<string> EventHashes);
}
