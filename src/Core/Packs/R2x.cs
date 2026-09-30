using System.Globalization;
using System.Text;
using System.Text.Json;
using CINE.LaunchHeim.Core.Instances;

namespace CINE.LaunchHeim.Core.Packs;

/// <summary>r2modman's profile list, <c>export.r2x</c>: the profile name and each Thunderstore mod's version.</summary>
/// <remarks>
/// <para>
/// r2modman and the Thunderstore Mod Manager share profiles as <c>.r2z</c>, a zip holding this file and
/// the profile's configs. It's what Valheim players already pass around, so LaunchHeim reads and writes
/// it too. The YAML looks like this:
/// </para>
/// <code>
/// profileName: Survival
/// mods:
///   - name: denikson-BepInExPack_Valheim
///     version:
///       major: 5
///       minor: 4
///       patch: 2202
///     enabled: true
/// </code>
/// <para>
/// That's all r2modman writes, so a line-based reader is enough and no YAML library has to ship (and be
/// added to the third-party notices) for one fixed shape.
/// </para>
/// </remarks>
public static class R2x
{
  public const string EntryName = "export.r2x";

  public sealed record Mod(string FullName, string Version, bool Enabled);

  public sealed record Profile(string Name, IReadOnlyList<Mod> Mods);

  /// <summary>
  /// Lists the Thunderstore mods. Nexus, CurseForge and local mods can't be expressed in r2modman's
  /// format; LaunchHeim's own <c>launchheim.json</c> next to it carries them.
  /// </summary>
  public static string Write(string profileName, IEnumerable<InstalledMod> mods)
  {
    var entries = mods
      .Where(m => m.Source == ModSource.Thunderstore)
      .Select(m => (m, Version: SemVer(m.Version)))
      .Where(x => x.Version is not null)
      .ToList();

    var text = new StringBuilder();
    // A JSON string is a valid YAML double-quoted scalar, escapes included.
    text.Append("profileName: ").AppendLine(JsonSerializer.Serialize(profileName));
    if (entries.Count == 0)
    {
      text.AppendLine("mods: []");
      return text.ToString();
    }

    text.AppendLine("mods:");
    foreach (var (mod, version) in entries)
    {
      text.Append("  - name: ").AppendLine(mod.SourceId);
      text.AppendLine("    version:");
      text.Append("      major: ").AppendLine(version![0].ToString(CultureInfo.InvariantCulture));
      text.Append("      minor: ").AppendLine(version[1].ToString(CultureInfo.InvariantCulture));
      text.Append("      patch: ").AppendLine(version[2].ToString(CultureInfo.InvariantCulture));
      text.Append("    enabled: ").AppendLine(mod.Enabled ? "true" : "false");
    }

    return text.ToString();
  }

  public static Profile Read(string text)
  {
    var name = "";
    var mods = new List<Mod>();
    string? fullName = null;
    int major = 0, minor = 0, patch = 0;
    var enabled = true;

    void Flush()
    {
      if (!string.IsNullOrWhiteSpace(fullName))
      {
        mods.Add(new Mod(fullName, $"{major}.{minor}.{patch}", enabled));
      }

      (fullName, major, minor, patch, enabled) = (null, 0, 0, 0, true);
    }

    foreach (var raw in text.Split('\n'))
    {
      var line = raw.TrimEnd('\r', ' ', '\t');
      var content = line.TrimStart();
      if (content.Length == 0 || content.StartsWith('#'))
      {
        continue;
      }

      if (content.StartsWith("- ", StringComparison.Ordinal))
      {
        Flush();
        content = content[2..].TrimStart();
      }

      var colon = content.IndexOf(':');
      if (colon <= 0)
      {
        continue;
      }

      var key = content[..colon].Trim();
      var value = Unquote(content[(colon + 1)..].Trim());
      switch (key)
      {
        case "profileName" when line.Length == content.Length:
          name = value;
          break;
        case "name":
          fullName = value;
          break;
        case "major":
          major = Number(value);
          break;
        case "minor":
          minor = Number(value);
          break;
        case "patch":
          patch = Number(value);
          break;
        case "enabled":
          enabled = !value.Equals("false", StringComparison.OrdinalIgnoreCase);
          break;
      }
    }

    Flush();
    return new Profile(name, mods);
  }

  private static int[]? SemVer(string version)
  {
    var parts = version.Split('.');
    return parts.Length == 3 && parts.All(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _))
      ? parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray()
      : null;
  }

  private static int Number(string value) =>
    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;

  private static string Unquote(string value)
  {
    if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
    {
      try
      {
        return JsonSerializer.Deserialize<string>(value) ?? "";
      }
      catch (JsonException)
      {
        return value[1..^1];
      }
    }

    return value.Length >= 2 && value[0] == '\'' && value[^1] == '\'' ? value[1..^1].Replace("''", "'") : value;
  }
}
