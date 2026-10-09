namespace CINE.LaunchHeim.Core.Instances;

/// <summary>
/// One self-contained modded setup: its own BepInEx, plugins and configs in its own folder.
/// </summary>
public sealed class Instance
{
  /// <summary>The folder name under the instances directory. Never changes, even on rename.</summary>
  public string Id { get; set; } = "";

  public string Name { get; set; } = "";

  /// <summary>
  /// The id a pack carried (<c>launchheim.json</c> <c>instanceId</c>) when this instance was imported from
  /// it and no instance here had that id: the companion app's own id for a list made on the phone, or
  /// another PC's instance. Packs of this instance carry it instead of <see cref="Id"/>, so the device the
  /// list came from recognises it, and a pack coming back with it updates this instance instead of
  /// importing another copy. The phone never learns <see cref="Id"/> otherwise, since nothing is sent back
  /// when a pack is imported. Null for instances made here.
  /// </summary>
  public string? LinkId { get; set; }

  /// <summary>The icon's color as <c>#rrggbb</c>, picked by the user, or null for one derived from the id.</summary>
  public string? Color { get; set; }

  /// <summary>The icon's letters, up to three, or null for the name's initials.</summary>
  public string? Initials { get; set; }

  /// <summary>
  /// A picture shown instead of the letters: a file in the instance folder (<see cref="InstanceIcon"/>),
  /// so it goes along when the folder is copied, duplicated or backed up. Null for none.
  /// </summary>
  public string? IconFile { get; set; }

  public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

  public DateTimeOffset? LastPlayedAt { get; set; }

  public string LaunchArguments { get; set; } = "";

  public List<InstalledMod> Mods { get; set; } = [];

  public InstalledMod? FindMod(string key) =>
    Mods.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));

  public InstalledMod? Loader => Mods.FirstOrDefault(m => m.IsLoader);

  /// <summary>Mods that list <paramref name="key"/> as a dependency and would break without it.</summary>
  public IEnumerable<InstalledMod> Dependents(string key) =>
    Mods.Where(m => m.Dependencies.Any(d => string.Equals(d, key, StringComparison.OrdinalIgnoreCase)));
}

public enum ModSource
{
  Thunderstore,
  Nexus,
  CurseForge,
  Local,
}

public sealed class InstalledMod
{
  /// <summary>
  /// Identity across versions, e.g. <c>thunderstore:ValheimModding-Jotunn</c> or <c>nexus:1042</c>.
  /// Installing another version of the same key replaces the old one instead of adding a copy.
  /// </summary>
  public string Key { get; set; } = "";

  public ModSource Source { get; set; }

  /// <summary>The id on the source site: the Thunderstore full name, or the Nexus or CurseForge mod id.</summary>
  public string SourceId { get; set; } = "";

  /// <summary>The Nexus or CurseForge file id. For Thunderstore, the version number is the file.</summary>
  public string? FileId { get; set; }

  public string Name { get; set; } = "";
  public string Author { get; set; } = "";
  public string Version { get; set; } = "";
  public string? IconUrl { get; set; }
  public string? WebsiteUrl { get; set; }
  public bool Enabled { get; set; } = true;

  /// <summary>BepInExPack itself. It lives in the instance root instead of a plugin folder.</summary>
  public bool IsLoader { get; set; }

  /// <summary>Pulled in by another mod rather than picked by the user.</summary>
  public bool InstalledAsDependency { get; set; }

  public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;

  /// <summary>Installed files relative to the instance folder, with forward slashes, as enabled.</summary>
  public List<string> Files { get; set; } = [];

  /// <summary>Keys of the mods this one needs.</summary>
  public List<string> Dependencies { get; set; } = [];

  public static string MakeKey(ModSource source, string sourceId) =>
    $"{source.ToString().ToLowerInvariant()}:{sourceId}";
}
