using System;

namespace Utsav.Infrastructure.Data.Entities;

/// <summary>
/// A problem encountered with a vendor — late delivery, quality dispute,
/// payment disagreement, etc. Always tied to the vendor; optionally tied to
/// the specific contract it happened under, since a vendor can have more
/// than one and not every issue is contract-specific.
/// </summary>
public partial class VendorIssue
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid VendorId { get; set; }

    public Guid? ContractId { get; set; }

    public string IssueType { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string? OccurredOn { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual Vendor Vendor { get; set; } = null!;

    public virtual Contract? Contract { get; set; }

    public virtual Organization? Org { get; set; }
}
