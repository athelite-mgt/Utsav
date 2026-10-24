using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class OrgMember
{
    public Guid Id { get; set; }

    public Guid OrgId { get; set; }

    public Guid UserId { get; set; }

    public string Role { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual Organization Org { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
