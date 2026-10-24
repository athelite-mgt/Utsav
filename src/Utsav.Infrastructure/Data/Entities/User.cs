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

    /// <summary>
    /// Platform operator: may provision committees. Deliberately a flag on the
    /// account and NOT a committee role (AppRole), because committee roles are
    /// granted by a committee's own admin: if this lived there, approving
    /// someone at one committee would hand them every other committee's books.
    /// It grants no access to any committee's data; an operator still needs an
    /// ordinary membership to see a committee's figures.
    /// </summary>
    public bool IsPlatformAdmin { get; set; }

    public virtual ICollection<Collector> Collectors { get; set; } = new List<Collector>();

    public virtual ICollection<OrgMember> OrgMembers { get; set; } = new List<OrgMember>();

    public virtual ICollection<PushSubscriptionRecord> PushSubscriptionRecords { get; set; } = new List<PushSubscriptionRecord>();

    public virtual ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();

    public virtual ICollection<Session> Sessions { get; set; } = new List<Session>();

    public virtual ICollection<WorkflowNote> WorkflowNotes { get; set; } = new List<WorkflowNote>();
}
