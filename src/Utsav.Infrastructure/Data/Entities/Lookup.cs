using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class Lookup
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public string Category { get; set; } = null!;

    public string Label { get; set; } = null!;

    public int? SortOrder { get; set; }

    public bool Active { get; set; }

    public virtual Organization? Org { get; set; }
}
