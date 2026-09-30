using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Mods;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Packs;

public sealed record PackExportReport(int Mods, int ConfigFiles, int LocalMods, IReadOnlyList<string> NotInR2modman);

public sealed record PackImportReport(Instance Instance, InstallReport Install, int ConfigFiles, bool FromR2modman);

/// <summary>Exports an instance as a modpack file and imports one as a new instance.</summary>
/// <remarks>
/// <para>
/// A pack is an r2modman profile (<c>.r2z</c>): a zip with <c>export.r2x</c>, the configs under
/// <c>BepInEx/config/</c>, and LaunchHeim's <c>launchheim.json</c>. r2modman and the Thunderstore Mod
/// Manager import it as a profile (they extract launchheim.json into the profile folder and leave it be),
/// and LaunchHeim imports their exports, so a pack made here reaches players on either.
/// </para>
/// <para>
/// Mods are listed, not packed: they're downloaded again on import at the exact version, which keeps the
/// file small and respects that Nexus and CurseForge files may not be passed on. Only local mods, which
/// have nowhere to be downloaded from, travel as files, at their place in the instance, which r2modman
/// also understands. Configs travel whole; they are what makes a modpack more than a mod list.
/// </para>
/// </remarks>
public sealed class PackService(InstanceStore store, ModService mods, AppPaths paths)
{
  public const string ManifestEntry = "launchheim.json";
  public const string FileExtension = ".r2z";
  private const string ConfigFolder = "BepInEx/config/";

  // r2modman's older exports put the configs under config/ instead of BepInEx/config/.
  private const string LegacyConfigFolder = "config/";

  private string TempRoot => Path.Combine(paths.CacheDirectory, "tmp");

  public PackExportReport Export(Instance instance, string destination)
  {
    var directory = store.DirectoryOf(instance);
    var manifest = PackManifest.From(instance);
    var configFiles = 0;

    // Written next to the target and renamed at the end, so a failure never leaves half a pack behind.
    var temp = destination + ".tmp";
    try
    {
      using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
      {
        WriteText(zip, ManifestEntry, JsonSerializer.Serialize(manifest, JsonFile.Options));
        WriteText(zip, R2x.EntryName, R2x.Write(instance.Name, instance.Mods));

        var config = Path.Combine(directory, "BepInEx", "config");
        if (Directory.Exists(config))
        {
          foreach (var file in Directory.EnumerateFiles(config, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
          {
            AddFile(zip, file, ConfigFolder + ModInstaller.NormalizePath(Path.GetRelativePath(config, file)));
            configFiles++;
          }
        }

        foreach (var mod in manifest.Mods.Where(m => m.Files is not null))
        {
          foreach (var file in mod.Files!.Where(f => File.Exists(Path.Combine(directory, f))))
          {
            AddFile(zip, Path.Combine(directory, file), file);
          }
        }
      }

      File.Move(temp, destination, overwrite: true);
    }
    finally
    {
      File.Delete(temp);
    }

    var notInR2modman = instance.Mods.Where(m => m.Source is ModSource.Nexus or ModSource.CurseForge).Select(m => m.Name).ToList();
    return new PackExportReport(
      instance.Mods.Count(m => !m.IsLoader),
      configFiles,
      instance.Mods.Count(m => m.Source == ModSource.Local),
      notInR2modman);
  }

  /// <summary>Creates a new instance from a LaunchHeim or r2modman pack, downloading its mods.</summary>
  public async Task<PackImportReport> ImportAsync(string file, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
  {
    progress?.Report(new InstallProgress(Path.GetFileName(file), "Unpacking", null));
    using var extracted = await Task.Run(() => ExtractedMod.FromFile(file, TempRoot), cancellationToken);

    var manifest = ReadManifest(extracted.Directory);
    var fromR2modman = manifest is null;
    manifest ??= ReadR2x(extracted.Directory)
      ?? throw new InvalidDataException($"{Path.GetFileName(file)} is not a modpack: it has neither {ManifestEntry} nor r2modman's {R2x.EntryName}.");

    var name = string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileNameWithoutExtension(file) : manifest.Name;
    var instance = store.Create(name);
    try
    {
      instance.LaunchArguments = manifest.LaunchArguments;
      var pins = manifest.Mods.Where(m => m.Source != ModSource.Local).Select(m => m.ToPin(inferDependency: fromR2modman)).ToList();
      var install = await mods.InstallPinnedAsync(instance, pins, progress, cancellationToken);

      progress?.Report(new InstallProgress(name, "Copying configs", null));
      var directory = store.DirectoryOf(instance);
      var configFiles = await Task.Run(() => CopyPayload(extracted.Directory, directory), cancellationToken);
      AddLocalMods(instance, directory, manifest.Mods.Where(m => m.Source == ModSource.Local));

      store.Save(instance);
      return new PackImportReport(instance, install, configFiles, fromR2modman);
    }
    catch
    {
      store.Delete(instance);
      throw;
    }
  }

  /// <summary>
  /// Copies everything but the two lists into the instance, over what the mods installed: a pack's
  /// configs are the point of the pack, so they win over each mod's defaults.
  /// </summary>
  private static int CopyPayload(string source, string instanceDirectory)
  {
    var root = Path.GetFullPath(instanceDirectory) + Path.DirectorySeparatorChar;
    var configFiles = 0;
    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
      var relative = ModInstaller.NormalizePath(Path.GetRelativePath(source, file));
      if (relative.Equals(ManifestEntry, StringComparison.OrdinalIgnoreCase) || relative.Equals(R2x.EntryName, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      if (relative.StartsWith(LegacyConfigFolder, StringComparison.OrdinalIgnoreCase))
      {
        relative = ConfigFolder + relative[LegacyConfigFolder.Length..];
      }

      // ExtractedMod already refuses paths that leave its folder; this holds for the instance as well.
      var target = Path.GetFullPath(Path.Combine(instanceDirectory, relative));
      if (!target.StartsWith(root, StringComparison.Ordinal))
      {
        continue;
      }

      Directory.CreateDirectory(Path.GetDirectoryName(target)!);
      File.Copy(file, target, overwrite: true);
      if (relative.StartsWith(ConfigFolder, StringComparison.OrdinalIgnoreCase))
      {
        configFiles++;
      }
    }

    return configFiles;
  }

  /// <remarks>
  /// The file list comes from the pack, and uninstalling deletes exactly those files, so a path that
  /// points outside the instance is dropped here rather than trusted.
  /// </remarks>
  private static void AddLocalMods(Instance instance, string directory, IEnumerable<PackMod> locals)
  {
    var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
    foreach (var local in locals)
    {
      var key = InstalledMod.MakeKey(ModSource.Local, local.Id);
      if (instance.FindMod(key) is not null)
      {
        continue;
      }

      var files = (local.Files ?? [])
        .Select(ModInstaller.NormalizePath)
        .Where(f => Path.GetFullPath(Path.Combine(directory, f)).StartsWith(root, StringComparison.Ordinal) && File.Exists(Path.Combine(directory, f)))
        // InstalledMod lists files by their enabled name.
        .Select(f => f.EndsWith(".dll" + ModInstaller.DisabledSuffix, StringComparison.OrdinalIgnoreCase) ? f[..^ModInstaller.DisabledSuffix.Length] : f)
        .ToList();
      if (files.Count == 0)
      {
        continue;
      }

      instance.Mods.Add(new InstalledMod
      {
        Key = key,
        Source = ModSource.Local,
        SourceId = local.Id,
        Name = local.Name,
        Author = local.Author,
        Version = local.Version,
        WebsiteUrl = local.WebsiteUrl,
        Enabled = local.Enabled,
        InstalledAsDependency = local.InstalledAsDependency,
        Files = files,
        Dependencies = local.Dependencies,
      });
    }
  }

  private static PackManifest? ReadManifest(string directory)
  {
    var path = Path.Combine(directory, ManifestEntry);
    if (!File.Exists(path))
    {
      return null;
    }

    PackManifest? manifest;
    try
    {
      manifest = JsonFile.Read<PackManifest>(path);
    }
    catch (JsonException ex)
    {
      throw new InvalidDataException($"The pack's {ManifestEntry} is damaged: {ex.Message}");
    }

    if (manifest is { Format: > PackManifest.CurrentFormat })
    {
      throw new InvalidDataException("This pack was made by a newer LaunchHeim. Update LaunchHeim to import it.");
    }

    return manifest;
  }

  private static PackManifest? ReadR2x(string directory)
  {
    var path = Path.Combine(directory, R2x.EntryName);
    if (!File.Exists(path))
    {
      return null;
    }

    var profile = R2x.Read(File.ReadAllText(path));
    return new PackManifest
    {
      Name = profile.Name,
      Mods = profile.Mods.Select(m => new PackMod
      {
        Source = ModSource.Thunderstore,
        Id = m.FullName,
        Name = m.FullName[(m.FullName.IndexOf('-') + 1)..].Replace('_', ' '),
        Version = m.Version,
        Enabled = m.Enabled,
      }).ToList(),
    };
  }

  private static void WriteText(ZipArchive zip, string entry, string text)
  {
    using var writer = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    writer.Write(text);
  }

  // Read with sharing, so a config the running game holds open can still be packed.
  private static void AddFile(ZipArchive zip, string file, string entry)
  {
    using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    var zipEntry = zip.CreateEntry(entry, CompressionLevel.Optimal);
    zipEntry.LastWriteTime = File.GetLastWriteTime(file);
    using var output = zipEntry.Open();
    input.CopyTo(output);
  }
}
