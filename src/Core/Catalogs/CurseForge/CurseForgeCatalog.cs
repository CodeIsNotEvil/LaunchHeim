using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using CINE.LaunchHeim.Core.Instances;

namespace CINE.LaunchHeim.Core.Catalogs.CurseForge;

/// <summary>CurseForge, through the official "CurseForge for Studios" REST API.</summary>
/// <remarks>
/// <para>
/// Every call needs an API key from console.curseforge.com; without one the API answers 403 to
/// everything, including search. Authors can also opt out of third-party distribution, in which case
/// the file has no download URL and the user is sent to the website instead.
/// </para>
/// <para>
/// Valheim's game id is looked up by slug on first use rather than hardcoded, since it is not in the
/// public docs and a wrong id would silently return some other game's mods.
/// </para>
/// </remarks>
public sealed class CurseForgeCatalog(HttpClient http, Func<string?> apiKey) : IModCatalog
{
  private const string ApiUrl = "https://api.curseforge.com/v1";
  private const int RequiredDependency = 3;
  private int? _gameId;

  public ModSource Source => ModSource.CurseForge;

  public string? SetupHint => string.IsNullOrWhiteSpace(apiKey())
    ? "CurseForge only answers apps with an API key. Create one at console.curseforge.com and paste it in Settings."
    : null;

  public IReadOnlyList<ModSort> SupportedSorts { get; } =
    [ModSort.Relevance, ModSort.Popular, ModSort.Downloads, ModSort.Updated, ModSort.Newest, ModSort.Name];

  public async Task<ModSearchPage> SearchAsync(ModSearchQuery query, CancellationToken cancellationToken)
  {
    var gameId = await GameIdAsync(cancellationToken);

    // ModsSearchSortField: 1 Featured, 2 Popularity, 3 LastUpdated, 4 Name, 6 TotalDownloads, 11 ReleasedDate.
    var sortField = query.Sort switch
    {
      ModSort.Relevance when query.Text.Length > 0 => 1,
      ModSort.Popular => 2,
      ModSort.Updated => 3,
      ModSort.Name => 4,
      ModSort.Newest => 11,
      _ => 6,
    };
    var order = query.Sort == ModSort.Name ? "asc" : "desc";
    var url = $"{ApiUrl}/mods/search?gameId={gameId}&searchFilter={Uri.EscapeDataString(query.Text.Trim())}" +
      $"&sortField={sortField}&sortOrder={order}&index={query.Page * query.PageSize}&pageSize={query.PageSize}";

    var json = await GetAsync(url, cancellationToken);
    var items = json["data"]!.AsArray().Select(m => ToSummary(m!)).ToList();

    // The API refuses index + pageSize beyond 10,000, so never offer pages past that.
    var total = Math.Min(json["pagination"]?["totalCount"]?.GetValue<int>() ?? items.Count, 10_000);
    return new ModSearchPage(items, query.Page, query.PageSize, total);
  }

  public async Task<ModDetails> GetDetailsAsync(string modId, CancellationToken cancellationToken)
  {
    var modTask = GetAsync($"{ApiUrl}/mods/{modId}", cancellationToken);
    var filesTask = GetAsync($"{ApiUrl}/mods/{modId}/files?pageSize=50", cancellationToken);
    var descriptionTask = GetAsync($"{ApiUrl}/mods/{modId}/description", cancellationToken);
    await Task.WhenAll(modTask, filesTask, descriptionTask);

    var summary = ToSummary((await modTask)["data"]!);
    var files = (await filesTask)["data"]!.AsArray()
      .Where(f => f!["isAvailable"]?.GetValue<bool>() != false)
      .OrderByDescending(f => DateTimeOffset.Parse(f!["fileDate"]!.GetValue<string>()))
      .Select((f, i) => new ModFileInfo(
        f!["id"]!.GetValue<long>().ToString(),
        f["displayName"]?.GetValue<string>() ?? f["fileName"]!.GetValue<string>(),
        VersionOf(f),
        DateTimeOffset.Parse(f["fileDate"]!.GetValue<string>()),
        f["fileLength"]?.GetValue<long>() ?? 0,
        null,
        (f["releaseType"]?.GetValue<int>()) switch { 2 => "Beta", 3 => "Alpha", _ => "Release" },
        i == 0))
      .ToList();

    var description = (await descriptionTask)["data"]?.GetValue<string>() ?? summary.Summary;
    return new ModDetails(summary, description, DescriptionFormat.Html, files);
  }

  public async Task<DownloadTicket> ResolveDownloadAsync(string modId, string? fileId, CancellationToken cancellationToken)
  {
    var mod = ToSummary((await GetAsync($"{ApiUrl}/mods/{modId}", cancellationToken))["data"]!);
    JsonNode file;
    if (fileId is null)
    {
      var files = (await GetAsync($"{ApiUrl}/mods/{modId}/files?pageSize=50", cancellationToken))["data"]!.AsArray();
      file = files.Where(f => f!["releaseType"]?.GetValue<int>() == 1).OrderByDescending(f => f!["fileDate"]!.GetValue<string>()).FirstOrDefault()
        ?? files.OrderByDescending(f => f!["fileDate"]!.GetValue<string>()).FirstOrDefault()
        ?? throw new CatalogException($"{mod.Name} has no files.");
    }
    else
    {
      file = (await GetAsync($"{ApiUrl}/mods/{modId}/files/{fileId}", cancellationToken))["data"]!;
    }

    var id = file["id"]!.GetValue<long>().ToString();
    var url = file["downloadUrl"]?.GetValue<string>();
    if (string.IsNullOrEmpty(url))
    {
      try
      {
        url = (await GetAsync($"{ApiUrl}/mods/{modId}/files/{id}/download-url", cancellationToken))["data"]?.GetValue<string>();
      }
      catch (CatalogException)
      {
        url = null;
      }
    }

    if (string.IsNullOrEmpty(url))
    {
      return new BrowserRequired(
        new Uri($"{mod.WebsiteUrl}/files/{id}"),
        "The author of this mod does not allow downloads through other apps. Download it on CurseForge and add it with \"Install from file\".");
    }

    var dependencies = file["dependencies"]?.AsArray()
      .Where(d => d?["relationType"]?.GetValue<int>() == RequiredDependency)
      .Select(d => new ModReference(ModSource.CurseForge, d!["modId"]!.GetValue<long>().ToString()))
      .ToList() ?? [];

    return new DirectDownload(new Uri(url), file["fileName"]!.GetValue<string>(), mod, id, VersionOf(file), dependencies);
  }

  public async Task ValidateAsync(CancellationToken cancellationToken) => await GameIdAsync(cancellationToken);

  private async Task<int> GameIdAsync(CancellationToken cancellationToken)
  {
    if (_gameId is { } cached)
    {
      return cached;
    }

    for (var index = 0; index < 1000; index += 50)
    {
      var json = await GetAsync($"{ApiUrl}/games?index={index}&pageSize=50", cancellationToken);
      var games = json["data"]!.AsArray();
      var valheim = games.FirstOrDefault(g => string.Equals(g?["slug"]?.GetValue<string>(), "valheim", StringComparison.OrdinalIgnoreCase));
      if (valheim is not null)
      {
        _gameId = valheim["id"]!.GetValue<int>();
        return _gameId.Value;
      }

      if (games.Count < 50)
      {
        break;
      }
    }

    throw new CatalogException("CurseForge does not list Valheim for this API key.");
  }

  private async Task<JsonNode> GetAsync(string url, CancellationToken cancellationToken)
  {
    var key = apiKey();
    if (string.IsNullOrWhiteSpace(key))
    {
      throw new CatalogException(SetupHint!);
    }

    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.TryAddWithoutValidation("x-api-key", key.Trim());
    using var response = await http.SendAsync(request, cancellationToken);
    if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
    {
      throw new CatalogException("CurseForge rejected the API key. Check it in Settings.");
    }

    if (response.StatusCode is HttpStatusCode.NotFound)
    {
      throw new CatalogException("CurseForge could not find that mod or file.");
    }

    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken) ?? throw new CatalogException("CurseForge returned an empty response.");
  }

  // CurseForge has no version field; display names usually carry it ("MyMod 1.2.3").
  private static string VersionOf(JsonNode file)
  {
    var name = file["displayName"]?.GetValue<string>() ?? file["fileName"]?.GetValue<string>() ?? "";
    var match = System.Text.RegularExpressions.Regex.Match(name, @"\d+(\.\d+)+");
    return match.Success ? match.Value : name;
  }

  internal static ModSummary ToSummary(JsonNode mod)
  {
    var authors = mod["authors"]?.AsArray().Select(a => a?["name"]?.GetValue<string>()).OfType<string>().ToList() ?? [];
    var latest = mod["latestFiles"]?.AsArray().OrderByDescending(f => f?["fileDate"]?.GetValue<string>()).FirstOrDefault();
    return new ModSummary(
      ModSource.CurseForge,
      mod["id"]!.GetValue<long>().ToString(),
      mod["name"]?.GetValue<string>() ?? "",
      string.Join(", ", authors),
      mod["summary"]?.GetValue<string>() ?? "",
      mod["logo"]?["thumbnailUrl"]?.GetValue<string>(),
      (long)(mod["downloadCount"]?.GetValue<double>() ?? 0),
      mod["thumbsUpCount"]?.GetValue<long>() ?? 0,
      DateTimeOffset.TryParse(mod["dateModified"]?.GetValue<string>(), out var modified) ? modified : DateTimeOffset.MinValue,
      latest is null ? null : VersionOf(latest),
      mod["links"]?["websiteUrl"]?.GetValue<string>() ?? $"https://www.curseforge.com/valheim/mods/{mod["slug"]?.GetValue<string>()}",
      mod["categories"]?.AsArray().Select(c => c?["name"]?.GetValue<string>()).OfType<string>().ToList() ?? []);
  }
}
