using System.Text.Json;
using System.Text.Json.Nodes;
using CINE.LaunchHeim.Core.Catalogs.Thunderstore;

namespace CINE.LaunchHeim.Core.Updates;

/// <summary>A file of a release, by name and download link.</summary>
public sealed record ReleaseAsset(string Name, string Url);

/// <summary>A LaunchHeim release newer than the one running.</summary>
public sealed record AvailableUpdate(string Version, string ReleaseUrl, IReadOnlyList<ReleaseAsset> Assets)
{
  public ReleaseAsset? Asset(string suffix) => Assets.FirstOrDefault(a => a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Asks GitHub whether a newer LaunchHeim has been released.</summary>
/// <remarks>
/// <para>
/// The monorepo releases several apps, so GitHub's "latest release" may well be another app's. The
/// list of releases is read instead and the newest <c>launchheim-v*</c> one taken that is neither a
/// draft nor a pre-release, the same rule <c>site/build.py</c> follows for the download page.
/// </para>
/// <para>
/// Anonymous: GitHub allows 60 such requests an hour per address, and LaunchHeim asks once at start and
/// then twice a day. Any failure (offline, rate limit, a changed API) only means no reminder.
/// </para>
/// </remarks>
public sealed class UpdateChecker(HttpClient http)
{
  public const string ReleasesApi = "https://api.github.com/repos/CodeIsNotEvil/MyMonoRepo/releases?per_page=30";
  public const string DownloadPage = "https://codeisnotevil.github.io/MyMonoRepo/download.html#launchheim";
  public const string TagPrefix = "launchheim-v";

  /// <returns>The newest release when it's newer than <paramref name="current"/>, otherwise null.</returns>
  public async Task<AvailableUpdate?> CheckAsync(string current, CancellationToken cancellationToken)
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
    request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
    using var response = await http.SendAsync(request, cancellationToken);
    response.EnsureSuccessStatusCode();
    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
    var releases = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonArray;
    return releases is null ? null : Newer(releases, current);
  }

  internal static AvailableUpdate? Newer(JsonArray releases, string current)
  {
    var newest = releases
      .OfType<JsonObject>()
      .Where(r => r["draft"]?.GetValue<bool>() != true && r["prerelease"]?.GetValue<bool>() != true)
      .Select(r => (Release: r, Tag: r["tag_name"]?.GetValue<string>() ?? ""))
      .Where(r => r.Tag.StartsWith(TagPrefix, StringComparison.Ordinal))
      .Select(r => (r.Release, Version: r.Tag[TagPrefix.Length..]))
      // By version, not by date: a 0.4.3 bugfix published after 0.5.0 is not the newest.
      .OrderByDescending(r => r.Version, Comparer<string>.Create(ModVersion.Compare))
      .FirstOrDefault();

    if (newest.Release is null || ModVersion.Compare(newest.Version, current) <= 0)
    {
      return null;
    }

    var assets = (newest.Release["assets"] as JsonArray ?? [])
      .OfType<JsonObject>()
      .Select(a => new ReleaseAsset(a["name"]?.GetValue<string>() ?? "", a["browser_download_url"]?.GetValue<string>() ?? ""))
      .Where(a => a.Name.Length > 0 && a.Url.Length > 0)
      .ToList();
    return new AvailableUpdate(newest.Version, newest.Release["html_url"]?.GetValue<string>() ?? DownloadPage, assets);
  }

  /// <summary>For tests: the same as <see cref="CheckAsync"/> on a JSON string.</summary>
  internal static AvailableUpdate? Newer(string json, string current) =>
    JsonNode.Parse(json) is JsonArray releases ? Newer(releases, current) : throw new JsonException("Not a list of releases.");
}
