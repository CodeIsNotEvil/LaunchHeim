namespace CINE.LaunchHeim.Core.Updates;

/// <summary>The terminal commands that install an update, the same ones the download page shows.</summary>
public static class UpdateCommands
{
  /// <returns>The commands, one per line, or none when they can't be given (unknown system, file missing from the release).</returns>
  /// <param name="appDirectory">Where this copy runs from; Windows copies the new files over it.</param>
  public static IReadOnlyList<string> For(InstallKind kind, AvailableUpdate update, string appDirectory) => kind switch
  {
    InstallKind.Arch => Package(update, ".pkg.tar.zst", "sudo pacman -U"),
    InstallKind.Debian => Package(update, "_amd64.deb", "sudo apt install"),
    InstallKind.Fedora => Package(update, ".x86_64.rpm", "sudo dnf install"),
    InstallKind.Windows => Windows(update, appDirectory),
    InstallKind.InstallScript =>
    [
      "# in your MyMonoRepo checkout",
      "git pull",
      "Applications/LaunchHeim/install.sh",
    ],
    _ => [],
  };

  // A package straight from its link fails with pacman (it looks for a .sig the release doesn't have),
  // so all three download first and install the local file, as the download page explains.
  private static IReadOnlyList<string> Package(AvailableUpdate update, string suffix, string install) =>
    update.Asset(suffix) is { } asset ? [$"curl -LO {asset.Url}", $"{install} ./{asset.Name}"] : [];

  /// <remarks>
  /// The zip holds one folder, <c>LaunchHeim</c>. It's unpacked to the temporary folder and its contents
  /// copied over the folder this copy runs from, whatever that folder is called. LaunchHeim has to be
  /// closed first: Windows doesn't let a running program's files be replaced.
  /// <para>
  /// The backslashes are PowerShell text for the user to run (<c>$env:TEMP</c> only means something
  /// there), not paths .NET opens, so Path.Combine has nothing to join; the app folder itself is the
  /// real path .NET reported.
  /// </para>
  /// </remarks>
  private static IReadOnlyList<string> Windows(AvailableUpdate update, string appDirectory)
  {
    if (update.Asset("-win-x64.zip") is not { } asset)
    {
      return [];
    }

    var target = Path.TrimEndingDirectorySeparator(appDirectory);
    return
    [
      "# close LaunchHeim first, then in PowerShell",
      $"Invoke-WebRequest -Uri \"{asset.Url}\" -OutFile \"$env:TEMP\\{asset.Name}\"",
      $"Expand-Archive -Path \"$env:TEMP\\{asset.Name}\" -DestinationPath \"$env:TEMP\\LaunchHeim-update\" -Force",
      $"Copy-Item -Path \"$env:TEMP\\LaunchHeim-update\\LaunchHeim\\*\" -Destination \"{target}\" -Recurse -Force",
    ];
  }
}
