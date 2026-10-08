using CINE.LaunchHeim.Core.Saves;

namespace CINE.LaunchHeim.Core.Tests;

public class SteamScreenshotsTests : IDisposable
{
  private readonly TempDirectory _temp = new();

  public void Dispose() => _temp.Dispose();

  // userdata is keyed by the 32-bit account id: 76561198066017536 - 76561197960265728.
  private const string Account = "105751808";

  private string ScreenshotFolder(string steam, string account) =>
    Path.Combine(steam, "userdata", account, "760", "remote", "892970", "screenshots");

  private string Steam(params string[] files)
  {
    var steam = _temp.Tree("Steam", files);
    Directory.CreateDirectory(Path.Combine(steam, "config"));
    File.WriteAllText(Path.Combine(steam, "config", "loginusers.vdf"), """
      "users" { "76561198066017536" { "Timestamp" "1791108716" } }
      """);
    return steam;
  }

  [Fact]
  public void Lists_valheims_steam_screenshots_newest_first_with_their_thumbnails()
  {
    const string folder = $"userdata/{Account}/760/remote/892970/screenshots";
    var steam = Steam(
      $"{folder}/20260906005112_1.jpg",
      $"{folder}/20261008204512_1.jpg",
      $"{folder}/20261008204512_2.jpg",
      $"{folder}/thumbnails/20261008204512_1.jpg",
      // Another game's screenshots and Steam's own bookkeeping stay out.
      $"userdata/{Account}/760/remote/105600/screenshots/20261008204600_1.jpg",
      $"userdata/{Account}/760/screenshots.vdf");
    var screenshots = SteamScreenshots.ForCurrentUser([steam]);

    var list = screenshots.List();

    Assert.Equal([ScreenshotFolder(steam, Account)], screenshots.Directories);
    Assert.Equal(["20261008204512_2.jpg", "20261008204512_1.jpg", "20260906005112_1.jpg"], list.Select(s => Path.GetFileName(s.File)));
    Assert.Equal(new DateTime(2026, 10, 8, 20, 45, 12), list[0].TakenAt);
    Assert.Null(list[0].Thumbnail);
    Assert.Equal(Path.Combine(ScreenshotFolder(steam, Account), "thumbnails", "20261008204512_1.jpg"), list[1].Thumbnail);
  }

  [Fact]
  public void Only_the_account_that_logged_in_last_counts()
  {
    var steam = Steam(
      $"userdata/{Account}/760/remote/892970/screenshots/20261008204512_1.jpg",
      "userdata/40000/760/remote/892970/screenshots/20261001120000_1.jpg");

    var list = SteamScreenshots.ForCurrentUser([steam]).List();

    Assert.Equal(["20261008204512_1.jpg"], list.Select(s => Path.GetFileName(s.File)));
  }

  [Fact]
  public void Nothing_is_listed_before_steam_saved_a_screenshot()
  {
    var screenshots = SteamScreenshots.ForCurrentUser([Steam(), _temp.Combine("no-steam")]);

    Assert.Empty(screenshots.Directories);
    Assert.Empty(screenshots.List());
  }

  [Fact]
  public void A_picture_not_named_by_steam_is_dated_by_its_file()
  {
    var file = Path.Combine(_temp.Tree("pictures", "valheim.png"), "valheim.png");
    var written = new DateTime(2026, 10, 1, 18, 30, 0);
    File.SetLastWriteTime(file, written);

    Assert.Equal(written, SteamScreenshots.TakenAt(file));
  }
}
