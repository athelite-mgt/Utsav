namespace Utsav.Infrastructure.Data.Entities;

/// <summary>
/// One festival day of a puja year with its calendar date — the canonical
/// day→date map (Mahalaya, Panchami, Sasthi … Dasami). Kept separate from
/// <c>puja_schedule</c>, which is a duty roster: this is "what day is Ashtami
/// this year", that is "who runs the bhog counter on Ashtami". The date follows
/// whichever panjika the committee keeps (this committee: Surja Siddhanta), so
/// it is entered/curated rather than computed.
/// </summary>
public partial class PujaCalendarDay
{
    public Guid Id { get; set; }
    public Guid? OrgId { get; set; }
    public Guid YearId { get; set; }
    /// <summary>Day name, e.g. "Sasthi" — mirrors the puja_day lookup labels.</summary>
    public string Label { get; set; } = null!;
    /// <summary>Calendar date as yyyy-MM-dd, matching the app's other date columns (stored as text).</summary>
    public string? EventDate { get; set; }
    /// <summary>Optional note — e.g. the Sandhi Puja window, or a ritual detail.</summary>
    public string? Note { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual Organization? Org { get; set; }
    public virtual PujaYear Year { get; set; } = null!;
}
