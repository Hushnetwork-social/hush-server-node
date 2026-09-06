// FEAT-015 Task 2.4 — canonical licence transaction JSON (byte owner).
//
// Reproduces the exact established Hush unsigned-transaction bytes for the licence
// payload kind (same semantics as the FEAT-001/FEAT-011 producer corpus):
//   {"TransactionId":"...","PayloadKind":"...","TransactionTimeStamp":"...",
//    "Payload":{...},"PayloadSize":N}
// with:
//   - fixed declaration order (never reordered; licence payload members exactly as
//     HushVotingLicencePayloadCanonicalMembers.Order);
//   - ISO-8601 UTC timestamp with exactly 3 millisecond digits (JS
//     Date.toISOString equivalent);
//   - JS JSON.stringify escaping (quotes, backslash, control chars <0x20,
//     U+2028/U+2029 as \uXXXX with lowercase hex; all other code points literal);
//   - PayloadSize = exact UTF-8 byte length of the payload JSON;
//   - TransactionId/PayloadKind as lowercase canonical "D" UUID strings.
//
// Baseline payloads OMIT the expected-current precondition members entirely;
// upgrade payloads include them (frozen by HushVotingLicencePayloadCanonicalMembers).
//
// This writer is the frozen byte owner. The Phase 3.1 codec and the .NET fixture
// vectors must reproduce exactly these bytes; the fixture contract tests freeze
// the resulting digest so later TS/Rust producers can be proven byte-identical.

using System.Globalization;

namespace HushNode.HushVoting.Licence.Transactions;

/// <summary>
/// Byte-exact canonical unsigned-JSON builder for licence assignment transactions.
/// </summary>
public static class HushVotingLicenceCanonicalJson
{
    /// <summary>Canonical payload JSON for a licence payload (member order frozen).</summary>
    public static string BuildPayloadJson(HushVotingLicenceAssignmentPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var upgrade = string.Equals(
            payload.TransitionIntent,
            HushVotingLicenceTransitionIntent.ConfirmedUpgrade,
            StringComparison.Ordinal);

        if (!upgrade)
        {
            return string.Concat(
                "{\"", HushVotingLicencePayloadCanonicalMembers.TransitionIntent, "\":\"", Escape(payload.TransitionIntent),
                "\",\"", HushVotingLicencePayloadCanonicalMembers.RequestedPlanId, "\":\"", Escape(payload.RequestedPlanId),
                "\",\"", HushVotingLicencePayloadCanonicalMembers.ObservedCatalogueVersion, "\":\"", Escape(payload.ObservedCatalogueVersion),
                "\"}");
        }

        var expectedCurrentTransactionId = payload.ExpectedCurrentLicenceTransactionId?.ToString("D", CultureInfo.InvariantCulture) ?? string.Empty;
        return string.Concat(
            "{\"", HushVotingLicencePayloadCanonicalMembers.TransitionIntent, "\":\"", Escape(payload.TransitionIntent),
            "\",\"", HushVotingLicencePayloadCanonicalMembers.RequestedPlanId, "\":\"", Escape(payload.RequestedPlanId),
            "\",\"", HushVotingLicencePayloadCanonicalMembers.ObservedCatalogueVersion, "\":\"", Escape(payload.ObservedCatalogueVersion),
            "\",\"", HushVotingLicencePayloadCanonicalMembers.ExpectedCurrentLicenceTransactionId, "\":\"", expectedCurrentTransactionId,
            "\",\"", HushVotingLicencePayloadCanonicalMembers.ExpectedCurrentPlanId, "\":\"", Escape(payload.ExpectedCurrentPlanId ?? string.Empty),
            "\"}");
    }

    /// <summary>Exact UTF-8 byte length of the canonical payload JSON.</summary>
    public static int PayloadJsonUtf8Length(HushVotingLicenceAssignmentPayload payload) =>
        System.Text.Encoding.UTF8.GetByteCount(BuildPayloadJson(payload));

    /// <summary>
    /// Canonical unsigned JSON of the outer licence transaction. <paramref name="payloadSize"/>
    /// is the recorded PayloadSize (validators require it to equal the canonical payload length).
    /// </summary>
    public static string BuildCanonicalUnsignedJson(
        Guid transactionId,
        Guid payloadKind,
        DateTime transactionTimeStampUtc,
        HushVotingLicenceAssignmentPayload payload,
        long payloadSize) =>
        string.Concat(
            "{\"TransactionId\":\"", Escape(transactionId.ToString("D", CultureInfo.InvariantCulture)),
            "\",\"PayloadKind\":\"", Escape(payloadKind.ToString("D", CultureInfo.InvariantCulture)),
            "\",\"TransactionTimeStamp\":\"", ToCanonicalTimestamp(transactionTimeStampUtc),
            "\",\"Payload\":", BuildPayloadJson(payload),
            ",\"PayloadSize\":", payloadSize.ToString(CultureInfo.InvariantCulture),
            "}");

    /// <summary>JS Date.toISOString equivalent: yyyy-MM-ddTHH:mm:ss.fffZ (3-digit ms, UTC).</summary>
    public static string ToCanonicalTimestamp(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    /// <summary>
    /// JS JSON.stringify string escaping: quote, backslash, control chars &lt;0x20,
    /// U+2028 and U+2029 as \uXXXX (lowercase hex); everything else literal.
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
                    builder.Append("\\u").Append(value.ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default:
                    if (value < 0x20)
                    {
                        builder.Append("\\u").Append(value.ToString("x4", CultureInfo.InvariantCulture));
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
