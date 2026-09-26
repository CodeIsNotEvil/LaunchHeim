namespace CINE.LaunchHeim.Core.Storage;

public sealed class AppSettings
{
  /// <summary>Set only when the user picked a folder by hand. Otherwise the Steam library is searched.</summary>
  public string? GameDirectory { get; set; }

  /// <summary>Personal API key from nexusmods.com/users/myaccount?tab=api. Needed for downloads only.</summary>
  public string? NexusApiKey { get; set; }

  /// <summary>Key from console.curseforge.com. CurseForge rejects every request without one.</summary>
  public string? CurseForgeApiKey { get; set; }

  public bool ShowNsfw { get; set; }

  public string? LastInstanceId { get; set; }

  /// <summary>Extra arguments for the vanilla launch, which has no instance to store them on.</summary>
  public string VanillaLaunchArguments { get; set; } = "";
}

public sealed class SettingsStore(AppPaths paths)
{
  public AppSettings Load()
  {
    try
    {
      return JsonFile.Read<AppSettings>(paths.SettingsFile) ?? new AppSettings();
    }
    catch (System.Text.Json.JsonException)
    {
      // A hand-edited file with a typo should not stop the launcher from starting.
      return new AppSettings();
    }
  }

  /// <summary>Saves with mode 0600, because the file holds API keys.</summary>
  public void Save(AppSettings settings) =>
    JsonFile.Write(paths.SettingsFile, settings, UnixFileMode.UserRead | UnixFileMode.UserWrite);
}
