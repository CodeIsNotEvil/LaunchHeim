using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CINE.LaunchHeim.Core.Catalogs.Thunderstore;

namespace CINE.LaunchHeim.Core.Updates;

/// <summary>A file of a release, by name and download link.</summary>
/// <param name="Sha256">
/// The SHA-256 GitHub computed when the file was uploaded (lowercase hex), or null for files uploaded
/// before GitHub started recording it. The download page shows the same value.
/// </param>
public sealed record ReleaseAsset(string Name, string Url, string? Sha256 = null);

/// <summary>A LaunchHeim release newer than the one running.</summary>
public sealed record AvailableUpdate(string Version, string ReleaseUrl, IReadOnlyList<ReleaseAsset> Assets)
{
  public ReleaseAsset? Asset(string suffix) => Assets.FirstOrDefault(a => a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

  /// <summary>The Windows setup, which an installed copy downloads and runs to update itself.</summary>
  public ReleaseAsset? WindowsSetup => Asset(UpdateChecker.WindowsSetupSuffix);
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

  /// <summary>How the setup's name ends (LaunchHeim-0.5.3-win-x64-setup.exe, from build.ps1).</summary>
  public const string WindowsSetupSuffix = "-win-x64-setup.exe";

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
      .Select(a => new ReleaseAsset(a["name"]?.GetValue<string>() ?? "", a["browser_download_url"]?.GetValue<string>() ?? "", Sha256Of(a["digest"]?.GetValue<string>())))
      .Where(a => a.Name.Length > 0 && a.Url.Length > 0)
      .ToList();
    return new AvailableUpdate(newest.Version, newest.Release["html_url"]?.GetValue<string>() ?? DownloadPage, assets);
  }

  // GitHub writes "sha256:<hex>"; another algorithm or a missing field means there's nothing to check against.
  private static string? Sha256Of(string? digest) =>
    digest is not null && digest.StartsWith("sha256:", StringComparison.Ordinal) && digest.Length == 7 + 64
      ? digest[7..].ToLowerInvariant()
      : null;

  /// <summary>
  /// Downloads a release file into <paramref name="directory"/> and checks it against the SHA-256
  /// GitHub recorded for it, before anything runs it.
  /// </summary>
  /// <returns>The path of the checked file.</returns>
  /// <exception cref="InvalidDataException">The release has no SHA-256 for the file, or the download doesn't match it.</exception>
  /// <remarks>
  /// HTTPS already protects the transfer; the digest also catches a download cut short or a proxy that
  /// served something else. The file is written as <c>.part</c> and renamed only once it matches, so a
  /// half-written setup is never left where it could be started.
  /// </remarks>
  public async Task<string> DownloadAsync(ReleaseAsset asset, string directory, IProgress<double>? progress, CancellationToken cancellationToken)
  {
    if (asset.Sha256 is null)
    {
      throw new InvalidDataException($"The release doesn't list a SHA-256 for {asset.Name}, so the download couldn't be checked.");
    }

    Directory.CreateDirectory(directory);
    // The name comes from GitHub's answer; GetFileName keeps it inside the folder whatever it says.
    var path = Path.Combine(directory, Path.GetFileName(asset.Name));
    var partial = path + ".part";

    using var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    response.EnsureSuccessStatusCode();
    var length = response.Content.Headers.ContentLength;
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
    await using (var target = File.Create(partial))
    {
      var buffer = new byte[81920];
      long received = 0;
      int read;
      while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
      {
        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        hash.AppendData(buffer, 0, read);
        received += read;
        if (length > 0)
        {
          progress?.Report((double)received / length.Value);
        }
      }
    }

    var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
    if (actual != asset.Sha256)
    {
      File.Delete(partial);
      throw new InvalidDataException($"{asset.Name} doesn't match the release: its SHA-256 is {actual}, the release lists {asset.Sha256}.");
    }

    File.Move(partial, path, overwrite: true);
    return path;
  }

  /// <summary>For tests: the same as <see cref="CheckAsync"/> on a JSON string.</summary>
  internal static AvailableUpdate? Newer(string json, string current) =>
    JsonNode.Parse(json) is JsonArray releases ? Newer(releases, current) : throw new JsonException("Not a list of releases.");
}
