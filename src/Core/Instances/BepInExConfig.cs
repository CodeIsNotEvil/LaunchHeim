namespace CINE.LaunchHeim.Core.Instances;

/// <summary>Reads and changes single settings in an instance's <c>BepInEx/config/BepInEx.cfg</c>.</summary>
/// <remarks>
/// The file is edited line by line rather than parsed and rewritten, so the comments BepInEx writes
/// above every entry, and anything the user changed by hand, stay as they are. BepInEx fills in the
/// entries a file lacks on its next start, so a missing file or section only needs the one entry.
/// </remarks>
public static class BepInExConfig
{
  public const string RelativePath = "BepInEx/config/BepInEx.cfg";

  public const string ConsoleSection = "Logging.Console";
  public const string EnabledKey = "Enabled";

  /// <summary>
  /// BepInEx's own console window. On Windows it opens a real console next to the game; on Linux it
  /// writes to the game's stdout, which no one sees when the game is started from a launcher.
  /// </summary>
  public static bool IsConsoleEnabled(string instanceDirectory) =>
    string.Equals(Get(instanceDirectory, ConsoleSection, EnabledKey), "true", StringComparison.OrdinalIgnoreCase);

  public static void SetConsoleEnabled(string instanceDirectory, bool enabled) =>
    Set(instanceDirectory, ConsoleSection, EnabledKey, enabled ? "true" : "false");

  public static string? Get(string instanceDirectory, string section, string key)
  {
    var path = Path.Combine(instanceDirectory, RelativePath);
    if (!File.Exists(path))
    {
      return null;
    }

    var current = "";
    foreach (var line in File.ReadLines(path))
    {
      if (SectionOf(line) is { } name)
      {
        current = name;
      }
      else if (current.Equals(section, StringComparison.OrdinalIgnoreCase) && Entry(line) is { } entry && entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
      {
        return entry.Value;
      }
    }

    return null;
  }

  public static void Set(string instanceDirectory, string section, string key, string value)
  {
    var path = Path.Combine(instanceDirectory, RelativePath);
    var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
    var newLine = $"{key} = {value}";

    var sectionStart = lines.FindIndex(l => SectionOf(l) is { } name && name.Equals(section, StringComparison.OrdinalIgnoreCase));
    if (sectionStart < 0)
    {
      if (lines.Count > 0 && lines[^1].Trim().Length > 0)
      {
        lines.Add("");
      }

      lines.AddRange([$"[{section}]", "", newLine]);
    }
    else
    {
      var sectionEnd = lines.FindIndex(sectionStart + 1, l => SectionOf(l) is not null);
      if (sectionEnd < 0)
      {
        sectionEnd = lines.Count;
      }

      var at = lines.FindIndex(sectionStart + 1, sectionEnd - sectionStart - 1, l => Entry(l) is { } e && e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
      if (at >= 0)
      {
        lines[at] = newLine;
      }
      else
      {
        lines.Insert(sectionStart + 1, newLine);
      }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    // BepInEx reads the file with File.ReadAllLines, which takes UTF-8 with or without a BOM.
    File.WriteAllLines(path, lines);
  }

  private static string? SectionOf(string line)
  {
    var trimmed = line.Trim();
    return trimmed.Length > 2 && trimmed[0] == '[' && trimmed[^1] == ']' ? trimmed[1..^1].Trim() : null;
  }

  private static (string Key, string Value)? Entry(string line)
  {
    var trimmed = line.Trim();
    if (trimmed.StartsWith('#') || trimmed.StartsWith(';'))
    {
      return null;
    }

    var equals = trimmed.IndexOf('=');
    return equals > 0 ? (trimmed[..equals].Trim(), trimmed[(equals + 1)..].Trim()) : null;
  }
}
