using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Mods;

namespace CINE.LaunchHeim.Core.Packs;

/// <summary><c>launchheim.json</c>, LaunchHeim's own description of a modpack, next to r2modman's <c>export.r2x</c>.</summary>
/// <remarks>
/// It holds what r2modman's format can't: Nexus and CurseForge mods (by id, since their files may not be
/// passed on), local mods (whose files travel inside the pack), which mods were only pulled in as
/// dependencies, and the launch arguments.
/// </remarks>
public sealed class PackManifest
{
  public const int CurrentFormat = 1;

  /// <summary>Raised when the layout changes in a way an older LaunchHeim would get wrong.</summary>
  public int Format { get; set; } = CurrentFormat;

  public string Name { get; set; } = "";

  /// <summary>The LaunchHeim version that wrote the pack, to make sense of bug reports.</summary>
  public string ExportedBy { get; set; } = "";

  public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;

  public string LaunchArguments { get; set; } = "";

  public List<PackMod> Mods { get; set; } = [];

  public static PackManifest From(Instance instance) => new()
  {
    Name = instance.Name,
    ExportedBy = $"LaunchHeim {AppInfo.Version}",
    LaunchArguments = instance.LaunchArguments,
    Mods = instance.Mods.Select(PackMod.From).ToList(),
  };
}

public sealed class PackMod
{
  public ModSource Source { get; set; }

  /// <summary>The Thunderstore full name, or the Nexus or CurseForge mod id, as in <see cref="InstalledMod.SourceId"/>.</summary>
  public string Id { get; set; } = "";

  /// <summary>The Nexus or CurseForge file id. Thunderstore pins by <see cref="Version"/>.</summary>
  public string? FileId { get; set; }

  public string Name { get; set; } = "";
  public string Author { get; set; } = "";
  public string Version { get; set; } = "";
  public string? WebsiteUrl { get; set; }
  public string? IconUrl { get; set; }
  public bool Enabled { get; set; } = true;
  public bool InstalledAsDependency { get; set; }

  /// <summary>Local mods only: their files as stored in the pack, relative to the instance, as they are on disk.</summary>
  public List<string>? Files { get; set; }

  public List<string> Dependencies { get; set; } = [];

  public static PackMod From(InstalledMod mod) => new()
  {
    Source = mod.Source,
    Id = mod.SourceId,
    FileId = mod.Source == ModSource.Thunderstore ? null : mod.FileId,
    Name = mod.Name,
    Author = mod.Author,
    Version = mod.Version,
    WebsiteUrl = mod.WebsiteUrl,
    IconUrl = mod.IconUrl,
    Enabled = mod.Enabled,
    InstalledAsDependency = mod.InstalledAsDependency,
    Files = mod.Source == ModSource.Local ? mod.Files.Select(f => mod.Enabled ? f : ModInstaller.DisabledName(f)).ToList() : null,
    Dependencies = mod.Dependencies,
  };

  /// <param name="inferDependency">For r2modman's list, which doesn't say which mods were only pulled in.</param>
  public PinnedMod ToPin(bool inferDependency = false) => new(
    Source,
    Id,
    Source == ModSource.Thunderstore ? NullIfEmpty(Version) : FileId,
    string.IsNullOrWhiteSpace(Name) ? Id : Name,
    Enabled,
    inferDependency ? null : InstalledAsDependency);

  private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
