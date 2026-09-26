using SharpCompress.Archives;
using SharpCompress.Common;

namespace CINE.LaunchHeim.Core.Mods;

/// <summary>A downloaded mod unpacked into a temporary folder, deleted again on dispose.</summary>
/// <remarks>
/// Thunderstore and CurseForge ship zips, but Nexus authors upload whatever they like: zip, 7z, rar, or
/// a bare .dll. Unpacking everything to a folder first gives the installer one shape to work with.
/// </remarks>
public sealed class ExtractedMod : IDisposable
{
  private ExtractedMod(string directory) => Directory = directory;

  public string Directory { get; }

  public static ExtractedMod FromFile(string file, string tempRoot)
  {
    var directory = Path.Combine(tempRoot, "extract-" + Guid.NewGuid().ToString("N"));
    System.IO.Directory.CreateDirectory(directory);
    var extracted = new ExtractedMod(directory);

    try
    {
      if (string.Equals(Path.GetExtension(file), ".dll", StringComparison.OrdinalIgnoreCase))
      {
        File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
      }
      else
      {
        Extract(file, directory);
      }
    }
    catch
    {
      extracted.Dispose();
      throw;
    }

    return extracted;
  }

  /// <remarks>
  /// Solid archives (most 7z files) are read front to back once; opening their entries one by one
  /// would decompress the archive again for every file. Zips allow direct access to each entry.
  /// </remarks>
  private static void Extract(string file, string directory)
  {
    using var archive = ArchiveFactory.Open(file);
    if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
    {
      using var reader = archive.ExtractAllEntries();
      while (reader.MoveToNextEntry())
      {
        if (Target(directory, reader.Entry) is { } target)
        {
          using var output = File.Create(target);
          reader.WriteEntryTo(output);
        }
      }

      return;
    }

    foreach (var entry in archive.Entries)
    {
      if (Target(directory, entry) is { } target)
      {
        using var input = entry.OpenEntryStream();
        using var output = File.Create(target);
        input.CopyTo(output);
      }
    }
  }

  private static string? Target(string directory, IEntry entry)
  {
    if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key))
    {
      return null;
    }

    var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
    var target = Path.GetFullPath(Path.Combine(directory, ModInstaller.NormalizePath(entry.Key)));

    // Zip slip: an entry named ../../.bashrc must not escape the extraction folder.
    if (!target.StartsWith(root, StringComparison.Ordinal))
    {
      throw new InvalidDataException($"The archive contains an unsafe path: {entry.Key}");
    }

    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    return target;
  }

  public void Dispose()
  {
    try
    {
      System.IO.Directory.Delete(Directory, recursive: true);
    }
    catch (IOException)
    {
      // Temp files are best effort; the cache folder gets cleaned on the next start anyway.
    }
  }
}
