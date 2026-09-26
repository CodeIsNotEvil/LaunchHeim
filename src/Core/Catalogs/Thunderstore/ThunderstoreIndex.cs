using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Catalogs.Thunderstore;

internal sealed record ThunderstoreVersion(
  string Version,
  string[] Dependencies,
  long Downloads,
  DateTimeOffset Created,
  long FileSize);

internal sealed record ThunderstorePackage(
  string FullName,
  string Owner,
  string Name,
  string PackageUrl,
  DateTimeOffset Created,
  DateTimeOffset Updated,
  int Rating,
  bool IsDeprecated,
  bool IsNsfw,
  bool IsPinned,
  string[] Categories,
  string Description,
  string? Icon,
  long TotalDownloads,
  ThunderstoreVersion[] Versions)
{
  /// <summary>Versions come newest first from the API and are kept that way.</summary>
  public ThunderstoreVersion Latest => Versions[0];

  public ThunderstoreVersion? Find(string version) =>
    Versions.FirstOrDefault(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The whole Valheim package list, cached on disk and searched in memory.</summary>
/// <remarks>
/// <para>
/// Thunderstore has no search API for third-party apps. What it offers is the community's full package
/// list, split into gzipped chunks behind <c>api/v1/package-listing-index</c> (about 17 MB compressed,
/// 170 MB as plain JSON). r2modman works the same way.
/// </para>
/// <para>
/// Only the fields LaunchHeim uses are kept, which shrinks the cache to a few MB. It is refreshed after
/// <see cref="MaxAge"/>, and a stale copy is used when there is no network, so browsing and dependency
/// resolution keep working offline.
/// </para>
/// </remarks>
public sealed class ThunderstoreIndex(HttpClient http, AppPaths paths)
{
  public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);
  public const string ListingIndexUrl = "https://thunderstore.io/c/valheim/api/v1/package-listing-index/";

  private readonly SemaphoreSlim _gate = new(1, 1);
  private Dictionary<string, ThunderstorePackage> _packages = new(StringComparer.OrdinalIgnoreCase);

  public DateTimeOffset? LastUpdated { get; private set; }

  public bool IsLoaded => _packages.Count > 0;

  public int Count => _packages.Count;

  internal IReadOnlyCollection<ThunderstorePackage> Packages => _packages.Values;

  internal ThunderstorePackage? Find(string fullName) => _packages.GetValueOrDefault(fullName);

  /// <summary>Loads from the disk cache if it is fresh enough, otherwise downloads a new list.</summary>
  public async Task EnsureLoadedAsync(bool forceRefresh, CancellationToken cancellationToken)
  {
    await _gate.WaitAsync(cancellationToken);
    try
    {
      if (!forceRefresh && IsLoaded && DateTimeOffset.UtcNow - LastUpdated < MaxAge)
      {
        return;
      }

      var cache = new FileInfo(paths.ThunderstoreIndexFile);
      if (!forceRefresh && !IsLoaded && cache.Exists && DateTimeOffset.UtcNow - cache.LastWriteTimeUtc < MaxAge)
      {
        await LoadCacheAsync(cache, cancellationToken);
        return;
      }

      try
      {
        var packages = await DownloadAsync(cancellationToken);
        await SaveCacheAsync(packages, cancellationToken);
        Use(packages, DateTimeOffset.UtcNow);
      }
      catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
      {
        if (!cache.Exists)
        {
          throw new CatalogException("Could not download the Thunderstore package list. Check your connection.", ex);
        }

        // Offline: an old list is far more useful than none.
        if (!IsLoaded)
        {
          await LoadCacheAsync(cache, cancellationToken);
        }
      }
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>For tests and for callers that already have the list.</summary>
  internal void Use(IEnumerable<ThunderstorePackage> packages, DateTimeOffset updated)
  {
    _packages = packages.Where(p => p.Versions.Length > 0)
      .GroupBy(p => p.FullName, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    LastUpdated = updated;
  }

  private async Task LoadCacheAsync(FileInfo cache, CancellationToken cancellationToken)
  {
    await using var file = cache.OpenRead();
    await using var gzip = new GZipStream(file, CompressionMode.Decompress);
    var packages = await JsonSerializer.DeserializeAsync<List<ThunderstorePackage>>(gzip, PooledOptions(CacheOptions), cancellationToken) ?? [];
    Use(packages, cache.LastWriteTimeUtc);
  }

  private async Task SaveCacheAsync(IReadOnlyList<ThunderstorePackage> packages, CancellationToken cancellationToken)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(paths.ThunderstoreIndexFile)!);
    var temp = paths.ThunderstoreIndexFile + ".tmp";
    await using (var file = File.Create(temp))
    await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
    {
      await JsonSerializer.SerializeAsync(gzip, packages, CacheOptions, cancellationToken);
    }

    File.Move(temp, paths.ThunderstoreIndexFile, overwrite: true);
  }

  private async Task<IReadOnlyList<ThunderstorePackage>> DownloadAsync(CancellationToken cancellationToken)
  {
    var chunkUrls = await ReadGzipJsonAsync<List<string>>(new Uri(ListingIndexUrl), RawOptions, cancellationToken) ?? [];
    var options = PooledOptions(RawOptions);

    // A handful of chunks at a time: fast, without hammering the CDN.
    var results = new List<ThunderstorePackage>[chunkUrls.Count];
    await Parallel.ForEachAsync(
      chunkUrls.Select((url, index) => (url, index)),
      new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
      async (chunk, token) =>
      {
        var raw = await ReadGzipJsonAsync<List<RawPackage>>(new Uri(chunk.url), options, token) ?? [];
        results[chunk.index] = raw.Select(Compact).ToList();
      });

    return results.SelectMany(r => r).ToList();
  }

  private async Task<T?> ReadGzipJsonAsync<T>(Uri url, JsonSerializerOptions options, CancellationToken cancellationToken)
  {
    using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    response.EnsureSuccessStatusCode();
    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
    await using var gzip = new GZipStream(stream, CompressionMode.Decompress);
    return await JsonSerializer.DeserializeAsync<T>(gzip, options, cancellationToken);
  }

  internal static ThunderstorePackage Compact(RawPackage raw)
  {
    var versions = raw.Versions ?? [];
    var latest = versions.FirstOrDefault();
    return new ThunderstorePackage(
      raw.FullName,
      raw.Owner,
      raw.Name,
      raw.PackageUrl,
      raw.DateCreated,
      raw.DateUpdated,
      raw.RatingScore,
      raw.IsDeprecated,
      raw.HasNsfwContent,
      raw.IsPinned,
      raw.Categories ?? [],
      latest?.Description ?? "",
      latest?.Icon,
      versions.Sum(v => v.Downloads),
      versions.Select(v => new ThunderstoreVersion(v.VersionNumber, v.Dependencies ?? [], v.Downloads, v.DateCreated, v.FileSize)).ToArray());
  }

  private static readonly JsonSerializerOptions RawOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
  private static readonly JsonSerializerOptions CacheOptions = new(JsonSerializerDefaults.Web);

  /// <summary>
  /// The list holds about 3 million dependency strings but fewer than 30,000 distinct ones (every
  /// version of every modpack repeats the same entries). Sharing one instance per distinct string
  /// takes the index from roughly 300 MB to a small fraction of that.
  /// </summary>
  private static JsonSerializerOptions PooledOptions(JsonSerializerOptions options) =>
    new(options) { Converters = { new PooledStringConverter() } };

  private sealed class PooledStringConverter : JsonConverter<string>
  {
    private readonly ConcurrentDictionary<string, string> _pool = new(StringComparer.Ordinal);

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
      reader.GetString() is { } value ? _pool.GetOrAdd(value, value) : null;

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
      writer.WriteStringValue(value);
  }

  internal sealed record RawPackage(
    string Name,
    string FullName,
    string Owner,
    string PackageUrl,
    DateTimeOffset DateCreated,
    DateTimeOffset DateUpdated,
    int RatingScore,
    bool IsPinned,
    bool IsDeprecated,
    bool HasNsfwContent,
    string[]? Categories,
    RawVersion[]? Versions);

  internal sealed record RawVersion(
    string VersionNumber,
    string? Description,
    string? Icon,
    string[]? Dependencies,
    long Downloads,
    DateTimeOffset DateCreated,
    long FileSize);
}

/// <summary>Thunderstore's <c>Owner-Name-1.2.3</c> dependency strings.</summary>
public readonly record struct DependencyString(string FullName, string Version)
{
  /// <remarks>
  /// Team and package names may only contain letters, digits and underscores, so the last hyphen always
  /// separates the version and the first separates the owner.
  /// </remarks>
  public static DependencyString? Parse(string value)
  {
    var lastDash = value.LastIndexOf('-');
    if (lastDash <= 0 || lastDash == value.Length - 1 || value.IndexOf('-') == lastDash)
    {
      return null;
    }

    return new DependencyString(value[..lastDash], value[(lastDash + 1)..]);
  }
}

public static class ModVersion
{
  /// <summary>
  /// Compares dotted versions part by part, treating missing parts as zero, so <c>2.0</c> equals
  /// <c>2.0.0</c> (System.Version says it is older). Anything non-numeric falls back to text.
  /// </summary>
  public static int Compare(string? a, string? b)
  {
    var left = Parts(a);
    var right = Parts(b);
    if (left is null || right is null)
    {
      return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
    {
      var difference = (i < left.Length ? left[i] : 0).CompareTo(i < right.Length ? right[i] : 0);
      if (difference != 0)
      {
        return difference;
      }
    }

    return 0;
  }

  // Nexus and CurseForge authors write "v1.2" as often as "1.2".
  private static long[]? Parts(string? version)
  {
    var parts = version?.Trim().TrimStart('v', 'V').Split('.');
    if (parts is null || parts.Length == 0)
    {
      return null;
    }

    var numbers = new long[parts.Length];
    for (var i = 0; i < parts.Length; i++)
    {
      if (!long.TryParse(parts[i], out numbers[i]))
      {
        return null;
      }
    }

    return numbers;
  }
}
