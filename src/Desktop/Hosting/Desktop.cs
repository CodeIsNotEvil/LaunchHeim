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

  /// <summary>Opens the file manager on the file's folder with the file selected.</summary>
  /// <remarks>
  /// Windows: <c>explorer /select,</c>. Linux: the freedesktop <c>org.freedesktop.FileManager1.ShowItems</c>
  /// D-Bus call, which Dolphin, Nautilus, Nemo, Thunar and Caja answer (and are started for if needed).
  /// Without one, the folder opens with xdg-open, just without the selection.
  /// </remarks>
  public static async Task ShowInFolder(string file)
  {
    if (!File.Exists(file))
    {
      return;
    }

    if (OperatingSystem.IsWindows())
    {
      // Explorer parses its own command line: the quotes go around the path only, after the comma.
      var info = new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = false };
      try
      {
        Process.Start(info)?.Dispose();
      }
      catch (System.ComponentModel.Win32Exception)
      {
      }

      return;
    }

    // dbus-send splits array items at commas, which a file URI keeps unescaped.
    var uri = new Uri(file).AbsoluteUri.Replace(",", "%2C", StringComparison.Ordinal);
    // --print-reply waits for the answer, so a missing file manager shows in the exit code.
    var shown = await ExitCodeAsync("dbus-send", "--session", "--print-reply", "--reply-timeout=5000", "--dest=org.freedesktop.FileManager1",
      "--type=method_call", "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1.ShowItems", "array:string:" + uri, "string:");
    if (shown != 0)
    {
      Open(Path.GetDirectoryName(file)!);
    }
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

  /// <summary>Runs a helper to the end with its output discarded; null when it couldn't be started.</summary>
  private static async Task<int?> ExitCodeAsync(string fileName, params string[] arguments)
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
        return null;
      }

      // Read both, or a chatty helper blocks on a full pipe and never exits.
      await Task.WhenAll(process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync(), process.WaitForExitAsync());
      return process.ExitCode;
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
