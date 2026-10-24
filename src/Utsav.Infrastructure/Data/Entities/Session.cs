using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class Session
{
    public string Token { get; set; } = null!;

    public Guid UserId { get; set; }

    public Guid? OrgId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual User User { get; set; } = null!;
}
