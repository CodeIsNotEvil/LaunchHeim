using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using CINE.LaunchHeim.Core.Logging;
using Qml.Net.Runtimes;

namespace CINE.LaunchHeim.Desktop.Hosting;

/// <summary>Picks the Qt 5 runtime and patches the two places where Qml.Net 0.11 breaks today.</summary>
/// <remarks>
/// <para>
/// Qml.Net's native library is built against Qt 5.15. Plasma 6 systems only ship Qt 5 partially (Arch
/// has no <c>qt5-quickcontrols2</c> by default), so by default the matching Qt runtime that Qml.Net
/// publishes is downloaded once into <c>~/.qmlnet-qt-runtimes</c> (about 60 MB). Setting
/// <c>LAUNCHHEIM_QT=system</c> uses the distribution's Qt 5 instead, which needs qt5-base,
/// qt5-declarative, qt5-quickcontrols2, qt5-svg and qt5-wayland.
/// </para>
/// <para>
/// Distribution packages default to the system Qt: they declare those libraries as dependencies, and a
/// package that downloads 60 MB into the home folder on first start is not what pacman or apt users
/// expect. libQmlNet only uses public Qt 5.15 API plus QMetaObjectBuilder, which is unchanged across
/// 5.15.x, so every distribution's 5.15 works. <c>LAUNCHHEIM_QT=bundled</c> forces the download anyway.
/// </para>
/// <para>Workarounds, both needed on any current system:</para>
/// <list type="number">
///   <item>Its tar extractor assumes a stream read always fills the buffer. Since .NET 6 GZipStream may
///   return less, and the extractor throws "Couldn't ready block". System.Formats.Tar replaces it.</item>
///   <item>NetNativeLibLoader P/Invokes into <c>libdl.so</c>. glibc 2.34 folded libdl into libc and
///   only keeps <c>libdl.so.2</c> for compatibility, so the unversioned name no longer exists.</item>
/// </list>
/// </remarks>
public static class QtRuntime
{
  public static string Description { get; private set; } = "";

  public static bool UsesBundledRuntime { get; private set; }

  public static void Prepare()
  {
    if (OperatingSystem.IsLinux())
    {
      NativeLibrary.SetDllImportResolver(typeof(NetNativeLibLoader.Loader.PlatformLoaderBase).Assembly, (name, _, _) =>
        name is "dl" or "libdl" or "libdl.so" ? NativeLibrary.Load("libdl.so.2") : IntPtr.Zero);
    }

    // The Windows build ships Qt next to LaunchHeim.exe (windeployqt), because its QmlNet.dll is built
    // from source against that Qt (see packaging/windows). Windows finds DLLs in the exe's folder first.
    if (OperatingSystem.IsWindows() && File.Exists(Path.Combine(AppContext.BaseDirectory, "Qt5Core.dll")))
    {
      Description = "Qt 5.15 (shipped with LaunchHeim)";
      return;
    }

    RuntimeManager.ExtractTarGZStream = (stream, destination) =>
    {
      using var gzip = new GZipStream(stream, CompressionMode.Decompress);
      TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: true);
    };

    UsePortalFileDialogs();

    var requested = Environment.GetEnvironmentVariable("LAUNCHHEIM_QT");
    var useSystem = string.IsNullOrEmpty(requested)
      ? DistroPackage.IsInstalled
      : string.Equals(requested, "system", StringComparison.OrdinalIgnoreCase);
    if (useSystem)
    {
      Description = "System Qt 5";
      return;
    }

    if (RuntimeManager.FindSuitableQtRuntime() is null)
    {
      // On stderr as well, since the window takes a while to appear the first time.
      Console.Error.WriteLine("LaunchHeim: downloading the Qt 5.15 runtime for Qml.Net (about 60 MB, first start only)...");
      Log.Info("Downloading the Qt 5.15 runtime for Qml.Net.");
    }

    RuntimeManager.DiscoverOrDownloadSuitableQtRuntime();
    UsesBundledRuntime = true;
    Description = "Qt 5.15.1 (Qml.Net runtime)";
  }

  /// <summary>Makes Qt use the desktop portal's file dialogs on Linux, unless the user chose a theme.</summary>
  /// <remarks>
  /// <para>
  /// The UI is QML only, without Qt Widgets, so Qt.labs.platform can only show a file dialog that the
  /// platform theme provides. Without one, every "Install from file", "Import a modpack", "Export",
  /// "Choose folder" and icon picture silently does nothing. The xdg-desktop-portal theme (part of
  /// qt5-base everywhere) asks the desktop for its own dialog, Plasma's on KDE and GTK's on GNOME, and
  /// opens links through the portal too.
  /// </para>
  /// <para>
  /// This was first done for the bundled runtime only (2026-09-30), which has no KDE theme. The system
  /// Qt of the distribution packages has none on Plasma 6 either: Plasma 6 ships its platform theme for
  /// Qt 6 only, so Qt 5 falls back to its built-in KDE theme, which has no file dialog. 0.7.1 from the
  /// Arch package did nothing on "Install from file" (2026-11-06). A probe against Arch's qt5-base
  /// 5.15.19+kde confirmed it: built-in KDE theme, no file dialog; portal theme, a file dialog.
  /// </para>
  /// </remarks>
  private static void UsePortalFileDialogs()
  {
    if (OperatingSystem.IsLinux() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("QT_QPA_PLATFORMTHEME")))
    {
      SetForQt("QT_QPA_PLATFORMTHEME", "xdgdesktopportal");
    }
  }

  /// <summary>Sets an environment variable that Qt, which reads it with getenv, can see.</summary>
  /// <remarks>
  /// On Linux <see cref="Environment.SetEnvironmentVariable(string, string)"/> only changes .NET's own
  /// copy of the environment; the C environment native code reads stays as it was. Qml.Net sets
  /// QT_PLUGIN_PATH through its native qt_putenv for the same reason. The managed copy is set too, so
  /// <see cref="Core.Game.GameLauncher.HostOnlyVariables"/> still sees it and keeps it from the game.
  /// </remarks>
  public static void SetForQt(string name, string value)
  {
    Environment.SetEnvironmentVariable(name, value);
    if (!OperatingSystem.IsWindows() && setenv(name, value, overwrite: 1) != 0)
    {
      Log.Warning($"{name} could not be set for Qt (errno {Marshal.GetLastPInvokeError()}).");
    }
  }

  // libc.so.6 by its versioned name: plain "libc.so" is a linker script that dlopen can't load.
  [DllImport("libc.so.6", SetLastError = true)]
  private static extern int setenv([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int overwrite);
}
