using System.IO.Compression;
using CINE.LaunchHeim.Core.Servers;
using CINE.LaunchHeim.Core.Servers.Backups;

namespace CINE.LaunchHeim.Core.Tests;

public class ServerBackupTests : IDisposable
{
  private readonly TempDirectory _temp = new();

  public void Dispose() => _temp.Dispose();

  // A save folder the way Valheim 1.0.21's server leaves it (from the 2026-11-06 run), with an older
  // world beside it and one of Valheim's own backups.
  private string Saves() => _temp.Tree("saves",
    "worlds_local/Midgard/_main.4.fwl2",
    "worlds_local/Midgard/_main.4.db2",
    "worlds_local/Midgard/_main.4.ok",
    "worlds_local/Midgard/00_00__0_3.chunk",
    "worlds_local/Old.fwl",
    "worlds_local/Old.db",
    "worlds_local/Old_backup_auto-20261101.db",
    "adminlist.txt",
    "bannedlist.txt");

  private string ServerFolder() => _temp.Tree("server",
    "BepInEx/config/BepInEx.cfg",
    "BepInEx/config/mod.cfg",
    "BepInEx/config/LaunchHeim.Server/status.json");

  private static List<string> Entries(string zip)
  {
    using var archive = ZipFile.OpenRead(zip);
    return archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToList();
  }

  [Fact]
  public async Task A_backup_holds_the_worlds_lists_and_configs_but_not_valheims_own_backups()
  {
    var zip = _temp.Combine("backup.zip");
    BackupManifest manifest;
    await using (var output = File.Create(zip))
    {
      manifest = await ServerBackup.CreateAsync(new LocalServerFiles(Saves()), new LocalServerFiles(ServerFolder()), null, "Friday", "test", false, output, null, default);
    }

    Assert.Equal(
      [
        "BepInEx/config/BepInEx.cfg", "BepInEx/config/mod.cfg", ServerBackup.ManifestName,
        "saves/adminlist.txt", "saves/bannedlist.txt",
        "saves/worlds_local/Midgard/00_00__0_3.chunk", "saves/worlds_local/Midgard/_main.4.db2",
        "saves/worlds_local/Midgard/_main.4.fwl2", "saves/worlds_local/Midgard/_main.4.ok",
        "saves/worlds_local/Old.db", "saves/worlds_local/Old.fwl",
      ],
      Entries(zip));
    Assert.Equal(["Midgard", "Old"], manifest.Worlds);
    Assert.Equal("Friday", ServerBackup.ReadManifest(zip)!.Server);
  }

  [Fact]
  public async Task Only_the_servers_world_when_the_save_folder_is_shared()
  {
    var zip = _temp.Combine("backup.zip");
    await using (var output = File.Create(zip))
    {
      await ServerBackup.CreateAsync(new LocalServerFiles(Saves()), null, "Midgard", "Friday", "test", false, output, null, default);
    }

    Assert.DoesNotContain(Entries(zip), e => e.Contains("Old", StringComparison.Ordinal) || e.StartsWith("BepInEx", StringComparison.Ordinal));
  }

  [Fact]
  public async Task No_worlds_is_an_error_not_an_empty_backup()
  {
    await using var output = new MemoryStream();

    await Assert.ThrowsAsync<BackupException>(() => ServerBackup.CreateAsync(new LocalServerFiles(_temp.Tree("empty")), null, null, "Friday", "test", false, output, null, default));
  }

  [Fact]
  public async Task A_restore_replaces_the_world_and_drops_a_newer_save()
  {
    var saves = Saves();
    var zip = _temp.Combine("backup.zip");
    await using (var output = File.Create(zip))
    {
      await ServerBackup.CreateAsync(new LocalServerFiles(saves), null, "Midgard", "Friday", "test", false, output, null, default);
    }

    // Played on: a newer save Valheim would load instead of the restored one, and a changed list.
    File.WriteAllText(Path.Combine(saves, "worlds_local", "Midgard", "_main.5.db2"), "newer");
    File.WriteAllText(Path.Combine(saves, "worlds_local", "Midgard", "_main.4.db2"), "changed");
    File.WriteAllText(Path.Combine(saves, "adminlist.txt"), "changed");

    var report = await ServerBackup.RestoreAsync(zip, new LocalServerFiles(saves), null, _temp.Combine("tmp"), null, default);

    Assert.Equal(["Midgard"], report.Worlds);
    Assert.False(File.Exists(Path.Combine(saves, "worlds_local", "Midgard", "_main.5.db2")));
    Assert.Equal("worlds_local/Midgard/_main.4.db2", File.ReadAllText(Path.Combine(saves, "worlds_local", "Midgard", "_main.4.db2")));
    Assert.Equal("adminlist.txt", File.ReadAllText(Path.Combine(saves, "adminlist.txt")));
    // Another world in the folder isn't touched.
    Assert.True(File.Exists(Path.Combine(saves, "worlds_local", "Old.db")));
  }

  [Fact]
  public async Task A_zip_with_an_escaping_path_is_refused()
  {
    var zip = _temp.Combine("evil.zip");
    using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
    {
      archive.CreateEntry("saves/../../.bashrc");
    }

    await Assert.ThrowsAsync<BackupException>(() => ServerBackup.RestoreAsync(zip, new LocalServerFiles(Saves()), null, _temp.Combine("tmp"), null, default));
  }

  [Fact]
  public void Names_carry_the_time_and_tell_servers_apart()
  {
    var at = new DateTimeOffset(2026, 11, 7, 13, 5, 9, TimeSpan.FromHours(1));
    var name = ServerBackup.FileName("friday-night", at);

    Assert.Equal("friday-night-20261107-120509.zip", name);
    Assert.Equal((at.ToUniversalTime(), (string?)null), ServerBackup.TimeOf("friday-night", name));
    Assert.Equal("before-restore", ServerBackup.TimeOf("friday-night", ServerBackup.FileName("friday-night", at, "before-restore"))!.Value.Label);
    Assert.Null(ServerBackup.TimeOf("friday", name));
    Assert.Null(ServerBackup.TimeOf("friday-night", "notes.zip"));
  }

  [Fact]
  public void Retention_keeps_the_newest_and_one_per_recent_day()
  {
    var now = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    // Every 6 hours for 30 days, plus a safety copy from long ago.
    var backups = Enumerable.Range(0, 120)
      .Select(i => now.AddHours(-6 * i))
      .Select(at => new BackupEntry(ServerBackup.FileName("s", at), at, 1, null, true, false))
      .Append(new BackupEntry("s-20250101-000000-before-restore.zip", now.AddYears(-1), 1, "before-restore", true, false))
      .ToList();

    var removed = BackupRetention.ToRemove(backups, now, keepLast: 4, keepDays: 7);
    var kept = backups.Except(removed).ToList();

    // The newest 4 (three from today, one from yesterday), one each for the 5 days before those within
    // the 7 days (today counts), and the safety copy.
    Assert.Equal(4 + 5 + 1, kept.Count);
    Assert.DoesNotContain(removed, b => b.Label is not null);
    Assert.All(kept.Where(b => b.Label is null), b => Assert.True(now - b.CreatedAt < TimeSpan.FromDays(7)));
  }

  [Fact]
  public void Rclone_listings_are_read_without_folders()
  {
    var list = RcloneBackupTarget.ParseList("""
      [
      {"Path":"friday-20261107-120000.zip","Name":"friday-20261107-120000.zip","Size":12345,"MimeType":"application/zip","ModTime":"2026-11-07T12:00:01Z","IsDir":false},
      {"Path":"old","Name":"old","Size":-1,"IsDir":true}
      ]
      """);

    Assert.Equal([("friday-20261107-120000.zip", 12345L)], list);
  }

  [Fact]
  public void Proton_drive_listings_take_names_as_text_or_results()
  {
    var list = ProtonDriveBackupTarget.ParseList("""
      [
      {"uid":"a","name":{"ok":true,"value":"friday-20261107-120000.zip"},"type":"file","activeRevision":{"claimedSize":2048}},
      {"uid":"b","name":"friday-20261106-120000.zip","type":"file","claimedSize":1024},
      {"uid":"c","name":{"ok":true,"value":"Sub"},"type":"folder"}
      ]
      """);

    Assert.Equal([("friday-20261107-120000.zip", 2048L), ("friday-20261106-120000.zip", 1024L)], list);
  }

  [Fact]
  public async Task Folder_targets_keep_backups_as_files()
  {
    var target = new FolderBackupTarget(_temp.Combine("nas", "valheim"));
    var zip = _temp.Combine("x.zip");
    File.WriteAllText(zip, "zip");

    await target.PutAsync(zip, "friday-20261107-120000.zip", default);
    Assert.Equal([("friday-20261107-120000.zip", 3L)], await target.ListAsync(default));
    await target.GetAsync("friday-20261107-120000.zip", _temp.Combine("back.zip"), default);
    Assert.Equal("zip", File.ReadAllText(_temp.Combine("back.zip")));
    await target.RemoveAsync("friday-20261107-120000.zip", default);
    Assert.Empty(await target.ListAsync(default));
  }
}
