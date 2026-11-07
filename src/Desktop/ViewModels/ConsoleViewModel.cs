using System.Text;
using System.Text.Json;
using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Logging;
using CINE.LaunchHeim.Desktop.Hosting;
using Qml.Net;

namespace CINE.LaunchHeim.Desktop.ViewModels;

/// <summary>The console window: one log at a time, followed live, filtered and coloured by level.</summary>
/// <remarks>
/// <para>
/// Three logs: BepInEx's <c>LogOutput.log</c> in the instance (the mods, and Unity's messages as BepInEx
/// relays them), Unity's <c>Player.log</c> (the game on its own, and what happens before BepInEx loads)
/// and LaunchHeim's own log (the launch itself: paths, arguments, Steam, installs). A fourth once a
/// dedicated server was opened: the server's own log (<c>-logFile</c>), with a command line under it that
/// talks to LaunchHeim's server plugin. Commands and their answers are added to the lines shown, not to
/// the file, so they go when the server starts a new log.
/// </para>
/// <para>
/// Lines reach QML through <see cref="Poll"/>, which the window's timer calls, as one JSON string per
/// batch. Handing thousands of .NET objects to QML would go through a Qml.Net wrapper each, and a
/// replaced list would reset the view's scroll position; a QML ListModel that only ever gets appended
/// to keeps it. The file is read on the thread pool, so a burst of log lines never stalls the UI.
/// </para>
/// </remarks>
[Signal("raiseRequested")]
public sealed class ConsoleViewModel : ViewModel
{
  /// <summary>Kept in memory for filtering and copying; the window keeps the same number.</summary>
  public const int MaxLines = 10_000;

  private readonly AppViewModel _app;
  private readonly List<LogLine> _lines = [];
  private readonly List<LogLine> _pending = [];
  private LogFollower? _follower;
  private Task? _reading;
  private DateTime _lastRead;
  private bool _reset = true;
  private InstanceViewModel? _instance;
  private ServerViewModel? _server;
  private string _source = "bepinex";
  private string _filter = "";
  private bool _problemsOnly;
  private bool _visible;
  private bool _fileExists;
  private int _errorCount;
  private int _warningCount;

  public ConsoleViewModel(AppViewModel app)
  {
    _app = app;
  }

  [NotifySignal]
  public bool Visible
  {
    get => _visible;
    set
    {
      if (Set(ref _visible, value) && value)
      {
        Follow();
      }
    }
  }

  /// <summary>bepinex, unity, app or server.</summary>
  [NotifySignal]
  public string Source { get => _source; private set => Set(ref _source, value); }

  [NotifySignal]
  public string InstanceName => _instance?.Name ?? "";

  [NotifySignal]
  public bool HasInstance => _instance is not null;

  /// <summary>A dedicated server was opened, so the Server tab has a log to follow.</summary>
  [NotifySignal]
  public bool HasServer => _server is not null;

  [NotifySignal]
  public string ServerName => _server?.Name ?? "";

  /// <summary>The Server tab is shown and that server has LaunchHeim's plugin, so commands can be typed.</summary>
  [NotifySignal]
  public bool CanSendCommands => _source == "server" && _server is { ConsoleEnabled: true };

  [NotifySignal]
  public string FilePath => _follower?.Path ?? "";

  [NotifySignal]
  public bool FileExists { get => _fileExists; private set => Set(ref _fileExists, value); }

  /// <summary>Why the log is empty, in words, for the window's empty state.</summary>
  [NotifySignal]
  public string MissingText => _source switch
  {
    "bepinex" when _instance is null => "Create an instance to see its BepInEx log.",
    "bepinex" => $"BepInEx writes this log when {_instance!.Name} starts. Play it and the log follows here as it's written.",
    "unity" => "Unity writes Valheim's Player.log when the game starts, with or without mods.",
    "server" when _server is null => "Open a server on the Servers page to follow its log.",
    "server" => $"{_server!.Name} writes this log when it starts. Start it and the log follows here as it's written.",
    _ => "LaunchHeim's own log is written to this file.",
  };

  [NotifySignal]
  public string Filter
  {
    get => _filter;
    set
    {
      if (Set(ref _filter, value ?? ""))
      {
        _reset = true;
      }
    }
  }

  /// <summary>Only warnings and errors.</summary>
  [NotifySignal]
  public bool ProblemsOnly
  {
    get => _problemsOnly;
    set
    {
      if (Set(ref _problemsOnly, value))
      {
        _reset = true;
      }
    }
  }

  /// <summary>Entries (not stack trace lines) counted since the log (re)started.</summary>
  [NotifySignal]
  public int ErrorCount { get => _errorCount; private set => Set(ref _errorCount, value); }

  [NotifySignal]
  public int WarningCount { get => _warningCount; private set => Set(ref _warningCount, value); }

  [NotifySignal]
  public int MaxLineCount => MaxLines;

  public void Show(string source)
  {
    SetSource(source);
    Visible = true;
    this.ActivateSignal("raiseRequested");
  }

  public void Close() => Visible = false;

  public void SetSource(string source)
  {
    if (source is not ("bepinex" or "unity" or "app" or "server") || source == "server" && _server is null)
    {
      return;
    }

    Source = source;
    Follow();
  }

  /// <summary>Follows a dedicated server's log.</summary>
  /// <param name="onlyIfOpen">
  /// After a start: switch an open console to the server, or open it if Settings say to open it on
  /// every launch, but don't pop it up otherwise.
  /// </param>
  internal void ShowForServer(ServerViewModel server, bool onlyIfOpen = false)
  {
    _server = server;
    Raise(nameof(HasServer));
    Raise(nameof(ServerName));
    if (!onlyIfOpen || _app.SettingsModel.OpenConsoleOnLaunch)
    {
      Show("server");
    }
    else if (_visible)
    {
      SetSource("server");
    }
  }

  /// <summary>Runs a command on the server shown and adds it and its answer to the lines.</summary>
  public async void SendCommand(string? line)
  {
    var server = _server;
    line = line?.Trim();
    if (server is null || string.IsNullOrEmpty(line) || _source != "server")
    {
      return;
    }

    AddLocal(new LogLine("> " + line, LogLevel.Debug));
    var answer = await server.SendAsync(line);
    var error = answer.StartsWith("Error: ", StringComparison.Ordinal);
    var first = true;
    foreach (var text in answer.Split('\n'))
    {
      AddLocal(new LogLine(text, error ? LogLevel.Error : LogLevel.Info, Continues: !first));
      first = false;
    }
  }

  private void AddLocal(LogLine line)
  {
    // Only if the server tab is still what's shown; the answer to a command on another log is dropped.
    if (_source != "server")
    {
      return;
    }

    _lines.Add(line);
    if (!_reset && Matches(line))
    {
      _pending.Add(line);
    }
  }

  internal void ShowFor(InstanceViewModel instance)
  {
    _instance = instance;
    Show("bepinex");
  }

  /// <summary>Points the BepInEx log at the instance that was just started and opens the window if the user wants that.</summary>
  internal void GameStarted(InstanceViewModel? instance)
  {
    if (instance is not null)
    {
      _instance = instance;
    }

    var source = instance is null && _source == "bepinex" ? "unity" : _source;
    if (_app.SettingsModel.OpenConsoleOnLaunch)
    {
      Show(source);
    }
    else if (_visible)
    {
      SetSource(source);
    }
  }

  /// <summary>Called when an instance is deleted, so the console stops following a folder that's gone.</summary>
  internal void InstanceRemoved(InstanceViewModel instance)
  {
    if (_instance == instance)
    {
      _instance = _app.SelectedInstance;
      if (_source == "bepinex")
      {
        Follow();
      }
    }
  }

  /// <summary>
  /// New lines as <c>{"reset":bool,"lines":[[text, level], ...]}</c>, level being d, i, w or e. After a
  /// reset (another log, a new game session, a changed filter) the lines replace what the window shows.
  /// </summary>
  public string Poll()
  {
    // A log at a host is read every few seconds (its PollInterval), a local one on every tick.
    if (_follower is not null && (_reading is null || _reading.IsCompleted) && DateTime.UtcNow - _lastRead >= _follower.PollInterval)
    {
      _lastRead = DateTime.UtcNow;
      _reading = ReadAsync(_follower);
    }

    var reset = _reset;
    var lines = reset ? _lines.Where(Matches).ToList() : _pending.ToList();
    _pending.Clear();
    _reset = false;

    using var buffer = new MemoryStream();
    using (var json = new Utf8JsonWriter(buffer))
    {
      json.WriteStartObject();
      json.WriteBoolean("reset", reset);
      json.WriteStartArray("lines");
      foreach (var line in lines)
      {
        json.WriteStartArray();
        json.WriteStringValue(line.Text);
        json.WriteStringValue(LevelCode(line.Level));
        json.WriteEndArray();
      }

      json.WriteEndArray();
      json.WriteEndObject();
    }

    return Encoding.UTF8.GetString(buffer.ToArray());
  }

  /// <summary>Hides what's there so far; only new lines show. The file itself is left alone.</summary>
  public void Clear()
  {
    _lines.Clear();
    _pending.Clear();
    _reset = true;
    ErrorCount = 0;
    WarningCount = 0;
  }

  /// <summary>The lines the window shows, for the clipboard (a bug report, a Discord message).</summary>
  public string ShownText() => string.Join('\n', _lines.Where(Matches).Select(l => l.Text));

  public void OpenFile()
  {
    if (File.Exists(FilePath))
    {
      DesktopShell.Open(FilePath);
    }
  }

  public void OpenFolder()
  {
    var folder = Path.GetDirectoryName(FilePath);
    if (Directory.Exists(folder))
    {
      DesktopShell.Open(folder);
    }
  }

  private void Follow()
  {
    // Opened from Settings or the sidebar before any instance was picked: the selected one is the best guess.
    _instance ??= _app.SelectedInstance;
    // A server's log may be at a host, so it comes as a source rather than a path.
    var serverSource = _source == "server" ? _server?.LogSource : null;
    var path = _source switch
    {
      "bepinex" => _instance is null ? null : Path.Combine(_instance.Directory, "BepInEx", "LogOutput.log"),
      "unity" => UnityPlayerLog.Find(),
      "server" => serverSource?.Path,
      _ => Log.FilePath ?? _app.Paths.LogFile,
    };

    if (_follower?.Path != path || path is null)
    {
      _follower = path is null ? null : serverSource is not null ? new LogFollower(serverSource) : new LogFollower(path);
      _lastRead = DateTime.MinValue;
      _reading = null;
      _lines.Clear();
      _pending.Clear();
      _reset = true;
      ErrorCount = 0;
      WarningCount = 0;
      FileExists = path is not null && (serverSource is not null || File.Exists(path));
    }

    Raise(nameof(FilePath));
    Raise(nameof(CanSendCommands));
    Raise(nameof(ServerName));
    Raise(nameof(InstanceName));
    Raise(nameof(HasInstance));
    Raise(nameof(MissingText));
  }

  private async Task ReadAsync(LogFollower follower)
  {
    LogChunk chunk;
    try
    {
      chunk = await Task.Run(follower.Read);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return;
    }

    // Another log was picked while this one was being read.
    if (follower != _follower)
    {
      return;
    }

    FileExists = follower.Exists;
    if (chunk.Restarted)
    {
      _lines.Clear();
      _pending.Clear();
      _reset = true;
      ErrorCount = 0;
      WarningCount = 0;
    }

    foreach (var line in chunk.Lines)
    {
      _lines.Add(line);
      if (!_reset && Matches(line))
      {
        _pending.Add(line);
      }
    }

    if (_lines.Count > MaxLines)
    {
      _lines.RemoveRange(0, _lines.Count - MaxLines);
    }

    if (chunk.Lines.Count > 0)
    {
      ErrorCount += chunk.Lines.Count(l => l.Level == LogLevel.Error && !l.Continues);
      WarningCount += chunk.Lines.Count(l => l.Level == LogLevel.Warning && !l.Continues);
    }
  }

  private bool Matches(LogLine line) =>
    (!_problemsOnly || line.Level >= LogLevel.Warning)
    && (_filter.Length == 0 || line.Text.Contains(_filter, StringComparison.OrdinalIgnoreCase));

  private static string LevelCode(LogLevel level) => level switch
  {
    LogLevel.Error => "e",
    LogLevel.Warning => "w",
    LogLevel.Debug => "d",
    _ => "i",
  };
}
