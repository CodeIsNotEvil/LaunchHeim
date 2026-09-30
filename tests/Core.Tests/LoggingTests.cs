using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Logging;

namespace CINE.LaunchHeim.Core.Tests;

public class LogFollowerTests : IDisposable
{
  private readonly TempDirectory _temp = new();

  public void Dispose() => _temp.Dispose();

  private string LogFile => _temp.Combine("LogOutput.log");

  [Fact]
  public void A_missing_file_is_empty_until_it_appears()
  {
    var follower = new LogFollower(LogFile);
    Assert.Empty(follower.Read().Lines);

    File.WriteAllText(LogFile, "[Info   :   BepInEx] Loading\n");

    Assert.Equal(["[Info   :   BepInEx] Loading"], follower.Read().Lines.Select(l => l.Text));
  }

  [Fact]
  public void Only_new_complete_lines_are_returned()
  {
    File.WriteAllText(LogFile, "one\ntw");
    var follower = new LogFollower(LogFile);

    Assert.Equal(["one"], follower.Read().Lines.Select(l => l.Text));

    File.AppendAllText(LogFile, "o\r\nthree\n");

    Assert.Equal(["two", "three"], follower.Read().Lines.Select(l => l.Text));
    Assert.Empty(follower.Read().Lines);
  }

  [Fact]
  public void A_truncated_file_is_a_new_session()
  {
    File.WriteAllText(LogFile, "[Message:   BepInEx] BepInEx 5.4 (1 Oct 2026 10:00)\nfirst session, a long line\n");
    var follower = new LogFollower(LogFile);
    follower.Read();

    File.WriteAllText(LogFile, "[Message:   BepInEx] BepInEx 5.4 (1 Oct 2026 11:00)\n");
    var chunk = follower.Read();

    Assert.True(chunk.Restarted);
    Assert.Equal(["[Message:   BepInEx] BepInEx 5.4 (1 Oct 2026 11:00)"], chunk.Lines.Select(l => l.Text));
  }

  [Fact]
  public void A_rewritten_file_that_grew_is_a_new_session_too()
  {
    File.WriteAllText(LogFile, "[Message:   BepInEx] session 1\n");
    var follower = new LogFollower(LogFile);
    follower.Read();

    File.WriteAllText(LogFile, "[Message:   BepInEx] session 2 with a longer first line\nand more\n");

    Assert.True(follower.Read().Restarted);
  }

  [Fact]
  public void A_big_file_starts_at_its_tail_without_a_partial_line()
  {
    File.WriteAllText(LogFile, string.Concat(Enumerable.Range(0, 100).Select(i => $"line {i:000}\n")));
    var follower = new LogFollower(LogFile, maxInitialBytes: 20);

    var lines = follower.Read().Lines.Select(l => l.Text).ToList();

    Assert.Equal(["line 098", "line 099"], lines);
  }

  // Unity's own lines inside BepInEx's log keep the entry's level, so a warning's details stay yellow.
  [Fact]
  public void Levels_come_from_the_prefix_and_carry_over_to_stack_traces()
  {
    File.WriteAllText(LogFile, string.Join('\n',
      "[Info   :   BepInEx] Loading [Jotunn 2.20]",
      "[Error  : Unity Log] NullReferenceException: Object reference not set",
      "Stack trace:",
      "  at Player.Update () [0x00000] in <abc>:0",
      "[Warning:   BepInEx] Skipping plugin",
      "Initialize engine version: 2022.3",
      "NullReferenceException: from Player.log",
      "(Filename: ./Runtime/Export/Debug.cs Line: 35)",
      "",
      "Unloading 5 unused Assets",
      ""));

    var levels = new LogFollower(LogFile).Read().Lines.Select(l => l.Level).ToList();

    Assert.Equal(
      [
        LogLevel.Info, LogLevel.Error, LogLevel.Error, LogLevel.Error, LogLevel.Warning, LogLevel.Warning,
        LogLevel.Error, LogLevel.Error, LogLevel.Info, LogLevel.Info,
      ],
      levels);
  }

  [Fact]
  public void Stack_trace_lines_continue_their_entry()
  {
    File.WriteAllText(LogFile, "[Error  : Unity Log] NullReferenceException\nStack trace:\n  at Player.Update ()\n[Info   :   BepInEx] next\n");

    var continues = new LogFollower(LogFile).Read().Lines.Select(l => l.Continues).ToList();

    Assert.Equal([false, true, true, false], continues);
  }

  [Fact]
  public void LaunchHeims_own_lines_classify_like_bepinex_ones()
  {
    var classifier = new LogClassifier();

    Assert.Equal(LogLevel.Error, classifier.Classify(Log.Format(DateTimeOffset.Now, LogLevel.Error, "Could not start Valheim")));
    Assert.Equal(LogLevel.Warning, classifier.Classify(Log.Format(DateTimeOffset.Now, LogLevel.Warning, "Heads up")));
    Assert.Equal(LogLevel.Info, classifier.Classify(Log.Format(DateTimeOffset.Now, LogLevel.Info, "Started")));
  }
}

public class UnityPlayerLogTests
{
  // Built with Path.Combine like the code, so the Linux layout is also checked on the Windows runner.
  [Fact]
  public void Linux_looks_in_the_config_folder_and_the_flatpak()
  {
    var candidates = UnityPlayerLog.Candidates(GamePlatform.Linux, "/home/v", xdgConfigHome: null);

    Assert.Equal(
      [
        Path.Combine("/home/v", ".config", "unity3d", "IronGate", "Valheim", "Player.log"),
        Path.Combine("/home/v", ".var", "app", "com.valvesoftware.Steam", "config", "unity3d", "IronGate", "Valheim", "Player.log"),
      ],
      candidates);
  }

  [Fact]
  public void A_relative_xdg_config_home_is_ignored()
  {
    Assert.StartsWith(Path.Combine("/home/v", ".config", "unity3d"), UnityPlayerLog.Candidates(GamePlatform.Linux, "/home/v", "relative")[0]);
    Assert.StartsWith(Path.Combine("/xdg", "unity3d"), UnityPlayerLog.Candidates(GamePlatform.Linux, "/home/v", "/xdg")[0]);
  }

  [Fact]
  public void The_newest_existing_log_wins()
  {
    using var temp = new TempDirectory();
    var old = temp.Combine("a.log");
    var recent = temp.Combine("b.log");
    File.WriteAllText(old, "");
    File.WriteAllText(recent, "");
    File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-1));

    Assert.Equal(recent, UnityPlayerLog.Find([temp.Combine("missing.log"), old, recent]));
    Assert.Equal(temp.Combine("missing.log"), UnityPlayerLog.Find([temp.Combine("missing.log")]));
  }
}

public class BepInExConfigTests : IDisposable
{
  private readonly TempDirectory _temp = new();

  public void Dispose() => _temp.Dispose();

  private string Config => _temp.Combine("BepInEx", "config", "BepInEx.cfg");

  [Fact]
  public void The_console_is_switched_on_in_place_keeping_everything_else()
  {
    Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
    File.WriteAllText(Config, """
      [Caching]

      ## Enable/disable assembly metadata cache
      Enabled = true

      [Logging.Console]

      ## Enables showing a console for log output.
      # Setting type: Boolean
      # Default value: false
      Enabled = false

      [Logging.Disk]
      Enabled = true
      """);

    Assert.False(BepInExConfig.IsConsoleEnabled(_temp.Path));
    BepInExConfig.SetConsoleEnabled(_temp.Path, true);

    Assert.True(BepInExConfig.IsConsoleEnabled(_temp.Path));
    var lines = File.ReadAllLines(Config);
    Assert.Equal("Enabled = true", lines[3]);
    Assert.Equal("Enabled = true", lines[10]);
    Assert.Equal("## Enables showing a console for log output.", lines[7]);
    Assert.Equal("true", BepInExConfig.Get(_temp.Path, "Logging.Disk", "Enabled"));
  }

  [Fact]
  public void A_missing_file_gets_just_the_one_entry()
  {
    BepInExConfig.SetConsoleEnabled(_temp.Path, true);

    Assert.Equal(["[Logging.Console]", "", "Enabled = true"], File.ReadAllLines(Config));
  }

  [Fact]
  public void A_missing_section_is_appended()
  {
    Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
    File.WriteAllText(Config, "[Caching]\nEnabled = true\n");

    BepInExConfig.SetConsoleEnabled(_temp.Path, true);

    Assert.Equal(["[Caching]", "Enabled = true", "", "[Logging.Console]", "", "Enabled = true"], File.ReadAllLines(Config));
    Assert.True(BepInExConfig.IsConsoleEnabled(_temp.Path));
  }
}
