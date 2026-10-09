using System.Runtime.InteropServices;
using CINE.LaunchHeim.Core.Logging;

namespace CINE.LaunchHeim.Desktop.Hosting;

/// <summary>
/// Shows LaunchHeim's logo in the window's title bar and the taskbar instead of the desktop's placeholder,
/// and on Windows matches the title bar to the theme, through <c>native/app_icon.cpp</c>. See that file
/// for why each is needed.
/// </summary>
public static class AppIcon
{
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate int SetDarkFramesDelegate(int dark);

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
    if (Load() is not { } library)
    {
      Log.Warning("The window has no app icon.");
      return;
    }

    var folder = Path.Combine(AppContext.BaseDirectory, "packaging", "icons");
    var icons = Directory.Exists(folder) ? Directory.GetFiles(folder, "launchheim-*.png") : [];
    var setAppIcon = Marshal.GetDelegateForFunctionPointer<SetAppIconDelegate>(NativeLibrary.GetExport(library, "launchheim_set_app_icon"));
    // The desktop entry's name, which on Wayland is how Plasma finds the icon (Icon=launchheim in it).
    if (setAppIcon(Path.GetFileNameWithoutExtension(NxmHandler.DesktopFileName), icons, icons.Length) == 0)
    {
      Log.Warning("None of the icons in packaging/icons could be loaded, so the window has no app icon.");
    }
  }

  /// <summary>Gives every LaunchHeim window a dark title bar on Windows when the theme is dark.</summary>
  /// <remarks>
  /// Windows draws the title bar light unless a window asks otherwise, whatever the app mode, so in dark
  /// mode LaunchHeim had a white bar over its dark page. Call it before the QML window is loaded; it also
  /// covers windows opened later (the console). Without the library the bar only stays light.
  /// </remarks>
  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  public static void SetDarkFrames(bool dark)
  {
    if (Load() is not { } library || !NativeLibrary.TryGetExport(library, "launchheim_set_dark_frames", out var export))
    {
      Log.Warning("The title bar keeps Windows' light colors.");
      return;
    }

    Marshal.GetDelegateForFunctionPointer<SetDarkFramesDelegate>(export)(dark ? 1 : 0);
  }

  /// <summary>The native library (also used by <see cref="FileClipboard"/>), or null with Windows' or the loader's reason in the log.</summary>
  internal static IntPtr? Load()
  {
    var file = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "LaunchHeimAppIcon.dll" : "libLaunchHeimAppIcon.so");
    try
    {
      return NativeLibrary.Load(file);
    }
    catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
    {
      // The message carries the reason: a missing file, a missing dependency, a blocked signature.
      Log.Warning($"{file} could not be loaded: {ex.Message}");
      return null;
    }
  }
}
