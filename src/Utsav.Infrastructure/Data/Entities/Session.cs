using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class Session
{
    /// <summary>The SHA-256 HASH of the token the browser holds, never the token itself (see SessionTokens).</summary>
    public string Token { get; set; } = null!;

    public Guid UserId { get; set; }

    public Guid? OrgId { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Absolute end of life, however active the session has been. A stolen token has a bounded life.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Last request on this session, for the idle timeout. Updated at most every few minutes.</summary>
    public DateTime LastSeenAt { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual User User { get; set; } = null!;
}
