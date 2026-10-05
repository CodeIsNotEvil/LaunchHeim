using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Mods;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Packs;

public sealed record PackExportReport(int Mods, int ConfigFiles, int LocalMods, IReadOnlyList<string> NotInR2modman);

public sealed record PackImportReport(Instance Instance, InstallReport Install, int ConfigFiles, bool FromR2modman);

/// <summary>A pack's mod list, read without installing anything.</summary>
public sealed record PackContents(PackManifest Manifest, bool FromR2modman);

/// <summary>What applying a pack to an instance would change, for the confirmation dialog.</summary>
/// <param name="Changed">Mods at another version (Thunderstore) or file (Nexus, CurseForge) in the pack.</param>
public sealed record PackChanges(
  IReadOnlyList<PackMod> Added,
  IReadOnlyList<InstalledMod> Removed,
  IReadOnlyList<(InstalledMod Mod, PackMod To)> Changed,
  IReadOnlyList<(InstalledMod Mod, bool Enabled)> Toggled)
{
  public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0 && Toggled.Count == 0;
}

public sealed record PackApplyReport(InstallReport Install, int Removed, int Toggled);

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

  /// <summary>Reads a pack's mod list straight from the zip, without unpacking or installing anything.</summary>
  public static PackContents Read(string file)
  {
    using var zip = ZipFile.OpenRead(file);
    var manifestEntry = zip.Entries.FirstOrDefault(e => e.FullName.Equals(ManifestEntry, StringComparison.OrdinalIgnoreCase));
    if (manifestEntry is not null)
    {
      using var stream = manifestEntry.Open();
      return new PackContents(ParseManifest(stream, Path.GetFileNameWithoutExtension(file)), FromR2modman: false);
    }

    var r2x = zip.Entries.FirstOrDefault(e => e.FullName.Equals(R2x.EntryName, StringComparison.OrdinalIgnoreCase))
      ?? throw new InvalidDataException($"{Path.GetFileName(file)} is not a modpack: it has neither {ManifestEntry} nor r2modman's {R2x.EntryName}.");
    using var reader = new StreamReader(r2x.Open());
    return new PackContents(FromProfile(R2x.Read(reader.ReadToEnd())), FromR2modman: true);
  }

  /// <summary>
  /// Compares an instance with a pack of it that was edited elsewhere (the companion app). Local mods
  /// can only be added through their files, so a pack never adds one, and BepInEx is never removed.
  /// </summary>
  public static PackChanges Compare(Instance instance, PackManifest manifest)
  {
    var pack = PackMods(manifest);
    var added = new List<PackMod>();
    var changed = new List<(InstalledMod, PackMod)>();
    var toggled = new List<(InstalledMod, bool)>();
    foreach (var (key, packMod) in pack)
    {
      if (instance.FindMod(key) is not { } mod)
      {
        if (packMod.Source != ModSource.Local)
        {
          added.Add(packMod);
        }

        continue;
      }

      if (Differs(mod, packMod))
      {
        changed.Add((mod, packMod));
      }

      if (mod.Enabled != packMod.Enabled)
      {
        toggled.Add((mod, packMod.Enabled));
      }
    }

    // A dependency the pack doesn't list stays while a mod the pack keeps needs it. The phone only
    // resolves Thunderstore's and CurseForge's dependencies itself; what LaunchHeim pulled in on
    // install (a Nexus mod's requirements, a dependency added after the pack left) isn't in the list.
    var removed = instance.Mods
      .Where(m => !m.IsLoader && !pack.ContainsKey(m.Key))
      .Where(m => !(m.InstalledAsDependency && NeededBy(instance, m.Key, pack)))
      .ToList();
    return new PackChanges(added, removed, changed, toggled);
  }

  /// <summary>
  /// Makes the instance match the pack's mod list: removes what the pack dropped, installs what it added
  /// or moved to another version (at exactly that version), and switches mods on and off as it says.
  /// </summary>
  /// <remarks>
  /// Configs, launch arguments and the name stay as they are. The pack's configs are older copies of
  /// this instance's own (the phone can't edit them), so copying them would only undo changes made here.
  /// </remarks>
  public async Task<PackApplyReport> ApplyAsync(Instance instance, PackManifest manifest, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
  {
    var changes = Compare(instance, manifest);
    var pack = PackMods(manifest);

    // Removing first, because removing a mod also removes the dependencies only it needed; one the pack
    // still lists is installed again below.
    var removed = 0;
    foreach (var mod in changes.Removed.Where(m => instance.FindMod(m.Key) is not null))
    {
      progress?.Report(new InstallProgress(mod.Name, "Removing", null));
      removed += (await mods.RemoveAsync(instance, mod.Key, cancellationToken)).Removed.Count;
    }

    var pins = pack
      .Where(p => p.Value.Source != ModSource.Local && (instance.FindMod(p.Key) is not { } mod || Differs(mod, p.Value)))
      .Select(p => p.Value.ToPin())
      .ToList();
    var install = pins.Count > 0
      ? await mods.InstallPinnedAsync(instance, pins, progress, cancellationToken)
      : new InstallReport([], []);

    var toggled = 0;
    foreach (var (key, packMod) in pack)
    {
      if (instance.FindMod(key) is not { } mod)
      {
        continue;
      }

      if (mod.Enabled != packMod.Enabled)
      {
        toggled += (await mods.SetEnabledAsync(instance, key, packMod.Enabled)).Count;
      }

      // The phone may have picked a mod that was only a dependency here, which keeps it when its
      // dependents go. BepInEx always stays.
      mod.InstalledAsDependency = !mod.IsLoader && packMod.InstalledAsDependency;
    }

    store.Save(instance);
    return new PackApplyReport(install, removed, toggled);
  }

  /// <summary>Whether a mod the pack keeps needs this one, directly or through other dependencies.</summary>
  private static bool NeededBy(Instance instance, string key, Dictionary<string, PackMod> pack)
  {
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var queue = new Queue<string>([key]);
    while (queue.TryDequeue(out var next))
    {
      foreach (var dependent in instance.Dependents(next).Where(d => seen.Add(d.Key)))
      {
        if (pack.ContainsKey(dependent.Key))
        {
          return true;
        }

        queue.Enqueue(dependent.Key);
      }
    }

    return false;
  }

  private static Dictionary<string, PackMod> PackMods(PackManifest manifest) =>
    manifest.Mods
      .GroupBy(m => InstalledMod.MakeKey(m.Source, m.Id), StringComparer.OrdinalIgnoreCase)
      .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

  private static bool Differs(InstalledMod mod, PackMod packMod) => packMod.Source switch
  {
    ModSource.Thunderstore => !string.IsNullOrWhiteSpace(packMod.Version) && !string.Equals(mod.Version, packMod.Version, StringComparison.OrdinalIgnoreCase),
    ModSource.Local => false,
    _ => packMod.FileId is not null && !string.Equals(mod.FileId, packMod.FileId, StringComparison.OrdinalIgnoreCase),
  };

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

    using var stream = File.OpenRead(path);
    return ParseManifest(stream, "");
  }

  private static PackManifest ParseManifest(Stream stream, string fallbackName)
  {
    PackManifest? manifest;
    try
    {
      manifest = JsonSerializer.Deserialize<PackManifest>(stream, JsonFile.Options);
    }
    catch (JsonException ex)
    {
      throw new InvalidDataException($"The pack's {ManifestEntry} is damaged: {ex.Message}");
    }

    if (manifest is null)
    {
      throw new InvalidDataException($"The pack's {ManifestEntry} is empty.");
    }

    if (manifest.Format > PackManifest.CurrentFormat)
    {
      throw new InvalidDataException("This pack was made by a newer LaunchHeim. Update LaunchHeim to import it.");
    }

    if (string.IsNullOrWhiteSpace(manifest.Name))
    {
      manifest.Name = fallbackName;
    }

    return manifest;
  }

  private static PackManifest? ReadR2x(string directory)
  {
    var path = Path.Combine(directory, R2x.EntryName);
    return File.Exists(path) ? FromProfile(R2x.Read(File.ReadAllText(path))) : null;
  }

  private static PackManifest FromProfile(R2x.Profile profile) => new()
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
