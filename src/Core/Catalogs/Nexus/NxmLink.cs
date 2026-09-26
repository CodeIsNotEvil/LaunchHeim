using System.Web;

namespace CINE.LaunchHeim.Core.Catalogs.Nexus;

/// <summary>
/// The link Nexus hands to a mod manager when you click "Mod Manager Download", e.g.
/// <c>nxm://valheim/mods/1042/files/12345?key=abc&amp;expires=1790000000&amp;user_id=42</c>.
/// </summary>
/// <remarks>
/// The key and expiry are what let a free account download through the API: the website issues them
/// after the user has clicked through, so an app cannot mint them on its own.
/// </remarks>
public sealed record NxmLink(string Game, string ModId, string FileId, string? Key, string? Expires, string? UserId)
{
  public static bool TryParse(string? value, out NxmLink link)
  {
    link = null!;
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.Scheme.Equals("nxm", StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    var segments = uri.AbsolutePath.Trim('/').Split('/');
    if (segments.Length != 4
      || !segments[0].Equals("mods", StringComparison.OrdinalIgnoreCase)
      || !segments[2].Equals("files", StringComparison.OrdinalIgnoreCase)
      || !long.TryParse(segments[1], out _)
      || !long.TryParse(segments[3], out _))
    {
      return false;
    }

    var query = HttpUtility.ParseQueryString(uri.Query);
    link = new NxmLink(uri.Host, segments[1], segments[3], query["key"], query["expires"], query["user_id"]);
    return true;
  }
}
