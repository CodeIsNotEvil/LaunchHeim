using CINE.LaunchHeim.Core.Instances;

namespace CINE.LaunchHeim.Core.Catalogs;

public enum ModSort
{
  Relevance,
  Popular,
  Downloads,
  Updated,
  Newest,
  Name,
}

public sealed record ModSearchQuery(string Text, ModSort Sort, int Page, int PageSize = 24, bool IncludeNsfw = false);

public sealed record ModSummary(
  ModSource Source,
  string Id,
  string Name,
  string Author,
  string Summary,
  string? IconUrl,
  long Downloads,
  long Likes,
  DateTimeOffset Updated,
  string? LatestVersion,
  string WebsiteUrl,
  IReadOnlyList<string> Categories,
  bool IsDeprecated = false);

public sealed record ModSearchPage(IReadOnlyList<ModSummary> Items, int Page, int PageSize, int TotalCount)
{
  public int PageCount => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record ModFileInfo(
  string Id,
  string Name,
  string Version,
  DateTimeOffset Date,
  long SizeBytes,
  string? Description,
  string Category,
  bool IsRecommended);

public enum DescriptionFormat
{
  Markdown,
  Html,
  PlainText,
}

public sealed record ModDetails(ModSummary Summary, string Description, DescriptionFormat Format, IReadOnlyList<ModFileInfo> Files);

/// <summary>A mod another mod needs. <see cref="MinimumVersion"/> is only known on Thunderstore.</summary>
public sealed record ModReference(ModSource Source, string ModId, string? MinimumVersion = null);

public abstract record DownloadTicket;

public sealed record DirectDownload(
  Uri Url,
  string FileName,
  ModSummary Mod,
  string FileId,
  string Version,
  IReadOnlyList<ModReference> Dependencies,
  IReadOnlyDictionary<string, string>? Headers = null) : DownloadTicket;

/// <summary>
/// The site will not hand out a file to a third-party app, so the user has to click through in a
/// browser. For Nexus without Premium, the page's "Mod Manager Download" comes back as an nxm:// link.
/// </summary>
public sealed record BrowserRequired(Uri PageUrl, string Reason) : DownloadTicket;

public interface IModCatalog
{
  ModSource Source { get; }

  /// <summary>Null when the catalog can be used, otherwise what the user needs to set up first.</summary>
  string? SetupHint { get; }

  IReadOnlyList<ModSort> SupportedSorts { get; }

  Task<ModSearchPage> SearchAsync(ModSearchQuery query, CancellationToken cancellationToken);

  Task<ModDetails> GetDetailsAsync(string modId, CancellationToken cancellationToken);

  /// <param name="fileId">A specific file, or null for the newest recommended one.</param>
  Task<DownloadTicket> ResolveDownloadAsync(string modId, string? fileId, CancellationToken cancellationToken);
}

public sealed class CatalogException(string message, Exception? inner = null) : Exception(message, inner);
