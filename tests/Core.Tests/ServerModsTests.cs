using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Servers;

namespace CINE.LaunchHeim.Core.Tests;

public class ServerModsTests : IDisposable
{
  private readonly TempDirectory _temp = new();

  public void Dispose() => _temp.Dispose();

  private static InstalledMod Mod(string key, params string[] files) =>
    new() { Key = key, Name = key, SourceId = key, Files = [.. files] };

  [Theory]
  [InlineData(new[] { "Client-side" }, true)]
  [InlineData(new[] { "Client-side", "Server-side" }, false)]
  [InlineData(new[] { "Server-side" }, false)]
  [InlineData(new string[0], false)]
  public void Only_mods_listed_as_client_side_alone_stay_off_the_server(string[] categories, bool clientOnly)
  {
    Assert.Equal(clientOnly, ServerMods.IsClientOnly(categories));
  }

  [Fact]
  public void Unknown_categories_count_as_a_server_mod()
  {
    Assert.False(ServerMods.IsClientOnly(null));
  }

  [Fact]
  public void Entries_follow_the_categories_unless_the_player_chose()
  {
    var instance = new Instance
    {
      Mods =
      [
        Mod("thunderstore:a-Minimap") , Mod("thunderstore:b-Valheim_Plus"), Mod("thunderstore:c-Hud"),
        new InstalledMod { Key = "thunderstore:denikson-BepInExPack_Valheim", IsLoader = true },
        new InstalledMod { Key = "thunderstore:d-Off", Enabled = false },
      ],
    };
    var server = new ServerProfile { ModChoices = { ["thunderstore:c-Hud"] = true } };

    var entries = ServerMods.Entries(instance, server, m => m.Key.Contains("Plus") ? ["Server-side", "Client-side"] : ["Client-side"]);

    Assert.Equal(["thunderstore:a-Minimap", "thunderstore:b-Valheim_Plus", "thunderstore:c-Hud"], entries.Select(e => e.Mod.Key));
    Assert.Equal([false, true, true], entries.Select(e => e.Included));
    Assert.Equal([true, false, true], entries.Select(e => e.ClientOnly));
  }

  [Fact]
  public void Sync_copies_the_mods_and_removes_what_left_the_instance()
  {
    var instance = _temp.Tree("instance", "BepInEx/plugins/A/a.dll", "BepInEx/plugins/B/b.dll", "BepInEx/core/BepInEx.Preloader.dll");
    var server = _temp.Tree("server");
    var loader = Mod("loader", "BepInEx/core/BepInEx.Preloader.dll");
    var a = Mod("a", "BepInEx/plugins/A/a.dll");
    var b = Mod("b", "BepInEx/plugins/B/b.dll");

    var first = ServerMods.Sync(instance, [loader, a, b], server, replaceConfigs: false);
    Assert.Equal(3, first.Copied);
    Assert.True(File.Exists(Path.Combine(server, "BepInEx", "plugins", "B", "b.dll")));

    var unchanged = ServerMods.Sync(instance, [loader, a, b], server, replaceConfigs: false);
    Assert.Equal(0, unchanged.Copied);

    File.WriteAllText(Path.Combine(server, "BepInEx", "plugins", "mine.dll"), "the admin's own");
    var second = ServerMods.Sync(instance, [loader, a], server, replaceConfigs: false);
    Assert.Equal(1, second.Removed);
    Assert.False(File.Exists(Path.Combine(server, "BepInEx", "plugins", "B", "b.dll")));
    // Never synced, so never removed.
    Assert.True(File.Exists(Path.Combine(server, "BepInEx", "plugins", "mine.dll")));
  }

  [Fact]
  public void Configs_are_only_added_unless_asked_to_replace_them()
  {
    var instance = _temp.Tree("instance", "BepInEx/config/a.cfg", "BepInEx/config/sub/b.cfg");
    var server = _temp.Tree("server", "BepInEx/config/a.cfg");
    var serverConfig = Path.Combine(server, "BepInEx", "config", "a.cfg");
    File.WriteAllText(serverConfig, "tuned on the server");
    var a = Mod("a", "BepInEx/config/a.cfg");

    var sync = ServerMods.Sync(instance, [a], server, replaceConfigs: false);
    Assert.Equal("tuned on the server", File.ReadAllText(serverConfig));
    Assert.Equal(1, sync.ConfigsCopied);
    Assert.True(File.Exists(Path.Combine(server, "BepInEx", "config", "sub", "b.cfg")));

    ServerMods.Sync(instance, [a], server, replaceConfigs: true);
    Assert.Equal("BepInEx/config/a.cfg", File.ReadAllText(serverConfig));

    // A config is never removed with its mod.
    ServerMods.Sync(instance, [], server, replaceConfigs: false);
    Assert.True(File.Exists(serverConfig));
  }

  [Fact]
  public void Plugin_config_carries_the_rcon_settings()
  {
    var server = new ServerProfile { Port = 2500, RconPassword = "pw123" };
    var plugin = _temp.Tree("bundle", ServerPluginInstaller.PluginFileName);
    var folder = _temp.Tree("server");

    ServerPluginInstaller.Install(folder, server, bundledPlugin: Path.Combine(plugin, ServerPluginInstaller.PluginFileName));

    Assert.True(File.Exists(Path.Combine(folder, ServerPluginInstaller.PluginDirectory, ServerPluginInstaller.PluginFileName)));
    var config = File.ReadAllText(Path.Combine(folder, ServerPluginInstaller.ConfigPath));
    Assert.Contains("Port = 2502\n", config, StringComparison.Ordinal);
    Assert.Contains("Password = pw123\n", config, StringComparison.Ordinal);
    Assert.Contains("Address = 127.0.0.1\n", config, StringComparison.Ordinal);

    ServerPluginInstaller.Remove(folder);
    Assert.False(Directory.Exists(Path.Combine(folder, ServerPluginInstaller.PluginDirectory)));
  }

  [Theory]
  [InlineData(" Update state (0x61) downloading, progress: 42.17 (123 / 456)", "Downloading the server", 0.4217)]
  [InlineData(" Update state (0x81) verifying update, progress: 100.00 (1 / 1)", "Checking the server's files", 1.0)]
  public void SteamCmd_progress_is_read_from_its_output(string line, string stage, double fraction)
  {
    var progress = SteamCmd.ParseProgress(line);

    Assert.Equal(stage, progress?.Stage);
    Assert.Equal(fraction, progress?.Fraction ?? -1, 4);
  }

  [Fact]
  public void SteamCmd_installs_the_chosen_platforms_build_anonymously()
  {
    var arguments = SteamCmd.Arguments(_temp.Combine("server"), Game.GamePlatform.Windows);

    Assert.Equal(["+@sSteamCmdForcePlatformType", "windows"], arguments.Take(2));
    Assert.True(arguments.ToList().IndexOf("+force_install_dir") < arguments.ToList().IndexOf("+login"));
    Assert.Equal(["+login", "anonymous", "+app_update", "896660", "validate", "+quit"], arguments.Skip(4));
  }
}
