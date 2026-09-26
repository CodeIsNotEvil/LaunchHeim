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

  public static void Apply()
  {
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
    string[] candidates =
    [
      Path.Combine(AppContext.BaseDirectory, "runtimes", "linux-x64", "native", "libQmlNet.so"),
      Path.Combine(AppContext.BaseDirectory, "libQmlNet.so"),
    ];
    return candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("libQmlNet.so was not found next to LaunchHeim.");
  }
}
