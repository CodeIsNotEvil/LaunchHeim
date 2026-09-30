using System.Text;
using System.Text.RegularExpressions;

namespace CINE.LaunchHeim.Core.Logging;

/// <summary>What a <see cref="LogFollower"/> found since the last read.</summary>
/// <param name="Restarted">The file was replaced (a new game session): earlier lines no longer belong to it.</param>
public sealed record LogChunk(IReadOnlyList<LogLine> Lines, bool Restarted);

public sealed record LogLine(string Text, LogLevel Level);

/// <summary>Follows a log file as it grows, like <c>tail -F</c>.</summary>
/// <remarks>
/// <para>
/// The game's logs are followed from disk instead of reading the game's stdout. A pipe would tie the
/// game to LaunchHeim: closing the launcher while Valheim runs would leave the game writing into a pipe
/// nobody reads, which blocks or kills it. The files also exist when LaunchHeim did not start the game.
/// </para>
/// <para>
/// BepInEx truncates <c>LogOutput.log</c> on every start and Unity rewrites <c>Player.log</c>, so a
/// file that got shorter, or whose first line changed, is a new session and is read from the top.
/// Only complete lines are returned; the position never moves past the last newline, so a line that is
/// still being written is picked up whole on the next read.
/// </para>
/// </remarks>
public sealed class LogFollower(string path, long maxInitialBytes = LogFollower.DefaultInitialBytes)
{
  /// <summary>A log several MB long starts at its tail; the start of a long session is rarely what's wanted.</summary>
  public const long DefaultInitialBytes = 2 * 1024 * 1024;

  /// <summary>Caps one read, so a huge burst reaches the window in slices instead of freezing it.</summary>
  private const int MaxReadBytes = 1024 * 1024;

  private const int HeadBytes = 128;

  private readonly LogClassifier _classifier = new();
  private long _position = -1;
  private byte[] _head = [];

  public string Path => path;

  public LogChunk Read()
  {
    FileStream stream;
    try
    {
      stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }
    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or IOException)
    {
      // Deleted, or not written yet: whatever appears next is a new file, read from its start.
      var wasOpen = _position > 0;
      _position = 0;
      _head = [];
      return new LogChunk([], wasOpen);
    }

    using (stream)
    {
      var length = stream.Length;
      var head = ReadHead(stream, length);
      var restarted = false;
      if (_position < 0)
      {
        _position = length > maxInitialBytes ? length - maxInitialBytes : 0;
      }
      else if (length < _position || !StartsWith(head, _head))
      {
        restarted = _position > 0;
        _position = 0;
        _classifier.Reset();
      }

      _head = head;
      if (length <= _position)
      {
        return new LogChunk([], restarted);
      }

      var start = _position;
      var buffer = new byte[(int)Math.Min(length - start, MaxReadBytes)];
      stream.Position = start;
      stream.ReadExactly(buffer);

      var end = Array.LastIndexOf(buffer, (byte)'\n');
      if (end < 0)
      {
        // No complete line yet. A single line longer than a whole read is passed on as it is.
        if (buffer.Length < MaxReadBytes)
        {
          return new LogChunk([], restarted);
        }

        end = buffer.Length - 1;
      }

      var offset = 0;
      if (start > 0 && !restarted && _classifier.IsFresh)
      {
        // Started in the middle of a big file: drop the partial first line.
        offset = Math.Min(Array.IndexOf(buffer, (byte)'\n') + 1, end + 1);
      }

      _position = start + end + 1;
      var text = Encoding.UTF8.GetString(buffer, offset, end + 1 - offset);
      var lines = text.Split('\n')
        .SkipLast(1)
        .Select(line => line.TrimEnd('\r'))
        .Select(line => new LogLine(line, _classifier.Classify(line)))
        .ToList();
      return new LogChunk(lines, restarted);
    }
  }

  private static byte[] ReadHead(FileStream stream, long length)
  {
    var head = new byte[(int)Math.Min(length, HeadBytes)];
    stream.ReadExactly(head);
    return head;
  }

  // The head is compared by prefix: while the first line is still being written it only grows.
  private static bool StartsWith(byte[] current, byte[] previous) =>
    current.AsSpan().StartsWith(previous.AsSpan()) || previous.AsSpan().StartsWith(current.AsSpan());
}

/// <summary>Finds the level of each line in a BepInEx, Unity or LaunchHeim log, for colouring and filtering.</summary>
/// <remarks>
/// BepInEx and LaunchHeim prefix every entry with <c>[Error  : Source]</c>, and the lines after it without
/// a prefix (a stack trace, a multi-line message) belong to that entry. Unity's Player.log has no prefix
/// at all: an exception is recognised by its name, and a blank line ends each entry.
/// </remarks>
public sealed partial class LogClassifier
{
  private LogLevel _last = LogLevel.Info;

  internal bool IsFresh { get; private set; } = true;

  public void Reset()
  {
    _last = LogLevel.Info;
    IsFresh = true;
  }

  public LogLevel Classify(string line)
  {
    IsFresh = false;
    var prefix = Prefix().Match(line);
    if (prefix.Success)
    {
      return _last = prefix.Groups[1].Value.ToLowerInvariant() switch
      {
        "fatal" or "error" => LogLevel.Error,
        "warning" => LogLevel.Warning,
        "debug" => LogLevel.Debug,
        _ => LogLevel.Info,
      };
    }

    if (line.Trim().Length == 0)
    {
      return _last = LogLevel.Info;
    }

    return Exception().IsMatch(line) ? _last = LogLevel.Error : _last;
  }

  [GeneratedRegex(@"^\[(Fatal|Error|Warning|Message|Info|Debug)\s*:", RegexOptions.IgnoreCase)]
  private static partial Regex Prefix();

  [GeneratedRegex(@"^(\w+\.)*\w*Exception\b|^Error\b|^Crash!!!", RegexOptions.IgnoreCase)]
  private static partial Regex Exception();
}
