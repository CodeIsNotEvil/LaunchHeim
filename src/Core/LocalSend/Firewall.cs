using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace CINE.LaunchHeim.Core.LocalSend;

/// <summary>
/// Whether the system's firewall lets phones reach the LocalSend server, and how to open it.
/// </summary>
/// <remarks>
/// A firewall that drops incoming traffic breaks phone sync in a way that looks like a LaunchHeim bug:
/// LaunchHeim's own announcements go out, so the phone lists the PC for a few seconds, but the phone's
/// announcements, its HTTP scan and every upload are dropped. The next search clears the PC from the
/// list again, and a send fails. CachyOS turns ufw on with a dropping default, and Windows Firewall
/// blocks an app whose "Allow access" prompt was cancelled.
/// </remarks>
public static class Firewall
{
  /// <summary>
  /// The highest TCP port the server may take (<see cref="MiniHttpServer.Start"/> tries ten from the
  /// default). The ufw profile and the firewalld service in <c>src/Desktop/packaging/firewall</c> open
  /// the same range.
  /// </summary>
  public const int LastPort = LocalSendProtocol.DefaultPort + 9;

  /// <summary>
  /// The command that opens phone sync in ufw, or null when ufw is off or already has a rule for the
  /// port. ufw's files under <c>/etc/ufw</c> are world-readable on Arch and Debian, so no root is needed
  /// to look. Any rule naming the port counts, including one limited to the LAN.
  /// </summary>
  /// <param name="ufwDirectory">Only tests pass another directory.</param>
  public static string? UfwCommand(string ufwDirectory = "/etc/ufw")
  {
    try
    {
      var config = Path.Combine(ufwDirectory, "ufw.conf");
      if (!File.Exists(config) || !File.ReadLines(config).Any(l => l.Trim().Equals("ENABLED=yes", StringComparison.OrdinalIgnoreCase)))
      {
        return null;
      }

      var rules = Path.Combine(ufwDirectory, "user.rules");
      if (File.Exists(rules) && File.ReadAllText(rules).Contains(LocalSendProtocol.DefaultPort.ToString(), StringComparison.Ordinal))
      {
        return null;
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      // Rules nobody may read: ufw is on, so showing the command is the safer guess.
    }

    // The packages install the profile; an install.sh or dev build has to name the ports.
    return File.Exists(Path.Combine(ufwDirectory, "applications.d", "launchheim"))
      ? "sudo ufw allow LaunchHeim"
      : $"sudo ufw allow {LocalSendProtocol.DefaultPort}/udp && sudo ufw allow {LocalSendProtocol.DefaultPort}:{LastPort}/tcp";
  }

  /// <summary>
  /// Allows <paramref name="program"/> through Windows Firewall for incoming connections on every network
  /// profile, after Windows asks for an administrator. Returns null when it worked, else what went wrong.
  /// </summary>
  /// <remarks>
  /// Windows 11 puts a new network in the Public profile unless the user changes it, so a rule for
  /// Private only would miss most home Wi-Fi. Receiving stays off until Settings switches it on, and every
  /// transfer is accepted in a dialog. Rules already there for the program are removed first: cancelling
  /// Windows' own prompt leaves block rules behind, and a block rule wins over any allow rule.
  /// </remarks>
  public static async Task<string?> AllowOnWindowsAsync(string program)
  {
    // -EncodedCommand (UTF-16, base64) carries the script past every quoting layer of the elevation.
    var info = new ProcessStartInfo("powershell.exe",
      $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(WindowsScript(program)))}")
    {
      UseShellExecute = true,
      Verb = "runas",
      WindowStyle = ProcessWindowStyle.Hidden,
    };

    try
    {
      using var process = Process.Start(info);
      if (process is null)
      {
        return "PowerShell did not start.";
      }

      await process.WaitForExitAsync();
      return process.ExitCode == 0 ? null : $"Changing the firewall failed (PowerShell exit code {process.ExitCode}).";
    }
    catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
    {
      // ERROR_CANCELLED: the administrator prompt was answered with No.
      return "Cancelled at the administrator prompt.";
    }
    catch (Win32Exception ex)
    {
      return ex.Message;
    }
  }

  /// <summary>The PowerShell that <see cref="AllowOnWindowsAsync"/> runs elevated.</summary>
  internal static string WindowsScript(string program)
  {
    // A single-quoted PowerShell string takes everything literally except a doubled single quote.
    var quoted = "'" + program.Replace("'", "''") + "'";
    // Windows' prompt may store the path with environment variables, so each filter's program is
    // expanded before comparing, without regard to case as Windows paths go.
    return $$"""
      $ErrorActionPreference = 'Stop'
      $program = {{quoted}}
      Get-NetFirewallApplicationFilter |
        Where-Object { [Environment]::ExpandEnvironmentVariables($_.Program) -ieq $program } |
        Get-NetFirewallRule |
        Remove-NetFirewallRule
      New-NetFirewallRule -DisplayName 'LaunchHeim phone sync' -Direction Inbound -Action Allow -Program $program -Profile Any | Out-Null
      """;
  }
}
