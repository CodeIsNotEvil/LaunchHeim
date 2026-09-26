using CINE.LaunchHeim.Core.Instances;

namespace CINE.LaunchHeim.Core.Mods;

/// <summary>Copies an unpacked mod into an instance and remembers every file it placed.</summary>
/// <remarks>
/// <para>
/// The layout follows r2modman's BepInEx rules, because that is what Thunderstore mods are packaged for
/// and what users expect to find when they open the folder:
/// </para>
/// <list type="bullet">
///   <item>BepInExPack (the loader) goes into the instance root, next to where the game would have it.</item>
///   <item>Plugins go into <c>BepInEx/plugins/&lt;Mod&gt;/</c>, one folder per mod, so two mods shipping
///   a file with the same name cannot overwrite each other and uninstalling removes exactly one folder.</item>
///   <item>Patchers and MonoMod hooks get the same per-mod folder in their own directories.</item>
///   <item>Config files go straight into <c>BepInEx/config/</c>, because plugins look for them by name
///   there. An existing config is never overwritten: it holds the user's settings.</item>
/// </list>
/// <para>
/// Archives that already contain a <c>BepInEx/</c> tree (common on Nexus) are mapped from that tree.
/// Anything else is treated as plugin content.
/// </para>
/// </remarks>
public sealed class ModInstaller
{
  private static readonly string[] PerModFolders = ["plugins", "patchers", "monomod"];
  private static readonly string[] JunkNames = ["__MACOSX", ".DS_Store", "Thumbs.db"];

  public const string PreloaderInArchive = "BepInEx/core/BepInEx.Preloader.dll";
  public const string DisabledSuffix = ".disabled";

  /// <summary>Installs the files and returns them relative to the instance, with forward slashes.</summary>
  /// <param name="folderName">The per-mod folder name, e.g. <c>ValheimModding-Jotunn</c>.</param>
  public InstallResult Install(string sourceDirectory, string instanceDirectory, string folderName)
  {
    var files = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
      .Select(f => NormalizePath(Path.GetRelativePath(sourceDirectory, f)))
      .Where(f => !f.Split('/').Any(part => JunkNames.Contains(part, StringComparer.OrdinalIgnoreCase)))
      .ToList();

    var loaderRoot = FindLoaderRoot(files);
    var mapping = loaderRoot is not null
      ? files.Where(f => f.StartsWith(loaderRoot, StringComparison.Ordinal))
          .Select(f => (Source: f, Target: f[loaderRoot.Length..]))
      : files.Select(f => (Source: f, Target: MapModFile(StripWrapper(f, files), SafeFolderName(folderName))));

    var installed = new List<string>();
    var root = Path.GetFullPath(instanceDirectory) + Path.DirectorySeparatorChar;
    foreach (var (source, target) in mapping)
    {
      if (target.Length == 0)
      {
        continue;
      }

      var destination = Path.GetFullPath(Path.Combine(instanceDirectory, target));
      if (!destination.StartsWith(root, StringComparison.Ordinal))
      {
        throw new InvalidDataException($"The mod tried to write outside the instance: {source}");
      }

      var isConfig = target.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase);
      if (isConfig && File.Exists(destination))
      {
        continue;
      }

      Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
      File.Copy(Path.Combine(sourceDirectory, source), destination, overwrite: true);

      // Configs are left out of the file list so uninstalling or updating a mod keeps the user's settings.
      if (!isConfig)
      {
        installed.Add(target);
      }
    }

    return new InstallResult(installed, loaderRoot is not null);
  }

  /// <summary>
  /// Deletes a mod's files and then any folders that became empty, but never the top-level BepInEx
  /// folders themselves.
  /// </summary>
  public void Uninstall(string instanceDirectory, InstalledMod mod)
  {
    var directories = new HashSet<string>();
    foreach (var file in mod.Files)
    {
      var path = Path.Combine(instanceDirectory, mod.Enabled ? file : DisabledName(file));
      if (File.Exists(path))
      {
        File.Delete(path);
      }

      directories.Add(Path.GetDirectoryName(path)!);
    }

    var keep = new[] { "", "BepInEx", "BepInEx/plugins", "BepInEx/patchers", "BepInEx/monomod", "BepInEx/config", "BepInEx/core" };
    foreach (var directory in directories.OrderByDescending(d => d.Length))
    {
      var current = directory;
      while (current.Length > instanceDirectory.Length)
      {
        var relative = NormalizePath(Path.GetRelativePath(instanceDirectory, current));
        if (keep.Contains(relative, StringComparer.OrdinalIgnoreCase)
          || !Directory.Exists(current)
          || Directory.EnumerateFileSystemEntries(current).Any())
        {
          break;
        }

        Directory.Delete(current);
        current = Path.GetDirectoryName(current)!;
      }
    }
  }

  /// <summary>
  /// Disabling renames the mod's assemblies to <c>*.dll.disabled</c>. BepInEx only loads <c>*.dll</c>,
  /// so this turns a mod off without losing it, and turning it back on needs no download.
  /// </summary>
  public void SetEnabled(string instanceDirectory, InstalledMod mod, bool enabled)
  {
    if (mod.Enabled == enabled)
    {
      return;
    }

    foreach (var file in mod.Files.Where(IsAssembly))
    {
      var on = Path.Combine(instanceDirectory, file);
      var off = Path.Combine(instanceDirectory, DisabledName(file));
      var (from, to) = enabled ? (off, on) : (on, off);
      if (File.Exists(from))
      {
        File.Move(from, to, overwrite: true);
      }
    }

    mod.Enabled = enabled;
  }

  public static string DisabledName(string file) => IsAssembly(file) ? file + DisabledSuffix : file;

  private static bool IsAssembly(string file) => file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

  public static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');

  /// <summary>The folder inside the archive that holds a whole BepInEx install, or null for normal mods.</summary>
  internal static string? FindLoaderRoot(IReadOnlyCollection<string> files)
  {
    var preloader = files.FirstOrDefault(f => f.EndsWith(PreloaderInArchive, StringComparison.OrdinalIgnoreCase)
      && (f.Length == PreloaderInArchive.Length || f[^(PreloaderInArchive.Length + 1)] == '/'));
    if (preloader is null)
    {
      return null;
    }

    var root = preloader[..^PreloaderInArchive.Length];
    // Doorstop is what makes it a loader pack. A mod that merely bundles BepInEx's core dlls is not one.
    return files.Any(f => f.StartsWith(root + "doorstop_libs/", StringComparison.OrdinalIgnoreCase)) ? root : null;
  }

  /// <summary>
  /// Drops leading folders that only wrap the content, such as <c>MyMod-1.2/</c> or <c>Valheim/</c>,
  /// so the rules below see <c>BepInEx/</c>, <c>plugins/</c> or the plugin files themselves.
  /// </summary>
  internal static string StripWrapper(string file, IReadOnlyCollection<string> files)
  {
    var bepInEx = IndexOfSegment(file, "BepInEx");
    if (bepInEx >= 0)
    {
      return file[bepInEx..];
    }

    var prefix = CommonWrapper(files);
    return prefix.Length > 0 && file.StartsWith(prefix, StringComparison.Ordinal) ? file[prefix.Length..] : file;
  }

  private static string CommonWrapper(IReadOnlyCollection<string> files)
  {
    var prefix = "";
    while (true)
    {
      var remaining = files.Where(f => f.StartsWith(prefix, StringComparison.Ordinal)).Select(f => f[prefix.Length..]).ToList();
      var first = remaining.Select(f => f.Split('/')).ToList();
      if (first.Count == 0 || first.Any(parts => parts.Length < 2))
      {
        return prefix;
      }

      var folder = first[0][0];
      if (first.Any(parts => parts[0] != folder) || IsBepInExFolder(folder))
      {
        return prefix;
      }

      prefix += folder + "/";
    }
  }

  internal static string MapModFile(string file, string folderName)
  {
    var parts = file.Split('/');
    if (parts[0].Equals("BepInEx", StringComparison.OrdinalIgnoreCase))
    {
      if (parts.Length == 2)
      {
        return $"BepInEx/plugins/{folderName}/{parts[1]}";
      }

      return MapBepInExFolder(parts[1], string.Join('/', parts[2..]), folderName);
    }

    if (parts.Length > 1 && IsBepInExFolder(parts[0]))
    {
      return MapBepInExFolder(parts[0], string.Join('/', parts[1..]), folderName);
    }

    // Loose config files next to the dll, which several Nexus mods ship.
    if (parts.Length == 1 && file.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
    {
      return $"BepInEx/config/{file}";
    }

    return $"BepInEx/plugins/{folderName}/{file}";
  }

  private static string MapBepInExFolder(string folder, string rest, string folderName)
  {
    var lower = folder.ToLowerInvariant();
    if (PerModFolders.Contains(lower))
    {
      // Some archives already contain the per-mod folder (plugins/MyMod/MyMod.dll). Nesting it again
      // would still load, but it's tidier to keep one level.
      var restParts = rest.Split('/');
      if (restParts.Length > 1 && restParts[0].Equals(folderName, StringComparison.OrdinalIgnoreCase))
      {
        rest = string.Join('/', restParts[1..]);
      }

      return $"BepInEx/{lower}/{folderName}/{rest}";
    }

    return $"BepInEx/{lower}/{rest}";
  }

  private static bool IsBepInExFolder(string name) =>
    name.ToLowerInvariant() is "plugins" or "patchers" or "monomod" or "config" or "core";

  private static int IndexOfSegment(string path, string segment)
  {
    var index = 0;
    foreach (var part in path.Split('/'))
    {
      if (part.Equals(segment, StringComparison.OrdinalIgnoreCase))
      {
        return index;
      }

      index += part.Length + 1;
    }

    return -1;
  }

  internal static string SafeFolderName(string name)
  {
    var invalid = Path.GetInvalidFileNameChars().Append('/').Append('\\').ToHashSet();
    var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().Trim('.');
    return safe.Length == 0 ? "mod" : safe;
  }
}

public sealed record InstallResult(IReadOnlyList<string> Files, bool IsLoader);
