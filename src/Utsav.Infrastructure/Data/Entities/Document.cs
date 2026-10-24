using System;

namespace Utsav.Infrastructure.Data.Entities;

/// <summary>
/// Puja permission and contract-level paperwork — police/municipal NOCs,
/// signed vendor contracts, insurance, anything the committee needs on file.
/// The actual bytes live in blob storage (<c>Storage.IBlobStorageService</c>
/// isn't configured yet — see <c>BlobStorageOptions.IsConfigured</c>); this
/// row is the searchable record plus the storage key.
/// </summary>
public partial class Document
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    /// <summary>Null for paperwork that isn't tied to one festival year (e.g. a standing registration).</summary>
    public Guid? YearId { get; set; }

    /// <summary>Optional link to the vendor contract this document belongs to.</summary>
    public Guid? ContractId { get; set; }

    public string Title { get; set; } = null!;

    /// <summary>From the <c>document_category</c> lookup — "Puja Permission", "Contract", "Other".</summary>
    public string? Category { get; set; }

    public string? Notes { get; set; }

    public string FileName { get; set; } = null!;

    /// <summary>The blob's storage key — opaque to everything except <see cref="Push.IBlobStorageService"/>.</summary>
    public string BlobName { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public long SizeBytes { get; set; }

    public Guid? UploadedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual PujaYear? Year { get; set; }

    public virtual Contract? Contract { get; set; }

    public virtual User? UploadedBy { get; set; }
}
