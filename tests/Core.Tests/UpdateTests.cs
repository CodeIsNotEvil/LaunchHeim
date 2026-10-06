using CINE.LaunchHeim.Core.Updates;

namespace CINE.LaunchHeim.Core.Tests;

public class UpdateCheckerTests
{
  // The shape of GitHub's /releases answer, trimmed to the fields used, newest published first.
  private const string Releases = """
    [
      { "tag_name": "launchheim-v0.4.3", "draft": false, "prerelease": false, "html_url": "https://example/0.4.3", "assets": [] },
      { "tag_name": "grocerytracker-v0.9.0", "draft": false, "prerelease": false, "html_url": "https://example/gt", "assets": [] },
      { "tag_name": "launchheim-v0.7.0", "draft": true, "prerelease": false, "html_url": "https://example/draft", "assets": [] },
      { "tag_name": "launchheim-v0.6.0-rc1", "draft": false, "prerelease": true, "html_url": "https://example/rc", "assets": [] },
      { "tag_name": "launchheim-v0.5.2", "draft": false, "prerelease": false, "html_url": "https://example/0.5.2",
        "assets": [
          { "name": "LaunchHeim-0.5.2-win-x64.zip", "browser_download_url": "https://example/LaunchHeim-0.5.2-win-x64.zip" },
          { "name": "launchheim_0.5.2-1_amd64.deb", "browser_download_url": "https://example/launchheim_0.5.2-1_amd64.deb" }
        ] },
      { "tag_name": "launchheim-v0.5.1", "draft": false, "prerelease": false, "html_url": "https://example/0.5.1", "assets": [] }
    ]
    """;

  [Fact]
  public void The_newest_published_LaunchHeim_release_is_offered()
  {
    var update = UpdateChecker.Newer(Releases, "0.5.1")!;

    // Not 0.4.3 (published later, but older), not another app's, not a draft or pre-release.
    Assert.Equal("0.5.2", update.Version);
    Assert.Equal("https://example/0.5.2", update.ReleaseUrl);
    Assert.Equal("launchheim_0.5.2-1_amd64.deb", update.Asset("_amd64.deb")!.Name);
  }

  [Theory]
  [InlineData("0.5.2")]
  [InlineData("0.6.0")]
  public void Nothing_is_offered_when_this_version_is_as_new(string current) =>
    Assert.Null(UpdateChecker.Newer(Releases, current));

  [Fact]
  public void No_LaunchHeim_release_means_no_update() =>
    Assert.Null(UpdateChecker.Newer("""[{ "tag_name": "grocerytracker-v1.0.0", "draft": false, "prerelease": false }]""", "0.1.0"));
}

public class InstallDetectionTests
{
  private static readonly string Home = Path.Combine(Path.GetTempPath(), "home");

  [Theory]
  [InlineData("NAME=\"CachyOS Linux\"\nID=cachyos\nID_LIKE=arch\n", InstallKind.Arch)]
  [InlineData("ID=arch\n", InstallKind.Arch)]
  [InlineData("ID=ubuntu\nID_LIKE=debian\n", InstallKind.Debian)]
  [InlineData("ID=\"linuxmint\"\nID_LIKE=\"ubuntu debian\"\n", InstallKind.Debian)]
  [InlineData("ID=fedora\n", InstallKind.Fedora)]
  [InlineData("ID=\"rocky\"\nID_LIKE=\"rhel centos fedora\"\n", InstallKind.Fedora)]
  [InlineData("ID=nixos\n", InstallKind.Unknown)]
  public void A_package_install_is_told_apart_by_os_release(string osRelease, InstallKind expected) =>
    Assert.Equal(expected, InstallDetection.Detect(false, true, osRelease, "/usr/lib/launchheim", Home));

  [Fact]
  public void A_package_whose_os_release_cant_be_read_is_unknown() =>
    Assert.Equal(InstallKind.Unknown, InstallDetection.Detect(false, true, null, "/usr/lib/launchheim", Home));

  [Fact]
  public void Install_sh_is_recognised_by_its_folder()
  {
    var folder = Path.Combine(Home, ".local", "opt", "LaunchHeim");
    Assert.Equal(InstallKind.InstallScript, InstallDetection.Detect(false, false, "ID=arch", folder + Path.DirectorySeparatorChar, Home));
    // A build run from a checkout (dotnet run) isn't something install.sh can update.
    Assert.Equal(InstallKind.Unknown, InstallDetection.Detect(false, false, "ID=arch", Path.Combine(Home, "repos", "bin"), Home));
  }

  [Fact]
  public void Windows_is_always_the_zip() =>
    Assert.Equal(InstallKind.Windows, InstallDetection.Detect(true, false, null, Path.Combine(Home, "LaunchHeim"), Home));
}

public class UpdateCommandsTests
{
  private static readonly AvailableUpdate Update = new("0.5.2", "https://example/0.5.2",
  [
    new("launchheim-git-0.5.2.r160.gabc1234-1-x86_64.pkg.tar.zst", "https://example/arch.pkg.tar.zst"),
    new("launchheim_0.5.2-1_amd64.deb", "https://example/deb"),
    new("launchheim-0.5.2-1.x86_64.rpm", "https://example/rpm"),
    new("LaunchHeim-0.5.2-win-x64.zip", "https://example/win.zip"),
  ]);

  [Fact]
  public void Packages_are_downloaded_then_installed_from_the_file()
  {
    Assert.Equal(["curl -LO https://example/arch.pkg.tar.zst", "sudo pacman -U ./launchheim-git-0.5.2.r160.gabc1234-1-x86_64.pkg.tar.zst"],
      UpdateCommands.For(InstallKind.Arch, Update, "/usr/lib/launchheim"));
    Assert.Equal(["curl -LO https://example/deb", "sudo apt install ./launchheim_0.5.2-1_amd64.deb"],
      UpdateCommands.For(InstallKind.Debian, Update, "/usr/lib/launchheim"));
    Assert.Equal(["curl -LO https://example/rpm", "sudo dnf install ./launchheim-0.5.2-1.x86_64.rpm"],
      UpdateCommands.For(InstallKind.Fedora, Update, "/usr/lib/launchheim"));
  }

  [Fact]
  public void Windows_copies_the_new_files_over_its_own_folder()
  {
    // Built with Path.Combine so the test means the same on the Linux and the Windows runner.
    var folder = Path.Combine(Path.GetTempPath(), "Tools", "LH");
    var commands = UpdateCommands.For(InstallKind.Windows, Update, folder + Path.DirectorySeparatorChar);

    Assert.Contains(commands, c => c.StartsWith("Invoke-WebRequest -Uri \"https://example/win.zip\"", StringComparison.Ordinal));
    Assert.EndsWith($"-Destination \"{folder}\" -Recurse -Force", commands[^1]);
  }

  [Fact]
  public void No_commands_without_the_file_or_the_system()
  {
    var bare = Update with { Assets = [] };
    Assert.Empty(UpdateCommands.For(InstallKind.Arch, bare, "/usr/lib/launchheim"));
    Assert.Empty(UpdateCommands.For(InstallKind.Unknown, Update, "/usr/lib/launchheim"));
  }
}
