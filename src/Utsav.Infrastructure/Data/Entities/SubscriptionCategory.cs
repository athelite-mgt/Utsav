namespace Utsav.Infrastructure.Data.Entities;

/// <summary>
/// The two sections <c>subscriptions.category</c> splits the table into. Rows
/// move between them freely; nothing else about a row changes when they do.
/// </summary>
public static class SubscriptionCategory
{
    public const string Membership = "membership";

    public const string Special = "special";

    public static bool IsValid(string? value) => value is Membership or Special;
}
