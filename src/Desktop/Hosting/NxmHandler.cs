using System.Text;

namespace CINE.LaunchHeim.Desktop.Hosting;

/// <summary>Registers LaunchHeim with the desktop so it shows up in the launcher and receives nxm:// links.</summary>
public static class NxmHandler
{
  public const string DesktopFileName = "launchheim.desktop";
  private const string Scheme = "x-scheme-handler/nxm";

  public static async Task<bool> IsRegisteredAsync() =>
    string.Equals(await DesktopShell.CaptureAsync("xdg-mime", "query", "default", Scheme), DesktopFileName, StringComparison.Ordinal);

  public static async Task RegisterAsync()
  {
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(home, ".local/share");
    var applications = Path.Combine(dataHome, "applications");
    var icons = Path.Combine(dataHome, "icons/hicolor/scalable/apps");
    Directory.CreateDirectory(applications);
    Directory.CreateDirectory(icons);

    File.Copy(Path.Combine(AppContext.BaseDirectory, "packaging", "launchheim.svg"), Path.Combine(icons, "launchheim.svg"), overwrite: true);
    await File.WriteAllTextAsync(Path.Combine(applications, DesktopFileName), DesktopEntry(), Encoding.UTF8);

    await DesktopShell.CaptureAsync("update-desktop-database", applications);
    await DesktopShell.CaptureAsync("xdg-mime", "default", DesktopFileName, Scheme);
  }

  /// <summary>
  /// Points at the running build: the apphost when there is one, otherwise <c>dotnet LaunchHeim.dll</c>.
  /// Registering again after moving the install fixes a stale path.
  /// </summary>
  private static string DesktopEntry()
  {
    var processPath = Environment.ProcessPath ?? "LaunchHeim";
    var exec = Path.GetFileNameWithoutExtension(processPath) == "dotnet"
      ? $"{Quote(processPath)} {Quote(Path.Combine(AppContext.BaseDirectory, "LaunchHeim.dll"))} %u"
      : $"{Quote(processPath)} %u";

    return $"""
      [Desktop Entry]
      Type=Application
      Name=LaunchHeim
      GenericName=Valheim Mod Launcher
      Comment=Manage modded Valheim instances and install mods from Thunderstore, Nexus Mods and CurseForge
      Exec={exec}
      Icon=launchheim
      Terminal=false
      Categories=Game;Utility;
      Keywords=valheim;mods;bepinex;thunderstore;nexus;curseforge;
      MimeType={Scheme};
      StartupWMClass=LaunchHeim
      """ + "\n";
  }

  // Desktop entry Exec quoting: double quotes, with backslash escapes for the few special characters.
  private static string Quote(string value) =>
    "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$") + "\"";
}
