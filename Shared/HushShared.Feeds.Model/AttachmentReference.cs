namespace HushShared.Feeds.Model;

/// <summary>
/// Attachment metadata that travels inside the signed transaction payload (on-chain).
/// The actual encrypted binary data is stored off-chain in PostgreSQL.
/// </summary>
public record AttachmentReference(
    string Id,           // Client-generated UUID
    string Hash,         // SHA-256 hex of the original plaintext file (64 chars)
    string MimeType,     // e.g., "image/jpeg", "application/pdf"
    long Size,           // Original file size in bytes (before encryption)
    string FileName);    // Original file name
