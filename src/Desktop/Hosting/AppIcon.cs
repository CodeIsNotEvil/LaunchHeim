using System.Runtime.InteropServices;

namespace CINE.LaunchHeim.Desktop.Hosting;

/// <summary>
/// Shows LaunchHeim's logo in the window's title bar and the taskbar instead of the desktop's placeholder,
/// through <c>native/app_icon.cpp</c>. See that file for why each desktop needs it.
/// </summary>
public static class AppIcon
{
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate int SetAppIconDelegate(
    [MarshalAs(UnmanagedType.LPWStr)] string desktopFileName,
    [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] iconFiles,
    int count);

  /// <remarks>
  /// Call after the QApplication exists and before the QML window is loaded. A missing icon only costs
  /// looks, so it is logged rather than stopping the app. That happens on Windows when the app wasn't
  /// built with packaging/windows/build.ps1, which compiles the library there.
  /// </remarks>
  public static void Apply()
  {
    var file = OperatingSystem.IsWindows() ? "LaunchHeimAppIcon.dll" : "libLaunchHeimAppIcon.so";
    if (!NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, file), out var library))
    {
      Console.Error.WriteLine($"LaunchHeim: {file} could not be loaded, so the window has no app icon.");
      return;
    }

    var folder = Path.Combine(AppContext.BaseDirectory, "packaging", "icons");
    var icons = Directory.Exists(folder) ? Directory.GetFiles(folder, "launchheim-*.png") : [];
    var setAppIcon = Marshal.GetDelegateForFunctionPointer<SetAppIconDelegate>(NativeLibrary.GetExport(library, "launchheim_set_app_icon"));
    // The desktop entry's name, which on Wayland is how Plasma finds the icon (Icon=launchheim in it).
    if (setAppIcon(Path.GetFileNameWithoutExtension(NxmHandler.DesktopFileName), icons, icons.Length) == 0)
    {
      Console.Error.WriteLine("LaunchHeim: none of the icons in packaging/icons could be loaded, so the window has no app icon.");
    }
  }
}
