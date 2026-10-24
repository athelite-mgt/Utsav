namespace Utsav.Infrastructure.Storage;

public sealed record BlobDownload(Stream Content, string ContentType);

/// <summary>
/// The one seam <c>DocumentEndpoints</c> depends on — swap the
/// implementation without touching a single endpoint. The registered
/// implementation is <see cref="AzureBlobStorageService"/>.
/// </summary>
public interface IBlobStorageService
{
    bool IsConfigured { get; }

    /// <summary>No-op when not configured — called once at startup, same "must not be why the app fails to start" shape as <c>CosmosAuditSink.EnsureContainerExistsAsync</c>.</summary>
    Task EnsureContainerExistsAsync(CancellationToken ct = default);

    /// <summary>Returns the storage-side blob name (opaque key), not a public URL — downloads go back through the API, not direct blob access.</summary>
    Task<string> UploadAsync(Stream content, string blobName, string contentType, CancellationToken ct = default);

    Task<BlobDownload> DownloadAsync(string blobName, CancellationToken ct = default);

    Task DeleteAsync(string blobName, CancellationToken ct = default);
}
