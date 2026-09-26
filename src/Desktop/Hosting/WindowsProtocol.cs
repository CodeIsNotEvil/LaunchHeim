using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CINE.LaunchHeim.Desktop.Hosting;

/// <summary>Makes LaunchHeim the handler for nxm:// links on Windows.</summary>
/// <remarks>
/// Windows keeps URL protocols in the registry. Under HKEY_CURRENT_USER\Software\Classes this needs no
/// administrator rights and only affects the current user, like the per-user desktop entry on Linux.
/// Browsers then start <c>LaunchHeim.exe "nxm://..."</c>, and the single-instance socket hands the link
/// to the running window.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class WindowsProtocol
{
  private const string KeyPath = @"Software\Classes\nxm";

  private static string Command => $"\"{Environment.ProcessPath}\" \"%1\"";

  public static bool IsRegistered()
  {
    using var command = Registry.CurrentUser.OpenSubKey(KeyPath + @"\shell\open\command");
    return string.Equals(command?.GetValue(null) as string, Command, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>Registers the running build. Registering again after moving the folder fixes a stale path.</summary>
  public static void Register()
  {
    using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
    key.SetValue(null, "URL:Nexus Mod Manager Download");
    key.SetValue("URL Protocol", "");
    using (var icon = key.CreateSubKey("DefaultIcon"))
    {
      icon.SetValue(null, $"\"{Environment.ProcessPath}\",0");
    }

    using var command = key.CreateSubKey(@"shell\open\command");
    command.SetValue(null, Command);
  }
}
