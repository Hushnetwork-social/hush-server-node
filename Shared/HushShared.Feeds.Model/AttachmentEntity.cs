using HushShared.Blockchain.Model;

namespace HushShared.Feeds.Model;

/// <summary>
/// Database entity for off-chain attachment storage.
/// Stores encrypted binary data in PostgreSQL bytea columns.
/// Metadata (UUID, hash, MIME, size) is stored on-chain in the transaction payload.
/// </summary>
public record AttachmentEntity(
    string Id,                       // UUID (matches AttachmentReference.Id from on-chain metadata)
    byte[] EncryptedOriginal,        // AES-256-GCM encrypted original file bytes
    byte[]? EncryptedThumbnail,      // AES-256-GCM encrypted thumbnail bytes (nullable)
    FeedMessageId FeedMessageId,     // FK to FeedMessage
    long OriginalSize,               // Original file size before encryption
    long ThumbnailSize,              // Thumbnail size before encryption (0 if no thumbnail)
    string MimeType,                 // e.g., "image/jpeg"
    string FileName,                 // Original file name
    string Hash,                     // SHA-256 hex of original plaintext file (64 chars)
    DateTime CreatedAt);             // UTC timestamp
