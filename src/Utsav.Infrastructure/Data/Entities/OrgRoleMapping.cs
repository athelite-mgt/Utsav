using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class OrgRoleMapping
{
    public Guid Id { get; set; }

    public Guid OrgId { get; set; }

    public string EntraGroupId { get; set; } = null!;

    public string? EntraGroupName { get; set; }

    public string Role { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual Organization Org { get; set; } = null!;
}
