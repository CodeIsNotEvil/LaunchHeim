using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Logging;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Servers;

/// <summary>What SteamCMD is doing, for a progress bar.</summary>
/// <param name="Fraction">0 to 1 while it downloads, null while it updates itself or checks files.</param>
public readonly record struct SteamCmdProgress(string Stage, double? Fraction);

public sealed class SteamCmdException(string message) : Exception(message);

/// <summary>Downloads Valheim's dedicated server with Valve's SteamCMD, without a Steam account.</summary>
/// <remarks>
/// <para>
/// SteamCMD is fetched from Valve on first use into the cache folder and updates itself on every run.
/// The server is an anonymous download (<c>+login anonymous</c>), so nothing about the user's Steam
/// account is involved. <c>+@sSteamCmdForcePlatformType</c> picks the build, which lets a Linux PC fetch
/// the Windows server for a remote Windows machine and the other way round.
/// </para>
/// <para>
/// The Linux SteamCMD is a 32-bit program. Distributions that ship without 32-bit libraries (most of
/// them, until Steam is installed) can't run it, and it then fails before printing anything. That case
/// is recognised and explained; installing the server through Steam avoids it.
/// </para>
/// </remarks>
public sealed partial class SteamCmd(HttpClient http, AppPaths paths)
{
  public const string LinuxUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";
  public const string WindowsUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";

  public string Directory => Path.Combine(paths.CacheDirectory, "steamcmd");

  /// <summary>The home folder SteamCMD runs with on Linux, so it never touches the Steam client's files.</summary>
  public string PrivateHome => Path.Combine(Directory, "home");

  private string Executable => Path.Combine(Directory, OperatingSystem.IsWindows() ? "steamcmd.exe" : "steamcmd.sh");

  /// <summary>Installs or updates the dedicated server for <paramref name="platform"/> in <paramref name="directory"/>.</summary>
  public async Task InstallServerAsync(string directory, GamePlatform platform, IProgress<SteamCmdProgress>? progress, CancellationToken cancellationToken)
  {
    await EnsureSteamCmdAsync(progress, cancellationToken);
    System.IO.Directory.CreateDirectory(directory);

    var info = new ProcessStartInfo(Executable)
    {
      WorkingDirectory = Directory,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true,
    };

    foreach (var argument in Arguments(directory, platform))
    {
      info.ArgumentList.Add(argument);
    }

    foreach (var variable in GameLauncher.HostOnlyVariables)
    {
      info.Environment.Remove(variable);
    }

    if (!OperatingSystem.IsWindows())
    {
      // A home of its own. Linux SteamCMD keeps its data in ~/.steam/steam, which on a PC with Steam is
      // the Steam client's own folder: it then rewrote the client's steamapps/libraryfolders.vdf with
      // only that folder in it, and every other Steam library (with Valheim on it) was gone from the
      // list, for LaunchHeim and for Steam's next start (found 2026-11-07).
      var home = PrivateHome;
      System.IO.Directory.CreateDirectory(home);
      info.Environment["HOME"] = home;
      info.Environment["XDG_DATA_HOME"] = Path.Combine(home, ".local", "share");
      info.Environment.Remove("STEAM_RUNTIME");
      info.Environment.Remove("STEAMROOT");
    }

    Log.Info($"Running SteamCMD for the {platform} dedicated server in {directory}");
    progress?.Report(new SteamCmdProgress("Starting SteamCMD", null));
    using var process = Process.Start(info) ?? throw new SteamCmdException("SteamCMD did not start.");
    using var cancel = cancellationToken.Register(() =>
    {
      try
      {
        process.Kill(entireProcessTree: true);
      }
      catch (InvalidOperationException)
      {
      }
    });

    var lastLines = new Queue<string>();
    var error = process.StandardError.ReadToEndAsync(cancellationToken);
    while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
    {
      if (string.IsNullOrWhiteSpace(line))
      {
        continue;
      }

      lastLines.Enqueue(line.Trim());
      if (lastLines.Count > 8)
      {
        lastLines.Dequeue();
      }

      if (ParseProgress(line) is { } update)
      {
        progress?.Report(update);
      }
    }

    await process.WaitForExitAsync(cancellationToken);
    var stderr = await error;
    Log.Info($"SteamCMD exited with {process.ExitCode}");

    // SteamCMD's exit codes aren't documented and it sometimes reports failure after a self-update that
    // went fine, so the result is judged by the files.
    if (!DedicatedServer.IsServerDirectory(directory, platform))
    {
      var detail = string.Join('\n', lastLines.Concat(stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(4)));
      throw new SteamCmdException(LooksLikeMissing32Bit(process.ExitCode, detail)
        ? "SteamCMD can't run: it is a 32-bit program and this system has no 32-bit libraries. Install Steam (or your distribution's lib32 glibc), or install the server through Steam."
        : "SteamCMD didn't install the server." + (detail.Length > 0 ? "\n" + detail : ""));
    }
  }

  /// <summary>SteamCMD's arguments. <c>force_install_dir</c> has to come before the login.</summary>
  public static IReadOnlyList<string> Arguments(string directory, GamePlatform platform) =>
  [
    "+@sSteamCmdForcePlatformType", platform == GamePlatform.Windows ? "windows" : "linux",
    "+force_install_dir", Path.GetFullPath(directory),
    "+login", "anonymous",
    "+app_update", DedicatedServer.SteamAppId, "validate",
    "+quit",
  ];

  /// <summary>Reads lines like <c>Update state (0x61) downloading, progress: 42.17 (123 / 456)</c>.</summary>
  internal static SteamCmdProgress? ParseProgress(string line)
  {
    var match = ProgressPattern().Match(line);
    if (match.Success)
    {
      var stage = match.Groups["stage"].Value.Trim();
      var percent = double.Parse(match.Groups["percent"].Value, CultureInfo.InvariantCulture);
      var label = stage switch
      {
        "downloading" => "Downloading the server",
        "verifying install" or "verifying update" => "Checking the server's files",
        "preallocating" => "Making room for the server",
        "committing" => "Finishing the install",
        _ => "Installing the server",
      };
      return new SteamCmdProgress(label, Math.Clamp(percent / 100, 0, 1));
    }

    if (line.Contains("Checking for available update", StringComparison.Ordinal) || line.Contains("Downloading update", StringComparison.Ordinal))
    {
      return new SteamCmdProgress("Updating SteamCMD", null);
    }

    if (line.Contains("Logging in user", StringComparison.Ordinal))
    {
      return new SteamCmdProgress("Connecting to Steam", null);
    }

    return null;
  }

  private static bool LooksLikeMissing32Bit(int exitCode, string output) =>
    !OperatingSystem.IsWindows() && (exitCode == 127 || output.Contains("No such file or directory", StringComparison.Ordinal) && output.Contains("linux32", StringComparison.Ordinal));

  private async Task EnsureSteamCmdAsync(IProgress<SteamCmdProgress>? progress, CancellationToken cancellationToken)
  {
    if (File.Exists(Executable))
    {
      return;
    }

    progress?.Report(new SteamCmdProgress("Downloading SteamCMD", null));
    var url = OperatingSystem.IsWindows() ? WindowsUrl : LinuxUrl;
    Log.Info($"Downloading SteamCMD from {url}");
    var temp = Directory + ".download";
    if (System.IO.Directory.Exists(temp))
    {
      System.IO.Directory.Delete(temp, recursive: true);
    }

    System.IO.Directory.CreateDirectory(temp);
    using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
    {
      response.EnsureSuccessStatusCode();
      await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
      if (OperatingSystem.IsWindows())
      {
        // ZipArchive needs a seekable stream.
        using var buffer = new MemoryStream();
        await body.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        ZipFile.ExtractToDirectory(buffer, temp);
      }
      else
      {
        // .NET's tar reader keeps the files' modes, so steamcmd.sh and linux32/steamcmd stay executable.
        await using var gzip = new GZipStream(body, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzip, temp, overwriteFiles: true, cancellationToken);
      }
    }

    if (System.IO.Directory.Exists(Directory))
    {
      System.IO.Directory.Delete(Directory, recursive: true);
    }

    System.IO.Directory.Move(temp, Directory);
  }

  [GeneratedRegex(@"Update state \(0x[0-9a-fA-F]+\) (?<stage>[a-z ]+), progress: (?<percent>[0-9.]+)")]
  private static partial Regex ProgressPattern();
}
