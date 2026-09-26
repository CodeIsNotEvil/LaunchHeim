using System.Text.Json;
using System.Text.RegularExpressions;
using CINE.LaunchHeim.Core.Catalogs.Thunderstore;
using CINE.LaunchHeim.Core.Instances;

namespace CINE.LaunchHeim.Core.Mods;

/// <summary>
/// Turns a BepInEx setup installed straight into the game folder (the way the BepInExPack README and
/// most guides describe it) into a LaunchHeim instance.
/// </summary>
/// <remarks>
/// The game folder is only read, never changed: the instance gets copies. Plugins become "local" mods,
/// except folders that carry a Thunderstore manifest, which are recognised as the Thunderstore mod so
/// they can be updated later. Configs are copied as they are, so all existing settings come along.
/// </remarks>
public sealed partial class GameFolderImporter(InstanceStore store)
{
  private static readonly string[] LoaderEntries = ["BepInEx/core", "doorstop_libs", "unstripped_corlib", "doorstop_config.ini", ".doorstop_version"];

  public static bool HasBepInEx(string? gameDirectory) =>
    !string.IsNullOrEmpty(gameDirectory) && File.Exists(Path.Combine(gameDirectory, "BepInEx", "core", "BepInEx.Preloader.dll"));

  public Instance Import(string gameDirectory, string name)
  {
    if (!HasBepInEx(gameDirectory))
    {
      throw new InvalidOperationException("There is no BepInEx installation in the game folder to import.");
    }

    var instance = store.Create(name);
    var target = store.DirectoryOf(instance);

    try
    {
      instance.Mods.Add(ImportLoader(gameDirectory, target));

      foreach (var folder in new[] { "plugins", "patchers" })
      {
        var source = Path.Combine(gameDirectory, "BepInEx", folder);
        if (!Directory.Exists(source))
        {
          continue;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
          if (ImportEntry(entry, $"BepInEx/{folder}", target) is { } mod)
          {
            instance.Mods.Add(mod);
          }
        }
      }

      var config = Path.Combine(gameDirectory, "BepInEx", "config");
      if (Directory.Exists(config))
      {
        InstanceStore.CopyDirectory(config, Path.Combine(target, "BepInEx", "config"));
      }

      store.Save(instance);
      return instance;
    }
    catch
    {
      store.Delete(instance);
      throw;
    }
  }

  private static InstalledMod ImportLoader(string gameDirectory, string target)
  {
    var files = new List<string>();
    foreach (var entry in LoaderEntries)
    {
      var source = Path.Combine(gameDirectory, entry);
      files.AddRange(Copy(source, Path.Combine(target, entry), entry));
    }

    return new InstalledMod
    {
      Key = InstalledMod.MakeKey(ModSource.Thunderstore, ThunderstoreCatalog.LoaderFullName),
      Source = ModSource.Thunderstore,
      SourceId = ThunderstoreCatalog.LoaderFullName,
      Name = "BepInExPack Valheim",
      Author = "denikson",
      Version = LoaderVersion(gameDirectory) ?? "",
      WebsiteUrl = "https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/",
      IsLoader = true,
      Files = files,
    };
  }

  private static InstalledMod? ImportEntry(string entry, string relativeFolder, string target)
  {
    var entryName = Path.GetFileName(entry);
    var isDirectory = Directory.Exists(entry);
    if (!isDirectory && !entryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
    {
      // Loose readmes and the like would otherwise each show up as a "mod".
      Copy(entry, Path.Combine(target, relativeFolder, entryName), $"{relativeFolder}/{entryName}");
      return null;
    }

    var files = Copy(entry, Path.Combine(target, relativeFolder, entryName), $"{relativeFolder}/{entryName}");
    var manifest = isDirectory ? ReadManifest(Path.Combine(entry, "manifest.json")) : null;
    var fullName = isDirectory && DependencyString.Parse(entryName + "-0") is { } parsed && manifest?.Name is not null ? parsed.FullName : null;

    if (fullName is not null)
    {
      return new InstalledMod
      {
        Key = InstalledMod.MakeKey(ModSource.Thunderstore, fullName),
        Source = ModSource.Thunderstore,
        SourceId = fullName,
        Name = manifest!.Name!.Replace('_', ' '),
        Author = fullName[..fullName.IndexOf('-')],
        Version = manifest.VersionNumber ?? "",
        Files = files,
        Dependencies = (manifest.Dependencies ?? []).Select(DependencyString.Parse).OfType<DependencyString>()
          .Select(d => InstalledMod.MakeKey(ModSource.Thunderstore, d.FullName)).ToList(),
      };
    }

    var name = isDirectory ? entryName : Path.GetFileNameWithoutExtension(entryName);
    return new InstalledMod
    {
      Key = InstalledMod.MakeKey(ModSource.Local, name),
      Source = ModSource.Local,
      SourceId = name,
      Name = name,
      Files = files,
    };
  }

  private static List<string> Copy(string source, string destination, string relative)
  {
    if (File.Exists(source))
    {
      Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
      File.Copy(source, destination, overwrite: true);
      return [relative];
    }

    if (!Directory.Exists(source))
    {
      return [];
    }

    var files = new List<string>();
    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
      var inner = ModInstaller.NormalizePath(Path.GetRelativePath(source, file));
      var target = Path.Combine(destination, inner);
      Directory.CreateDirectory(Path.GetDirectoryName(target)!);
      File.Copy(file, target, overwrite: true);
      files.Add($"{relative}/{inner}");
    }

    return files;
  }

  /// <summary>BepInExPack writes its version into the first lines of LogOutput.log on every start.</summary>
  private static string? LoaderVersion(string gameDirectory)
  {
    var log = Path.Combine(gameDirectory, "BepInEx", "LogOutput.log");
    if (!File.Exists(log))
    {
      return null;
    }

    foreach (var line in File.ReadLines(log).Take(10))
    {
      var match = PackVersion().Match(line);
      if (match.Success)
      {
        return match.Groups[1].Value;
      }
    }

    return null;
  }

  private static Manifest? ReadManifest(string path)
  {
    try
    {
      return File.Exists(path)
        ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })
        : null;
    }
    catch (JsonException)
    {
      return null;
    }
  }

  private sealed record Manifest(string? Name, string? VersionNumber, string[]? Dependencies);

  [GeneratedRegex(@"BepInExPack Valheim version ([\d.]+)")]
  private static partial Regex PackVersion();
}
