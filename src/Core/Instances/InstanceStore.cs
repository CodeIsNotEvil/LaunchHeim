using System.Text.RegularExpressions;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Instances;

/// <summary>Instances on disk: one folder each, with <c>instance.json</c> next to the BepInEx tree.</summary>
/// <remarks>
/// The folder is the unit of truth. Copying or backing up an instance is copying its folder, and the
/// layout inside matches what BepInEx expects, so it can also be inspected or fixed by hand.
/// </remarks>
public sealed partial class InstanceStore(AppPaths paths)
{
  public const string ManifestFile = "instance.json";

  public string DirectoryOf(Instance instance) => DirectoryOf(instance.Id);

  public string DirectoryOf(string id) => Path.Combine(paths.InstancesDirectory, id);

  public IReadOnlyList<Instance> LoadAll()
  {
    if (!Directory.Exists(paths.InstancesDirectory))
    {
      return [];
    }

    var instances = new List<Instance>();
    foreach (var directory in Directory.EnumerateDirectories(paths.InstancesDirectory))
    {
      try
      {
        if (JsonFile.Read<Instance>(Path.Combine(directory, ManifestFile)) is { } instance)
        {
          // The folder name wins, so moving a folder by hand cannot produce two instances with one id.
          instance.Id = Path.GetFileName(directory);
          instances.Add(instance);
        }
      }
      catch (System.Text.Json.JsonException)
      {
        // Skip a damaged manifest rather than hiding every other instance.
      }
    }

    return instances.OrderByDescending(i => i.LastPlayedAt ?? i.CreatedAt).ToList();
  }

  public Instance Create(string name)
  {
    var instance = new Instance { Id = NewId(name), Name = name.Trim() };
    Directory.CreateDirectory(DirectoryOf(instance));
    Save(instance);
    return instance;
  }

  public void Save(Instance instance) => JsonFile.Write(Path.Combine(DirectoryOf(instance), ManifestFile), instance);

  public void Delete(Instance instance)
  {
    var directory = DirectoryOf(instance);
    if (Directory.Exists(directory))
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  public Instance Duplicate(Instance source, string name)
  {
    var copy = JsonFile.Read<Instance>(Path.Combine(DirectoryOf(source), ManifestFile)) ?? throw new InvalidOperationException("The instance manifest is missing.");
    copy.Id = NewId(name);
    copy.Name = name.Trim();
    copy.CreatedAt = DateTimeOffset.UtcNow;
    copy.LastPlayedAt = null;

    CopyDirectory(DirectoryOf(source), DirectoryOf(copy));
    Save(copy);
    return copy;
  }

  private string NewId(string name)
  {
    var slug = SlugPattern().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
    if (slug.Length == 0)
    {
      slug = "instance";
    }

    if (slug.Length > 40)
    {
      slug = slug[..40].TrimEnd('-');
    }

    var id = slug;
    for (var n = 2; Directory.Exists(DirectoryOf(id)); n++)
    {
      id = $"{slug}-{n}";
    }

    return id;
  }

  internal static void CopyDirectory(string source, string destination)
  {
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
      var target = Path.Combine(destination, Path.GetRelativePath(source, file));
      Directory.CreateDirectory(Path.GetDirectoryName(target)!);
      File.Copy(file, target, overwrite: true);
    }
  }

  [GeneratedRegex("[^a-z0-9]+")]
  private static partial Regex SlugPattern();
}
