using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HushIdentityCompatibilityConformance.Adapters;

/// <summary>
/// Canonical unsigned-transaction serialization matching the historical
/// TypeScript producer byte-for-byte: property declaration order
/// (TransactionId, PayloadKind, TransactionTimeStamp, Payload, PayloadSize),
/// 3-digit-millisecond ISO timestamp, computed PayloadSize, raw UTF-8 output
/// (no non-ASCII escaping). No RFC 8785/JCS and no new transaction digest.
/// </summary>
public static class CanonicalTransactionAdapter
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>UTF-8 byte length of a payload JSON serialization.</summary>
    public static int PayloadSizeBytes(JsonObject payload)
    {
        return Encoding.UTF8.GetByteCount(payload.ToJsonString(SerializeOptions));
    }

    /// <summary>
    /// Serialize an unsigned transaction to its canonical JSON string.
    /// The supplied PayloadSize is preserved (it was computed by the producer).
    /// </summary>
    public static string SerializeUnsignedTransaction(JsonObject tx)
    {
        var result = new JsonObject
        {
            ["TransactionId"] = (JsonNode?)tx["TransactionId"]?.DeepClone(),
            ["PayloadKind"] = (JsonNode?)tx["PayloadKind"]?.DeepClone(),
            ["TransactionTimeStamp"] = (JsonNode?)tx["TransactionTimeStamp"]?.DeepClone(),
            ["Payload"] = (JsonNode?)tx["Payload"]?.DeepClone(),
            ["PayloadSize"] = (JsonNode?)tx["PayloadSize"]?.DeepClone(),
        };
        return result.ToJsonString(SerializeOptions);
    }

    /// <summary>Parse a canonical transaction JSON string.</summary>
    public static JsonObject Parse(string json)
    {
        return JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("invalid canonical transaction JSON");
    }
}
