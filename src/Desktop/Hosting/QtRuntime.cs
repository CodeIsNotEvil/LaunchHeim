using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
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
    NativeLibrary.SetDllImportResolver(typeof(NetNativeLibLoader.Loader.PlatformLoaderBase).Assembly, (name, _, _) =>
      name is "dl" or "libdl" or "libdl.so" ? NativeLibrary.Load("libdl.so.2") : IntPtr.Zero);

    RuntimeManager.ExtractTarGZStream = (stream, destination) =>
    {
      using var gzip = new GZipStream(stream, CompressionMode.Decompress);
      TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: true);
    };

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
      Console.Error.WriteLine("LaunchHeim: downloading the Qt 5.15 runtime for Qml.Net (about 60 MB, first start only)...");
    }

    RuntimeManager.DiscoverOrDownloadSuitableQtRuntime();
    UsesBundledRuntime = true;
    Description = "Qt 5.15.1 (Qml.Net runtime)";

    // The bundled Qt has no KDE platform theme. The portal theme gives native Plasma file dialogs
    // and opens links through the desktop portal instead.
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("QT_QPA_PLATFORMTHEME")))
    {
      Environment.SetEnvironmentVariable("QT_QPA_PLATFORMTHEME", "xdgdesktopportal");
    }
  }
}
