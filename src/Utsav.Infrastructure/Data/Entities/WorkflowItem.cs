using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class WorkflowItem
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid YearId { get; set; }

    public string Title { get; set; } = null!;

    public string? Category { get; set; }

    public string? OwnerName { get; set; }

    public Guid? OwnerCollectorId { get; set; }

    public string Status { get; set; } = null!;

    public string? DueDate { get; set; }

    public string? CompletedDate { get; set; }

    public string? Notes { get; set; }

    public int? SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual Collector? OwnerCollector { get; set; }

    public virtual ICollection<WorkflowNote> WorkflowNotes { get; set; } = new List<WorkflowNote>();

    public virtual PujaYear Year { get; set; } = null!;
}
