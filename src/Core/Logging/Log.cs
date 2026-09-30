using System.Globalization;

namespace CINE.LaunchHeim.Core.Logging;

public enum LogLevel
{
  Debug,
  Info,
  Warning,
  Error,
}

/// <summary>LaunchHeim's own log: <c>launchheim.log</c> in the state folder, plus stderr for warnings and errors.</summary>
/// <remarks>
/// <para>
/// A file, because a launcher started from the application menu has no terminal: its stderr ends up in
/// the journal at best, and on Windows nowhere. The console window follows this file like it follows the
/// game's logs, so what LaunchHeim did before a launch sits next to what BepInEx did after it.
/// </para>
/// <para>
/// Static rather than injected: every layer logs, and threading a logger through the Qml.Net view models
/// buys nothing in a single-process desktop app. Until <see cref="Open"/> is called (the tests never do),
/// only stderr is written.
/// </para>
/// </remarks>
public static class Log
{
  /// <summary>Past this size the file is moved to <c>launchheim.log.1</c> on the next start.</summary>
  public const long RotateBytes = 2 * 1024 * 1024;

  private static readonly Lock Gate = new();
  private static StreamWriter? _writer;

  public static string? FilePath { get; private set; }

  /// <summary>Opens the log for appending. Keeps the previous session unless the file grew too large.</summary>
  public static void Open(string path)
  {
    lock (Gate)
    {
      try
      {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && new FileInfo(path).Length > RotateBytes)
        {
          File.Move(path, path + ".1", overwrite: true);
        }

        // Shared for reading and deleting, so the console window (and an editor) can follow it while it grows.
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream) { AutoFlush = true };
        FilePath = path;
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        // A launcher that can't write its log should still launch.
        Console.Error.WriteLine($"LaunchHeim: the log {path} could not be opened: {ex.Message}");
      }
    }
  }

  public static void Close()
  {
    lock (Gate)
    {
      _writer?.Dispose();
      _writer = null;
    }
  }

  public static void Debug(string message) => Write(LogLevel.Debug, message);

  public static void Info(string message) => Write(LogLevel.Info, message);

  public static void Warning(string message) => Write(LogLevel.Warning, message);

  public static void Error(string message, Exception? exception = null) =>
    Write(LogLevel.Error, exception is null ? message : $"{message}{Environment.NewLine}{exception}");

  public static void Write(LogLevel level, string message)
  {
    var line = Format(DateTimeOffset.Now, level, message);
    lock (Gate)
    {
      if (level >= LogLevel.Warning)
      {
        Console.Error.WriteLine("LaunchHeim: " + message);
      }

      try
      {
        _writer?.WriteLine(line);
      }
      catch (IOException)
      {
        // A full disk must not turn every toast into a crash.
      }
    }
  }

  /// <summary>
  /// BepInEx's bracketed level prefix, so <see cref="LogClassifier"/> reads both logs the same way and a
  /// search for <c>[Error</c> works in either file.
  /// </summary>
  internal static string Format(DateTimeOffset when, LogLevel level, string message)
  {
    var name = level switch
    {
      LogLevel.Warning => "Warning",
      LogLevel.Error => "Error",
      LogLevel.Debug => "Debug",
      _ => "Info",
    };
    return $"[{name,-7}: {when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}] {message}";
  }
}
