using System.Net.Http.Json;
using CINE.LaunchHeim.Core.Instances;

namespace CINE.LaunchHeim.Core.Catalogs.Thunderstore;

/// <summary>Thunderstore, the home of most Valheim mods and of BepInExPack_Valheim itself.</summary>
public sealed class ThunderstoreCatalog(HttpClient http, ThunderstoreIndex index) : IModCatalog
{
  public const string LoaderFullName = "denikson-BepInExPack_Valheim";

  public ModSource Source => ModSource.Thunderstore;

  public string? SetupHint => null;

  public IReadOnlyList<ModSort> SupportedSorts { get; } =
    [ModSort.Relevance, ModSort.Popular, ModSort.Downloads, ModSort.Updated, ModSort.Newest, ModSort.Name];

  public ThunderstoreIndex Index => index;

  public async Task<ModSearchPage> SearchAsync(ModSearchQuery query, CancellationToken cancellationToken)
  {
    await index.EnsureLoadedAsync(forceRefresh: false, cancellationToken);
    return Search(index.Packages, query);
  }

  internal static ModSearchPage Search(IEnumerable<ThunderstorePackage> packages, ModSearchQuery query)
  {
    var terms = query.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var candidates = packages.Where(p => query.IncludeNsfw || !p.IsNsfw);

    IEnumerable<(ThunderstorePackage Package, int Score)> scored = terms.Length == 0
      ? candidates.Select(p => (p, 0))
      : candidates.Select(p => (p, Score(p, terms))).Where(x => x.Item2 > 0);

    var sorted = query.Sort switch
    {
      ModSort.Relevance when terms.Length > 0 => scored.OrderByDescending(x => x.Score).ThenByDescending(x => x.Package.TotalDownloads),
      ModSort.Popular => scored.OrderByDescending(x => x.Package.Rating),
      ModSort.Updated => scored.OrderByDescending(x => x.Package.Updated),
      ModSort.Newest => scored.OrderByDescending(x => x.Package.Created),
      ModSort.Name => scored.OrderBy(x => x.Package.Name, StringComparer.OrdinalIgnoreCase),
      _ => scored.OrderByDescending(x => x.Package.TotalDownloads),
    };

    // Like the website: deprecated packages sink to the bottom, and without a search the pinned
    // essentials (BepInExPack, Jötunn) come first.
    var ordered = sorted
      .OrderBy(x => x.Package.IsDeprecated)
      .ThenByDescending(x => terms.Length == 0 && x.Package.IsPinned)
      .Select(x => x.Package)
      .ToList();

    var items = ordered.Skip(query.Page * query.PageSize).Take(query.PageSize).Select(ToSummary).ToList();
    return new ModSearchPage(items, query.Page, query.PageSize, ordered.Count);
  }

  private static int Score(ThunderstorePackage package, string[] terms)
  {
    var name = package.Name.Replace('_', ' ');
    var score = 0;
    foreach (var term in terms)
    {
      var termScore = 0;
      if (name.Equals(term, StringComparison.OrdinalIgnoreCase) || package.Name.Equals(term, StringComparison.OrdinalIgnoreCase))
      {
        termScore = 100;
      }
      else if (name.StartsWith(term, StringComparison.OrdinalIgnoreCase))
      {
        termScore = 50;
      }
      else if (name.Contains(term, StringComparison.OrdinalIgnoreCase) || package.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
      {
        termScore = 30;
      }
      else if (package.Owner.Contains(term, StringComparison.OrdinalIgnoreCase))
      {
        termScore = 15;
      }
      else if (package.Description.Contains(term, StringComparison.OrdinalIgnoreCase)
        || package.Categories.Any(c => c.Contains(term, StringComparison.OrdinalIgnoreCase)))
      {
        termScore = 5;
      }

      // Every term has to match somewhere, like any search box people are used to.
      if (termScore == 0)
      {
        return 0;
      }

      score += termScore;
    }

    return score;
  }

  internal static ModSummary ToSummary(ThunderstorePackage package) => new(
    ModSource.Thunderstore,
    package.FullName,
    package.Name.Replace('_', ' '),
    package.Owner,
    package.Description,
    package.Icon,
    package.TotalDownloads,
    package.Rating,
    package.Updated,
    package.Latest.Version,
    package.PackageUrl,
    package.Categories,
    package.IsDeprecated);

  public async Task<ModDetails> GetDetailsAsync(string modId, CancellationToken cancellationToken)
  {
    await index.EnsureLoadedAsync(forceRefresh: false, cancellationToken);
    var package = index.Find(modId) ?? throw new CatalogException($"{modId} is not on Thunderstore.");

    var readme = package.Description;
    var format = DescriptionFormat.PlainText;
    try
    {
      var url = $"https://thunderstore.io/api/experimental/package/{package.Owner}/{package.Name}/{package.Latest.Version}/readme/";
      var response = await http.GetFromJsonAsync<ReadmeResponse>(url, cancellationToken);
      if (!string.IsNullOrWhiteSpace(response?.Markdown))
      {
        readme = response.Markdown;
        format = DescriptionFormat.Markdown;
      }
    }
    catch (HttpRequestException)
    {
      // The short description is still worth showing without the README.
    }

    var files = package.Versions.Select((v, i) => new ModFileInfo(
      v.Version,
      $"{package.Name} {v.Version}",
      v.Version,
      v.Created,
      v.FileSize,
      v.Dependencies.Length == 0 ? null : "Requires " + string.Join(", ", v.Dependencies.Select(DisplayDependency)),
      i == 0 ? "Latest" : "Older",
      i == 0)).ToList();

    return new ModDetails(ToSummary(package), readme, format, files);
  }

  public async Task<DownloadTicket> ResolveDownloadAsync(string modId, string? fileId, CancellationToken cancellationToken)
  {
    await index.EnsureLoadedAsync(forceRefresh: false, cancellationToken);
    var package = index.Find(modId) ?? throw new CatalogException($"{modId} is not on Thunderstore.");
    var version = fileId is null ? package.Latest : package.Find(fileId) ?? throw new CatalogException($"{modId} has no version {fileId}.");

    var dependencies = version.Dependencies
      .Select(DependencyString.Parse)
      .OfType<DependencyString>()
      .Select(d => new ModReference(ModSource.Thunderstore, d.FullName, d.Version))
      .ToList();

    return new DirectDownload(
      new Uri($"https://thunderstore.io/package/download/{package.Owner}/{package.Name}/{version.Version}/"),
      $"{package.FullName}-{version.Version}.zip",
      ToSummary(package),
      version.Version,
      version.Version,
      dependencies);
  }

  /// <summary>The newest version, used to offer updates for installed Thunderstore mods.</summary>
  public string? LatestVersion(string fullName) => index.Find(fullName)?.Latest.Version;

  private static string DisplayDependency(string dependency) =>
    DependencyString.Parse(dependency) is { } d ? d.FullName[(d.FullName.IndexOf('-') + 1)..].Replace('_', ' ') : dependency;

  private sealed record ReadmeResponse(string? Markdown);
}
