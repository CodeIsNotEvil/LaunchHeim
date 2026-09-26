namespace CINE.LaunchHeim.Desktop.Hosting;

/// <summary>Whether this copy was installed by pacman, apt or dnf rather than by install.sh.</summary>
/// <remarks>
/// The packages build with <c>-p:LaunchHeimDistroPackage=true</c>, which writes the switch into
/// <c>LaunchHeim.runtimeconfig.json</c>. A switch rather than a wrapper script, because the desktop entry
/// and the nxm:// handler start the binary directly, and they must behave the same way.
/// </remarks>
public static class DistroPackage
{
  /// <summary>
  /// True when the package manager owns the install: Qt comes from the distribution, and the desktop
  /// entry lives in <c>/usr/share/applications</c>, so nothing is downloaded or written into the home folder.
  /// </summary>
  public static bool IsInstalled { get; } = AppContext.TryGetSwitch("LaunchHeim.DistroPackage", out var enabled) && enabled;
}
