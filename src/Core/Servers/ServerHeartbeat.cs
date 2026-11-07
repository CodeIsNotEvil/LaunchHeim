using System.Text.Json;

namespace CINE.LaunchHeim.Core.Servers;

/// <summary>
/// The plugin's <c>status.json</c>, written every 15 seconds while the world is loaded and once more when
/// the server stops (<c>FileChannel.Heartbeat</c> in the plugin).
/// </summary>
/// <remarks>
/// For a hosted server it's the only sign of life that needs nothing but its files: a private server
/// doesn't answer Steam's query, and its process is out of sight. Clocks differ between host and PC, so
/// a beat counts as current within <see cref="MaxAge"/>, well above the 15 s it's written at.
/// </remarks>
public sealed record ServerHeartbeat(DateTimeOffset Time, int Players, string World, string Version, bool Stopped)
{
  public const string FileName = ServerCommandChannel.Directory + "/status.json";

  public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(75);

  /// <summary>Up, as of <paramref name="now"/>: a recent beat that doesn't say it stopped.</summary>
  public bool IsAlive(DateTimeOffset now) => !Stopped && now - Time < MaxAge;

  public static ServerHeartbeat? Parse(string? json)
  {
    if (string.IsNullOrWhiteSpace(json))
    {
      return null;
    }

    try
    {
      using var document = JsonDocument.Parse(json);
      var root = document.RootElement;
      return new ServerHeartbeat(
        root.GetProperty("time").GetDateTimeOffset(),
        root.TryGetProperty("players", out var players) ? players.GetInt32() : 0,
        root.TryGetProperty("world", out var world) ? world.GetString() ?? "" : "",
        root.TryGetProperty("version", out var version) ? version.GetString() ?? "" : "",
        root.TryGetProperty("stopped", out var stopped) && stopped.GetBoolean());
    }
    catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
    {
      return null;
    }
  }
}
