using System.Net;
using CINE.LaunchHeim.Core;
using CINE.LaunchHeim.Core.Catalogs.CurseForge;
using CINE.LaunchHeim.Core.Catalogs.Nexus;
using CINE.LaunchHeim.Core.Catalogs.Thunderstore;
using CINE.LaunchHeim.Core.Downloads;
using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Mods;
using CINE.LaunchHeim.Core.Storage;
using CINE.LaunchHeim.Desktop.Hosting;
using CINE.LaunchHeim.Desktop.ViewModels;
using Qml.Net;

namespace CINE.LaunchHeim.Desktop;

public static class Program
{
  private static QCoreApplication? _app;

  /// <summary>Runs an action on the Qt thread. Safe to call from any thread.</summary>
  public static void Dispatch(Func<Task> action) => _app?.Dispatch(() => _ = action());

  public static int Main(string[] args)
  {
    // Used by install.sh: adds LaunchHeim to the application launcher and makes it the nxm:// handler.
    if (args is ["--register-desktop"])
    {
      NxmHandler.RegisterAsync().GetAwaiter().GetResult();
      Console.WriteLine($"Registered {NxmHandler.DesktopFileName} as the application entry and nxm:// handler.");
      return 0;
    }

    var paths = AppPaths.FromEnvironment();
    var commandLine = string.Join(' ', args);

    // A second start (typically the browser handing over an nxm:// link) passes its arguments to the
    // running window and exits.
    if (SingleInstance.TryForward(paths.IpcSocket, commandLine))
    {
      return 0;
    }

    QtRuntime.Prepare();
    QmlNetSignalFix.Apply();

    // Material's dense variant is the desktop-sized one; the default is sized for touch screens.
    Environment.SetEnvironmentVariable("QT_QUICK_CONTROLS_MATERIAL_VARIANT", "Dense");
    QCoreApplication.SetAttribute(ApplicationAttribute.EnableHighDpiScaling, true);
    QQuickStyle.SetStyle("Material");

    using var app = new QApplication(args);
    _app = app;
    QCoreApplication.OrganizationName = "CINE";
    QCoreApplication.OrganizationDomain = "cine.local";

    using var http = CreateHttpClient();
    var settingsStore = new SettingsStore(paths);
    var instances = new InstanceStore(paths);
    var index = new ThunderstoreIndex(http, paths);

    AppViewModel? viewModel = null;
    var catalogs = new CatalogRegistry(
      new ThunderstoreCatalog(http, index),
      new NexusCatalog(http, () => viewModel?.SettingsModel.NexusApiKey),
      new CurseForgeCatalog(http, () => viewModel?.SettingsModel.CurseForgeApiKey));
    var mods = new ModService(instances, new ModInstaller(), new ModDownloader(http, paths), catalogs, paths);
    CleanTemp(paths);

    viewModel = new AppViewModel(paths, settingsStore, instances, mods, catalogs, new GameFolderImporter(instances), new ImageCache(http, paths));

    // A singleton instead of a context property: Qml.Net gives context-property objects JavaScript
    // ownership, so the JS garbage collector deletes the wrapper after a while and `app` turns null.
    // Singletons belong to the engine. TypeCreator hands QML the instance built above.
    TypeCreator.Current = TypeCreator.FromDelegate(type => type == typeof(AppViewModel) ? viewModel : Activator.CreateInstance(type)!);
    Qml.Net.Qml.RegisterSingletonType(typeof(AppViewModel), "App", "LaunchHeim", 1, 0);

    using var engine = new QQmlApplicationEngine();
    engine.SetContextProperty("screenshotPath", Environment.GetEnvironmentVariable("LAUNCHHEIM_SCREENSHOT") ?? "");
    engine.SetContextProperty("screenshotDelay", int.TryParse(Environment.GetEnvironmentVariable("LAUNCHHEIM_SCREENSHOT_DELAY"), out var delay) ? delay : 4000);
    engine.Load(Path.Combine(AppContext.BaseDirectory, "qml", "Main.qml"));

    using var listener = SingleInstance.Listen(paths.IpcSocket, line => app.Dispatch(() => viewModel.HandleCommandLine(line)));

    viewModel.WarmUp();
    if (Environment.GetEnvironmentVariable("LAUNCHHEIM_SCREENSHOT_PAGE") is { Length: > 0 } screenshotPage)
    {
      viewModel.Navigate(screenshotPage);
    }

    if (commandLine.Length > 0)
    {
      viewModel.HandleCommandLine(commandLine);
    }

    var exitCode = app.Exec();
    viewModel.Theme.Dispose();
    return exitCode;
  }

  private static HttpClient CreateHttpClient()
  {
    var handler = new SocketsHttpHandler
    {
      AutomaticDecompression = DecompressionMethods.All,
      PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };
    var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
    return http;
  }

  // Leftovers from an install that was interrupted by closing the app.
  private static void CleanTemp(AppPaths paths)
  {
    try
    {
      var temp = Path.Combine(paths.CacheDirectory, "tmp");
      if (Directory.Exists(temp))
      {
        Directory.Delete(temp, recursive: true);
      }
    }
    catch (IOException)
    {
    }
  }
}
