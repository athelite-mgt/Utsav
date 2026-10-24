using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Utsav.Infrastructure.Storage;

/// <summary>
/// Thin wrapper over Azure Blob Storage. Every method assumes
/// <see cref="IsConfigured"/> is true — callers (endpoints) check that first
/// and return a clear "not set up yet" response rather than calling through,
/// same shape as <c>WebPushSender.IsConfigured</c>.
/// </summary>
public sealed class AzureBlobStorageService(IOptions<BlobStorageOptions> options, ILogger<AzureBlobStorageService> logger) : IBlobStorageService
{
    private readonly BlobStorageOptions _options = options.Value;
    private readonly Lazy<BlobContainerClient?> _container = new(() =>
        options.Value.IsConfigured ? new BlobContainerClient(options.Value.ConnectionString, options.Value.ContainerName) : null);

    public bool IsConfigured => _options.IsConfigured;

    /// <summary>
    /// Called once at startup (see <c>Program.cs</c>), same "must not be why
    /// the app fails to start" reasoning as <c>CosmosAuditSink.EnsureContainerExistsAsync</c>.
    /// A no-op until Boss supplies the connection string.
    /// </summary>
    public async Task EnsureContainerExistsAsync(CancellationToken ct = default)
    {
        if (_container.Value is null)
        {
            return;
        }

        await _container.Value.CreateIfNotExistsAsync(cancellationToken: ct);
    }

    public async Task<string> UploadAsync(Stream content, string blobName, string contentType, CancellationToken ct = default)
    {
        var client = RequireContainer();
        await client.UploadBlobAsync(blobName, content, ct);
        try
        {
            await client.GetBlobClient(blobName).SetHttpHeadersAsync(
                new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // The upload itself already succeeded; a missing content-type just
            // means the browser guesses on download instead of trusting the
            // header — not worth failing the whole upload over.
            logger.LogWarning(ex, "Uploaded {BlobName} but failed to set its content-type header", blobName);
        }

        return blobName;
    }

    public async Task<BlobDownload> DownloadAsync(string blobName, CancellationToken ct = default)
    {
        var client = RequireContainer();
        var blob = client.GetBlobClient(blobName);
        var download = await blob.DownloadStreamingAsync(cancellationToken: ct);
        return new BlobDownload(download.Value.Content, download.Value.Details.ContentType);
    }

    public async Task DeleteAsync(string blobName, CancellationToken ct = default)
    {
        var client = RequireContainer();
        await client.DeleteBlobIfExistsAsync(blobName, cancellationToken: ct);
    }

    private BlobContainerClient RequireContainer() =>
        _container.Value ?? throw new InvalidOperationException("Blob storage isn't configured (Storage:ConnectionString/ContainerName missing).");
}
