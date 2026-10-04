namespace CINE.LaunchHeim.Core.Storage;

/// <summary>The pictures chosen for servers and worlds on the Play page (<see cref="AppSettings.PlayImages"/>).</summary>
/// <remarks>
/// <para>
/// A picked image is copied into LaunchHeim's data folder rather than referenced where it lies, so moving
/// or deleting the original (a screenshot in Downloads) doesn't take the picture away. It is data, not
/// cache: the user chose it and can't get it back by downloading it again.
/// </para>
/// <para>
/// Every copy gets a new random name instead of one derived from the server or world. QML's Image caches
/// by URL, so replacing a picture under the same name would keep showing the old one until a restart.
/// </para>
/// </remarks>
public sealed class PlayImageStore(AppPaths paths)
{
  /// <summary>Large enough for any screenshot, small enough that a mistaken pick of a video fails fast.</summary>
  public const long MaxBytes = 32 * 1024 * 1024;

  /// <summary>What Qt can show with the image formats LaunchHeim ships (qtbase plus qtimageformats for WebP).</summary>
  public static readonly IReadOnlyList<string> Extensions = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"];

  public string Directory => Path.Combine(paths.DataDirectory, "play-images");

  /// <summary>Copies <paramref name="sourceFile"/> into the store.</summary>
  /// <returns>The copy's file name, to keep in the settings.</returns>
  /// <exception cref="ArgumentException">The file isn't an image LaunchHeim can show, or is too large.</exception>
  public string Import(string sourceFile)
  {
    var extension = Path.GetExtension(sourceFile).ToLowerInvariant();
    if (!Extensions.Contains(extension))
    {
      throw new ArgumentException($"{Path.GetFileName(sourceFile)} is not a PNG, JPEG, WebP, GIF or BMP image.", nameof(sourceFile));
    }

    if (new FileInfo(sourceFile).Length > MaxBytes)
    {
      throw new ArgumentException($"{Path.GetFileName(sourceFile)} is larger than {MaxBytes / 1024 / 1024} MB.", nameof(sourceFile));
    }

    System.IO.Directory.CreateDirectory(Directory);
    var name = Guid.NewGuid().ToString("N") + extension;
    File.Copy(sourceFile, Path.Combine(Directory, name));
    return name;
  }

  /// <summary>The full path of a stored image, or null when there is none or it was deleted by hand.</summary>
  public string? PathOf(string? fileName)
  {
    if (!IsOwnName(fileName))
    {
      return null;
    }

    var path = Path.Combine(Directory, fileName!);
    return File.Exists(path) ? path : null;
  }

  /// <summary>Removes a stored image. Missing files are fine; a locked one is left for later.</summary>
  public void Delete(string? fileName)
  {
    if (!IsOwnName(fileName))
    {
      return;
    }

    try
    {
      File.Delete(Path.Combine(Directory, fileName!));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
    }
  }

  // The name comes from settings.json, which can be edited by hand. A plain file name only, so a value
  // like "../instances/x" can never make Delete remove something outside the store.
  private static bool IsOwnName(string? fileName) =>
    !string.IsNullOrEmpty(fileName) && fileName == Path.GetFileName(fileName) && fileName is not ("." or "..");
}
