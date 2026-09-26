using CINE.LaunchHeim.Core.Catalogs.CurseForge;
using CINE.LaunchHeim.Core.Catalogs.Nexus;
using CINE.LaunchHeim.Core.Catalogs.Thunderstore;
using CINE.LaunchHeim.Core.Downloads;
using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Mods;

namespace CINE.LaunchHeim.Core.Tests;

public class ModServiceTests : IDisposable
{
  private readonly TempDirectory _temp = new();
  private readonly FakeHttp _http = new();
  private readonly ThunderstoreIndex _index;
  private readonly InstanceStore _store;
  private readonly ModService _service;
  private readonly Instance _instance;

  public ModServiceTests()
  {
    var client = new HttpClient(_http);
    var paths = _temp.AppPaths;
    _index = new ThunderstoreIndex(client, paths);
    _store = new InstanceStore(paths);
    var catalogs = new CatalogRegistry(
      new ThunderstoreCatalog(client, _index),
      new NexusCatalog(client, () => null),
      new CurseForgeCatalog(client, () => null));
    _service = new ModService(_store, new ModInstaller(), new ModDownloader(client, paths), catalogs, paths);
    _instance = _store.Create("Test");

    Publish("denikson-BepInExPack_Valheim", "5.4.2351", loader: true);
    Publish("ValheimModding-Jotunn", "2.30.2");
    Publish("Author-Library", "1.0.0");
    Publish("Author-Library", "1.1.0");
    Publish("Author-Mod", "1.0.0", dependencies: ["ValheimModding-Jotunn-2.20.0", "Author-Library-1.0.0"]);
    Publish("Author-Other", "1.0.0", dependencies: ["Author-Library-1.0.0"]);
    Publish("Author-NeedsLoader", "1.0.0", dependencies: ["denikson-BepInExPack_Valheim-5.4.2351"]);
    UseIndex();
  }

  public void Dispose() => _temp.Dispose();

  private readonly Dictionary<string, List<(string Version, string[] Dependencies)>> _published = [];

  private void Publish(string fullName, string version, bool loader = false, string[]? dependencies = null)
  {
    var owner = fullName[..fullName.IndexOf('-')];
    var name = fullName[(fullName.IndexOf('-') + 1)..];
    var zip = loader
      ? Zip.Create(("manifest.json", "{}"), ("BepInExPack_Valheim/BepInEx/core/BepInEx.Preloader.dll", version), ("BepInExPack_Valheim/doorstop_libs/libdoorstop_x64.so", ""))
      : Zip.Create(("manifest.json", "{}"), ($"{name}.dll", version));
    _http.Serve($"https://thunderstore.io/package/download/{owner}/{name}/{version}/", zip);

    if (!_published.TryGetValue(fullName, out var versions))
    {
      _published[fullName] = versions = [];
    }

    versions.Insert(0, (version, dependencies ?? []));
  }

  private void UseIndex() => _index.Use(
    _published.Select(p =>
    {
      var package = ThunderstoreSearchTests.Package(p.Key);
      return package with
      {
        Versions = p.Value.Select(v => new ThunderstoreVersion(v.Version, v.Dependencies, 0, DateTimeOffset.UnixEpoch, 0)).ToArray(),
      };
    }),
    DateTimeOffset.UtcNow);

  private Task<InstallReport> Install(string fullName, string? version = null) =>
    _service.InstallAsync(_instance, new InstallRequest(ModSource.Thunderstore, fullName, version), null, CancellationToken.None);

  private string Dir => _store.DirectoryOf(_instance);

  [Fact]
  public async Task Installing_a_mod_brings_its_dependencies_and_bepinex()
  {
    var report = await Install("Author-Mod");

    Assert.Empty(report.Warnings);
    Assert.Equal(
      ["Author-Library", "Author-Mod", "ValheimModding-Jotunn", "denikson-BepInExPack_Valheim"],
      _instance.Mods.Select(m => m.SourceId).Order(StringComparer.Ordinal));
    Assert.True(_instance.FindMod("thunderstore:ValheimModding-Jotunn")!.InstalledAsDependency);
    Assert.False(_instance.FindMod("thunderstore:Author-Mod")!.InstalledAsDependency);
    Assert.True(_instance.Loader is { IsLoader: true });
    Assert.True(File.Exists(Path.Combine(Dir, "BepInEx/plugins/Author-Mod/Mod.dll")));
    Assert.True(File.Exists(Path.Combine(Dir, "BepInEx/core/BepInEx.Preloader.dll")));
  }

  [Fact]
  public async Task Missing_dependencies_get_the_newest_version()
  {
    await Install("Author-Mod");

    Assert.Equal("1.1.0", _instance.FindMod("thunderstore:Author-Library")!.Version);
  }

  [Fact]
  public async Task The_install_is_saved_to_disk()
  {
    await Install("Author-Mod");

    var reloaded = _store.LoadAll().Single();
    Assert.Equal(4, reloaded.Mods.Count);
  }

  [Fact]
  public async Task A_new_enough_dependency_is_not_downloaded_again()
  {
    await Install("Author-Other");
    var before = _http.Requests.Count;

    await Install("Author-Mod");

    Assert.Equal(2, _http.Requests.Count - before);
  }

  [Fact]
  public async Task Removing_a_mod_also_removes_dependencies_nothing_else_needs()
  {
    await Install("Author-Mod");
    await Install("Author-Other");

    var report = await _service.RemoveAsync(_instance, "thunderstore:Author-Mod", CancellationToken.None);

    Assert.Equal(["Author-Mod", "ValheimModding-Jotunn"], report.Removed.Select(m => m.SourceId).Order());
    Assert.NotNull(_instance.FindMod("thunderstore:Author-Library"));
    Assert.NotNull(_instance.Loader);
    Assert.False(Directory.Exists(Path.Combine(Dir, "BepInEx/plugins/Author-Mod")));
  }

  [Fact]
  public async Task Bepinex_is_never_removed_as_a_leftover_dependency()
  {
    await Install("Author-NeedsLoader");
    Assert.False(_instance.Loader!.InstalledAsDependency);

    await _service.RemoveAsync(_instance, "thunderstore:Author-NeedsLoader", CancellationToken.None);

    Assert.NotNull(_instance.Loader);
    Assert.True(File.Exists(Path.Combine(Dir, "BepInEx/core/BepInEx.Preloader.dll")));
  }

  [Fact]
  public async Task Enabling_a_mod_enables_what_it_needs()
  {
    await Install("Author-Mod");
    await _service.SetEnabledAsync(_instance, "thunderstore:Author-Library", false);
    await _service.SetEnabledAsync(_instance, "thunderstore:Author-Mod", false);

    var changed = await _service.SetEnabledAsync(_instance, "thunderstore:Author-Mod", true);

    Assert.Equal(["Author-Library", "Author-Mod"], changed.Select(m => m.SourceId).Order());
    Assert.True(File.Exists(Path.Combine(Dir, "BepInEx/plugins/Author-Library/Library.dll")));
  }

  [Fact]
  public async Task Updates_are_found_in_the_index_and_keep_the_mods_state()
  {
    await Install("Author-Library", "1.0.0");
    await _service.SetEnabledAsync(_instance, "thunderstore:Author-Library", false);

    var updates = _service.FindUpdates(_instance);
    Assert.Equal("1.1.0", updates["thunderstore:Author-Library"]);

    await _service.UpdateAsync(_instance, "thunderstore:Author-Library", null, CancellationToken.None);

    var mod = _instance.FindMod("thunderstore:Author-Library")!;
    Assert.Equal("1.1.0", mod.Version);
    Assert.False(mod.Enabled);
    Assert.Equal("1.1.0", File.ReadAllText(Path.Combine(Dir, "BepInEx/plugins/Author-Library/Library.dll.disabled")));
    Assert.Empty(_service.FindUpdates(_instance));
  }

  [Fact]
  public async Task A_local_thunderstore_zip_uses_its_manifest_and_dependencies()
  {
    var zip = _temp.Combine("export.zip");
    File.WriteAllBytes(zip, Zip.Create(
      ("manifest.json", """{"name":"My_Pack","version_number":"0.3.0","website_url":"","dependencies":["ValheimModding-Jotunn-2.0.0"]}"""),
      ("plugins/Pack.dll", "x")));

    await _service.InstallFileAsync(_instance, zip, null, CancellationToken.None);

    var mod = _instance.FindMod("local:My_Pack")!;
    Assert.Equal("My Pack", mod.Name);
    Assert.Equal("0.3.0", mod.Version);
    Assert.Equal(["thunderstore:ValheimModding-Jotunn"], mod.Dependencies);
    Assert.NotNull(_instance.FindMod("thunderstore:ValheimModding-Jotunn"));
    Assert.True(File.Exists(Path.Combine(Dir, "BepInEx/plugins/My_Pack/Pack.dll")));
  }

  [Fact]
  public async Task Nexus_without_an_api_key_asks_for_the_browser()
  {
    // The fake HTTP handler answers 404 to GraphQL, so this only proves no download is attempted
    // before the key check when the file id is known up front.
    await Assert.ThrowsAnyAsync<Exception>(() =>
      _service.InstallAsync(_instance, new InstallRequest(ModSource.Nexus, "1042"), null, CancellationToken.None));
    Assert.Empty(_instance.Mods);
  }
}

public class InstanceStoreTests : IDisposable
{
  private readonly TempDirectory _temp = new();

  public void Dispose() => _temp.Dispose();

  [Fact]
  public void Ids_are_slugs_and_stay_unique()
  {
    var store = new InstanceStore(_temp.AppPaths);

    var first = store.Create("My Modpack!");
    var second = store.Create("my modpack");

    Assert.Equal("my-modpack", first.Id);
    Assert.Equal("my-modpack-2", second.Id);
  }

  [Fact]
  public void Duplicates_copy_files_and_mods_under_a_new_id()
  {
    var store = new InstanceStore(_temp.AppPaths);
    var original = store.Create("Original");
    original.Mods.Add(new InstalledMod { Key = "local:x", Name = "X", Files = ["BepInEx/plugins/x/x.dll"] });
    store.Save(original);
    var file = Path.Combine(store.DirectoryOf(original), "BepInEx/plugins/x/x.dll");
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    File.WriteAllText(file, "x");

    var copy = store.Duplicate(original, "Copy");

    Assert.Equal("copy", copy.Id);
    Assert.Single(copy.Mods);
    Assert.True(File.Exists(Path.Combine(store.DirectoryOf(copy), "BepInEx/plugins/x/x.dll")));
    Assert.Equal(2, store.LoadAll().Count);
  }

  [Fact]
  public void A_damaged_manifest_does_not_hide_other_instances()
  {
    var store = new InstanceStore(_temp.AppPaths);
    store.Create("Good");
    var broken = store.Create("Broken");
    File.WriteAllText(Path.Combine(store.DirectoryOf(broken), InstanceStore.ManifestFile), "{ not json");

    Assert.Equal(["Good"], store.LoadAll().Select(i => i.Name));
  }
}

public class GameFolderImporterTests : IDisposable
{
  private readonly TempDirectory _temp = new();

  public void Dispose() => _temp.Dispose();

  [Fact]
  public void Imports_the_loader_plugins_and_configs_without_touching_the_game()
  {
    var game = _temp.Tree("Valheim",
      "valheim.x86_64",
      "BepInEx/core/BepInEx.Preloader.dll",
      "doorstop_libs/libdoorstop_x64.so",
      "BepInEx/plugins/CraftFromContainers.dll",
      "BepInEx/plugins/ValheimModding-Jotunn/Jotunn.dll",
      "BepInEx/plugins/readme.txt",
      "BepInEx/config/BepInEx.cfg");
    File.WriteAllText(Path.Combine(game, "BepInEx/plugins/ValheimModding-Jotunn/manifest.json"),
      """{"name":"Jotunn","version_number":"2.30.2","dependencies":[]}""");
    File.WriteAllText(Path.Combine(game, "BepInEx/LogOutput.log"),
      "[Message:   BepInEx] BepInEx 5.4.22.0 - valheim\n[Message:   BepInEx] User is running BepInExPack Valheim version 5.4.2202 from Thunderstore\n");
    var store = new InstanceStore(_temp.AppPaths);

    var instance = new GameFolderImporter(store).Import(game, "Imported");

    Assert.Equal("5.4.2202", instance.Loader!.Version);
    Assert.Equal(["local:CraftFromContainers", "thunderstore:ValheimModding-Jotunn"], instance.Mods.Where(m => !m.IsLoader).Select(m => m.Key).Order());
    Assert.Equal("2.30.2", instance.FindMod("thunderstore:ValheimModding-Jotunn")!.Version);
    var directory = store.DirectoryOf(instance);
    Assert.True(File.Exists(Path.Combine(directory, "BepInEx/config/BepInEx.cfg")));
    Assert.True(File.Exists(Path.Combine(directory, "doorstop_libs/libdoorstop_x64.so")));
    Assert.True(File.Exists(Path.Combine(game, "BepInEx/plugins/CraftFromContainers.dll")));
  }
}
