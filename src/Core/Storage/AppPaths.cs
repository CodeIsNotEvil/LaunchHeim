namespace CINE.LaunchHeim.Core.Storage;

/// <summary>Where LaunchHeim keeps its files, following the XDG base directory spec.</summary>
/// <remarks>
/// Instances are data (they hold mods and configs the user cares about), settings are config, and the
/// Thunderstore index plus downloaded archives are cache that can be thrown away at any time. Keeping
/// them apart means clearing <c>~/.cache</c> never costs the user a modpack.
/// </remarks>
public sealed record AppPaths(string DataDirectory, string ConfigDirectory, string CacheDirectory, string RuntimeDirectory)
{
  public const string AppName = "LaunchHeim";

  public string InstancesDirectory => Path.Combine(DataDirectory, "instances");
  public string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");
  public string DownloadsDirectory => Path.Combine(CacheDirectory, "downloads");
  public string ThunderstoreIndexFile => Path.Combine(CacheDirectory, "thunderstore-valheim.json.gz");
  public string IpcSocket => Path.Combine(RuntimeDirectory, "launchheim.sock");

  public static AppPaths FromEnvironment()
  {
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    string Xdg(string variable, string fallback)
    {
      var value = Environment.GetEnvironmentVariable(variable);
      // The spec says relative values are invalid and must be ignored.
      return !string.IsNullOrEmpty(value) && Path.IsPathRooted(value) ? value : Path.Combine(home, fallback);
    }

    var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
    if (string.IsNullOrEmpty(runtime) || !Path.IsPathRooted(runtime))
    {
      runtime = Path.GetTempPath();
    }

    return new AppPaths(
      Path.Combine(Xdg("XDG_DATA_HOME", ".local/share"), AppName),
      Path.Combine(Xdg("XDG_CONFIG_HOME", ".config"), AppName),
      Path.Combine(Xdg("XDG_CACHE_HOME", ".cache"), AppName),
      runtime);
  }
}
