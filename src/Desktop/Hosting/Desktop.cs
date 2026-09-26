using System.Diagnostics;
using CINE.LaunchHeim.Core.Game;

namespace CINE.LaunchHeim.Desktop.Hosting;

/// <summary>Opens files, folders and links with the user's default apps.</summary>
public static class DesktopShell
{
  public static void Open(string target)
  {
    if (string.IsNullOrWhiteSpace(target))
    {
      return;
    }

    // Links from mod descriptions are untrusted; only web pages and local files are opened.
    var isWeb = target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
    var isLocal = Path.IsPathFullyQualified(target) || target.StartsWith("file://", StringComparison.OrdinalIgnoreCase);
    if (!isWeb && !isLocal)
    {
      return;
    }

    if (OperatingSystem.IsWindows())
    {
      // The shell picks the default browser, Explorer for folders, or the app for a file. Checked above
      // to be a web link or an existing path, so nothing else (a .exe URL scheme, say) is handed over.
      if (isWeb || File.Exists(target) || Directory.Exists(target))
      {
        try
        {
          Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
      }

      return;
    }

    Run("xdg-open", target);
  }

  /// <summary>
  /// Starts a helper without LaunchHeim's own Qt variables. Otherwise a Qt 6 app such as Dolphin would
  /// find QT_PLUGIN_PATH pointing at LaunchHeim's Qt 5 plugins.
  /// </summary>
  public static Process? Run(string fileName, params string[] arguments)
  {
    var info = new ProcessStartInfo(fileName) { UseShellExecute = false };
    foreach (var argument in arguments)
    {
      info.ArgumentList.Add(argument);
    }

    foreach (var variable in GameLauncher.HostOnlyVariables)
    {
      info.Environment.Remove(variable);
    }

    try
    {
      return Process.Start(info);
    }
    catch (System.ComponentModel.Win32Exception)
    {
      return null;
    }
  }

  public static async Task<string> CaptureAsync(string fileName, params string[] arguments)
  {
    var info = new ProcessStartInfo(fileName) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments)
    {
      info.ArgumentList.Add(argument);
    }

    foreach (var variable in GameLauncher.HostOnlyVariables)
    {
      info.Environment.Remove(variable);
    }

    try
    {
      using var process = Process.Start(info);
      if (process is null)
      {
        return "";
      }

      var output = await process.StandardOutput.ReadToEndAsync();
      await process.WaitForExitAsync();
      return output.Trim();
    }
    catch (System.ComponentModel.Win32Exception)
    {
      return "";
    }
  }
}
