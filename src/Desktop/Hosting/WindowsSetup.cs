using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CINE.LaunchHeim.Desktop.Hosting;

/// <summary>Where the Windows setup (packaging/windows/launchheim.iss) installed LaunchHeim, if it did.</summary>
/// <remarks>
/// Inno Setup writes an uninstall entry named after the setup's AppId plus <c>_is1</c>: under
/// HKEY_CURRENT_USER for a per-user install, HKEY_LOCAL_MACHINE for one for all users. Its
/// <c>InstallLocation</c> is the folder. Reading it, rather than a marker file next to the exe, means an
/// unzipped copy is never mistaken for an installed one, and the entry disappears with the uninstall.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class WindowsSetup
{
  // The AppId in launchheim.iss, which must never change.
  private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AA9F6EE2-43EF-4C30-A1B6-44AEFFC95ECB}_is1";

  /// <summary>The setup's install folders, per-user first, for InstallDetection to compare with the running copy's.</summary>
  public static IEnumerable<string> InstallFolders()
  {
    // A 64-bit process reads the 64-bit registry view, which is where the 64-bit setup writes.
    foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
    {
      string? folder = null;
      try
      {
        using var key = root.OpenSubKey(UninstallKey);
        folder = key?.GetValue("InstallLocation") as string;
      }
      catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
      {
      }

      if (!string.IsNullOrEmpty(folder))
      {
        yield return folder;
      }
    }
  }
}
