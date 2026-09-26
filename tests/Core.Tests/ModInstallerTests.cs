using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Mods;

namespace CINE.LaunchHeim.Core.Tests;

public class ModInstallerTests : IDisposable
{
  private readonly TempDirectory _temp = new();
  private readonly ModInstaller _installer = new();
  private readonly string _instance;

  public ModInstallerTests() => _instance = _temp.Tree("instance");

  public void Dispose() => _temp.Dispose();

  private IReadOnlyList<string> Install(params string[] files) =>
    _installer.Install(_temp.Tree("mod-" + Guid.NewGuid().ToString("N"), files), _instance, "Owner-Mod").Files;

  [Fact]
  public void A_bare_thunderstore_package_goes_into_its_own_plugin_folder()
  {
    var files = Install("manifest.json", "icon.png", "README.md", "Mod.dll");

    Assert.Contains("BepInEx/plugins/Owner-Mod/Mod.dll", files);
    Assert.Contains("BepInEx/plugins/Owner-Mod/manifest.json", files);
    Assert.True(File.Exists(Path.Combine(_instance, "BepInEx/plugins/Owner-Mod/Mod.dll")));
  }

  [Fact]
  public void Plugins_patchers_and_config_folders_are_mapped_into_bepinex()
  {
    var files = Install("manifest.json", "plugins/Mod.dll", "patchers/Patch.dll", "config/owner.mod.cfg");

    Assert.Contains("BepInEx/plugins/Owner-Mod/Mod.dll", files);
    Assert.Contains("BepInEx/patchers/Owner-Mod/Patch.dll", files);
    Assert.True(File.Exists(Path.Combine(_instance, "BepInEx/config/owner.mod.cfg")));
  }

  [Fact]
  public void A_wrapped_bepinex_tree_from_nexus_is_unwrapped()
  {
    var files = Install("Valheim/BepInEx/plugins/Mod.dll", "Valheim/BepInEx/config/mod.cfg", "Valheim/readme.txt");

    Assert.Contains("BepInEx/plugins/Owner-Mod/Mod.dll", files);
    Assert.True(File.Exists(Path.Combine(_instance, "BepInEx/config/mod.cfg")));
  }

  [Fact]
  public void A_single_wrapping_folder_is_stripped()
  {
    var files = Install("MyMod-1.2/MyMod.dll", "MyMod-1.2/Assets/bundle");

    Assert.Equal(["BepInEx/plugins/Owner-Mod/MyMod.dll", "BepInEx/plugins/Owner-Mod/Assets/bundle"], files.Order().Reverse().ToArray());
  }

  [Fact]
  public void An_existing_per_mod_folder_is_not_nested_twice()
  {
    var files = Install("BepInEx/plugins/Owner-Mod/Mod.dll");

    Assert.Equal(["BepInEx/plugins/Owner-Mod/Mod.dll"], files);
  }

  [Fact]
  public void The_loader_pack_is_installed_into_the_instance_root()
  {
    var result = _installer.Install(
      _temp.Tree("pack", "manifest.json", "BepInExPack_Valheim/BepInEx/core/BepInEx.Preloader.dll", "BepInExPack_Valheim/doorstop_libs/libdoorstop_x64.so", "BepInExPack_Valheim/BepInEx/config/BepInEx.cfg"),
      _instance,
      "denikson-BepInExPack_Valheim");

    Assert.True(result.IsLoader);
    Assert.Contains("BepInEx/core/BepInEx.Preloader.dll", result.Files);
    Assert.Contains("doorstop_libs/libdoorstop_x64.so", result.Files);
    Assert.DoesNotContain(result.Files, f => f.Contains("manifest.json"));
    Assert.True(File.Exists(Path.Combine(_instance, "BepInEx/config/BepInEx.cfg")));
  }

  [Fact]
  public void Bundled_bepinex_core_dlls_without_doorstop_are_not_a_loader()
  {
    var result = _installer.Install(_temp.Tree("m", "BepInEx/core/BepInEx.Preloader.dll", "BepInEx/plugins/Mod.dll"), _instance, "Owner-Mod");

    Assert.False(result.IsLoader);
  }

  [Fact]
  public void Existing_configs_are_never_overwritten_or_tracked()
  {
    var config = Path.Combine(_instance, "BepInEx/config/owner.mod.cfg");
    Directory.CreateDirectory(Path.GetDirectoryName(config)!);
    File.WriteAllText(config, "user settings");

    var files = Install("config/owner.mod.cfg", "Mod.dll");

    Assert.Equal("user settings", File.ReadAllText(config));
    Assert.DoesNotContain(files, f => f.EndsWith(".cfg"));
  }

  [Fact]
  public void Loose_cfg_files_next_to_the_dll_go_to_the_config_folder()
  {
    Install("Mod.dll", "owner.mod.cfg");

    Assert.True(File.Exists(Path.Combine(_instance, "BepInEx/config/owner.mod.cfg")));
  }

  [Fact]
  public void Macos_junk_is_skipped()
  {
    var files = Install("Mod.dll", "__MACOSX/._Mod.dll", ".DS_Store");

    Assert.Equal(["BepInEx/plugins/Owner-Mod/Mod.dll"], files);
  }

  [Fact]
  public void Uninstall_removes_the_files_and_empty_folders_but_keeps_bepinex_folders()
  {
    var mod = new InstalledMod { Files = Install("plugins/Mod.dll", "plugins/sub/data.bin").ToList() };

    _installer.Uninstall(_instance, mod);

    Assert.False(Directory.Exists(Path.Combine(_instance, "BepInEx/plugins/Owner-Mod")));
    Assert.True(Directory.Exists(Path.Combine(_instance, "BepInEx/plugins")));
  }

  [Fact]
  public void Disabling_renames_only_assemblies_and_enabling_restores_them()
  {
    var mod = new InstalledMod { Files = Install("Mod.dll", "data.bin").ToList() };
    var dll = Path.Combine(_instance, "BepInEx/plugins/Owner-Mod/Mod.dll");

    _installer.SetEnabled(_instance, mod, enabled: false);

    Assert.False(File.Exists(dll));
    Assert.True(File.Exists(dll + ".disabled"));
    Assert.True(File.Exists(Path.Combine(_instance, "BepInEx/plugins/Owner-Mod/data.bin")));
    Assert.False(mod.Enabled);

    _installer.SetEnabled(_instance, mod, enabled: true);

    Assert.True(File.Exists(dll));
    Assert.True(mod.Enabled);
  }

  [Fact]
  public void Uninstall_finds_the_files_of_a_disabled_mod()
  {
    var mod = new InstalledMod { Files = Install("Mod.dll").ToList() };
    _installer.SetEnabled(_instance, mod, enabled: false);

    _installer.Uninstall(_instance, mod);

    Assert.False(Directory.Exists(Path.Combine(_instance, "BepInEx/plugins/Owner-Mod")));
  }

  [Fact]
  public void A_bare_dll_download_is_installed_as_a_plugin()
  {
    var dll = _temp.Combine("Download.dll");
    File.WriteAllText(dll, "x");

    using var extracted = ExtractedMod.FromFile(dll, _temp.Combine("tmp"));
    var files = _installer.Install(extracted.Directory, _instance, "Owner-Mod").Files;

    Assert.Equal(["BepInEx/plugins/Owner-Mod/Download.dll"], files);
  }

  [Fact]
  public void Archives_with_paths_escaping_the_folder_are_rejected()
  {
    var zip = _temp.Combine("evil.zip");
    File.WriteAllBytes(zip, Zip.Create(("../../evil.txt", "boom"), ("Mod.dll", "x")));

    Assert.ThrowsAny<Exception>(() => ExtractedMod.FromFile(zip, _temp.Combine("tmp")));
    Assert.False(File.Exists(_temp.Combine("evil.txt")));
  }

  [Fact]
  public void Zips_are_extracted_with_their_folders()
  {
    var zip = _temp.Combine("mod.zip");
    File.WriteAllBytes(zip, Zip.Create(("plugins/Mod.dll", "x"), ("manifest.json", "{}")));

    using var extracted = ExtractedMod.FromFile(zip, _temp.Combine("tmp"));

    Assert.True(File.Exists(Path.Combine(extracted.Directory, "plugins", "Mod.dll")));
  }
}
