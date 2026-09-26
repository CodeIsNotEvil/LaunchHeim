using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CINE.LaunchHeim.Core.Instances;

namespace CINE.LaunchHeim.Core.Catalogs.Nexus;

public sealed record NexusAccount(string Name, bool IsPremium);

/// <summary>Nexus Mods, through its public GraphQL API for browsing and the v1 API for downloads.</summary>
/// <remarks>
/// <para>
/// Browsing needs no account: the v2 GraphQL API answers anonymous searches. Downloading does. The v1
/// <c>download_link</c> endpoint needs the user's personal API key, and even then only hands out links
/// directly to Premium members. Free accounts have to click "Slow download" on the website, which
/// opens an <c>nxm://</c> link carrying a one-off key; LaunchHeim registers itself for those.
/// </para>
/// </remarks>
public sealed class NexusCatalog(HttpClient http, Func<string?> apiKey) : IModCatalog
{
  public const string GameDomain = "valheim";
  public const int GameId = 3667;
  private const string GraphQlUrl = "https://api.nexusmods.com/v2/graphql";
  private const string ApiUrl = "https://api.nexusmods.com/v1";

  public ModSource Source => ModSource.Nexus;

  // Search works without a key, so the catalog is always usable. Downloads explain themselves.
  public string? SetupHint => null;

  public IReadOnlyList<ModSort> SupportedSorts { get; } =
    [ModSort.Relevance, ModSort.Popular, ModSort.Downloads, ModSort.Updated, ModSort.Newest, ModSort.Name];

  public bool HasApiKey => !string.IsNullOrWhiteSpace(apiKey());

  public async Task<ModSearchPage> SearchAsync(ModSearchQuery query, CancellationToken cancellationToken)
  {
    var filter = new JsonObject
    {
      ["gameDomainName"] = new JsonArray(Filter(GameDomain, "EQUALS")),
      ["status"] = new JsonArray(Filter("published", "EQUALS")),
    };
    if (!query.IncludeNsfw)
    {
      filter["adultContent"] = new JsonArray(new JsonObject { ["value"] = false, ["op"] = "EQUALS" });
    }

    var text = query.Text.Trim();
    if (text.Length > 0)
    {
      filter["name"] = new JsonArray(Filter(text, "WILDCARD"));
    }

    var sortField = query.Sort switch
    {
      ModSort.Relevance when text.Length > 0 => "relevance",
      ModSort.Popular => "endorsements",
      ModSort.Updated => "updatedAt",
      ModSort.Newest => "createdAt",
      ModSort.Name => "name",
      _ => "downloads",
    };
    var direction = query.Sort == ModSort.Name ? "ASC" : "DESC";

    const string gql = """
      query($filter: ModsFilter, $sort: [ModsSort!], $offset: Int, $count: Int) {
        mods(filter: $filter, sort: $sort, offset: $offset, count: $count) {
          totalCount
          nodes { modId name summary author version downloads endorsements thumbnailUrl pictureUrl updatedAt createdAt modCategory { name } }
        }
      }
      """;

    var data = await GraphQlAsync(gql, new JsonObject
    {
      ["filter"] = filter,
      ["sort"] = new JsonArray(new JsonObject { [sortField] = new JsonObject { ["direction"] = direction } }),
      ["offset"] = query.Page * query.PageSize,
      ["count"] = query.PageSize,
    }, cancellationToken);

    var mods = data["mods"]!;
    var items = mods["nodes"]!.AsArray().Select(n => ToSummary(n!)).ToList();
    return new ModSearchPage(items, query.Page, query.PageSize, mods["totalCount"]!.GetValue<int>());
  }

  public async Task<ModDetails> GetDetailsAsync(string modId, CancellationToken cancellationToken)
  {
    const string gql = """
      query($modId: ID!, $gameId: ID!) {
        mod(modId: $modId, gameId: $gameId) {
          modId name summary author version downloads endorsements thumbnailUrl pictureUrl updatedAt createdAt description modCategory { name }
        }
        modFiles(modId: $modId, gameId: $gameId) {
          fileId name version category sizeInBytes date description primary
        }
      }
      """;

    var data = await GraphQlAsync(gql, new JsonObject { ["modId"] = modId, ["gameId"] = GameId.ToString() }, cancellationToken);
    var mod = data["mod"] ?? throw new CatalogException($"Nexus mod {modId} was not found.");
    var files = ParseFiles(data["modFiles"]!.AsArray());
    var description = mod["description"]?.GetValue<string>() ?? "";
    return new ModDetails(ToSummary(mod), BbCode.ToHtml(description), DescriptionFormat.Html, files);
  }

  /// <summary>
  /// Main files first, then optional ones and patches; old, archived and removed files are left out
  /// because installing them is almost never what the user wants.
  /// </summary>
  internal static List<ModFileInfo> ParseFiles(JsonArray files)
  {
    var parsed = files
      .Select(f => new
      {
        Id = f!["fileId"]!.GetValue<long>().ToString(),
        Name = f["name"]?.GetValue<string>() ?? "",
        Version = f["version"]?.GetValue<string>() ?? "",
        Category = f["category"]?.GetValue<string>() ?? "",
        Size = long.TryParse(f["sizeInBytes"]?.ToString(), out var size) ? size : 0,
        Date = DateTimeOffset.FromUnixTimeSeconds(f["date"]?.GetValue<long>() ?? 0),
        Description = f["description"]?.GetValue<string>(),
        Primary = f["primary"]?.GetValue<int>() == 1,
      })
      .Where(f => f.Category is "MAIN" or "OPTIONAL" or "UPDATE" or "MISCELLANEOUS")
      .OrderBy(f => f.Category switch { "MAIN" => 0, "UPDATE" => 1, "OPTIONAL" => 2, _ => 3 })
      .ThenByDescending(f => f.Date)
      .ToList();

    var recommended = parsed.FirstOrDefault(f => f.Primary) ?? parsed.FirstOrDefault(f => f.Category == "MAIN");
    return parsed.Select(f => new ModFileInfo(
      f.Id,
      f.Name,
      f.Version,
      f.Date,
      f.Size,
      f.Description is null ? null : BbCode.ToHtml(f.Description),
      CategoryLabel(f.Category),
      ReferenceEquals(f, recommended))).ToList();
  }

  private static string CategoryLabel(string category) => category switch
  {
    "MAIN" => "Main",
    "OPTIONAL" => "Optional",
    "UPDATE" => "Update",
    _ => "Misc",
  };

  public async Task<DownloadTicket> ResolveDownloadAsync(string modId, string? fileId, CancellationToken cancellationToken)
  {
    var details = await GetDetailsAsync(modId, cancellationToken);
    var file = fileId is null
      ? details.Files.FirstOrDefault(f => f.IsRecommended) ?? details.Files.FirstOrDefault()
      : details.Files.FirstOrDefault(f => f.Id == fileId);
    if (file is null)
    {
      throw new CatalogException($"{details.Summary.Name} has no downloadable files.");
    }

    var filePage = new Uri($"https://www.nexusmods.com/{GameDomain}/mods/{modId}?tab=files&file_id={file.Id}&nmm=1");
    if (!HasApiKey)
    {
      return new BrowserRequired(filePage, "Downloading from Nexus needs your personal API key. Add it in Settings, or use \"Mod Manager Download\" on the Nexus page.");
    }

    var links = await DownloadLinksAsync(modId, file.Id, query: "", cancellationToken);
    if (links is null)
    {
      return new BrowserRequired(filePage, "Nexus only gives direct downloads to Premium members. Click \"Slow download\" on the page and LaunchHeim picks up the rest.");
    }

    return ToTicket(details.Summary, file, links);
  }

  /// <summary>Completes a download started on the website with "Mod Manager Download".</summary>
  public async Task<DirectDownload> ResolveNxmAsync(NxmLink link, CancellationToken cancellationToken)
  {
    if (!link.Game.Equals(GameDomain, StringComparison.OrdinalIgnoreCase))
    {
      throw new CatalogException($"That link is for {link.Game}, not Valheim.");
    }

    if (!HasApiKey)
    {
      throw new CatalogException("Add your Nexus API key in Settings to accept downloads from the website.");
    }

    var details = await GetDetailsAsync(link.ModId, cancellationToken);
    var file = details.Files.FirstOrDefault(f => f.Id == link.FileId)
      ?? new ModFileInfo(link.FileId, details.Summary.Name, details.Summary.LatestVersion ?? "", DateTimeOffset.UtcNow, 0, null, "Main", true);

    var query = $"?key={Uri.EscapeDataString(link.Key ?? "")}&expires={Uri.EscapeDataString(link.Expires ?? "")}";
    var links = await DownloadLinksAsync(link.ModId, link.FileId, query, cancellationToken)
      ?? throw new CatalogException("Nexus refused the download link. It may have expired; click the download button again.");
    return ToTicket(details.Summary, file, links);
  }

  private static DirectDownload ToTicket(ModSummary mod, ModFileInfo file, List<DownloadLink> links)
  {
    var uri = new Uri(links[0].Uri);
    var fileName = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
    return new DirectDownload(uri, fileName.Length > 0 ? fileName : $"{mod.Id}-{file.Id}.zip", mod, file.Id, file.Version.Length > 0 ? file.Version : mod.LatestVersion ?? "", []);
  }

  /// <returns>The CDN links, or null when Nexus says this account may not download directly.</returns>
  private async Task<List<DownloadLink>?> DownloadLinksAsync(string modId, string fileId, string query, CancellationToken cancellationToken)
  {
    using var request = V1Request($"{ApiUrl}/games/{GameDomain}/mods/{modId}/files/{fileId}/download_link.json{query}");
    using var response = await http.SendAsync(request, cancellationToken);
    if (response.StatusCode is HttpStatusCode.Forbidden)
    {
      return null;
    }

    if (response.StatusCode is HttpStatusCode.Unauthorized)
    {
      throw new CatalogException("Nexus rejected the API key. Check it in Settings.");
    }

    response.EnsureSuccessStatusCode();
    var links = await response.Content.ReadFromJsonAsync<List<DownloadLink>>(cancellationToken);
    return links is { Count: > 0 } ? links : null;
  }

  public async Task<NexusAccount> ValidateAsync(CancellationToken cancellationToken)
  {
    using var request = V1Request($"{ApiUrl}/users/validate.json");
    using var response = await http.SendAsync(request, cancellationToken);
    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
    {
      throw new CatalogException("Nexus rejected the API key.");
    }

    response.EnsureSuccessStatusCode();
    var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
    return new NexusAccount(json?["name"]?.GetValue<string>() ?? "", json?["is_premium"]?.GetValue<bool>() == true);
  }

  private HttpRequestMessage V1Request(string url)
  {
    var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.TryAddWithoutValidation("apikey", apiKey()?.Trim());

    // Nexus asks API clients to identify themselves with these two headers.
    request.Headers.TryAddWithoutValidation("Application-Name", "LaunchHeim");
    request.Headers.TryAddWithoutValidation("Application-Version", AppInfo.Version);
    return request;
  }

  private async Task<JsonNode> GraphQlAsync(string query, JsonObject variables, CancellationToken cancellationToken)
  {
    using var response = await http.PostAsJsonAsync(GraphQlUrl, new JsonObject { ["query"] = query, ["variables"] = variables }, cancellationToken);
    response.EnsureSuccessStatusCode();
    var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken)
      ?? throw new CatalogException("Nexus returned an empty response.");

    if (json["errors"] is JsonArray { Count: > 0 } errors)
    {
      throw new CatalogException("Nexus: " + (errors[0]?["message"]?.GetValue<string>() ?? "unknown error"));
    }

    return json["data"] ?? throw new CatalogException("Nexus returned no data.");
  }

  private static JsonObject Filter(string value, string op) => new() { ["value"] = value, ["op"] = op };

  internal static ModSummary ToSummary(JsonNode node)
  {
    var id = node["modId"]!.GetValue<long>().ToString();
    var category = node["modCategory"]?["name"]?.GetValue<string>();
    return new ModSummary(
      ModSource.Nexus,
      id,
      WebUtility.HtmlDecode(node["name"]?.GetValue<string>() ?? ""),
      node["author"]?.GetValue<string>() ?? "",
      WebUtility.HtmlDecode(node["summary"]?.GetValue<string>() ?? ""),
      node["thumbnailUrl"]?.GetValue<string>() ?? node["pictureUrl"]?.GetValue<string>(),
      node["downloads"]?.GetValue<long>() ?? 0,
      node["endorsements"]?.GetValue<long>() ?? 0,
      DateTimeOffset.TryParse(node["updatedAt"]?.GetValue<string>(), out var updated) ? updated : DateTimeOffset.MinValue,
      node["version"]?.GetValue<string>(),
      $"https://www.nexusmods.com/{GameDomain}/mods/{id}",
      category is null ? [] : [category]);
  }

  private sealed record DownloadLink(string Name, string Uri);
}
