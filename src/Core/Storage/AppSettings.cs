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

  /// <summary>Opens the console window with the game's log on every launch, for building and debugging modpacks.</summary>
  public bool OpenConsoleOnLaunch { get; set; }

  /// <summary>
  /// What was played on each server or world last time, keyed by <c>ValheimServer.Key</c> or
  /// <c>ValheimWorld.Key</c>, so the next launch there needs no choices.
  /// </summary>
  public Dictionary<string, PlayChoice> PlayChoices { get; set; } = [];

  /// <summary>
  /// The picture shown for a server or world, keyed like <see cref="PlayChoices"/>, as a file name in
  /// <see cref="PlayImageStore"/>. Kept apart from the choices because a picture can be set before the
  /// first launch there.
  /// </summary>
  public Dictionary<string, string> PlayImages { get; set; } = [];

  /// <summary>What was started last, which the sidebar's Play button starts again. Null before the first launch.</summary>
  public LastPlay? LastPlay { get; set; }

  /// <summary>
  /// Receives mod lists from the companion app (and other LocalSend devices) while LaunchHeim runs. Off
  /// until switched on, because it opens a port on the network. Sending works either way.
  /// </summary>
  public bool PhoneSyncEnabled { get; set; }

  /// <summary>The name phones show for this PC. Null means the computer's name.</summary>
  public string? PhoneSyncAlias { get; set; }

  /// <summary>LocalSend's device fingerprint: random, made once, so phones don't list this PC twice.</summary>
  public string? PhoneSyncFingerprint { get; set; }

  /// <summary>
  /// "Don't remind me again" in the update dialog: no reminder for any version, and no check either.
  /// Settings → About switches reminders back on.
  /// </summary>
  public bool UpdateRemindersDisabled { get; set; }

  /// <summary>"Skip this version": no reminder for it, but again for the one after.</summary>
  public string? SkippedUpdateVersion { get; set; }
}

/// <summary>The last launch: a server or world from the Play page, an instance, or vanilla.</summary>
public sealed class LastPlay
{
  /// <summary>
  /// The server or world's key in <see cref="AppSettings.PlayChoices"/>, whose choice (character, setup,
  /// password) is played again. Null when the game was started without one.
  /// </summary>
  public string? DestinationKey { get; set; }

  /// <summary>The server or world's name, for the sidebar, which shouldn't have to read Valheim's lists.</summary>
  public string? DestinationName { get; set; }

  /// <summary>The instance played without a destination, or null for vanilla.</summary>
  public string? InstanceId { get; set; }
}

/// <summary>The character and setup a server or world was last played with.</summary>
public sealed class PlayChoice
{
  /// <summary>The character's file name without <c>.fch</c>, which is how Valheim selects it.</summary>
  public string Character { get; set; } = "";

  /// <summary>The instance it was played with, or null for vanilla Valheim.</summary>
  public string? InstanceId { get; set; }

  /// <summary>
  /// A server's password, passed with <c>-password</c>. It sits in this file next to the API keys, which
  /// is why the file is only readable by its owner.
  /// </summary>
  public string? Password { get; set; }

  public DateTimeOffset LastPlayedAt { get; set; }
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
