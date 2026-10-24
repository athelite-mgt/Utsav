using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class Collector
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public string Name { get; set; } = null!;

    public string? Phone { get; set; }

    public Guid? UserId { get; set; }

    public bool Active { get; set; }

    public int? SortOrder { get; set; }

    public virtual ICollection<Advance> Advances { get; set; } = new List<Advance>();

    public virtual ICollection<Feast> Feasts { get; set; } = new List<Feast>();

    public virtual Organization? Org { get; set; }

    public virtual ICollection<PujaSchedule> PujaSchedules { get; set; } = new List<PujaSchedule>();

    public virtual ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

    public virtual User? User { get; set; }

    public virtual ICollection<WorkflowItem> WorkflowItems { get; set; } = new List<WorkflowItem>();
}
