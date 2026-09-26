namespace CINE.LaunchHeim.Desktop.Hosting;

/// <summary>Registers LaunchHeim with the desktop so it shows up in the launcher and receives nxm:// links.</summary>
public static class NxmHandler
{
  public const string DesktopFileName = "launchheim.desktop";
  private const string Scheme = "x-scheme-handler/nxm";

  public static async Task<bool> IsRegisteredAsync() =>
    string.Equals(await DesktopShell.CaptureAsync("xdg-mime", "query", "default", Scheme), DesktopFileName, StringComparison.Ordinal);

  /// <remarks>
  /// A distribution package already installs the entry and icon system-wide, so only the default
  /// handler is set. Writing a copy into the home folder would shadow the packaged entry and outlive
  /// an uninstall.
  /// </remarks>
  public static async Task RegisterAsync()
  {
    if (!DistroPackage.IsInstalled)
    {
      var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
      var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(home, ".local/share");
      var applications = Path.Combine(dataHome, "applications");
      var icons = Path.Combine(dataHome, "icons/hicolor/scalable/apps");
      Directory.CreateDirectory(applications);
      Directory.CreateDirectory(icons);

      File.Copy(Path.Combine(PackagingDirectory, "launchheim.svg"), Path.Combine(icons, "launchheim.svg"), overwrite: true);
      // No encoding argument on purpose: the default is UTF-8 without a BOM. Encoding.UTF8 writes one, and
      // the spec (and desktop-file-validate) then sees no [Desktop Entry] group at all.
      await File.WriteAllTextAsync(Path.Combine(applications, DesktopFileName), await DesktopEntryAsync());
      await DesktopShell.CaptureAsync("update-desktop-database", applications);
    }

    await DesktopShell.CaptureAsync("xdg-mime", "default", DesktopFileName, Scheme);
  }

  private static string PackagingDirectory => Path.Combine(AppContext.BaseDirectory, "packaging");

  /// <summary>
  /// The packaged entry (<c>packaging/launchheim.desktop</c>, which the distribution packages install as
  /// it is), with Exec pointed at the running build: the apphost when there is one, otherwise
  /// <c>dotnet LaunchHeim.dll</c>. Registering again after moving the install fixes a stale path.
  /// </summary>
  private static async Task<string> DesktopEntryAsync()
  {
    var processPath = Environment.ProcessPath ?? "LaunchHeim";
    var exec = Path.GetFileNameWithoutExtension(processPath) == "dotnet"
      ? $"{Quote(processPath)} {Quote(Path.Combine(AppContext.BaseDirectory, "LaunchHeim.dll"))} %u"
      : $"{Quote(processPath)} %u";

    var lines = await File.ReadAllLinesAsync(Path.Combine(PackagingDirectory, DesktopFileName));
    return string.Join('\n', lines.Select(line => line.StartsWith("Exec=", StringComparison.Ordinal) ? $"Exec={exec}" : line)) + "\n";
  }

  // Desktop entry Exec quoting: double quotes, with backslash escapes for the few special characters.
  private static string Quote(string value) =>
    "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$") + "\"";
}
