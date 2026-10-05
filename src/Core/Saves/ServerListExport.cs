using System.Text.Json;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Saves;

/// <summary>
/// <c>launchheim-servers.json</c>: the dedicated servers from Valheim's Favorites and Recent lists, sent to
/// the companion app with every sync so the phone can show who is online. Read by its
/// <c>servers/ServerList.kt</c>.
/// </summary>
/// <remarks>Passwords stay on the PC; the phone only needs the address to ask a server for its players.</remarks>
public sealed class ServerListExport
{
  public const string FileName = "launchheim-servers.json";

  public int Format { get; set; } = 1;
  public string ExportedBy { get; set; } = "";
  public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;
  public List<Entry> Servers { get; set; } = [];

  public sealed record Entry(string Name, string Address, bool IsFavorite, bool IsRecent);

  public static ServerListExport From(IEnumerable<ValheimServer> servers) => new()
  {
    ExportedBy = $"LaunchHeim {AppInfo.Version}",
    Servers = servers.Select(s => new Entry(s.Name, s.Address, s.IsFavorite, s.IsRecent)).ToList(),
  };

  public void Write(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonFile.Options));
}
