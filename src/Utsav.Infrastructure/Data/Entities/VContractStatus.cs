using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class VContractStatus
{
    public Guid? Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid? YearId { get; set; }

    public Guid? VendorId { get; set; }

    public string? Title { get; set; }

    public string? Category { get; set; }

    public decimal? ContractAmount { get; set; }

    public string? StartDate { get; set; }

    public string? EndDate { get; set; }

    public string? Status { get; set; }

    public string? Terms { get; set; }

    public string? Notes { get; set; }

    public DateTime? CreatedAt { get; set; }

    public string? VendorName { get; set; }

    public decimal? Paid { get; set; }

    public decimal? Scheduled { get; set; }

    public decimal? Outstanding { get; set; }
}
