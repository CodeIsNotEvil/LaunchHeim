using System.Globalization;
using CINE.LaunchHeim.Core.Game;

namespace CINE.LaunchHeim.Core.Saves;

/// <param name="File">The full-size picture.</param>
/// <param name="Thumbnail">Steam's small copy (200 pixels wide), when it made one.</param>
/// <param name="TakenAt">Local time, from the file name Steam gives it.</param>
public sealed record SteamScreenshot(string File, string? Thumbnail, DateTime TakenAt);

/// <summary>The screenshots Steam took in Valheim (F12 in the overlay, by default), newest first.</summary>
/// <remarks>
/// <para>
/// Valheim has no screenshot key of its own; Steam's overlay saves them, on Linux and Windows alike, as
/// <c>userdata/&lt;account&gt;/760/remote/892970/screenshots/&lt;yyyyMMddHHmmss&gt;_&lt;n&gt;.jpg</c> in the
/// Steam folder (760 is the screenshot service's app id), with a thumbnail of the same name in
/// <c>thumbnails</c>. Steam's own <c>screenshots.vdf</c> holds captions and upload state and isn't needed
/// to show them, so it is never read or changed: LaunchHeim only looks.
/// </para>
/// <para>
/// The account is the one that logged in last, the same as for the cloud saves
/// (<see cref="ValheimSaves.AccountDirectories"/>). Steam's optional uncompressed copies in a folder of
/// the user's choice aren't listed; the copy Steam keeps here is always there.
/// </para>
/// </remarks>
public sealed class SteamScreenshots(IReadOnlyList<string> directories)
{
  /// <summary>Where the screenshots are, most likely first. Empty until Steam has saved one.</summary>
  public IReadOnlyList<string> Directories => directories;

  public static SteamScreenshots ForCurrentUser(IEnumerable<string> steamRoots) =>
    new(ValheimSaves.AccountDirectories(steamRoots, "760", "remote", SteamLibraryLocator.ValheimAppId, "screenshots"));

  public IReadOnlyList<SteamScreenshot> List() =>
    directories
      .Where(Directory.Exists)
      .SelectMany(directory => Directory.EnumerateFiles(directory)
        .Where(IsPicture)
        .Select(file => new SteamScreenshot(file, ThumbnailOf(directory, file), TakenAt(file))))
      .OrderByDescending(s => s.TakenAt)
      // Several in one second are numbered _1, _2, ...: the higher number is the later one.
      .ThenByDescending(s => Path.GetFileName(s.File), StringComparer.Ordinal)
      .ToList();

  private static bool IsPicture(string file) =>
    Path.GetExtension(file).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png";

  private static string? ThumbnailOf(string directory, string file)
  {
    var thumbnail = Path.Combine(directory, "thumbnails", Path.GetFileName(file));
    return System.IO.File.Exists(thumbnail) ? thumbnail : null;
  }

  /// <summary>From Steam's name, <c>20261008204512_1.jpg</c>; a picture named otherwise by its file time.</summary>
  internal static DateTime TakenAt(string file)
  {
    var name = Path.GetFileNameWithoutExtension(file);
    var stamp = name.Split('_')[0];
    return DateTime.TryParseExact(stamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var taken)
      ? taken
      : System.IO.File.GetLastWriteTime(file);
  }
}
