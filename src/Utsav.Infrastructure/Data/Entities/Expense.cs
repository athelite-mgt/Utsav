using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class Expense
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid YearId { get; set; }

    public string? Category { get; set; }

    public string? Details { get; set; }

    public decimal Amount { get; set; }

    public string? SpentOn { get; set; }

    public string? Mode { get; set; }

    public string? Comment { get; set; }

    public Guid? VendorId { get; set; }

    public Guid? ContractId { get; set; }

    public Guid? AdvanceId { get; set; }

    public string? BillNo { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<AdvanceSettlement> AdvanceSettlements { get; set; } = new List<AdvanceSettlement>();

    public virtual Contract? Contract { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual Vendor? Vendor { get; set; }

    public virtual PujaYear Year { get; set; } = null!;
}
