using System.Collections.Concurrent;
using System.Text.Json;
using CINE.LaunchHeim.Core.Catalogs;
using CINE.LaunchHeim.Core.Catalogs.Nexus;
using CINE.LaunchHeim.Core.Catalogs.Thunderstore;
using CINE.LaunchHeim.Core.Downloads;
using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Mods;

public sealed record InstallRequest(ModSource Source, string ModId, string? FileId = null);

public readonly record struct InstallProgress(string ModName, string Stage, double? Fraction);

public sealed record InstallReport(
  IReadOnlyList<InstalledMod> Installed,
  IReadOnlyList<string> Warnings,
  BrowserRequired? Browser = null)
{
  public bool NeedsBrowser => Browser is not null;
}

public sealed record RemoveReport(IReadOnlyList<InstalledMod> Removed);

public sealed class CatalogRegistry(ThunderstoreCatalog thunderstore, NexusCatalog nexus, IModCatalog curseForge)
{
  public ThunderstoreCatalog Thunderstore => thunderstore;
  public NexusCatalog Nexus => nexus;
  public IModCatalog CurseForge => curseForge;

  public IModCatalog this[ModSource source] => source switch
  {
    ModSource.Thunderstore => thunderstore,
    ModSource.Nexus => nexus,
    ModSource.CurseForge => curseForge,
    _ => throw new ArgumentOutOfRangeException(nameof(source), "Local mods have no catalog."),
  };
}

/// <summary>Installs, updates, toggles and removes mods in an instance, dependencies included.</summary>
public sealed class ModService(
  InstanceStore store,
  ModInstaller installer,
  ModDownloader downloader,
  CatalogRegistry catalogs,
  AppPaths paths)
{
  private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

  private string TempRoot => Path.Combine(paths.CacheDirectory, "tmp");

  public async Task<InstallReport> InstallAsync(
    Instance instance,
    InstallRequest request,
    IProgress<InstallProgress>? progress,
    CancellationToken cancellationToken)
  {
    var ticket = await catalogs[request.Source].ResolveDownloadAsync(request.ModId, request.FileId, cancellationToken);
    if (ticket is BrowserRequired browser)
    {
      return new InstallReport([], [], browser);
    }

    return await InstallTicketAsync(instance, (DirectDownload)ticket, progress, cancellationToken);
  }

  /// <summary>Finishes a Nexus download the user started on the website.</summary>
  public async Task<InstallReport> InstallNxmAsync(
    Instance instance,
    NxmLink link,
    IProgress<InstallProgress>? progress,
    CancellationToken cancellationToken)
  {
    var ticket = await catalogs.Nexus.ResolveNxmAsync(link, cancellationToken);
    return await InstallTicketAsync(instance, ticket, progress, cancellationToken);
  }

  public Task<InstallReport> InstallTicketAsync(
    Instance instance,
    DirectDownload ticket,
    IProgress<InstallProgress>? progress,
    CancellationToken cancellationToken) =>
    WithLockAsync(instance, async () =>
    {
      var installed = new List<InstalledMod>();
      var warnings = new List<string>();
      await InstallRecursiveAsync(instance, ticket, asDependency: false, installed, warnings, [], progress, cancellationToken);
      await EnsureLoaderAsync(instance, installed, warnings, progress, cancellationToken);
      store.Save(instance);
      return new InstallReport(installed, warnings);
    });

  /// <summary>Installs an archive or dll the user downloaded themselves.</summary>
  /// <remarks>
  /// Thunderstore-style zips (an r2modman export, or a manual download from the website) carry a
  /// manifest.json, which gives the mod its proper name and lets its dependencies be fetched.
  /// </remarks>
  public Task<InstallReport> InstallFileAsync(
    Instance instance,
    string file,
    IProgress<InstallProgress>? progress,
    CancellationToken cancellationToken) =>
    WithLockAsync(instance, async () =>
    {
      var installed = new List<InstalledMod>();
      var warnings = new List<string>();
      var name = Path.GetFileNameWithoutExtension(file);
      progress?.Report(new InstallProgress(name, "Unpacking", null));

      using var extracted = await Task.Run(() => ExtractedMod.FromFile(file, TempRoot), cancellationToken);
      var manifest = ReadManifest(extracted.Directory);

      var mod = new InstalledMod
      {
        Source = ModSource.Local,
        SourceId = ModInstaller.SafeFolderName(manifest?.Name ?? name),
        Name = (manifest?.Name ?? name).Replace('_', ' '),
        Version = manifest?.VersionNumber ?? "",
        WebsiteUrl = string.IsNullOrWhiteSpace(manifest?.WebsiteUrl) ? null : manifest.WebsiteUrl,
      };
      mod.Key = InstalledMod.MakeKey(ModSource.Local, mod.SourceId);

      var dependencies = (manifest?.Dependencies ?? [])
        .Select(DependencyString.Parse)
        .OfType<DependencyString>()
        .Select(d => new ModReference(ModSource.Thunderstore, d.FullName, d.Version))
        .ToList();
      await InstallDependenciesAsync(instance, dependencies, installed, warnings, [mod.Key], progress, cancellationToken);

      await PlaceAsync(instance, mod, extracted.Directory, mod.SourceId, cancellationToken);
      mod.Dependencies = dependencies.Select(d => InstalledMod.MakeKey(d.Source, d.ModId)).ToList();
      installed.Add(mod);

      await EnsureLoaderAsync(instance, installed, warnings, progress, cancellationToken);
      store.Save(instance);
      return new InstallReport(installed, warnings);
    });

  /// <summary>
  /// Removes the mod and any dependencies that were only pulled in for it, the way a package manager
  /// cleans up. Mods the user picked themselves are never removed as a side effect.
  /// </summary>
  public Task<RemoveReport> RemoveAsync(Instance instance, string key, CancellationToken cancellationToken) =>
    WithLockAsync(instance, () =>
    {
      var removed = new List<InstalledMod>();
      var directory = store.DirectoryOf(instance);
      var queue = new Queue<string>([key]);
      while (queue.TryDequeue(out var next))
      {
        var mod = instance.FindMod(next);
        if (mod is null)
        {
          continue;
        }

        installer.Uninstall(directory, mod);
        instance.Mods.Remove(mod);
        removed.Add(mod);

        foreach (var dependency in mod.Dependencies)
        {
          if (instance.FindMod(dependency) is { InstalledAsDependency: true, IsLoader: false } orphan && !instance.Dependents(orphan.Key).Any())
          {
            queue.Enqueue(orphan.Key);
          }
        }
      }

      store.Save(instance);
      return Task.FromResult(new RemoveReport(removed));
    });

  /// <summary>Turning a mod on also turns on what it needs; turning it off leaves its dependencies alone.</summary>
  public Task<IReadOnlyList<InstalledMod>> SetEnabledAsync(Instance instance, string key, bool enabled) =>
    WithLockAsync(instance, () =>
    {
      var changed = new List<InstalledMod>();
      var directory = store.DirectoryOf(instance);
      var queue = new Queue<string>([key]);
      while (queue.TryDequeue(out var next))
      {
        if (instance.FindMod(next) is not { } mod || mod.Enabled == enabled || changed.Contains(mod))
        {
          continue;
        }

        installer.SetEnabled(directory, mod, enabled);
        changed.Add(mod);
        if (enabled)
        {
          mod.Dependencies.ForEach(queue.Enqueue);
        }
      }

      store.Save(instance);
      return Task.FromResult<IReadOnlyList<InstalledMod>>(changed);
    });

  /// <summary>Installed Thunderstore mods with a newer version in the index, keyed by mod key.</summary>
  /// <remarks>
  /// Only Thunderstore can be checked for free: the whole index is already in memory. Nexus and
  /// CurseForge would need one API call per mod and count against the user's rate limit.
  /// </remarks>
  public IReadOnlyDictionary<string, string> FindUpdates(Instance instance)
  {
    var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var mod in instance.Mods.Where(m => m.Source == ModSource.Thunderstore))
    {
      if (catalogs.Thunderstore.LatestVersion(mod.SourceId) is { } latest && ModVersion.Compare(latest, mod.Version) > 0)
      {
        updates[mod.Key] = latest;
      }
    }

    return updates;
  }

  public async Task<InstallReport> UpdateAsync(Instance instance, string key, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
  {
    var mod = instance.FindMod(key) ?? throw new InvalidOperationException("The mod is not installed.");
    return await InstallAsync(instance, new InstallRequest(mod.Source, mod.SourceId), progress, cancellationToken);
  }

  public Task<InstallReport> InstallLoaderAsync(Instance instance, IProgress<InstallProgress>? progress, CancellationToken cancellationToken) =>
    InstallAsync(instance, new InstallRequest(ModSource.Thunderstore, ThunderstoreCatalog.LoaderFullName), progress, cancellationToken);

  private async Task InstallRecursiveAsync(
    Instance instance,
    DirectDownload ticket,
    bool asDependency,
    List<InstalledMod> installed,
    List<string> warnings,
    HashSet<string> visited,
    IProgress<InstallProgress>? progress,
    CancellationToken cancellationToken)
  {
    var key = InstalledMod.MakeKey(ticket.Mod.Source, ticket.Mod.Id);
    if (!visited.Add(key))
    {
      return;
    }

    await InstallDependenciesAsync(instance, ticket.Dependencies, installed, warnings, visited, progress, cancellationToken);

    var name = ticket.Mod.Name;
    var download = new Progress<DownloadProgress>(p => progress?.Report(new InstallProgress(name, "Downloading", p.Fraction)));
    progress?.Report(new InstallProgress(name, "Downloading", null));
    var file = await downloader.DownloadAsync(
      ticket.Url,
      $"{ticket.Mod.Source}/{ticket.Mod.Id}/{ticket.FileId}",
      ticket.FileName,
      ticket.Headers,
      download,
      cancellationToken);

    progress?.Report(new InstallProgress(name, "Installing", null));
    using var extracted = await Task.Run(() => ExtractedMod.FromFile(file, TempRoot), cancellationToken);

    var previous = instance.FindMod(key);
    var mod = new InstalledMod
    {
      Key = key,
      Source = ticket.Mod.Source,
      SourceId = ticket.Mod.Id,
      FileId = ticket.FileId,
      Name = ticket.Mod.Name,
      Author = ticket.Mod.Author,
      Version = ticket.Version,
      IconUrl = ticket.Mod.IconUrl,
      WebsiteUrl = ticket.Mod.WebsiteUrl,
      // Updating a mod the user picked must not demote it to a removable dependency, and vice versa.
      InstalledAsDependency = previous?.InstalledAsDependency ?? asDependency,
      Dependencies = ticket.Dependencies.Select(d => InstalledMod.MakeKey(d.Source, d.ModId)).ToList(),
    };

    var folder = ticket.Mod.Source == ModSource.Thunderstore ? ticket.Mod.Id : $"{ticket.Mod.Name}-{ticket.Mod.Source}{ticket.Mod.Id}";
    await PlaceAsync(instance, mod, extracted.Directory, folder, cancellationToken);
    if (previous is { Enabled: false })
    {
      installer.SetEnabled(store.DirectoryOf(instance), mod, enabled: false);
    }

    installed.Add(mod);
  }

  /// <remarks>
  /// A missing dependency gets the newest version rather than the exact one listed. When two mods ask
  /// for different versions of the same library, the newest satisfies both, whereas installing each
  /// exact version would leave one of them broken. An installed version that is new enough is kept.
  /// </remarks>
  private async Task InstallDependenciesAsync(
    Instance instance,
    IReadOnlyList<ModReference> dependencies,
    List<InstalledMod> installed,
    List<string> warnings,
    HashSet<string> visited,
    IProgress<InstallProgress>? progress,
    CancellationToken cancellationToken)
  {
    foreach (var dependency in dependencies)
    {
      var key = InstalledMod.MakeKey(dependency.Source, dependency.ModId);
      var existing = instance.FindMod(key);
      if (visited.Contains(key)
        || (existing is not null && (dependency.MinimumVersion is null || ModVersion.Compare(existing.Version, dependency.MinimumVersion) >= 0)))
      {
        continue;
      }

      try
      {
        var ticket = await catalogs[dependency.Source].ResolveDownloadAsync(dependency.ModId, null, cancellationToken);
        if (ticket is DirectDownload direct)
        {
          await InstallRecursiveAsync(instance, direct, asDependency: true, installed, warnings, visited, progress, cancellationToken);
        }
        else
        {
          warnings.Add($"{dependency.ModId} is needed but has to be installed by hand.");
        }
      }
      catch (CatalogException ex)
      {
        warnings.Add($"Could not install the dependency {dependency.ModId}: {ex.Message}");
      }
    }
  }

  /// <summary>Every mod needs BepInEx, so an instance without it gets BepInExPack automatically.</summary>
  private async Task EnsureLoaderAsync(
    Instance instance,
    List<InstalledMod> installed,
    List<string> warnings,
    IProgress<InstallProgress>? progress,
    CancellationToken cancellationToken)
  {
    if (instance.Loader is not null)
    {
      return;
    }

    try
    {
      var ticket = await catalogs.Thunderstore.ResolveDownloadAsync(ThunderstoreCatalog.LoaderFullName, null, cancellationToken);
      await InstallRecursiveAsync(instance, (DirectDownload)ticket, asDependency: false, installed, warnings, [], progress, cancellationToken);
    }
    catch (Exception ex) when (ex is CatalogException or HttpRequestException)
    {
      warnings.Add("BepInEx could not be installed, so the mods will not load yet: " + ex.Message);
    }
  }

  private async Task PlaceAsync(Instance instance, InstalledMod mod, string sourceDirectory, string folder, CancellationToken cancellationToken)
  {
    var directory = store.DirectoryOf(instance);
    if (instance.FindMod(mod.Key) is { } previous)
    {
      installer.Uninstall(directory, previous);
      instance.Mods.Remove(previous);
    }

    var result = await Task.Run(() => installer.Install(sourceDirectory, directory, folder), cancellationToken);
    mod.Files = result.Files.ToList();
    mod.IsLoader = result.IsLoader;

    // BepInEx is what the instance is for, whoever pulled it in, so it never counts as a leftover
    // dependency that removing a mod would clean up.
    if (result.IsLoader)
    {
      mod.InstalledAsDependency = false;
    }
    mod.InstalledAt = DateTimeOffset.UtcNow;
    instance.Mods.Add(mod);
  }

  private async Task<T> WithLockAsync<T>(Instance instance, Func<Task<T>> action)
  {
    // Two installs into one instance at once would race on the same files and on instance.json.
    var gate = _locks.GetOrAdd(instance.Id, _ => new SemaphoreSlim(1, 1));
    await gate.WaitAsync();
    try
    {
      return await action();
    }
    finally
    {
      gate.Release();
    }
  }

  private static ThunderstoreManifest? ReadManifest(string directory)
  {
    var path = Path.Combine(directory, "manifest.json");
    if (!File.Exists(path))
    {
      return null;
    }

    try
    {
      // Thunderstore manifests are often saved with a BOM, which File.ReadAllText strips.
      return JsonSerializer.Deserialize<ThunderstoreManifest>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
    }
    catch (JsonException)
    {
      return null;
    }
  }

  private sealed record ThunderstoreManifest(string? Name, string? VersionNumber, string? WebsiteUrl, string[]? Dependencies);
}
