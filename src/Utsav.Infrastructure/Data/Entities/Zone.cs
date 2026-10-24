using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class Zone
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public string Name { get; set; } = null!;

    public int? SortOrder { get; set; }

    public virtual ICollection<Donor> Donors { get; set; } = new List<Donor>();

    public virtual Organization? Org { get; set; }
}
