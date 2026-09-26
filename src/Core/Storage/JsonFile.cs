using System.Text.Json;
using System.Text.Json.Serialization;

namespace CINE.LaunchHeim.Core.Storage;

internal static class JsonFile
{
  public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
  {
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() },
  };

  public static T? Read<T>(string path)
  {
    if (!File.Exists(path))
    {
      return default;
    }

    using var stream = File.OpenRead(path);
    return JsonSerializer.Deserialize<T>(stream, Options);
  }

  /// <summary>
  /// Writes to a temporary file first and renames it over the target, so a crash or a full disk halfway
  /// through never leaves a truncated instance.json behind.
  /// </summary>
  public static void Write<T>(string path, T value, UnixFileMode? mode = null)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var temp = path + ".tmp";
    using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
    {
      JsonSerializer.Serialize(stream, value, Options);
    }

    if (mode is { } unixMode && !OperatingSystem.IsWindows())
    {
      File.SetUnixFileMode(temp, unixMode);
    }

    File.Move(temp, path, overwrite: true);
  }
}
