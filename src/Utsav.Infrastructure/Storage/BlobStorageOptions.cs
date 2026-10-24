namespace Utsav.Infrastructure.Storage;

/// <summary>
/// Bound from the <c>Storage</c> config section — same "config-optional"
/// shape as <see cref="Push.VapidOptions"/>: the connection string is a
/// secret (local dev via <c>dotnet user-secrets</c>, production via Azure
/// App Service's <c>Storage__ConnectionString</c> application setting), and
/// the whole document-upload feature degrades to a clear "not set up yet"
/// response until Boss supplies it — it never blocks the app from starting
/// or breaks anything already working.
/// </summary>
public sealed class BlobStorageOptions
{
    public const string SectionName = "Storage";

    public string? ConnectionString { get; set; }
    public string? ContainerName { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ConnectionString) &&
        !string.IsNullOrWhiteSpace(ContainerName);
}
