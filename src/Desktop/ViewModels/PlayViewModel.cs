using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Logging;
using CINE.LaunchHeim.Core.Saves;
using CINE.LaunchHeim.Core.Storage;
using Qml.Net;

namespace CINE.LaunchHeim.Desktop.ViewModels;

/// <summary>What a launch should open in the game: a server to join, or a character and world to select.</summary>
/// <param name="Name">The server or world, for the log and the sidebar.</param>
/// <param name="Arguments">Command-line arguments for the game, see <see cref="GameLauncher.JoinArguments"/>.</param>
/// <param name="Prefs">PlayerPrefs to set first, see <see cref="ValheimPrefs"/>.</param>
internal sealed record DirectPlay(string Name, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Prefs);

/// <summary>
/// The Play page: the servers and worlds Valheim knows, each started with the character and setup
/// (vanilla or an instance) that was used there last time.
/// </summary>
/// <remarks>
/// <para>
/// Valheim stays the owner of saves and servers. LaunchHeim only reads its lists (<see cref="ValheimSaves"/>)
/// and remembers its own choices per server or world in the settings (<see cref="AppSettings.PlayChoices"/>),
/// along with a picture the user may pick for each (<see cref="AppSettings.PlayImages"/>).
/// </para>
/// <para>
/// How direct a launch can be is up to the game. A server is joined with <c>+connect</c>, which opens the
/// character selection with the remembered character already picked, so one click on Start joins. The
/// game has no option to load a world on its own; LaunchHeim sets the character and world the menus
/// select, and the player clicks through Start. Skipping those clicks would need a BepInEx plugin,
/// which a vanilla launch can't have.
/// </para>
/// </remarks>
public sealed class PlayViewModel : ViewModel
{
  internal const string VanillaName = "Vanilla Valheim";

  private readonly AppViewModel _app;
  private List<ValheimCharacter> _characters = [];
  private List<DestinationViewModel> _servers = [];
  private List<DestinationViewModel> _worlds = [];
  private string _gameCharacter = "";
  private bool _isLoading;
  private bool _loaded;
  private bool _isQueryingServers;

  public PlayViewModel(AppViewModel app)
  {
    _app = app;
    Saves = ValheimSaves.ForCurrentUser(SteamLibraryLocator.ForCurrentUser().SteamRoots);
    Images = new PlayImageStore(app.Paths);
  }

  internal ValheimSaves Saves { get; }
  internal PlayImageStore Images { get; }
  internal IReadOnlyList<ValheimCharacter> CharacterList => _characters;
  internal AppViewModel App => _app;

  [NotifySignal]
  public List<DestinationViewModel> Servers { get => _servers; private set => Set(ref _servers, value); }

  [NotifySignal]
  public List<DestinationViewModel> Worlds { get => _worlds; private set => Set(ref _worlds, value); }

  [NotifySignal]
  public int ServerCount => _servers.Count;

  [NotifySignal]
  public int WorldCount => _worlds.Count;

  [NotifySignal]
  public int CharacterCount => _characters.Count;

  /// <summary>For the character ComboBox, joined with U+001F like the other lists QML splits.</summary>
  [NotifySignal]
  public string CharacterNames => string.Join('\u001f', _characters.Select(c => c.DisplayName));

  /// <summary>Vanilla first, then the instances in the library's order. A setup index is an index into this.</summary>
  [NotifySignal]
  public string SetupNames => string.Join('\u001f', _app.InstanceList.Select(i => i.Name).Prepend(VanillaName));

  [NotifySignal]
  public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

  /// <summary>Where the game keeps its local saves and prefs, named when nothing was found.</summary>
  [NotifySignal]
  public string DataDirectory => Saves.DataDirectory;

  /// <summary>The character Valheim has selected right now, offered first for a server or world played the first time.</summary>
  internal string GameCharacter => _gameCharacter;

  /// <summary>Reads the lists again. Cheap, but it runs off the UI thread because Steam may sit on a slow drive.</summary>
  public async void Refresh()
  {
    if (IsLoading)
    {
      return;
    }

    IsLoading = true;
    try
    {
      var (characters, servers, worlds, gameCharacter) = await Task.Run(() =>
        (Saves.Characters(), Saves.Servers(), Saves.Worlds(), ReadGameCharacter()));
      _characters = characters.ToList();
      _gameCharacter = gameCharacter;
      Servers = servers.Select(s => DestinationViewModel.ForServer(this, s)).ToList();
      Worlds = worlds.Select(w => DestinationViewModel.ForWorld(this, w)).ToList();
      _loaded = true;
      RefreshServerStatus();
    }
    catch (Exception ex)
    {
      Log.Error("Reading Valheim's saves failed", ex);
      _app.Toast("error", "Could not read Valheim's saves", ex.Message);
    }
    finally
    {
      IsLoading = false;
      Raise(nameof(ServerCount));
      Raise(nameof(WorldCount));
      Raise(nameof(CharacterCount));
      Raise(nameof(CharacterNames));
    }
  }

  /// <summary>
  /// Asks every server who is on it (<see cref="ServerQuery"/>). The page calls it every 30 seconds while
  /// it is shown. The servers are asked at the same time, each row updating when its answer arrives.
  /// </summary>
  public async void RefreshServerStatus()
  {
    // A round takes at most the timeout, so a slow one is never overlapped by the next.
    if (_isQueryingServers || _servers.Count == 0)
    {
      return;
    }

    _isQueryingServers = true;
    try
    {
      await Task.WhenAll(_servers.Select(s => s.QueryStatusAsync()));
    }
    finally
    {
      _isQueryingServers = false;
    }
  }

  /// <summary>After the game closes its lists may have changed (a new recent server, a saved world).</summary>
  internal void RefreshIfLoaded()
  {
    if (_loaded)
    {
      Refresh();
    }
  }

  internal void InstancesChanged()
  {
    Raise(nameof(SetupNames));
    foreach (var destination in _servers.Concat(_worlds))
    {
      destination.RaiseChoice();
    }
  }

  private string ReadGameCharacter()
  {
    try
    {
      return UnityPrefs.ForValheim(Saves.DataDirectory).GetString(ValheimPrefs.Character) ?? "";
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return "";
    }
  }

  /// <summary>The setup index for an instance id, null meaning vanilla. An instance that no longer exists gives -1.</summary>
  internal int SetupIndexOf(string? instanceId)
  {
    if (instanceId is null)
    {
      return 0;
    }

    var index = _app.InstanceList.FindIndex(i => i.Id == instanceId);
    return index < 0 ? -1 : index + 1;
  }

  internal void Launch(DestinationViewModel destination, int characterIndex, int setupIndex, string password)
  {
    if (characterIndex < 0 || characterIndex >= _characters.Count)
    {
      _app.Toast("info", "No character", "Create a character in Valheim first, then pick it here.");
      return;
    }

    var character = _characters[characterIndex];
    var instance = setupIndex >= 1 && setupIndex <= _app.InstanceList.Count ? _app.InstanceList[setupIndex - 1] : null;
    var isServer = destination.Kind == DestinationViewModel.ServerKind;

    _app.SettingsModel.PlayChoices[destination.Key] = new PlayChoice
    {
      Character = character.FileName,
      InstanceId = instance?.Id,
      Password = isServer && password.Length > 0 ? password : null,
      LastPlayedAt = DateTimeOffset.UtcNow,
    };
    _app.SaveSettings();
    destination.RaiseChoice();

    // The character is set for a server too: +connect opens the character selection, which starts on it.
    var prefs = new Dictionary<string, string> { [ValheimPrefs.Character] = character.FileName };
    if (!isServer)
    {
      prefs[ValheimPrefs.World] = destination.Name;
    }

    var arguments = isServer ? GameLauncher.JoinArguments(destination.Address, password) : [];
    _app.Launch(instance, new DirectPlay(destination.Name, arguments, prefs));
  }

  internal void SetImage(DestinationViewModel destination, string file)
  {
    string name;
    try
    {
      name = Images.Import(file);
    }
    catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
    {
      Log.Error($"Setting the picture for {destination.Name} failed", ex);
      _app.Toast("error", "Could not use that picture", ex.Message);
      return;
    }

    // The old copy goes only once the new one is in place, so a failed pick keeps the old picture.
    Images.Delete(_app.SettingsModel.PlayImages.GetValueOrDefault(destination.Key));
    _app.SettingsModel.PlayImages[destination.Key] = name;
    _app.SaveSettings();
    destination.RaiseImage();
  }

  internal void RemoveImage(DestinationViewModel destination)
  {
    if (_app.SettingsModel.PlayImages.Remove(destination.Key, out var name))
    {
      Images.Delete(name);
      _app.SaveSettings();
      destination.RaiseImage();
    }
  }
}

/// <summary>One server or world on the Play page, with what it was last played with.</summary>
public sealed class DestinationViewModel : ViewModel
{
  internal const string ServerKind = "server";
  internal const string WorldKind = "world";

  private readonly PlayViewModel _play;
  private ServerStatus? _status;
  private bool _statusKnown;

  private DestinationViewModel(PlayViewModel play, string key, string kind, string name, string address, string detail)
  {
    _play = play;
    Key = key;
    Kind = kind;
    Name = name;
    Address = address;
    Detail = detail;
  }

  internal static DestinationViewModel ForServer(PlayViewModel play, ValheimServer server) =>
    new(play, server.Key, ServerKind, server.Name, server.Address, server.Address)
    {
      IsFavorite = server.IsFavorite,
      IsRecent = server.IsRecent,
    };

  internal static DestinationViewModel ForWorld(PlayViewModel play, ValheimWorld world) =>
    new(play, world.Key, WorldKind, world.Name, "", $"{SourceName(world.Source)} · saved {Format.Ago(new DateTimeOffset(world.LastWriteUtc))}");

  private static string SourceName(SaveSource source) => source switch
  {
    SaveSource.Cloud => "Steam Cloud",
    SaveSource.Local => "Local",
    _ => "Old save folder",
  };

  internal string Key { get; }

  /// <summary><c>server</c> or <c>world</c>.</summary>
  [NotifySignal]
  public string Kind { get; }

  [NotifySignal]
  public string Name { get; }

  /// <summary>A server's <c>host:port</c>. Empty for worlds.</summary>
  [NotifySignal]
  public string Address { get; }

  [NotifySignal]
  public string Detail { get; }

  [NotifySignal]
  public bool IsFavorite { get; private init; }

  [NotifySignal]
  public bool IsRecent { get; private init; }

  /// <summary>
  /// A server's players as of the last query: "2/10 online", "Offline" when it didn't answer, and empty
  /// before the first answer and for worlds.
  /// </summary>
  [NotifySignal]
  public string StatusText => !_statusKnown ? "" : _status is { } s ? $"{s.Players}/{s.MaxPlayers} online" : "Offline";

  [NotifySignal]
  public bool IsOnline => _status is not null;

  /// <summary>The tooltip on the status: who is playing, or why that isn't known.</summary>
  [NotifySignal]
  public string StatusTip => _status switch
  {
    null => "The server didn't answer. It may be off, or its query port (the game port + 1) may be closed.",
    { Players: 0 } => "Nobody is playing right now.",
    { PlayerNames.Count: > 0 } s => string.Join("\n", s.PlayerNames),
    { Players: 1 } => "1 player. Valheim servers don't share player names.",
    var s => $"{s.Players} players. Valheim servers don't share player names.",
  };

  /// <summary>A file:// URL of the picture chosen for it, or empty to show the server or world icon.</summary>
  [NotifySignal]
  public string ImageSource =>
    _play.Images.PathOf(_play.App.SettingsModel.PlayImages.GetValueOrDefault(Key)) is { } path ? new Uri(path).AbsoluteUri : "";

  [NotifySignal]
  public bool HasImage => ImageSource.Length > 0;

  private PlayChoice? Choice => _play.App.SettingsModel.PlayChoices.GetValueOrDefault(Key);

  private int ChoiceCharacterIndex => Choice is { } choice ? IndexOfCharacter(choice.Character) : -1;

  // A remembered instance that was deleted since counts as no choice, rather than silently going vanilla.
  private int ChoiceSetupIndex => Choice is { } choice ? _play.SetupIndexOf(choice.InstanceId) : -1;

  /// <summary>Whether Play can start right away, with a character and setup that both still exist.</summary>
  [NotifySignal]
  public bool HasChoice => ChoiceCharacterIndex >= 0 && ChoiceSetupIndex >= 0;

  /// <summary>"Lukaz · Vanilla Valheim", or a hint when there is nothing to remember yet.</summary>
  [NotifySignal]
  public string ChoiceText
  {
    get
    {
      if (Choice is null)
      {
        return "Not played from LaunchHeim yet";
      }

      if (!HasChoice)
      {
        return ChoiceCharacterIndex < 0 ? "The character played last is gone" : "The instance played last is gone";
      }

      var setup = ChoiceSetupIndex == 0 ? PlayViewModel.VanillaName : _play.App.InstanceList[ChoiceSetupIndex - 1].Name;
      return $"{_play.CharacterList[ChoiceCharacterIndex].DisplayName} · {setup}";
    }
  }

  /// <summary>The character the dialog starts on: the remembered one, else the one Valheim has selected.</summary>
  [NotifySignal]
  public int CharacterIndex => ChoiceCharacterIndex >= 0 ? ChoiceCharacterIndex : Math.Max(0, IndexOfCharacter(_play.GameCharacter));

  /// <summary>The setup the dialog starts on: the remembered one, else the instance selected in LaunchHeim.</summary>
  [NotifySignal]
  public int SetupIndex => ChoiceSetupIndex >= 0 ? ChoiceSetupIndex : Math.Max(0, _play.SetupIndexOf(_play.App.SelectedInstance?.Id));

  [NotifySignal]
  public string Password => Choice?.Password ?? "";

  /// <summary>Plays with the remembered choice. QML opens the dialog instead when there is none.</summary>
  public void Play()
  {
    if (HasChoice)
    {
      _play.Launch(this, ChoiceCharacterIndex, ChoiceSetupIndex, Password);
    }
  }

  // Not trimmed: a space may be part of a server password.
  public void PlayWith(int characterIndex, int setupIndex, string password) =>
    _play.Launch(this, characterIndex, setupIndex, password ?? "");

  /// <summary>Uses the image file the user picked (a file:// URL from the dialog) as its picture.</summary>
  public void SetImage(string fileUrl) => _play.SetImage(this, AppViewModel.LocalPath(fileUrl));

  /// <summary>Goes back to the server or world icon.</summary>
  public void RemoveImage() => _play.RemoveImage(this);

  internal void RaiseChoice()
  {
    Raise(nameof(HasChoice));
    Raise(nameof(ChoiceText));
    Raise(nameof(CharacterIndex));
    Raise(nameof(SetupIndex));
    Raise(nameof(Password));
  }

  internal async Task QueryStatusAsync()
  {
    _status = await ServerQuery.QueryAsync(Address, ServerQuery.DefaultTimeout);
    _statusKnown = true;
    Raise(nameof(StatusText));
    Raise(nameof(IsOnline));
    Raise(nameof(StatusTip));
  }

  internal void RaiseImage()
  {
    Raise(nameof(ImageSource));
    Raise(nameof(HasImage));
  }

  private int IndexOfCharacter(string fileName)
  {
    for (var i = 0; i < _play.CharacterList.Count; i++)
    {
      if (_play.CharacterList[i].FileName == fileName)
      {
        return i;
      }
    }

    return -1;
  }
}
