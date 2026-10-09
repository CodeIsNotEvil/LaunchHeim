using CINE.LaunchHeim.Core.LocalSend;
using CINE.LaunchHeim.Core.Logging;
using CINE.LaunchHeim.Core.Packs;
using CINE.LaunchHeim.Core.Saves;
using Qml.Net;

namespace CINE.LaunchHeim.Desktop.ViewModels;

/// <summary>
/// Syncing with the companion app over LocalSend: sending instances and the server list to a phone,
/// and taking edited mod lists back. Settings → Phone sync and the Send to phone dialog bind to it.
/// </summary>
/// <remarks>
/// The node runs while receiving is switched on, and while the send dialog is open (to find phones),
/// so LaunchHeim doesn't keep a port open nobody asked for.
/// </remarks>
public sealed class PhoneSyncViewModel : ViewModel, IDisposable
{
  // A pack lists mods and carries configs; hundreds of MB would be local mods nobody meant to send.
  private const long MaxPackBytes = 256L * 1024 * 1024;

  private readonly AppViewModel _app;
  private readonly LocalSendNode _node;
  private readonly Queue<PendingUpdate> _updates = new();
  private List<PeerViewModel> _peers = [];
  private bool _running;
  private bool _sendVisible;
  private bool _sending;
  private string _sendStatus = "";
  private string _sendTarget = "";
  private string _firewallCommand = "";
  private IncomingOffer? _offer;
  private PendingUpdate? _update;

  private sealed record PendingUpdate(string File, PackManifest Manifest, InstanceViewModel Target, PackChanges Changes, string From);

  public PhoneSyncViewModel(AppViewModel app)
  {
    _app = app;
    if (string.IsNullOrEmpty(app.SettingsModel.PhoneSyncFingerprint))
    {
      app.SettingsModel.PhoneSyncFingerprint = LocalSendNode.NewFingerprint();
      app.SaveSettings();
    }

    _node = new LocalSendNode(
      () => new LocalSendNode.Identity(Alias, app.SettingsModel.PhoneSyncFingerprint!),
      // Under cache/tmp, which every start clears, so a transfer cut short leaves nothing behind.
      Path.Combine(app.Paths.CacheDirectory, "tmp", "localsend"),
      file => file.FileName.EndsWith(PackService.FileExtension, StringComparison.OrdinalIgnoreCase) && file.Size <= MaxPackBytes);

    // The node raises these on thread-pool threads; Qml.Net only takes signals on the Qt thread.
    _node.PeersChanged += () => OnQt(RefreshPeers);
    _node.OfferReceived += offer => OnQt(() => ShowOffer(offer));
    _node.OfferClosed += offer => OnQt(() => CloseOffer(offer));
    _node.FilesReceived += files => OnQt(() => _ = ReceiveAsync(files));

    if (app.SettingsModel.PhoneSyncEnabled)
    {
      StartNode();
    }
  }

  [NotifySignal]
  public bool Enabled => _app.SettingsModel.PhoneSyncEnabled;

  [NotifySignal]
  public bool Running { get => _running; private set => Set(ref _running, value); }

  [NotifySignal]
  public string Alias => string.IsNullOrWhiteSpace(_app.SettingsModel.PhoneSyncAlias) ? Environment.MachineName : _app.SettingsModel.PhoneSyncAlias;

  [NotifySignal]
  public string StatusText => !Running
    ? Enabled ? "Could not start. Another program may be using the network port." : "Off"
    : $"Visible as “{Alias}”, port {_node.Port}";

  /// <summary>
  /// The terminal command that lets phones through ufw, while receiving is on and ufw would drop them;
  /// otherwise empty (<see cref="Firewall.UfwCommand"/>).
  /// </summary>
  [NotifySignal]
  public string FirewallCommand { get => _firewallCommand; private set => Set(ref _firewallCommand, value); }

  /// <summary>Windows Firewall can't be read without an administrator, so the button is always offered there.</summary>
  [NotifySignal]
  public bool CanAllowFirewall => OperatingSystem.IsWindows();

  [NotifySignal]
  public List<PeerViewModel> Peers { get => _peers; private set => Set(ref _peers, value); }

  [NotifySignal]
  public int PeerCount => _peers.Count;

  [NotifySignal]
  public bool SendVisible { get => _sendVisible; private set => Set(ref _sendVisible, value); }

  [NotifySignal]
  public bool Sending { get => _sending; private set => Set(ref _sending, value); }

  [NotifySignal]
  public string SendStatus { get => _sendStatus; private set => Set(ref _sendStatus, value); }

  /// <summary>The instance the dialog sends, or empty when it sends the server list.</summary>
  [NotifySignal]
  public string SendTarget { get => _sendTarget; private set => Set(ref _sendTarget, value); }

  [NotifySignal]
  public string SendTitle => SendInstance is { } instance ? $"Send {instance.Name} to a phone" : "Send the server list to a phone";

  [NotifySignal]
  public string SendText => SendInstance is not null
    ? "The companion app keeps a copy of this mod list to edit. Sent again, it updates that copy instead of adding another, and an edited list sent back from the phone updates this instance after you've seen the changes."
    : "Valheim's Favorites and Recent servers, so the companion app can show who's online. It replaces the list the phone had.";

  private InstanceViewModel? SendInstance => _sendTarget.Length == 0 ? null : _app.InstanceList.FirstOrDefault(i => i.Id == _sendTarget);

  [NotifySignal]
  public bool OfferVisible => _offer is not null;

  [NotifySignal]
  public string OfferTitle => _offer is null ? "" : $"{_offer.Sender.Alias} wants to send";

  [NotifySignal]
  public string OfferText => _offer is null ? "" : string.Join('\n', _offer.Files.Select(f => "• " + Path.GetFileNameWithoutExtension(f.FileName)));

  [NotifySignal]
  public bool UpdateVisible => _update is not null;

  [NotifySignal]
  public string UpdateTitle => _update is null ? "" : $"Update {_update.Target.Name}?";

  [NotifySignal]
  public string UpdateText => _update is null ? "" : Describe(_update);

  public void SetEnabled(bool enabled)
  {
    _app.SettingsModel.PhoneSyncEnabled = enabled;
    _app.SaveSettings();
    Raise(nameof(Enabled));
    if (enabled)
    {
      StartNode();
    }
    else if (!SendVisible)
    {
      StopNode();
    }

    Raise(nameof(StatusText));
  }

  public void SetAlias(string alias)
  {
    _app.SettingsModel.PhoneSyncAlias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
    _app.SaveSettings();
    Raise(nameof(Alias));
    Raise(nameof(StatusText));
    // Others only learn the new name from the next announcement.
    _node.Scan();
  }

  public void Scan() => _node.Scan();

  /// <summary>
  /// Opens the send dialog for one instance (its page) or, with an empty id, for the server list (the
  /// Play page). One thing per dialog, from where it lives, rather than one dialog with every instance
  /// and the server list to tick.
  /// </summary>
  /// <param name="instanceId">The instance to send. Qml.Net turns an empty string into null.</param>
  public void OpenSend(string? instanceId)
  {
    SendTarget = instanceId ?? "";
    Raise(nameof(SendTitle));
    Raise(nameof(SendText));
    SendStatus = "";
    SendVisible = true;
    StartNode();
    _node.Scan();
  }

  public void CloseSend()
  {
    SendVisible = false;
    if (!Enabled)
    {
      StopNode();
    }
  }

  /// <summary>Sends what the dialog was opened for (<see cref="SendTarget"/>) to <paramref name="peerId"/>.</summary>
  public async void Send(string? peerId)
  {
    if (Sending || _node.Peers.FirstOrDefault(p => p.Id == peerId) is not { } peer)
    {
      return;
    }

    var instance = SendInstance;
    if (instance is null && _sendTarget.Length > 0)
    {
      SendStatus = "That instance is gone.";
      return;
    }

    List<InstanceViewModel> instances = instance is null ? [] : [instance];
    var servers = instance is null;

    Sending = true;
    SendStatus = $"Waiting for {peer.Info.Alias} to accept…";
    var folder = Path.Combine(_app.Paths.CacheDirectory, "tmp", "send-" + Guid.NewGuid().ToString("N"));
    try
    {
      var files = await Task.Run(() => WriteFiles(folder, instances, servers));
      var result = await _node.SendAsync(peer, files, CancellationToken.None);
      Log.Info($"Sending {files.Count} file(s) to {peer.Info.Alias} ({peer.Address}): {result.Outcome} {result.Message}".TrimEnd());
      switch (result.Outcome)
      {
        case SendOutcome.Sent:
          _app.Toast("success", $"Sent to {peer.Info.Alias}", Summary(instances.Count, servers));
          CloseSend();
          break;
        case SendOutcome.Declined:
          SendStatus = $"{peer.Info.Alias} declined.";
          break;
        default:
          SendStatus = result.Message;
          break;
      }
    }
    catch (Exception ex)
    {
      Log.Error("Sending to a phone failed", ex);
      SendStatus = ex.Message;
    }
    finally
    {
      Sending = false;
      try
      {
        Directory.Delete(folder, recursive: true);
      }
      catch (IOException)
      {
      }
    }
  }

  private List<OutgoingFile> WriteFiles(string folder, IReadOnlyList<InstanceViewModel> instances, bool servers)
  {
    Directory.CreateDirectory(folder);
    var files = new List<OutgoingFile>();
    foreach (var instance in instances)
    {
      // Each in a folder of its own, because two instances may well export to the same file name.
      var directory = Path.Combine(folder, instance.Id);
      Directory.CreateDirectory(directory);
      var path = Path.Combine(directory, instance.PackFileName);
      _app.Packs.Export(instance.Model, path);
      files.Add(new OutgoingFile(path, instance.PackFileName, "application/zip"));
    }

    if (servers)
    {
      var path = Path.Combine(folder, ServerListExport.FileName);
      ServerListExport.From(_app.Play.Saves.Servers()).Write(path);
      files.Add(new OutgoingFile(path, ServerListExport.FileName, "application/json"));
    }

    return files;
  }

  public void AcceptOffer() => _offer?.Accept();

  public void DeclineOffer() => _offer?.Decline();

  /// <summary>Applies the shown pack to its instance.</summary>
  public void ApplyUpdate()
  {
    if (_update is { } update)
    {
      update.Target.ApplyPack(update.Manifest, update.From);
    }

    NextUpdate();
  }

  /// <summary>Keeps the instance as it is and imports the pack next to it.</summary>
  public void ImportUpdateAsCopy()
  {
    if (_update is { } update)
    {
      _ = _app.ImportPackFile(update.File);
    }

    NextUpdate();
  }

  public void SkipUpdate() => NextUpdate();

  public void Dispose() => _node.Dispose();

  private void StartNode()
  {
    if (_node.IsRunning)
    {
      return;
    }

    try
    {
      _node.Start();
      Log.Info($"Phone sync is listening on port {_node.Port} as {Alias}.");
    }
    catch (System.Net.Sockets.SocketException ex)
    {
      Log.Error("Phone sync could not start", ex);
      _app.Toast("error", "Phone sync could not start", ex.Message);
    }

    Running = _node.IsRunning;
    Raise(nameof(StatusText));
    CheckFirewall();
  }

  private void StopNode()
  {
    _node.Stop();
    Running = false;
    Raise(nameof(StatusText));
    CheckFirewall();
  }

  /// <summary>Looks at ufw again, after the command was run in a terminal.</summary>
  public void CheckFirewall() =>
    FirewallCommand = Running && OperatingSystem.IsLinux() ? Firewall.UfwCommand() ?? "" : "";

  /// <summary>Adds the Windows Firewall rule, after Windows asks for an administrator.</summary>
  public async void AllowFirewall()
  {
    try
    {
      var program = Environment.ProcessPath ?? throw new InvalidOperationException("LaunchHeim's own path is unknown.");
      if (await Firewall.AllowOnWindowsAsync(program) is { } error)
      {
        Log.Info($"Windows Firewall was not changed: {error}");
        _app.Toast("error", "Windows Firewall was not changed", error);
        return;
      }

      Log.Info($"Windows Firewall now lets phones reach {program}.");
      _app.Toast("success", "Windows Firewall allows phone sync", "Phones on your network can reach LaunchHeim now.");
    }
    catch (Exception ex)
    {
      // async void from QML: anything that escapes takes the app down.
      Log.Error("Changing Windows Firewall failed", ex);
      _app.Toast("error", "Windows Firewall was not changed", ex.Message);
    }
  }

  private void RefreshPeers()
  {
    Peers = _node.Peers.Select(p => new PeerViewModel(p)).ToList();
    Raise(nameof(PeerCount));
  }

  private void ShowOffer(IncomingOffer offer)
  {
    _offer = offer;
    RaiseOffer();
    // Bring the window up: someone is waiting at the phone.
    _app.Activate();
  }

  private void CloseOffer(IncomingOffer offer)
  {
    if (_offer == offer)
    {
      _offer = null;
      RaiseOffer();
    }
  }

  private void RaiseOffer()
  {
    Raise(nameof(OfferVisible));
    Raise(nameof(OfferTitle));
    Raise(nameof(OfferText));
  }

  /// <summary>
  /// A pack of an instance LaunchHeim has goes to the update dialog, so a change made on the phone
  /// (especially a removal) is seen before it happens. Any other pack is imported as a new instance.
  /// </summary>
  private async Task ReceiveAsync(ReceivedFiles received)
  {
    foreach (var file in received.Files)
    {
      PackContents contents;
      try
      {
        contents = await Task.Run(() => PackService.Read(file));
      }
      catch (Exception ex) when (ex is InvalidDataException or IOException)
      {
        _app.Toast("error", $"{Path.GetFileName(file)} from {received.Sender.Alias}", ex.Message);
        continue;
      }

      var linked = PackService.FindLinked(_app.InstanceList.Select(i => i.Model), contents.Manifest.InstanceId);
      var target = linked is null ? null : _app.InstanceList.FirstOrDefault(i => i.Model == linked);
      if (target is null)
      {
        await _app.ImportPackFile(file);
        continue;
      }

      var changes = PackService.Compare(target.Model, contents.Manifest);
      if (changes.IsEmpty)
      {
        _app.Toast("info", $"{target.Name} is up to date", $"The list from {received.Sender.Alias} has no changes.");
        continue;
      }

      _updates.Enqueue(new PendingUpdate(file, contents.Manifest, target, changes, received.Sender.Alias));
    }

    if (_update is null)
    {
      NextUpdate();
    }
  }

  private void NextUpdate()
  {
    _update = _updates.TryDequeue(out var next) ? next : null;
    Raise(nameof(UpdateVisible));
    Raise(nameof(UpdateTitle));
    Raise(nameof(UpdateText));
  }

  private static string Describe(PendingUpdate update)
  {
    var lines = new List<string> { $"{update.From} sent a changed mod list for this instance." };
    lines.AddRange(update.Changes.Added.Select(m => $"+ Add {Name(m.Name, m.Id)} {m.Version}".TrimEnd()));
    lines.AddRange(update.Changes.Changed.Select(c => $"↑ {c.Mod.Name} {c.Mod.Version} → {c.To.Version}"));
    lines.AddRange(update.Changes.Removed.Select(m => $"− Remove {m.Name}"));
    lines.AddRange(update.Changes.Toggled.Select(t => $"{(t.Enabled ? "● Turn on" : "○ Turn off")} {t.Mod.Name}"));
    lines.Add("");
    lines.Add("Mods are downloaded at these versions. Configs and launch options stay as they are.");
    return string.Join('\n', lines);
  }

  private static string Name(string name, string id) => string.IsNullOrWhiteSpace(name) ? id : name;

  private static string Summary(int instances, bool servers) =>
    (instances, servers) switch
    {
      (0, _) => "The server list.",
      (1, false) => "1 instance.",
      (1, true) => "1 instance and the server list.",
      (_, false) => $"{instances} instances.",
      _ => $"{instances} instances and the server list.",
    };

  private static void OnQt(Action action) => Program.Dispatch(() =>
  {
    action();
    return Task.CompletedTask;
  });
}

/// <summary>A device in the send dialog.</summary>
public sealed class PeerViewModel(LocalSendPeer peer) : ViewModel
{
  [NotifySignal]
  public string Id => peer.Id;

  [NotifySignal]
  public string Alias => peer.Info.Alias;

  [NotifySignal]
  public string Detail => $"{(peer.Info.IsLaunchHeim ? "LaunchHeim" : peer.Info.DeviceModel ?? "LocalSend")} · {peer.Address}";

  [NotifySignal]
  public bool IsLaunchHeim => peer.Info.IsLaunchHeim;

  [NotifySignal]
  public bool IsPhone => peer.Info.DeviceType == "mobile";
}
