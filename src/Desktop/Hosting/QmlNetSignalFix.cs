using System.Reflection;
using System.Runtime.InteropServices;

namespace CINE.LaunchHeim.Desktop.Hosting;

/// <summary>
/// Routes Qml.Net's signal activation through <c>native/signal_fix.cpp</c>, which notifies every QML
/// wrapper of an object instead of only the first one. See that file for the bug itself.
/// </summary>
/// <remarks>
/// Without this, a C# property change only reaches one of the bindings that show it, so most of the UI
/// would not update. Qml.Net resolves its native functions into delegate properties on an internal
/// interop class, so swapping one delegate is enough and nothing else in Qml.Net changes.
/// </remarks>
public static class QmlNetSignalFix
{
  private const string GetAllLiveInstances = "_ZN8NetValue19getAllLiveInstancesERK14QSharedPointerI12NetReferenceE";
  private const string ActivateSignal = "_ZN8NetValue14activateSignalERK7QStringRK14QSharedPointerI14NetVariantListE";

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate void InitDelegate(IntPtr getAllLiveInstances, IntPtr activateSignal);

  /// <summary>Installs the fix on Linux; on Windows, checks that QmlNet.dll was built with it.</summary>
  /// <remarks>
  /// MSVC only exports what is marked for export, and QmlNet.dll does not export the NetValue methods this
  /// fix calls. So the Windows build compiles Qml.Net's native library from source with the same change
  /// (packaging/windows/qmlnet-signal-fix.patch), which also exports a marker to check for here.
  /// </remarks>
  public static void Apply()
  {
    if (OperatingSystem.IsWindows())
    {
      var library = NativeLibrary.Load(FindQmlNet());
      if (!NativeLibrary.TryGetExport(library, "launchheim_signal_fix", out _))
      {
        Console.Error.WriteLine("LaunchHeim: QmlNet.dll is the upstream build without the signal fix, so parts of the UI will not update. Build with packaging/windows/build.ps1.");
      }

      return;
    }

    var qmlNet = NativeLibrary.Load(FindQmlNet());
    var fix = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "libLaunchHeimSignalFix.so"));

    var init = Marshal.GetDelegateForFunctionPointer<InitDelegate>(NativeLibrary.GetExport(fix, "launchheim_signalfix_init"));
    init(NativeLibrary.GetExport(qmlNet, GetAllLiveInstances), NativeLibrary.GetExport(qmlNet, ActivateSignal));

    var interop = typeof(Qml.Net.Qml).Assembly.GetType("Qml.Net.Internal.Interop")
      ?? throw new InvalidOperationException("Qml.Net's interop class was not found; the signal fix needs updating for this Qml.Net version.");
    var netReference = interop.GetProperty("NetReference", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
    var property = netReference.GetType().GetProperty("ActivateSignal")!;
    property.SetValue(netReference, Marshal.GetDelegateForFunctionPointer(NativeLibrary.GetExport(fix, "launchheim_activate_signal"), property.PropertyType));
  }

  // A plain build keeps NuGet's runtimes/ layout; a RID-specific publish flattens it into the root.
  private static string FindQmlNet()
  {
    var (rid, file) = OperatingSystem.IsWindows() ? ("win-x64", "QmlNet.dll") : ("linux-x64", "libQmlNet.so");
    string[] candidates =
    [
      Path.Combine(AppContext.BaseDirectory, file),
      Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", file),
    ];
    return candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException($"{file} was not found next to LaunchHeim.");
  }
}
