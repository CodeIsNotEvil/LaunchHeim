using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Servers;

namespace CINE.LaunchHeim.Core.Tests;

public class ServerProfileTests : IDisposable
{
  private readonly TempDirectory _temp = new();

  public void Dispose() => _temp.Dispose();

  [Fact]
  public void A_private_server_needs_no_password()
  {
    var server = new ServerProfile { Name = "Friends", World = "Midgard" };

    Assert.Empty(server.Problems());
  }

  [Theory]
  [InlineData("abcd", "at least 5")]
  [InlineData("Midga", "part of the world")]
  public void A_public_server_follows_valheims_password_rules(string password, string expected)
  {
    var server = new ServerProfile { Name = "Public", World = "Midgard", Public = true, Password = password };

    Assert.Contains(server.Problems(), p => p.Contains(expected, StringComparison.Ordinal));
  }

  [Fact]
  public void The_password_may_be_part_of_the_server_name_since_valheim_only_checks_the_world()
  {
    var server = new ServerProfile { Name = "Secret base", World = "Midgard", Public = true, Password = "Secret" };

    Assert.Empty(server.Problems());
  }

  [Fact]
  public void Bad_world_names_ports_and_presets_are_reported()
  {
    var server = new ServerProfile { Name = "S", World = "a/b", Port = 0, Preset = "nightmare" };

    Assert.Equal(3, server.Problems().Count);
  }

  [Fact]
  public void The_console_port_defaults_to_the_one_after_the_query_port()
  {
    Assert.Equal(2458, new ServerProfile().EffectiveRconPort);
    Assert.Equal(2600, new ServerProfile { RconPort = 2600 }.EffectiveRconPort);
  }

  [Fact]
  public void The_join_address_leaves_out_the_default_port()
  {
    Assert.Equal("127.0.0.1", new ServerProfile().JoinAddress());
    Assert.Equal("127.0.0.1:2500", new ServerProfile { Port = 2500 }.JoinAddress());
  }

  [Fact]
  public void Store_round_trips_profiles_with_unique_ids()
  {
    var store = new ServerStore(_temp.AppPaths);
    var first = store.Create("My Server!");
    var second = store.Create("My Server!");
    first.Port = 2500;
    first.ModChoices["thunderstore:a-b"] = false;
    store.Save(first);

    var loaded = store.LoadAll();

    Assert.Equal(["my-server", "my-server-2"], loaded.Select(s => s.Id).Order());
    var reloaded = loaded.Single(s => s.Id == "my-server");
    Assert.Equal(2500, reloaded.Port);
    Assert.False(reloaded.ModChoices["THUNDERSTORE:A-B"]);
    Assert.Equal(first.RconPassword, reloaded.RconPassword);
    Assert.NotEqual(first.RconPassword, second.RconPassword);
    Assert.Equal(Path.Combine(store.DirectoryOf(first), "saves"), store.SaveDirectoryOf(first));
  }

  [Fact]
  public void Profiles_with_passwords_are_readable_by_the_user_only()
  {
    if (OperatingSystem.IsWindows())
    {
      return;
    }

    var store = new ServerStore(_temp.AppPaths);
    var server = store.Create("S");

    var mode = File.GetUnixFileMode(Path.Combine(store.DirectoryOf(server), ServerStore.ProfileFile));
    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
  }

  [Fact]
  public void Dedicated_server_is_found_in_a_steam_library()
  {
    var steam = _temp.Tree("Steam", "steamapps/libraryfolders.vdf");
    File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), $$"""
      "libraryfolders" { "0" { "path" "{{steam.Replace("\\", "\\\\")}}" } }
      """);
    File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_896660.acf"), """
      "AppState" { "appid" "896660" "installdir" "Valheim dedicated server" }
      """);
    var serverDirectory = Path.Combine(steam, "steamapps", "common", "Valheim dedicated server");
    Directory.CreateDirectory(serverDirectory);
    var dedicated = new DedicatedServer(new SteamLibraryLocator([steam], GamePlatform.Linux), _temp.AppPaths);

    Assert.Null(dedicated.FindInSteam());

    File.WriteAllText(Path.Combine(serverDirectory, "valheim_server.x86_64"), "");
    Assert.Equal(serverDirectory, dedicated.FindInSteam());
    Assert.Equal(serverDirectory, dedicated.DirectoryFor(new ServerProfile { Binary = ServerBinary.Steam }));
    Assert.Null(dedicated.DirectoryFor(new ServerProfile { Binary = ServerBinary.LaunchHeim }));
  }
}
