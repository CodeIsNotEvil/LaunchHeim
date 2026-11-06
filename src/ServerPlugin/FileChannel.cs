#nullable enable
using System;
using System.IO;
using System.Linq;
using BepInEx.Logging;

namespace CINE.LaunchHeim.ServerPlugin;

/// <summary>Commands as files, for servers that are only reachable over FTP (decision 0020).</summary>
/// <remarks>
/// LaunchHeim writes <c>commands/&lt;id&gt;.cmd</c> with one command line; this runs it, deletes it and
/// writes the answer to <c>responses/&lt;id&gt;.txt</c>. Ids sort by time, so commands run in the order
/// they were sent. Answers are written under a temporary name and renamed, so an FTP download never gets
/// half of one. Core's <c>ServerCommandChannel</c> is the other side and has the same folder names.
/// </remarks>
internal sealed class FileChannel(string configDirectory, ManualLogSource logger)
{
  /// <summary>How often to look, in seconds. Core's ServerCommandChannel.PluginPollInterval matches it.</summary>
  private const float PollSeconds = 2f;

  /// <summary>An answer nobody picked up within this time is deleted; the console that asked has given up.</summary>
  private static readonly TimeSpan AnswerLifetime = TimeSpan.FromMinutes(10);

  private float _nextPoll;

  public string Directory { get; } = Path.Combine(configDirectory, "LaunchHeim.Server");

  private string Commands => Path.Combine(Directory, "commands");

  private string Responses => Path.Combine(Directory, "responses");

  public void Poll(float now, Func<string, string> run)
  {
    if (now < _nextPoll)
    {
      return;
    }

    _nextPoll = now + PollSeconds;
    try
    {
      System.IO.Directory.CreateDirectory(Commands);
      System.IO.Directory.CreateDirectory(Responses);
      foreach (var file in System.IO.Directory.GetFiles(Commands, "*.cmd").OrderBy(f => f, StringComparer.Ordinal))
      {
        var line = File.ReadAllText(file).Trim();
        File.Delete(file);
        var id = Path.GetFileNameWithoutExtension(file);
        logger.LogInfo($"Command from file: {line}");
        Write(Path.Combine(Responses, id + ".txt"), run(line));
      }

      foreach (var answer in System.IO.Directory.GetFiles(Responses, "*.txt"))
      {
        if (DateTime.UtcNow - File.GetLastWriteTimeUtc(answer) > AnswerLifetime)
        {
          File.Delete(answer);
        }
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      // An upload still in progress, or a host that locks files for a moment; the next poll tries again.
      logger.LogDebug($"File channel: {ex.Message}");
    }
  }

  private static void Write(string path, string text)
  {
    var temp = path + ".tmp";
    File.WriteAllText(temp, text);
    // .NET Framework's File.Move doesn't overwrite.
    if (File.Exists(path))
    {
      File.Delete(path);
    }

    File.Move(temp, path);
  }
}
