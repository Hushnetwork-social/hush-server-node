// FEAT-011 Task 3.1 — canonical FullIdentity unsigned-JSON serializer.
//
// Reproduces the exact FEAT-001 canonical unsigned transaction bytes:
//   {"TransactionId":"...","PayloadKind":"...","TransactionTimeStamp":"...",
//    "Payload":{"IdentityAlias":"...","PublicSigningAddress":"...",
//               "PublicEncryptAddress":"...","IsPublic":true},"PayloadSize":N}
// with the historical TypeScript producer semantics:
//   - fixed declaration order (never reordered);
//   - ISO-8601 UTC timestamp with exactly 3 millisecond digits (JS
//     Date.toISOString equivalent);
//   - JS JSON.stringify escaping (quotes, backslash, control chars <0x20,
//     U+2028/U+2029 as \uXXXX with lowercase hex; all other code points
//     literal, matching the corpus byte vectors).
//
// PayloadSize is the exact UTF-8 byte length of the payload JSON produced
// with the same escaping rules.

using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;

namespace HushNode.Identity;

/// <summary>Canonical message builder with JS-compatible escaping (byte-exact per FEAT-001).</summary>
public sealed class FullIdentityCanonicalSerializer : IFullIdentityCanonicalSerializer
{
    public string SerializeCanonicalUnsignedJson(SignedTransaction<FullIdentityPayload> transaction)
    {
        var payload = transaction.Payload;
        var payloadJson = BuildPayloadJson(payload.IdentityAlias, payload.PublicSigningAddress, payload.PublicEncryptAddress, payload.IsPublic);

        return string.Concat(
            "{\"TransactionId\":\"", Escape(transaction.TransactionId.Value.ToString()),
            "\",\"PayloadKind\":\"", Escape(transaction.PayloadKind.ToString()),
            "\",\"TransactionTimeStamp\":\"", Escape(ToCanonicalTimestamp(transaction.TransactionTimeStamp.Value)),
            "\",\"Payload\":", payloadJson,
            ",\"PayloadSize\":", transaction.PayloadSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "}");
    }

    /// <summary>Canonical payload JSON (byte-exact JS stringify semantics).</summary>
    public string BuildPayloadJson(string alias, string signingAddress, string encryptAddress, bool isPublic) =>
        string.Concat(
            "{\"IdentityAlias\":\"", Escape(alias),
            "\",\"PublicSigningAddress\":\"", Escape(signingAddress),
            "\",\"PublicEncryptAddress\":\"", Escape(encryptAddress),
            "\",\"IsPublic\":", isPublic ? "true" : "false",
            "}");

    /// <summary>Exact UTF-8 byte length of the canonical payload JSON.</summary>
    public int PayloadJsonUtf8Length(string alias, string signingAddress, string encryptAddress, bool isPublic) =>
        System.Text.Encoding.UTF8.GetByteCount(
            BuildPayloadJson(alias, signingAddress, encryptAddress, isPublic));

    /// <summary>JS Date.toISOString equivalent: yyyy-MM-ddTHH:mm:ss.fffZ (3-digit ms, UTC).</summary>
    public static string ToCanonicalTimestamp(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// JS JSON.stringify string escaping: quote, backslash, control chars
    /// &lt;0x20, U+2028 and U+2029 as \uXXXX (lowercase hex); everything else
    /// literal (no non-ASCII escaping).
    /// </summary>
    public static string Escape(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(input.Length + 8);
        foreach (var rune in input.EnumerateRunes())
        {
            var value = rune.Value;
            switch (value)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case 0x2028:
                case 0x2029:
                    builder.Append("\\u").Append(value.ToString("x4"));
                    break;
                default:
                    if (value < 0x20)
                    {
                        builder.Append("\\u").Append(value.ToString("x4"));
                    }
                    else
                    {
                        builder.Append(rune);
                    }
                    break;
            }
        }

        return builder.ToString();
    }
}
