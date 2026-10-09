using System.Text.RegularExpressions;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Instances;

/// <summary>How an instance's icon looks: its color, its letters, or a picture in place of the letters.</summary>
public static partial class InstanceIcon
{
  /// <summary>
  /// The colors an instance gets automatically, picked by its id: muted Breeze accents, so cards are told
  /// apart without shouting. Changing them recolors every instance that has no color of its own.
  /// </summary>
  public static readonly IReadOnlyList<string> AutomaticColors =
    ["#3daee9", "#1abc9c", "#9b59b6", "#f67400", "#da4453", "#27ae60", "#fdbc4b", "#2980b9", "#e93d8f"];

  public const int MaxInitials = 3;

  /// <summary>Pictures larger than this are a mistaken pick (a video, a RAW photo), not an icon.</summary>
  public const long MaxPictureBytes = 8 * 1024 * 1024;

  private const string FilePrefix = "launchheim-icon-";

  public static string AutomaticColor(string id) => AutomaticColors[(int)((uint)StableHash(id) % AutomaticColors.Count)];

  public static string ColorOf(Instance instance) => instance.Color ?? AutomaticColor(instance.Id);

  /// <summary>The first letters of the first two words, or the first two of a single word.</summary>
  public static string AutomaticInitials(string name)
  {
    var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    return words.Length switch
    {
      0 => "?",
      1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
      _ => $"{words[0][0]}{words[1][0]}".ToUpperInvariant(),
    };
  }

  public static string InitialsOf(Instance instance) => instance.Initials ?? AutomaticInitials(instance.Name);

  /// <summary><c>#rrggbb</c> in lower case, or null for anything else (which means automatic).</summary>
  public static string? NormalizeColor(string? color) =>
    color is not null && HexColor().IsMatch(color.Trim()) ? color.Trim().ToLowerInvariant() : null;

  /// <summary>Up to <see cref="MaxInitials"/> characters without spaces, or null when empty (automatic).</summary>
  public static string? NormalizeInitials(string? initials)
  {
    var text = string.Concat((initials ?? "").Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)));
    var letters = new System.Globalization.StringInfo(text);
    // By text elements, so an emoji or an accented letter isn't cut in half.
    return letters.LengthInTextElements == 0 ? null : letters.SubstringByTextElements(0, Math.Min(MaxInitials, letters.LengthInTextElements));
  }

  /// <summary>The picture's full path, or null when there is none or its file is gone.</summary>
  public static string? PicturePath(InstanceStore store, Instance instance)
  {
    if (instance.IconFile is not { Length: > 0 } file || Path.GetFileName(file) != file)
    {
      return null;
    }

    var path = Path.Combine(store.DirectoryOf(instance), file);
    return File.Exists(path) ? path : null;
  }

  /// <summary>
  /// Copies <paramref name="sourceFile"/> into the instance folder as its picture and removes the one
  /// before. Each picture gets a new name: QML's Image caches by URL and would keep showing the old one.
  /// Saves nothing; the caller saves the instance.
  /// </summary>
  /// <exception cref="ArgumentException">Not an image LaunchHeim can show, or too large.</exception>
  public static void SetPicture(InstanceStore store, Instance instance, string sourceFile)
  {
    var extension = Path.GetExtension(sourceFile).ToLowerInvariant();
    if (!PlayImageStore.Extensions.Contains(extension))
    {
      throw new ArgumentException($"{Path.GetFileName(sourceFile)} is not a PNG, JPEG, WebP, GIF or BMP image.", nameof(sourceFile));
    }

    if (new FileInfo(sourceFile).Length > MaxPictureBytes)
    {
      throw new ArgumentException($"{Path.GetFileName(sourceFile)} is larger than {MaxPictureBytes / 1024 / 1024} MB.", nameof(sourceFile));
    }

    var name = FilePrefix + Guid.NewGuid().ToString("N")[..12] + extension;
    File.Copy(sourceFile, Path.Combine(store.DirectoryOf(instance), name));
    RemovePicture(store, instance);
    instance.IconFile = name;
  }

  /// <summary>Deletes the picture file, if any, and forgets it. Saves nothing.</summary>
  public static void RemovePicture(InstanceStore store, Instance instance)
  {
    if (PicturePath(store, instance) is { } path && Path.GetFileName(path).StartsWith(FilePrefix, StringComparison.Ordinal))
    {
      File.Delete(path);
    }

    instance.IconFile = null;
  }

  // string.GetHashCode is randomized per process; the color must stay the same across starts.
  private static int StableHash(string value)
  {
    unchecked
    {
      var hash = 23;
      foreach (var c in value)
      {
        hash = hash * 31 + c;
      }

      return hash;
    }
  }

  [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
  private static partial Regex HexColor();
}
