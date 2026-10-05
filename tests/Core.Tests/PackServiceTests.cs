using System.IO.Compression;
using CINE.LaunchHeim.Core.Catalogs.CurseForge;
using CINE.LaunchHeim.Core.Catalogs.Nexus;
using CINE.LaunchHeim.Core.Catalogs.Thunderstore;
using CINE.LaunchHeim.Core.Downloads;
using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Mods;
using CINE.LaunchHeim.Core.Packs;

namespace CINE.LaunchHeim.Core.Tests;

public class PackServiceTests : IDisposable
{
  private readonly TempDirectory _temp = new();
  private readonly FakeHttp _http = new();
  private readonly ThunderstoreIndex _index;
  private readonly InstanceStore _store;
  private readonly ModService _mods;
  private readonly PackService _packs;
  private readonly Dictionary<string, List<(string Version, string[] Dependencies)>> _published = [];

  public PackServiceTests()
  {
    var client = new HttpClient(_http);
    var paths = _temp.AppPaths;
    _index = new ThunderstoreIndex(client, paths);
    _store = new InstanceStore(paths);
    var catalogs = new CatalogRegistry(
      new ThunderstoreCatalog(client, _index),
      new NexusCatalog(client, () => null),
      new CurseForgeCatalog(client, () => null));
    _mods = new ModService(_store, new ModInstaller(), new ModDownloader(client, paths), catalogs, paths);
    _packs = new PackService(_store, _mods, paths);

    Publish("denikson-BepInExPack_Valheim", "5.4.2202", loader: true);
    Publish("denikson-BepInExPack_Valheim", "5.4.2351", loader: true);
    Publish("Author-Library", "1.0.0");
    Publish("Author-Library", "1.1.0");
    Publish("Author-Mod", "1.0.0", dependencies: ["Author-Library-1.0.0"]);
    Publish("Author-Other", "2.0.0");
    _index.Use(
      _published.Select(p => ThunderstoreSearchTests.Package(p.Key) with
      {
        Versions = p.Value.Select(v => new ThunderstoreVersion(v.Version, v.Dependencies, 0, DateTimeOffset.UnixEpoch, 0)).ToArray(),
      }),
      DateTimeOffset.UtcNow);
  }

  public void Dispose() => _temp.Dispose();

  private void Publish(string fullName, string version, bool loader = false, string[]? dependencies = null)
  {
    var owner = fullName[..fullName.IndexOf('-')];
    var name = fullName[(fullName.IndexOf('-') + 1)..];
    var zip = loader
      ? Zip.Create(("BepInEx/core/BepInEx.Preloader.dll", version), ("doorstop_libs/libdoorstop_x64.so", ""))
      : Zip.Create(("manifest.json", "{}"), ($"{name}.dll", version), ($"{name}.cfg", "default"));
    _http.Serve($"https://thunderstore.io/package/download/{owner}/{name}/{version}/", zip);

    if (!_published.TryGetValue(fullName, out var versions))
    {
      _published[fullName] = versions = [];
    }

    versions.Insert(0, (version, dependencies ?? []));
  }

  private async Task<Instance> Modded()
  {
    var instance = _store.Create("Survival with friends");
    instance.LaunchArguments = "-windowed";
    await _mods.InstallAsync(instance, new InstallRequest(ModSource.Thunderstore, "Author-Mod"), null, CancellationToken.None);
    await _mods.InstallAsync(instance, new InstallRequest(ModSource.Thunderstore, "Author-Other"), null, CancellationToken.None);
    await _mods.SetEnabledAsync(instance, "thunderstore:Author-Other", false);

    var dll = _temp.Combine("Handmade.dll");
    File.WriteAllText(dll, "handmade");
    await _mods.InstallFileAsync(instance, dll, null, CancellationToken.None);

    File.WriteAllText(Path.Combine(_store.DirectoryOf(instance), "BepInEx/config/Author.Mod.cfg"), "tuned");
    Directory.CreateDirectory(Path.Combine(_store.DirectoryOf(instance), "BepInEx/config/Sub"));
    File.WriteAllText(Path.Combine(_store.DirectoryOf(instance), "BepInEx/config/Sub/data.json"), "{}");
    _store.Save(instance);
    return instance;
  }

  [Fact]
  public async Task An_exported_instance_imports_as_the_same_modpack()
  {
    var source = await Modded();
    // The first install picked the newest library, 1.1.0. Going back to 1.0.0 proves the pack keeps the
    // instance's versions rather than taking the newest again.
    await _mods.InstallAsync(source, new InstallRequest(ModSource.Thunderstore, "Author-Library", "1.0.0"), null, CancellationToken.None);
    var file = _temp.Combine("pack.r2z");

    var export = _packs.Export(source, file);
    var import = await _packs.ImportAsync(file, null, CancellationToken.None);

    var copy = import.Instance;
    Assert.NotEqual(source.Id, copy.Id);
    Assert.Equal("Survival with friends", copy.Name);
    Assert.Equal("-windowed", copy.LaunchArguments);
    Assert.False(import.FromR2modman);
    Assert.Empty(import.Install.Warnings);
    Assert.Equal(4, export.Mods);

    Assert.Equal("1.0.0", copy.FindMod("thunderstore:Author-Library")!.Version);
    Assert.True(copy.FindMod("thunderstore:Author-Library")!.InstalledAsDependency);
    Assert.False(copy.FindMod("thunderstore:Author-Mod")!.InstalledAsDependency);
    Assert.False(copy.FindMod("thunderstore:Author-Other")!.Enabled);
    Assert.Equal(source.Loader!.Version, copy.Loader!.Version);

    var directory = _store.DirectoryOf(copy);
    Assert.True(File.Exists(Path.Combine(directory, "BepInEx/plugins/Author-Other/Other.dll.disabled")));
    Assert.Equal("tuned", File.ReadAllText(Path.Combine(directory, "BepInEx/config/Author.Mod.cfg")));
    Assert.True(File.Exists(Path.Combine(directory, "BepInEx/config/Sub/data.json")));

    var local = copy.FindMod("local:Handmade")!;
    Assert.Equal(["BepInEx/plugins/Handmade/Handmade.dll"], local.Files);
    Assert.Equal("handmade", File.ReadAllText(Path.Combine(directory, "BepInEx/plugins/Handmade/Handmade.dll")));
  }

  [Fact]
  public async Task The_pack_is_an_r2modman_profile()
  {
    var source = await Modded();
    var file = _temp.Combine("pack.r2z");

    _packs.Export(source, file);

    using var zip = ZipFile.OpenRead(file);
    var entries = zip.Entries.Select(e => e.FullName).ToList();
    Assert.Contains("export.r2x", entries);
    Assert.Contains("launchheim.json", entries);
    Assert.Contains("BepInEx/config/Author.Mod.cfg", entries);

    using var reader = new StreamReader(zip.GetEntry("export.r2x")!.Open());
    var profile = R2x.Read(reader.ReadToEnd());
    Assert.Equal("Survival with friends", profile.Name);
    Assert.Contains(new R2x.Mod("Author-Other", "2.0.0", false), profile.Mods);
    Assert.Contains(profile.Mods, m => m.FullName == "denikson-BepInExPack_Valheim");
    // The local mod can't be downloaded by r2modman; its files travel in the zip instead.
    Assert.DoesNotContain(profile.Mods, m => m.FullName.Contains("Handmade"));
  }

  [Fact]
  public async Task An_r2modman_export_imports_with_its_versions_and_configs()
  {
    var file = _temp.Combine("friends.r2z");
    File.WriteAllBytes(file, Zip.Create(
      ("export.r2x", """
        profileName: Friends
        mods:
          - name: denikson-BepInExPack_Valheim
            version:
              major: 5
              minor: 4
              patch: 2202
            enabled: true
          - name: Author-Mod
            version:
              major: 1
              minor: 0
              patch: 0
            enabled: true
          - name: Author-Library
            version:
              major: 1
              minor: 0
              patch: 0
            enabled: false
        """),
      ("config/Author.Mod.cfg", "from r2modman")));

    var import = await _packs.ImportAsync(file, null, CancellationToken.None);

    var instance = import.Instance;
    Assert.True(import.FromR2modman);
    Assert.Equal("Friends", instance.Name);
    Assert.Equal("5.4.2202", instance.Loader!.Version);
    // Listed after the mod that needs it, and still installed at its own version rather than the newest.
    var library = instance.FindMod("thunderstore:Author-Library")!;
    Assert.Equal("1.0.0", library.Version);
    Assert.False(library.Enabled);
    Assert.True(library.InstalledAsDependency);
    Assert.False(instance.FindMod("thunderstore:Author-Mod")!.InstalledAsDependency);
    Assert.Equal("from r2modman", File.ReadAllText(Path.Combine(_store.DirectoryOf(instance), "BepInEx/config/Author.Mod.cfg")));
    Assert.Equal(1, import.ConfigFiles);
  }

  [Fact]
  public async Task A_missing_mod_is_a_warning_not_a_failed_import()
  {
    var file = _temp.Combine("pack.r2z");
    File.WriteAllBytes(file, Zip.Create(("export.r2x", """
      profileName: Gone
      mods:
        - name: Nobody-Deleted
          version:
            major: 1
            minor: 0
            patch: 0
          enabled: true
        - name: Author-Other
          version:
            major: 9
            minor: 0
            patch: 0
          enabled: true
      """)));

    var import = await _packs.ImportAsync(file, null, CancellationToken.None);

    Assert.Contains(import.Install.Warnings, w => w.Contains("Nobody-Deleted"));
    Assert.Contains(import.Install.Warnings, w => w.Contains("no longer available"));
    Assert.Equal("2.0.0", import.Instance.FindMod("thunderstore:Author-Other")!.Version);
  }

  [Fact]
  public async Task Local_mod_files_outside_the_instance_are_not_trusted()
  {
    var file = _temp.Combine("evil.r2z");
    File.WriteAllBytes(file, Zip.Create(
      ("launchheim.json", """
        {"format":1,"name":"Evil","mods":[{"source":"Local","id":"Evil","name":"Evil","files":["../../outside.txt","BepInEx/plugins/Evil/Evil.dll"]}]}
        """),
      ("BepInEx/plugins/Evil/Evil.dll", "x")));

    var import = await _packs.ImportAsync(file, null, CancellationToken.None);

    Assert.Equal(["BepInEx/plugins/Evil/Evil.dll"], import.Instance.FindMod("local:Evil")!.Files);
  }

  [Fact]
  public async Task A_pack_from_a_newer_launchheim_is_refused_and_leaves_nothing_behind()
  {
    var file = _temp.Combine("future.r2z");
    File.WriteAllBytes(file, Zip.Create(("launchheim.json", """{"format":99,"name":"Future","mods":[]}""")));

    await Assert.ThrowsAsync<InvalidDataException>(() => _packs.ImportAsync(file, null, CancellationToken.None));
    Assert.Empty(_store.LoadAll());
  }

  [Fact]
  public async Task A_zip_that_is_no_pack_is_refused()
  {
    var file = _temp.Combine("mod.zip");
    File.WriteAllBytes(file, Zip.Create(("Mod.dll", "x")));

    var error = await Assert.ThrowsAsync<InvalidDataException>(() => _packs.ImportAsync(file, null, CancellationToken.None));
    Assert.Contains("not a modpack", error.Message);
    Assert.Empty(_store.LoadAll());
  }

  [Fact]
  public async Task A_pack_remembers_its_instance_and_reads_without_installing()
  {
    var source = await Modded();
    var file = _temp.Combine("pack.r2z");
    _packs.Export(source, file);

    var contents = PackService.Read(file);

    Assert.False(contents.FromR2modman);
    Assert.Equal(source.Id, contents.Manifest.InstanceId);
    Assert.Contains(contents.Manifest.Mods, m => m.Id == "Author-Mod");
  }

  [Fact]
  public async Task An_unchanged_pack_changes_nothing()
  {
    var source = await Modded();
    var file = _temp.Combine("pack.r2z");
    _packs.Export(source, file);

    Assert.True(PackService.Compare(source, PackService.Read(file).Manifest).IsEmpty);
  }

  /// <summary>What the companion app does: edits the mod list in the pack and sends it back.</summary>
  [Fact]
  public async Task A_pack_edited_elsewhere_updates_its_instance()
  {
    var source = await Modded();
    var file = _temp.Combine("pack.r2z");
    _packs.Export(source, file);
    var manifest = PackService.Read(file).Manifest;

    // Removed Author-Mod (and so the library it pulled in), switched Author-Other back on, and added the
    // library back as a mod picked by hand, at 1.0.0 instead of the 1.1.0 installed here.
    manifest.Mods.RemoveAll(m => m.Id is "Author-Mod" or "Author-Library");
    var other = manifest.Mods.Single(m => m.Id == "Author-Other");
    other.Enabled = true;
    manifest.Mods.Add(new PackMod { Source = ModSource.Thunderstore, Id = "Author-Library", Name = "Library", Version = "1.0.0" });

    var changes = PackService.Compare(source, manifest);
    Assert.Equal(["Author-Mod"], changes.Removed.Select(m => m.SourceId));
    Assert.Equal(["Author-Library"], changes.Changed.Select(c => c.Mod.SourceId));
    Assert.Equal([("Author-Other", true)], changes.Toggled.Select(t => (t.Mod.SourceId, t.Enabled)));
    Assert.Empty(changes.Added);

    var report = await _packs.ApplyAsync(source, manifest, null, CancellationToken.None);

    Assert.Empty(report.Install.Warnings);
    Assert.Null(source.FindMod("thunderstore:Author-Mod"));
    var library = source.FindMod("thunderstore:Author-Library")!;
    Assert.Equal("1.0.0", library.Version);
    Assert.False(library.InstalledAsDependency);
    Assert.True(source.FindMod("thunderstore:Author-Other")!.Enabled);
    // The local mod and the configs made on the PC are left alone.
    Assert.NotNull(source.FindMod("local:Handmade"));
    Assert.Equal("tuned", File.ReadAllText(Path.Combine(_store.DirectoryOf(source), "BepInEx/config/Author.Mod.cfg")));
    Assert.True(PackService.Compare(source, manifest).IsEmpty);

    // And it is saved: a fresh load sees the same mods.
    var reloaded = _store.LoadAll().Single(i => i.Id == source.Id);
    Assert.Equal(source.Mods.Select(m => m.Key).Order(), reloaded.Mods.Select(m => m.Key).Order());
  }

  /// <summary>A list from the phone that lacks a dependency LaunchHeim pulled in itself.</summary>
  [Fact]
  public async Task A_dependency_the_pack_doesnt_list_stays_while_a_kept_mod_needs_it()
  {
    var source = await Modded();
    var file = _temp.Combine("pack.r2z");
    _packs.Export(source, file);
    var manifest = PackService.Read(file).Manifest;
    manifest.Mods.RemoveAll(m => m.Id == "Author-Library");

    Assert.Empty(PackService.Compare(source, manifest).Removed);
    await _packs.ApplyAsync(source, manifest, null, CancellationToken.None);
    Assert.NotNull(source.FindMod("thunderstore:Author-Library"));

    // Once the mod that needed it is gone too, the library goes with it.
    manifest.Mods.RemoveAll(m => m.Id == "Author-Mod");
    Assert.Equal(["Author-Library", "Author-Mod"], PackService.Compare(source, manifest).Removed.Select(m => m.SourceId).Order());
  }

  [Fact]
  public async Task A_mod_added_elsewhere_is_installed_with_its_dependencies()
  {
    var source = _store.Create("Fresh");
    await _mods.InstallLoaderAsync(source, null, CancellationToken.None);
    var file = _temp.Combine("pack.r2z");
    _packs.Export(source, file);
    var manifest = PackService.Read(file).Manifest;
    manifest.Mods.Add(new PackMod { Source = ModSource.Thunderstore, Id = "Author-Mod", Name = "Mod", Version = "1.0.0", Dependencies = ["thunderstore:Author-Library"] });

    Assert.Equal(["Author-Mod"], PackService.Compare(source, manifest).Added.Select(m => m.Id));
    await _packs.ApplyAsync(source, manifest, null, CancellationToken.None);

    Assert.Equal("1.0.0", source.FindMod("thunderstore:Author-Mod")!.Version);
    Assert.True(source.FindMod("thunderstore:Author-Library")!.InstalledAsDependency);
  }
}

public class R2xTests
{
  [Fact]
  public void Quoted_names_and_odd_versions_survive_a_round_trip()
  {
    var mods = new[]
    {
      new InstalledMod { Source = ModSource.Thunderstore, SourceId = "A-B", Version = "1.2.3" },
      new InstalledMod { Source = ModSource.Thunderstore, SourceId = "A-Odd", Version = "1.2" },
      new InstalledMod { Source = ModSource.Nexus, SourceId = "42", Version = "1.0.0" },
    };

    var profile = R2x.Read(R2x.Write("Tom's \"best\": pack", mods));

    Assert.Equal("Tom's \"best\": pack", profile.Name);
    Assert.Equal([new R2x.Mod("A-B", "1.2.3", true)], profile.Mods);
  }

  [Fact]
  public void An_empty_profile_has_no_mods()
  {
    var profile = R2x.Read(R2x.Write("Empty", []));

    Assert.Equal("Empty", profile.Name);
    Assert.Empty(profile.Mods);
  }

  [Fact]
  public void Single_quoted_yaml_is_read()
  {
    var profile = R2x.Read("profileName: 'It''s mine'\r\nmods:\r\n- name: 'A-B'\r\n  version:\r\n    major: 2\r\n    minor: 0\r\n    patch: 1\r\n  enabled: false\r\n");

    Assert.Equal("It's mine", profile.Name);
    Assert.Equal([new R2x.Mod("A-B", "2.0.1", false)], profile.Mods);
  }

}
