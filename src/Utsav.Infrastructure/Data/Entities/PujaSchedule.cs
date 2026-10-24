using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class PujaSchedule
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid YearId { get; set; }

    public string PujaDay { get; set; } = null!;

    public string? EventDate { get; set; }

    public string? StartTime { get; set; }

    public string? EndTime { get; set; }

    public string ChargeTitle { get; set; } = null!;

    public string? AssigneeName { get; set; }

    public Guid? AssigneeCollectorId { get; set; }

    public string? Contact { get; set; }

    public string? Notes { get; set; }

    public int? SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Collector? AssigneeCollector { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual PujaYear Year { get; set; } = null!;
}
