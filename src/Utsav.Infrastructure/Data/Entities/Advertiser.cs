using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class Advertiser
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public string Name { get; set; } = null!;

    public string? Contact { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<AdBooking> AdBookings { get; set; } = new List<AdBooking>();

    public virtual Organization? Org { get; set; }
}
