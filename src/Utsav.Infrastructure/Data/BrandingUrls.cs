using System.Security.Cryptography;
using System.Text;

namespace Utsav.Infrastructure.Data;

/// <summary>
/// The public address of a committee's logo.
///
/// Logos are public on purpose: an &lt;img&gt; tag cannot send the bearer token
/// this app authenticates with, and a committee logo is not a secret. The URL
/// carries a short version tag derived from the stored blob name, so replacing
/// the logo changes the URL and browsers fetch the new image rather than
/// showing a cached one.
/// </summary>
public static class BrandingUrls
{
    public static string? LogoUrl(Guid orgId, string? logoBlob)
    {
        if (string.IsNullOrEmpty(logoBlob))
        {
            return null;
        }

        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(logoBlob)))[..10].ToLowerInvariant();
        return $"/api/branding/{orgId}/logo?v={version}";
    }

    /// <summary>The only place a committee's logo blob may live.</summary>
    public static string BlobPrefix(Guid orgId) => $"{orgId}/branding/";
}
