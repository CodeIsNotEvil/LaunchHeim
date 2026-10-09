using System.Runtime.InteropServices;
using CINE.LaunchHeim.Core.Logging;

namespace CINE.LaunchHeim.Desktop.Hosting;

/// <summary>
/// Puts files on the clipboard as a file manager's Copy does, through <c>native/app_icon.cpp</c>
/// (<c>launchheim_copy_files</c>). Qml.Net has no clipboard, and QML 2 only copies text.
/// </summary>
public static class FileClipboard
{
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate int CopyFilesDelegate(
    [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] files,
    int count);

  /// <summary>Copies <paramref name="files"/>. Call on the Qt thread. False when the native library is missing.</summary>
  public static bool TryCopy(params string[] files)
  {
    if (AppIcon.Load() is not { } library || !NativeLibrary.TryGetExport(library, "launchheim_copy_files", out var export))
    {
      Log.Warning("Files can't be put on the clipboard without the native helper library.");
      return false;
    }

    return Marshal.GetDelegateForFunctionPointer<CopyFilesDelegate>(export)(files, files.Length) == files.Length;
  }
}
