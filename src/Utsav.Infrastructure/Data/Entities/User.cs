using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class User
{
    public Guid Id { get; set; }

    public string Email { get; set; } = null!;

    public string? PasswordHash { get; set; }

    public string? FullName { get; set; }

    public string Role { get; set; } = null!;

    public string AuthProvider { get; set; } = null!;

    public string? EntraObjectId { get; set; }

    public string? GoogleSub { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Collector> Collectors { get; set; } = new List<Collector>();

    public virtual ICollection<OrgMember> OrgMembers { get; set; } = new List<OrgMember>();

    public virtual ICollection<PushSubscriptionRecord> PushSubscriptionRecords { get; set; } = new List<PushSubscriptionRecord>();

    public virtual ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();

    public virtual ICollection<Session> Sessions { get; set; } = new List<Session>();

    public virtual ICollection<WorkflowNote> WorkflowNotes { get; set; } = new List<WorkflowNote>();
}
