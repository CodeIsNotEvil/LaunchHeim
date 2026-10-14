using System.Diagnostics;
using System.Runtime.InteropServices;
using CINE.LaunchHeim.Core;
using CINE.LaunchHeim.Core.Catalogs.CurseForge;
using CINE.LaunchHeim.Core.Catalogs.Nexus;
using CINE.LaunchHeim.Core.Catalogs.Thunderstore;
using CINE.LaunchHeim.Core.Cli;
using CINE.LaunchHeim.Core.Downloads;
using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Logging;
using CINE.LaunchHeim.Core.Mods;
using CINE.LaunchHeim.Core.Saves;
using CINE.LaunchHeim.Core.Storage;
using CINE.LaunchHeim.Desktop.Hosting;

namespace CINE.LaunchHeim.Desktop.Cli;

/// <summary>Runs a <see cref="CliCommand"/> without the window: no Qt is loaded for any of them.</summary>
/// <remarks>
/// <para>
/// <c>play</c> and <c>steam</c> launch the way the window does (<see cref="PlayTargets"/> picks the same
/// launch, <see cref="GameLauncher"/> plans it), then stay until the game exits. For <c>steam</c> that is
/// required: Steam counts the game as running while the process it started runs.
/// </para>
/// <para>
/// A launch from here counts like one from the window: it becomes the last play and updates the server's
/// or world's remembered choice. Settings are read again right before saving, so a window open meanwhile
/// loses as little as possible; the window itself still saves its own copy later.
/// </para>
/// </remarks>
public static class CliRunner
{
  /// <summary>
  /// What a start from Steam sends the open window when it can't launch, followed by the reason. Steam
  /// shows nothing when its game exits at once, so the window shows the reason instead.
  /// </summary>
  public const string LaunchErrorMessage = "--launch-error ";

  public static int Run(CliCommand command, AppPaths paths)
  {
    if (OperatingSystem.IsWindows() && command.Verb != CliVerb.Steam)
    {
      // LaunchHeim.exe is a GUI program on Windows, which has no console to print to; borrow the terminal's.
      AttachConsole(AttachParentProcess);
    }

    switch (command.Verb)
    {
      case CliVerb.Help:
        Console.WriteLine(CommandLine.Usage);
        return 0;
      case CliVerb.Version:
        Console.WriteLine($"LaunchHeim {AppInfo.Version}");
        return 0;
      case CliVerb.List:
        return List(paths);
    }

    Log.Open(paths.CommandLineLogFile);
    try
    {
      return command.Verb == CliVerb.Steam
        ? SteamAsync(command.Request, command.SteamCommand, paths).GetAwaiter().GetResult()
        : PlayAsync(command.Request, paths).GetAwaiter().GetResult();
    }
    finally
    {
      Log.Close();
    }
  }

  /// <summary>
  /// What to put into Valheim's launch options in Steam, for Settings. The name alone when the packages'
  /// <c>/usr/bin/launchheim</c> is this program, since Steam's PATH has <c>/usr/bin</c> (but not
  /// necessarily install.sh's <c>~/.local/bin</c>); otherwise the full path.
  /// </summary>
  public static string SteamLaunchOptions()
  {
    var self = Environment.ProcessPath ?? "LaunchHeim";
    if (!OperatingSystem.IsWindows() && SameFile("/usr/bin/launchheim", self))
    {
      return "launchheim steam %command%";
    }

    return OperatingSystem.IsWindows() || self.Contains(' ') ? $"\"{self}\" steam %command%" : $"{self} steam %command%";
  }

  private static bool SameFile(string link, string path)
  {
    try
    {
      var target = File.ResolveLinkTarget(link, returnFinalTarget: true)?.FullName;
      return target is not null && Path.GetFullPath(target) == Path.GetFullPath(path);
    }
    catch (IOException)
    {
      return false;
    }
  }

  private static async Task<int> PlayAsync(PlayRequest request, AppPaths paths)
  {
    try
    {
      var settings = new SettingsStore(paths).Load();
      var instances = new InstanceStore(paths);
      var locator = SteamLibraryLocator.ForCurrentUser();
      var saves = ValheimSaves.ForCurrentUser(locator.SteamRoots);
      var target = PlayTargets.Resolve(request, settings, PlayLibrary.Read(instances.LoadAll(), saves));

      var gameDirectory = locator.GameDirectory(settings.GameDirectory);
      // The window knows when its own game runs; here only the process list tells.
      if (GameProcess.IsRunning(SteamLibraryLocator.ValheimExecutable))
      {
        throw new LaunchException("Valheim is already running. Close it, then start it again.");
      }

      var (instanceDirectory, pluginArguments) = await PrepareGamePluginAsync(target, instances, settings, paths);
      GameLauncher.PrepareGameFolder(gameDirectory, instanceDirectory);
      var plan = GameLauncher.Plan(
        gameDirectory,
        instanceDirectory,
        target.Instance?.LaunchArguments ?? settings.VanillaLaunchArguments,
        GameLauncher.CurrentEnvironment(),
        joinArguments: [.. target.Direct?.Arguments ?? [], .. pluginArguments],
        steamDirectory: locator.ClientDirectory());

      // Valheim without a logged-in Steam client shows a black window and no error (see SteamClient).
      var steam = SteamClient.ForCurrentUser();
      await steam.EnsureReadyAsync(
        state => Say(state == SteamState.NotRunning ? "Starting Steam. Log in if it asks." : "Waiting for Steam. Log in if it asks."),
        CancellationToken.None);

      return await StartAndWaitAsync(target, plan, saves.DataDirectory, paths, steam.Timeout, rememberLastPlay: true);
    }
    catch (Exception ex) when (ex is LaunchException or IOException or UnauthorizedAccessException)
    {
      // Errors also go to stderr (see Log), which is where the terminal shows them.
      Log.Error($"Starting Valheim failed: {ex.Message}", ex is LaunchException ? null : ex);
      return 1;
    }
  }

  /// <summary>Starts Steam's command for Valheim with the last play's instance (or the one the options pick).</summary>
  /// <remarks>
  /// <para>
  /// Steam's command is run unchanged when it isn't Valheim (the options sit on another game) or when it
  /// is one of LaunchHeim's own launches on Windows, which go through <c>steam.exe -applaunch</c> and so
  /// through these launch options as well (<see cref="GameLauncher.IsLaunchHeimCommand"/>).
  /// </para>
  /// <para>
  /// When Steam already joins a server (a friend's game from the friends list), its <c>+connect</c> wins:
  /// the instance is kept, the remembered destination isn't added, and the last play stays as it was.
  /// </para>
  /// <para>
  /// Nothing is started when the launch can't be planned, such as an instance without BepInEx or a world
  /// that's gone: starting vanilla instead could open a modded world without its mods, and Valheim drops
  /// modded items when it saves. The reason goes to the open window, or a notification.
  /// </para>
  /// </remarks>
  private static async Task<int> SteamAsync(PlayRequest request, IReadOnlyList<string> command, AppPaths paths)
  {
    Log.Info($"LaunchHeim {AppInfo.Version} started by Steam: {string.Join(' ', GameLauncher.Redact(command))}");
    if (command.Contains("%command%"))
    {
      // Typed into a terminal: only Steam replaces %command% with the game's command.
      Console.Error.WriteLine("LaunchHeim: Steam fills in %command% when it starts Valheim, so this line only works in Valheim's launch options in Steam (Settings → \"Start from Steam\" has it). To start the game from a terminal, use `launchheim play`.");
      return 2;
    }

    var gameIndex = GameLauncher.FindGame(command);
    if (gameIndex < 0 || GameLauncher.IsLaunchHeimCommand(command))
    {
      Log.Info(gameIndex < 0 ? "  Not Valheim, so it runs unchanged." : "  Planned by LaunchHeim already, so it runs unchanged.");
      return await PassThroughAsync(command, paths);
    }

    PlayTarget target;
    LaunchPlan plan;
    ValheimSaves saves;
    try
    {
      var settings = new SettingsStore(paths).Load();
      var instances = new InstanceStore(paths);
      saves = ValheimSaves.ForCurrentUser(SteamLibraryLocator.ForCurrentUser().SteamRoots);
      target = PlayTargets.Resolve(request, settings, PlayLibrary.Read(instances.LoadAll(), saves));
      if (GameLauncher.JoinsServer(command))
      {
        Log.Info("  Steam joins a server itself, so the remembered server or world is left out.");
        target = target.WithoutDestination();
      }

      var (instanceDirectory, pluginArguments) = await PrepareGamePluginAsync(target, instances, settings, paths);
      GameLauncher.PrepareGameFolder(Path.GetDirectoryName(Path.GetFullPath(command[gameIndex]))!, instanceDirectory);
      plan = GameLauncher.WrapSteamCommand(
        command,
        instanceDirectory,
        target.Instance?.LaunchArguments ?? settings.VanillaLaunchArguments,
        joinArguments: [.. target.Direct?.Arguments ?? [], .. pluginArguments]);
    }
    catch (Exception ex) when (ex is LaunchException or IOException or UnauthorizedAccessException)
    {
      Log.Error($"Starting Valheim from Steam failed: {ex.Message}", ex is LaunchException ? null : ex);
      ReportFailure(paths, ex.Message);
      return 1;
    }

    // Steam started this, so it runs and is logged in; it has no game to wait for either.
    return await StartAndWaitAsync(target, plan, saves.DataDirectory, paths, TimeSpan.Zero, rememberLastPlay: !GameLauncher.JoinsServer(command));
  }

  /// <summary>
  /// The folder to launch with (the instance's, or for vanilla with "Start instantly" LaunchHeim's own
  /// BepInEx) and the arguments for LaunchHeim's game plugin in it (see <see cref="GamePlugin"/>). A
  /// launch the plugin can't join still starts.
  /// </summary>
  private static async Task<(string? Directory, IReadOnlyList<string> Arguments)> PrepareGamePluginAsync(
    PlayTarget target,
    InstanceStore instances,
    AppSettings settings,
    AppPaths paths)
  {
    var instanceDirectory = target.Instance is null ? null : instances.DirectoryOf(target.Instance);
    var gamePlugin = new GamePlugin(paths, store =>
    {
      // Only when BepInExPack has to be downloaded for the vanilla setup. This process ends with the
      // game, so the client isn't disposed.
      Say("Setting up BepInEx for instant start. This happens once.");
      var http = Program.CreateHttpClient();
      var catalogs = new CatalogRegistry(
        new ThunderstoreCatalog(http, new ThunderstoreIndex(http, paths)),
        new NexusCatalog(http, () => null),
        new CurseForgeCatalog(http, () => null));
      return new ModService(store, new ModInstaller(), new ModDownloader(http, paths), catalogs, paths);
    });
    // A problem is logged, which also prints it; the game starts without the plugin.
    var prepared = await gamePlugin.PrepareAsync(instanceDirectory, target.Direct, settings.KeepAchievements, null, CancellationToken.None);
    return (prepared.InstanceDirectory, prepared.Arguments);
  }

  private static async Task<int> StartAndWaitAsync(PlayTarget target, LaunchPlan plan, string dataDirectory, AppPaths paths, TimeSpan steamTimeout, bool rememberLastPlay)
  {
    if (target.Direct is { } direct)
    {
      try
      {
        direct.SelectInGame(dataDirectory);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        // Only costs the preselection, so the game still starts.
        Log.Error("Setting Valheim's selected character failed", ex);
      }
    }

    LogLaunch(target, plan);
    var started = DateTimeOffset.Now;
    using var process = await GameProcess.StartAsync(plan, steamTimeout, () => Say("Steam is starting Valheim. Answer Steam if it asks."), CancellationToken.None);
    Log.Info($"Valheim started, process {process.Id}.");
    Say($"Started {target.Describe()}. LaunchHeim waits here until the game closes.");
    Remember(target, paths, rememberLastPlay);

    var exitCode = await GameProcess.WaitForExitAsync(process, CancellationToken.None);
    Log.Info($"Valheim exited{(exitCode is { } code ? $" with code {code}" : "")} after {DateTimeOffset.Now - started:h\\:mm\\:ss}.");
    return exitCode ?? 0;
  }

  private static async Task<int> PassThroughAsync(IReadOnlyList<string> command, AppPaths paths)
  {
    try
    {
      using var process = Process.Start(new LaunchPlan(command[0], Environment.CurrentDirectory, command.Skip(1).ToList(), new Dictionary<string, string?>()).ToStartInfo())
        ?? throw new LaunchException("The game did not start.");
      return await GameProcess.WaitForExitAsync(process, CancellationToken.None) ?? 0;
    }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or LaunchException)
    {
      // A crash here showed nothing in Steam and a stack trace in a terminal (0.7.0).
      Log.Error($"Starting {command[0]} failed: {ex.Message}");
      ReportFailure(paths, $"{command[0]} could not be started: {ex.Message}");
      return 1;
    }
  }

  /// <summary>Stores the launch as the window would once the game is up (see <see cref="CliRunner"/>).</summary>
  private static void Remember(PlayTarget target, AppPaths paths, bool rememberLastPlay)
  {
    try
    {
      var store = new SettingsStore(paths);
      var settings = store.Load();
      if (target is { Direct: { } direct, Choice: { } choice })
      {
        settings.PlayChoices[direct.Key] = choice;
      }

      if (rememberLastPlay)
      {
        settings.LastPlay = target.ToLastPlay();
      }

      store.Save(settings);
      if (target.Instance is { } instance)
      {
        instance.LastPlayedAt = DateTimeOffset.UtcNow;
        new InstanceStore(paths).Save(instance);
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      Log.Error("Remembering the launch failed", ex);
    }
  }

  /// <summary>What the game was started with, so a launch that goes wrong can be reproduced by hand.</summary>
  private static void LogLaunch(PlayTarget target, LaunchPlan plan)
  {
    Log.Info($"Starting {target.Describe()}");
    foreach (var (key, value) in target.Direct?.Prefs ?? new Dictionary<string, string>())
    {
      Log.Info($"  PlayerPrefs {key} = {value}");
    }

    Log.Info($"  {plan.FileName} {string.Join(' ', GameLauncher.Redact(plan.Arguments))}".TrimEnd());
    foreach (var (key, value) in plan.Environment.Where(e => e.Value is not null).OrderBy(e => e.Key, StringComparer.Ordinal))
    {
      Log.Info($"  {key}={value}");
    }
  }

  private static void Say(string message)
  {
    Log.Info(message);
    Console.WriteLine(message);
  }

  /// <summary>Tells the user why Steam's Play did nothing: in the open window, else a notification or message box.</summary>
  private static void ReportFailure(AppPaths paths, string message)
  {
    if (SingleInstance.TryForward(paths.IpcSocket, LaunchErrorMessage + message))
    {
      return;
    }

    const string title = "LaunchHeim could not start Valheim";
    if (OperatingSystem.IsWindows())
    {
      MessageBoxW(IntPtr.Zero, message, title, MessageBoxIconError);
      return;
    }

    try
    {
      var info = new ProcessStartInfo("notify-send") { UseShellExecute = false };
      foreach (var argument in (string[])["--app-name=LaunchHeim", "--icon=launchheim", title, message])
      {
        info.ArgumentList.Add(argument);
      }

      // Steam's runtime libraries and overlay are meant for the game, not for desktop tools.
      info.Environment.Remove("LD_LIBRARY_PATH");
      info.Environment.Remove("LD_PRELOAD");
      Process.Start(info)?.Dispose();
    }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
    {
      // No notify-send: the reason is in the log.
    }
  }

  private static int List(AppPaths paths)
  {
    var settings = new SettingsStore(paths).Load();
    var instances = new InstanceStore(paths).LoadAll();
    var saves = ValheimSaves.ForCurrentUser(SteamLibraryLocator.ForCurrentUser().SteamRoots);
    var library = PlayLibrary.Read(instances, saves);

    string Choice(string key)
    {
      if (settings.PlayChoices.GetValueOrDefault(key) is not { } choice)
      {
        return "not played from LaunchHeim yet";
      }

      var setup = choice.InstanceId is null ? PlayTargets.VanillaName : instances.FirstOrDefault(i => i.Id == choice.InstanceId)?.Name ?? "a deleted instance";
      return $"{library.Characters.FirstOrDefault(c => c.FileName == choice.Character)?.DisplayName ?? choice.Character} · {setup}{(choice.InstantStart ? " · instant" : "")}";
    }

    Console.WriteLine("Instances (--instance, by name or id)");
    Table(instances.Select(i => (i.Name, i.Id, $"{i.Mods.Count} mods")));
    Console.WriteLine();
    Console.WriteLine("Characters (--character)");
    Table(library.Characters.DistinctBy(c => c.FileName).Select(c => (c.DisplayName, c.FileName, "")));
    Console.WriteLine();
    Console.WriteLine("Servers (--server, by name or address)");
    Table(library.Servers.Select(s => (s.Name, s.Address, Choice(s.Key))));
    Console.WriteLine();
    Console.WriteLine("Worlds (--world)");
    Table(library.Worlds.DistinctBy(w => w.Key).Select(w => (w.Name, "", Choice(w.Key))));
    Console.WriteLine();

    try
    {
      Console.WriteLine($"Played last, what `launchheim play` and Steam start: {PlayTargets.Resolve(new PlayRequest(), settings, library).Describe()}");
    }
    catch (LaunchException ex)
    {
      Console.WriteLine($"Played last, but it can't start again: {ex.Message}");
    }

    return 0;
  }

  private static void Table(IEnumerable<(string Name, string Detail, string Note)> rows)
  {
    var list = rows.ToList();
    if (list.Count == 0)
    {
      Console.WriteLine("  (none)");
      return;
    }

    var nameWidth = list.Max(r => r.Name.Length) + 2;
    var detailWidth = list.Max(r => r.Detail.Length) + 2;
    foreach (var (name, detail, note) in list)
    {
      Console.WriteLine($"  {name.PadRight(nameWidth)}{detail.PadRight(detailWidth)}{note}".TrimEnd());
    }
  }

  private const int AttachParentProcess = -1;
  private const uint MessageBoxIconError = 0x10;

  [DllImport("kernel32.dll")]
  private static extern bool AttachConsole(int processId);

  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
