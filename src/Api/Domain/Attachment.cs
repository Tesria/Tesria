namespace Tesria.Api.Domain;

/// <summary>
/// A file attached to a page. The bytes live on the uploads volume (PLAN §3);
/// this row holds the metadata and the <see cref="StorageKey"/> that locates
/// them. An S3-compatible backend can replace local storage later.
/// </summary>
public class Attachment
{
    public Guid Id { get; set; }

    public Guid PageId { get; set; }
    public Page? Page { get; set; }

    public required string Filename { get; set; }

    public required string ContentType { get; set; }

    /// <summary>Size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>Opaque key locating the bytes in the storage backend.</summary>
    public required string StorageKey { get; set; }

    public Guid UploadedById { get; set; }
    public User? UploadedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
