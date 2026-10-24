using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class WorkflowNote
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid WorkflowItemId { get; set; }

    public Guid? AuthorId { get; set; }

    public string? AuthorName { get; set; }

    public string Note { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual User? Author { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual WorkflowItem WorkflowItem { get; set; } = null!;
}
