using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class Donor
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public string Name { get; set; } = null!;

    public string? Surname { get; set; }

    public string? Contact { get; set; }

    public Guid? ZoneId { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Feast> Feasts { get; set; } = new List<Feast>();

    public virtual Organization? Org { get; set; }

    public virtual ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

    public virtual Zone? Zone { get; set; }
}
