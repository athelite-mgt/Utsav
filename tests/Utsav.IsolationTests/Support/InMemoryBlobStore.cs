using System.Collections.Concurrent;
using Utsav.Infrastructure.Storage;

namespace Utsav.IsolationTests.Support;

/// <summary>
/// Stands in for Azure Blob Storage. Keyed by blob name exactly as the real
/// service is, so the tenant prefix convention ({orgId}/...) is exercised.
/// </summary>
public sealed class InMemoryBlobStore : IBlobStorageService
{
    private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> _blobs = new();

    public bool IsConfigured => true;

    public IReadOnlyCollection<string> Names => _blobs.Keys.ToArray();

    public Task EnsureContainerExistsAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<string> UploadAsync(Stream content, string blobName, string contentType, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        _blobs[blobName] = (buffer.ToArray(), contentType);
        return blobName;
    }

    public Task<BlobDownload> DownloadAsync(string blobName, CancellationToken ct = default)
    {
        if (!_blobs.TryGetValue(blobName, out var blob))
        {
            throw new FileNotFoundException($"No such blob: {blobName}");
        }

        return Task.FromResult(new BlobDownload(new MemoryStream(blob.Bytes), blob.ContentType));
    }

    public Task DeleteAsync(string blobName, CancellationToken ct = default)
    {
        _blobs.TryRemove(blobName, out _);
        return Task.CompletedTask;
    }

    public void Put(string blobName, string text, string contentType = "application/pdf") =>
        _blobs[blobName] = (System.Text.Encoding.UTF8.GetBytes(text), contentType);
}
