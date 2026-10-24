namespace Utsav.Infrastructure.Data;

/// <summary>
/// What a brand-new committee's reference data starts as.
///
/// All of it is <c>lookups</c> and <c>zones</c> rows rather than code, which is
/// the single luckiest fact about making this app multi-festival: Kali Puja's
/// calendar differs completely from Durga Puja's, and accommodating it is seed
/// data, not a domain change. Admins rename and extend everything from Settings
/// afterwards, so these are starting points, not constraints.
/// </summary>
public sealed record FestivalTemplate(
    string DisplayName,
    string[] PujaDays,
    (string Category, string[] Labels)[] Lookups);

public static class FestivalTemplates
{
    private static readonly (string Category, string[] Labels)[] SharedLookups =
    [
        ("payment_mode", ["Cash", "Online - Gpay", "Cheque", "Bank Transfer", "Pending"]),
        ("ad_type", ["Banner", "Leaflet", "Banner + Leaflet", "Front Page Leaflet", "Others"]),
        ("ad_status", [
            "Write-up received", "Banner received", "Banner received, Writeup Pending",
            "Write-up received, Banner Pending", "Pending All", "NA",
        ]),
        ("offering_type", ["Vog", "Saree", "Dhuti", "Flower", "Others"]),
        ("donation_purpose", ["Idol", "Feast", "Vog", "General", "Others"]),
        ("vendor_category", [
            "Pandal", "Decoration", "Light & Sound", "Idol", "Catering",
            "Dhak & Music", "Printing", "Transport", "Security", "Others",
        ]),
    ];

    /// <summary>
    /// Shared by tasks and expenses (one list, not two, so "how much did we
    /// spend on Decoration" and "what Decoration tasks are open" use the same
    /// word), and common to every festival. Festival-specific headings go
    /// between <see cref="ExpenseHead"/> and <see cref="ExpenseTail"/>.
    /// </summary>
    private static readonly string[] ExpenseHead =
    [
        "IDOL", "Purohit", "Puja Essentials & Offerings", "Decoration", "Electricity",
        "Light & Sound with Vasan", "Cleaning Stuffs", "Transportation", "Bisarjan & Feast",
        "Pandal", "Management & Stationary", "Dhak", "Cultural Program", "Advertisment",
        "Flowers", "New Assets", "Pritivoj",
    ];

    /// <summary>"Others" stays last among the expense headings; the rest are task categories.</summary>
    private static readonly string[] ExpenseTail =
    [
        "Others",
        "Catering", "Finance", "Idol & Puja", "Logistics", "Permissions & Legal", "Publicity",
    ];

    // One neutral starting zone. The lane names the original committee used
    // ("16 Feet"...) are that committee's own streets, not a default.
    internal static readonly string[] DefaultZones = ["General"];

    private static (string Category, string[] Labels)[] WithExpenses(params string[] extra) =>
    [
        .. SharedLookups,
        ("expense_category", [.. ExpenseHead, .. extra, .. ExpenseTail]),
    ];

    public static FestivalTemplate For(string festival) => festival.Trim().ToLowerInvariant() switch
    {
        "durga" => new FestivalTemplate(
            "Durga Puja",
            ["Mahalaya", "Panchami", "Sasthi", "Saptami", "Astami", "Sandhi Puja", "Nabami", "Dasami"],
            WithExpenses("Siddhi", "Lakshmi Puja", "Khuti Puja")),

        "kali" => new FestivalTemplate(
            "Kali Puja",
            // Kali Puja is kept on the amavasya night itself, with the eve
            // before it, the immersion, and Bhai Phota two days on. Editable in
            // Settings: committees keep it differently.
            ["Bhoot Chaturdashi", "Kali Puja", "Bisarjan", "Bhai Phota"],
            WithExpenses()),

        "saraswati" => new FestivalTemplate(
            "Saraswati Puja",
            ["Panchami", "Sasthi", "Bisarjan"],
            WithExpenses()),

        "lakshmi" => new FestivalTemplate(
            "Lakshmi Puja",
            ["Purnima", "Bisarjan"],
            WithExpenses()),

        // A festival we have not met still gets a usable start rather than a
        // refusal; the day list is theirs to edit.
        _ => new FestivalTemplate("Puja", ["Puja Day", "Bisarjan"], WithExpenses()),
    };

    public static IReadOnlyList<string> Known { get; } = ["durga", "kali", "saraswati", "lakshmi"];
}
