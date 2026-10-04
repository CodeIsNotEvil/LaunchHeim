using System.Text;
using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Saves;

namespace CINE.LaunchHeim.Core.Tests;

public class ValheimSavesTests : IDisposable
{
  private readonly TempDirectory _temp = new();
  private readonly string _data;
  private readonly string _cloud;

  public ValheimSavesTests()
  {
    _data = _temp.Combine("unity3d", "IronGate", "Valheim");
    _cloud = _temp.Combine("Steam", "userdata", "105751808", "892970", "remote");
    Directory.CreateDirectory(_data);
    Directory.CreateDirectory(_cloud);
  }

  public void Dispose() => _temp.Dispose();

  private ValheimSaves Saves => new(_data, [_cloud]);

  private static void Touch(string path, DateTime? written = null)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, "");
    File.SetLastWriteTimeUtc(path, written ?? DateTime.UtcNow);
  }

  /// <summary>Writes a server list the way LocalServerList.Save does (version 2).</summary>
  private static byte[] ServerList(params (string Type, string Name, Action<BinaryWriter> Data)[] entries) =>
    ServerListOfVersion(2, entries);

  private static byte[] ServerListOfVersion(uint version, params (string Type, string Name, Action<BinaryWriter> Data)[] entries)
  {
    using var package = new MemoryStream();
    using (var writer = new BinaryWriter(package, Encoding.UTF8, leaveOpen: true))
    {
      writer.Write(version);
      writer.Write(entries.Length);
      foreach (var (type, name, data) in entries)
      {
        writer.Write(type);
        writer.Write(name);
        data(writer);
      }
    }

    using var file = new MemoryStream();
    using (var writer = new BinaryWriter(file))
    {
      writer.Write((int)package.Length);
      writer.Write(package.ToArray());
    }

    return file.ToArray();
  }

  private static (string, string, Action<BinaryWriter>) Dedicated(string name, string host, uint port) =>
    ("Dedicated", name, w => { w.Write(host); w.Write(port); });

  private static (string, string, Action<BinaryWriter>) SteamUser(string name) =>
    ("Steam user", name, w => { w.Write(76561198000000000UL); w.Write("Steam_76561198000000000"); });

  [Fact]
  public void Server_lists_give_the_dedicated_servers_and_skip_friends_games()
  {
    var file = ServerList(
      Dedicated("Walheim", "139.162.178.10", 2456),
      SteamUser("A friend [Blyton]"),
      ("PlayFab user", "Crossplay", w => { w.Write("ABCDEF"); w.Write("Xbox_1"); }),
      Dedicated("Home", "valheim.example.org", 2459),
      Dedicated("V6", "2001:db8::1", 2456));

    var servers = ServerListFile.ReadDedicated(file);

    Assert.Equal(
      [new("Walheim", "139.162.178.10:2456"), new("Home", "valheim.example.org:2459"), new("V6", "[2001:db8::1]:2456")],
      servers);
  }

  [Fact]
  public void A_cut_off_server_list_gives_what_could_be_read()
  {
    var file = ServerList(Dedicated("First", "10.0.0.1", 2456), Dedicated("Second", "10.0.0.2", 2456));

    var servers = ServerListFile.ReadDedicated(file[..^6]);

    Assert.Equal([new("First", "10.0.0.1:2456")], servers);
  }

  [Fact]
  public void An_unknown_server_list_version_gives_nothing()
  {
    Assert.Empty(ServerListFile.ReadDedicated(ServerListOfVersion(9, Dedicated("Future", "10.0.0.1", 2456))));
    Assert.Empty(ServerListFile.ReadDedicated([1, 2]));
  }

  [Fact]
  public void Favorites_come_first_and_are_marked_when_also_recent()
  {
    File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Path.Combine(_cloud, "serverlist")).FullName, "favorite"),
      ServerList(Dedicated("Walheim", "10.0.0.1", 2456), Dedicated("Old", "10.0.0.2", 2456)));
    File.WriteAllBytes(Path.Combine(_cloud, "serverlist", "recent"),
      ServerList(Dedicated("Public", "10.0.0.3", 2456), Dedicated("Walheim", "10.0.0.1", 2456)));

    var servers = Saves.Servers();

    Assert.Equal(
      [
        new("Walheim", "10.0.0.1:2456", IsFavorite: true, IsRecent: true),
        new("Old", "10.0.0.2:2456", IsFavorite: true, IsRecent: false),
        new("Public", "10.0.0.3:2456", IsFavorite: false, IsRecent: true),
      ],
      servers);
  }

  [Fact]
  public void The_local_and_cloud_server_lists_are_merged_newest_first()
  {
    var local = Path.Combine(_data, "serverlist_local", "favorite");
    var cloud = Path.Combine(_cloud, "serverlist", "favorite");
    Directory.CreateDirectory(Path.GetDirectoryName(local)!);
    Directory.CreateDirectory(Path.GetDirectoryName(cloud)!);
    File.WriteAllBytes(local, ServerList(Dedicated("Local only", "10.0.0.1", 2456), Dedicated("Renamed", "10.0.0.9", 2456)));
    File.WriteAllBytes(cloud, ServerList(Dedicated("Renamed later", "10.0.0.9", 2456)));
    File.SetLastWriteTimeUtc(local, DateTime.UtcNow.AddDays(-1));

    var names = Saves.Servers().Select(s => s.Name);

    Assert.Equal(["Renamed later", "Local only"], names);
  }

  [Fact]
  public void Characters_come_from_every_location_without_backups_newest_first()
  {
    var now = DateTime.UtcNow;
    Touch(Path.Combine(_cloud, "characters", "bolo hard.fch"), now.AddDays(-1));
    Touch(Path.Combine(_cloud, "characters", "bolo hard.fch.old"));
    Touch(Path.Combine(_cloud, "characters", "bolo hard_backup_auto-20261001-130611.fch"));
    Touch(Path.Combine(_cloud, "characters", "steam_autocloud.vdf"));
    Touch(Path.Combine(_data, "characters_local", "lukaz.fch"), now);
    Touch(Path.Combine(_data, "characters", "olaf.fch"), now.AddDays(-30));

    var characters = Saves.Characters();

    Assert.Equal(["lukaz", "bolo hard", "olaf"], characters.Select(c => c.FileName));
    Assert.Equal([SaveSource.Local, SaveSource.Cloud, SaveSource.Legacy], characters.Select(c => c.Source));
    Assert.Equal("Bolo Hard", characters[1].DisplayName);
  }

  [Fact]
  public void A_character_in_two_places_is_listed_once()
  {
    Touch(Path.Combine(_cloud, "characters", "olaf.fch"), DateTime.UtcNow);
    Touch(Path.Combine(_data, "characters_local", "olaf.fch"), DateTime.UtcNow.AddDays(-1));

    var olaf = Assert.Single(Saves.Characters());

    Assert.Equal(SaveSource.Cloud, olaf.Source);
  }

  [Fact]
  public void Worlds_are_found_in_both_formats_without_backups_or_caches()
  {
    var now = DateTime.UtcNow;
    Touch(Path.Combine(_cloud, "worlds", "Walheim.fwl"), now.AddDays(-9));
    Touch(Path.Combine(_cloud, "worlds", "Walheim.db"), now.AddDays(-2));
    Touch(Path.Combine(_cloud, "worlds", "Walheim_backup_20230828-150827.fwl"));
    Touch(Path.Combine(_cloud, "worlds", "2023Solo.fwl.old"));
    Touch(Path.Combine(_cloud, "worlds", "2023Solo", "_main.16.fwl2"), now.AddDays(-1));
    Touch(Path.Combine(_cloud, "worlds", "2023Solo", "00_01__0_1.chunk"));
    // Valheim keeps minimap caches in a folder named after the world; without _main.* it is no world.
    Touch(Path.Combine(_data, "worlds_local", "SoloHard", "cacheMinimapMeta"));
    Touch(Path.Combine(_data, "worlds_local", "Local", "_main.1.fwl2"), now);

    var worlds = Saves.Worlds();

    Assert.Equal(["Local", "2023Solo", "Walheim"], worlds.Select(w => w.Name));
    Assert.Equal(now.AddDays(-2), worlds[2].LastWriteUtc, TimeSpan.FromSeconds(1));
    Assert.Equal("world:2023solo", worlds[1].Key);
  }

  [Fact]
  public void Nothing_is_found_when_the_game_never_ran()
  {
    var saves = new ValheimSaves(_temp.Combine("missing"), []);

    Assert.Empty(saves.Characters());
    Assert.Empty(saves.Worlds());
    Assert.Empty(saves.Servers());
  }

  [Theory]
  [InlineData("""
    "users"
    {
      "76561198000000001" { "Timestamp" "1700000000" }
      "76561198066017536" { "Timestamp" "1791108716" }
    }
    """, 105751808UL)]
  [InlineData("""
    "users"
    {
      "76561198000000001" { "MostRecent" "1" "Timestamp" "1700000000" }
      "76561198066017536" { "MostRecent" "0" "Timestamp" "1791108716" }
    }
    """, 39734273UL)]
  public void The_cloud_folder_belongs_to_the_last_logged_in_account(string loginUsers, ulong accountId)
  {
    var file = _temp.Combine("loginusers.vdf");
    File.WriteAllText(file, loginUsers);

    Assert.Equal(accountId, ValheimSaves.MostRecentAccountId(file));
  }

  [Fact]
  public void Cloud_folders_come_from_the_steam_root_for_that_account()
  {
    var steam = _temp.Combine("Steam");
    Directory.CreateDirectory(Path.Combine(steam, "config"));
    File.WriteAllText(Path.Combine(steam, "config", "loginusers.vdf"), """
      "users" { "76561198066017536" { "Timestamp" "1791108716" } }
      """);
    Directory.CreateDirectory(Path.Combine(steam, "userdata", "999", "892970", "remote"));

    Assert.Equal([_cloud], ValheimSaves.CloudDirectoriesFor([steam, _temp.Combine("no-steam")]));
  }

  [Fact]
  public void The_data_folder_follows_xdg_config_home_and_flatpak()
  {
    // Path.Combine, like the code: the Windows build runs these tests too, with backslashes.
    Assert.Equal(
      [
        Path.Combine("/cfg", "unity3d", "IronGate", "Valheim"),
        Path.Combine("/home/u", ".var", "app", "com.valvesoftware.Steam", "config", "unity3d", "IronGate", "Valheim"),
      ],
      ValheimSaves.DataDirectories(GamePlatform.Linux, "/home/u", "/cfg"));
    Assert.Equal(
      Path.Combine("/home/u", ".config", "unity3d", "IronGate", "Valheim"),
      ValheimSaves.DataDirectories(GamePlatform.Linux, "/home/u", null)[0]);
  }
}

public class UnityPrefsTests : IDisposable
{
  private readonly TempDirectory _temp = new();

  public void Dispose() => _temp.Dispose();

  // Unity writes the file with LF. A Windows checkout (core.autocrlf) turns the line ends inside these
  // raw strings into CRLF, so they are normalized rather than trusted.
  private static readonly string Prefs = """
    <unity_prefs version_major="1" version_minor="1">
    	<pref name="SwapTriggers" type="int">0</pref>
    	<pref name="profile" type="string">bHVrYXo=</pref>
    	<pref name="MasterVolume" type="float">0.068551</pref>
    </unity_prefs>

    """.ReplaceLineEndings("\n");

  [Fact]
  public void Strings_are_read_from_base64()
  {
    var file = _temp.Combine("prefs");
    File.WriteAllText(file, Prefs);

    var prefs = new FileUnityPrefs(file);

    Assert.Equal("lukaz", prefs.GetString(ValheimPrefs.Character));
    Assert.Null(prefs.GetString(ValheimPrefs.World));
    Assert.Null(prefs.GetString("SwapTriggers"));
  }

  [Fact]
  public void Setting_a_string_changes_only_that_value()
  {
    var file = _temp.Combine("prefs");
    File.WriteAllText(file, Prefs);

    new FileUnityPrefs(file).SetString(ValheimPrefs.Character, "bolo hard");

    Assert.Equal(Prefs.Replace("bHVrYXo=", "Ym9sbyBoYXJk"), File.ReadAllText(file));
    Assert.Equal("bolo hard", new FileUnityPrefs(file).GetString(ValheimPrefs.Character));
  }

  [Fact]
  public void A_missing_key_is_added_before_the_end()
  {
    var file = _temp.Combine("prefs");
    File.WriteAllText(file, Prefs);

    new FileUnityPrefs(file).SetString(ValheimPrefs.World, "Rånheim");

    var text = File.ReadAllText(file);
    Assert.EndsWith("\t<pref name=\"world\" type=\"string\">UsOlbmhlaW0=</pref>\n</unity_prefs>\n", text);
    Assert.Equal("Rånheim", new FileUnityPrefs(file).GetString(ValheimPrefs.World));
    Assert.Equal("lukaz", new FileUnityPrefs(file).GetString(ValheimPrefs.Character));
  }

  [Fact]
  public void An_empty_value_is_replaced()
  {
    var file = _temp.Combine("prefs");
    File.WriteAllText(file, Prefs.Replace("bHVrYXo=", ""));

    new FileUnityPrefs(file).SetString(ValheimPrefs.Character, "olaf");

    Assert.Equal("olaf", new FileUnityPrefs(file).GetString(ValheimPrefs.Character));
  }

  [Fact]
  public void The_file_is_created_when_the_game_never_ran()
  {
    var file = _temp.Combine("unity3d", "IronGate", "Valheim", "prefs");

    new FileUnityPrefs(file).SetString(ValheimPrefs.Character, "olaf");

    Assert.Equal("""
      <unity_prefs version_major="1" version_minor="1">
      	<pref name="profile" type="string">b2xhZg==</pref>
      </unity_prefs>

      """.ReplaceLineEndings("\n"), File.ReadAllText(file));
  }

  [Theory]
  [InlineData("UnityGraphicsQuality", "UnityGraphicsQuality_h1669003810")]
  [InlineData("Screenmanager Resolution Width", "Screenmanager Resolution Width_h182942802")]
  public void Registry_value_names_match_unitys(string key, string valueName) =>
    Assert.Equal(valueName, UnityPrefs.RegistryValueName(key));
}
