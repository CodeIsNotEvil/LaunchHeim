using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CINE.LaunchHeim.Core.LocalSend;

/// <summary>
/// One LocalSend device: it announces itself, keeps the list of <see cref="Peers"/>, receives files
/// through <see cref="OfferReceived"/> and <see cref="FilesReceived"/>, and sends files with
/// <see cref="SendAsync"/>. The companion app's <c>LocalSendNode.kt</c> is the same thing for the phone.
/// </summary>
/// <remarks>
/// Events are raised on thread-pool threads; the UI marshals them to its own thread.
/// </remarks>
/// <param name="accepts">
/// Which offered files LaunchHeim wants at all. The others are left out of the answer to
/// <c>/prepare-upload</c>, which LocalSend's partial accept allows, and a transfer with none of them
/// is rejected without asking.
/// </param>
public sealed partial class LocalSendNode(
  Func<LocalSendNode.Identity> identity,
  string inbox,
  Func<FileDto, bool> accepts,
  int multicastPort = LocalSendProtocol.DefaultPort,
  int httpPort = LocalSendProtocol.DefaultPort,
  bool legacyDiscovery = true) : IDisposable
{
  /// <summary>Who this device is. The fingerprint is random per install and stays the same.</summary>
  public sealed record Identity(string Alias, string Fingerprint, string DeviceType = "desktop");

  private static readonly TimeSpan OfferTimeout = TimeSpan.FromMinutes(2);
  private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(10);

  private readonly object _gate = new();
  private readonly ConcurrentDictionary<string, Session> _sessions = new();
  private readonly ConcurrentDictionary<string, LocalSendPeer> _peers = new();
  private MiniHttpServer? _server;
  private UdpClient? _multicast;
  private CancellationTokenSource? _stop;
  private IncomingOffer? _pending;

  public event Action? PeersChanged;
  public event Action<IncomingOffer>? OfferReceived;
  public event Action<IncomingOffer>? OfferClosed;
  public event Action<ReceivedFiles>? FilesReceived;

  public bool IsRunning => _server is not null;

  /// <summary>The HTTP port actually bound, or -1 while stopped.</summary>
  public int Port => _server?.Port ?? -1;

  public IReadOnlyList<LocalSendPeer> Peers => _peers.Values
    .OrderByDescending(p => p.Info.IsLaunchHeim)
    .ThenBy(p => p.Info.Alias, StringComparer.CurrentCultureIgnoreCase)
    .ToList();

  public DeviceInfo Info(bool? announce = null)
  {
    var me = identity();
    return new DeviceInfo
    {
      Alias = me.Alias,
      DeviceModel = LocalSendProtocol.DeviceModel,
      DeviceType = me.DeviceType,
      Fingerprint = me.Fingerprint,
      Port = Port > 0 ? Port : httpPort,
      Protocol = "http",
      Download = false,
      Announce = announce,
    };
  }

  public void Start()
  {
    lock (_gate)
    {
      if (_server is not null)
      {
        return;
      }

      _stop = new CancellationTokenSource();
      var server = new MiniHttpServer(HandleAsync);
      server.Start(httpPort);
      _server = server;

      try
      {
        _multicast = OpenMulticast();
        _ = ListenAsync(_multicast, _stop.Token);
      }
      catch (SocketException)
      {
        // Another app holds the port without sharing it. Discovery still works one way: announcements
        // go out, and devices answer them over HTTP.
        _multicast = null;
      }
    }

    Scan();
  }

  public void Stop()
  {
    IncomingOffer? pending;
    lock (_gate)
    {
      if (_server is null)
      {
        return;
      }

      _stop?.Cancel();
      _multicast?.Dispose();
      _multicast = null;
      _server.Dispose();
      _server = null;
      pending = _pending;
      _pending = null;
    }

    pending?.Decline();
    foreach (var session in _sessions.Values)
    {
      TryDelete(session.Directory);
    }

    _sessions.Clear();
    _peers.Clear();
    PeersChanged?.Invoke();
  }

  public void Dispose() => Stop();

  /// <summary>
  /// Looks for devices again: forgets the list, announces three times, and if nobody answers, asks every
  /// address in the local /24 networks directly (LocalSend's "legacy" discovery, for Wi-Fi that drops multicast).
  /// </summary>
  public void Scan()
  {
    if (_stop?.Token is not { IsCancellationRequested: false } token)
    {
      return;
    }

    _peers.Clear();
    PeersChanged?.Invoke();
    _ = Task.Run(async () =>
    {
      for (var attempt = 0; attempt < 3; attempt++)
      {
        Announce();
        await Task.Delay(attempt == 0 ? 500 : 1500, token);
      }

      if (_peers.IsEmpty && legacyDiscovery)
      {
        await LegacyScanAsync(token);
      }
    }, token);
  }

  // ---- discovery ----------------------------------------------------------------------------------

  private UdpClient OpenMulticast()
  {
    var udp = new UdpClient { ExclusiveAddressUse = false };
    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    udp.Client.Bind(new IPEndPoint(IPAddress.Any, multicastPort));
    var group = IPAddress.Parse(LocalSendProtocol.MulticastGroup);
    var joined = false;
    foreach (var address in LanAddresses())
    {
      try
      {
        udp.JoinMulticastGroup(group, address);
        joined = true;
      }
      catch (SocketException)
      {
      }
    }

    if (!joined)
    {
      udp.JoinMulticastGroup(group);
    }

    return udp;
  }

  private async Task ListenAsync(UdpClient udp, CancellationToken stop)
  {
    while (!stop.IsCancellationRequested)
    {
      UdpReceiveResult packet;
      try
      {
        packet = await udp.ReceiveAsync(stop);
      }
      catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
      {
        return;
      }

      DeviceInfo? info;
      try
      {
        info = JsonSerializer.Deserialize<DeviceInfo>(packet.Buffer, LocalSendProtocol.Json);
      }
      catch (JsonException)
      {
        continue;
      }

      if (info is null || info.Fingerprint == identity().Fingerprint)
      {
        continue;
      }

      var address = packet.RemoteEndPoint.Address.ToString();
      Remember(info, address);
      if (info.Announce == true)
      {
        _ = AnswerAsync(new LocalSendPeer(info, address, DateTimeOffset.UtcNow), stop);
      }
    }
  }

  /// <summary>Answers an announcement over HTTP, as the protocol asks, or by multicast when that fails.</summary>
  private async Task AnswerAsync(LocalSendPeer peer, CancellationToken stop)
  {
    if (!await RegisterAsync(peer, stop))
    {
      SendMulticast(Info(announce: false));
    }
  }

  private async Task<bool> RegisterAsync(LocalSendPeer peer, CancellationToken cancellationToken)
  {
    try
    {
      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      timeout.CancelAfter(TimeSpan.FromSeconds(3));
      using var response = await QuickHttp.PostAsJsonAsync($"{peer.BaseUrl}/register", Info(), LocalSendProtocol.Json, timeout.Token);
      if (!response.IsSuccessStatusCode)
      {
        return false;
      }

      try
      {
        // The answer has no port or protocol, so the ones it was asked on are the right ones.
        if (await response.Content.ReadFromJsonAsync<DeviceInfo>(LocalSendProtocol.Json, timeout.Token) is { } answer)
        {
          Remember(answer with { Port = peer.Port, Protocol = peer.Info.Protocol }, peer.Address);
        }
      }
      catch (JsonException)
      {
      }

      return true;
    }
    catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
    {
      return false;
    }
  }

  private void Announce() => SendMulticast(Info(announce: true));

  private void SendMulticast(DeviceInfo info)
  {
    var bytes = JsonSerializer.SerializeToUtf8Bytes(info, LocalSendProtocol.Json);
    var group = new IPEndPoint(IPAddress.Parse(LocalSendProtocol.MulticastGroup), multicastPort);
    var addresses = LanAddresses();
    foreach (var address in addresses.Count == 0 ? [IPAddress.Any] : addresses)
    {
      try
      {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        if (!address.Equals(IPAddress.Any))
        {
          udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
        }

        udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 4);
        udp.Send(bytes, bytes.Length, group);
      }
      catch (SocketException)
      {
      }
    }
  }

  private async Task LegacyScanAsync(CancellationToken stop)
  {
    var own = LanAddresses();
    var targets = own
      .SelectMany(a => Enumerable.Range(1, 254).Select(host => new IPAddress([.. a.GetAddressBytes()[..3], (byte)host])))
      .Where(t => !own.Contains(t))
      .Distinct()
      .ToList();

    await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = 48, CancellationToken = stop }, async (target, token) =>
    {
      var peer = new LocalSendPeer(new DeviceInfo { Port = LocalSendProtocol.DefaultPort, Protocol = "http" }, target.ToString(), DateTimeOffset.UtcNow);
      // The LocalSend app usually runs HTTPS, LaunchHeim HTTP; one of the two answers.
      if (!await RegisterAsync(peer, token))
      {
        await RegisterAsync(peer with { Info = peer.Info with { Protocol = "https" } }, token);
      }
    });
  }

  private void Remember(DeviceInfo info, string address)
  {
    if (info.Fingerprint == identity().Fingerprint)
    {
      return;
    }

    var peer = new LocalSendPeer(info, address, DateTimeOffset.UtcNow);
    foreach (var stale in _peers.Where(p => p.Value.Info.Fingerprint == info.Fingerprint || (p.Value.Address == address && p.Value.Port == peer.Port)))
    {
      _peers.TryRemove(stale.Key, out _);
    }

    _peers[peer.Id] = peer;
    PeersChanged?.Invoke();
  }

  // ---- receiving ----------------------------------------------------------------------------------

  private sealed class Session(string id, DeviceInfo sender, string address, Dictionary<string, FileDto> files, Dictionary<string, string> tokens, string directory)
  {
    public string Id => id;
    public DeviceInfo Sender => sender;
    public string Address => address;
    public Dictionary<string, FileDto> Files => files;
    public Dictionary<string, string> Tokens => tokens;
    public string Directory => directory;
    public ConcurrentDictionary<string, string> Done { get; } = new();
    public DateTimeOffset LastActivity { get; set; } = DateTimeOffset.UtcNow;
  }

  private async Task<HttpResponse> HandleAsync(HttpRequest request, CancellationToken cancellationToken)
  {
    if (!request.Path.StartsWith(LocalSendProtocol.Api, StringComparison.Ordinal))
    {
      return new HttpResponse(404);
    }

    var route = request.Path[LocalSendProtocol.Api.Length..];
    return (route, request.Method) switch
    {
      ("/register", "POST") => await RegisterReceivedAsync(request, cancellationToken),
      ("/info", "GET") => Json(200, Info()),
      ("/prepare-upload", "POST") => await PrepareUploadAsync(request, cancellationToken),
      ("/upload", "POST") => await UploadAsync(request, cancellationToken),
      ("/cancel", "POST") => Cancel(request),
      _ => new HttpResponse(404),
    };
  }

  private async Task<HttpResponse> RegisterReceivedAsync(HttpRequest request, CancellationToken cancellationToken)
  {
    DeviceInfo? info;
    try
    {
      info = JsonSerializer.Deserialize<DeviceInfo>(await request.ReadTextAsync(cancellationToken), LocalSendProtocol.Json);
    }
    catch (JsonException)
    {
      return new HttpResponse(400);
    }

    if (info is null)
    {
      return new HttpResponse(400);
    }

    Remember(info, request.RemoteAddress.ToString());
    return Json(200, Info() with { Port = null, Protocol = null });
  }

  private async Task<HttpResponse> PrepareUploadAsync(HttpRequest request, CancellationToken cancellationToken)
  {
    PrepareUploadRequest? prepare;
    try
    {
      prepare = JsonSerializer.Deserialize<PrepareUploadRequest>(await request.ReadTextAsync(cancellationToken), LocalSendProtocol.Json);
    }
    catch (JsonException)
    {
      return new HttpResponse(400);
    }

    if (prepare?.Files is null || prepare.Info is null)
    {
      return new HttpResponse(400);
    }

    var wanted = prepare.Files.Values.Where(accepts).ToList();
    if (wanted.Count == 0)
    {
      return new HttpResponse(403);
    }

    // One transfer at a time. One that went quiet for ten minutes is given up.
    foreach (var stale in _sessions.Values.Where(s => DateTimeOffset.UtcNow - s.LastActivity > SessionTimeout))
    {
      _sessions.TryRemove(stale.Id, out _);
      TryDelete(stale.Directory);
    }

    var address = request.RemoteAddress.ToString();
    var offer = new IncomingOffer(prepare.Info, address, wanted);
    lock (_gate)
    {
      if (!_sessions.IsEmpty || _pending is not null)
      {
        return new HttpResponse(409);
      }

      _pending = offer;
    }

    Remember(prepare.Info, address);
    OfferReceived?.Invoke(offer);

    // The sender waits for this answer, as it does while a person taps Accept in LocalSend.
    bool accepted;
    try
    {
      accepted = await offer.Decision.WaitAsync(OfferTimeout, cancellationToken);
    }
    catch (TimeoutException)
    {
      accepted = false;
    }
    finally
    {
      lock (_gate)
      {
        if (_pending == offer)
        {
          _pending = null;
        }
      }

      OfferClosed?.Invoke(offer);
    }

    if (!accepted)
    {
      return new HttpResponse(403);
    }

    var id = Guid.NewGuid().ToString("N");
    var tokens = wanted.ToDictionary(f => f.Id, _ => Guid.NewGuid().ToString("N"));
    var directory = Path.Combine(inbox, id);
    Directory.CreateDirectory(directory);
    _sessions[id] = new Session(id, prepare.Info, address, wanted.ToDictionary(f => f.Id), tokens, directory);
    return Json(200, new PrepareUploadResponse(id, tokens));
  }

  private async Task<HttpResponse> UploadAsync(HttpRequest request, CancellationToken cancellationToken)
  {
    if (!request.Query.TryGetValue("sessionId", out var sessionId) || !_sessions.TryGetValue(sessionId, out var session))
    {
      return new HttpResponse(403);
    }

    if (!request.Query.TryGetValue("fileId", out var fileId) || !session.Files.TryGetValue(fileId, out var file))
    {
      return new HttpResponse(400);
    }

    if (!request.Query.TryGetValue("token", out var token) || session.Tokens[fileId] != token || session.Address != request.RemoteAddress.ToString())
    {
      return new HttpResponse(403);
    }

    session.LastActivity = DateTimeOffset.UtcNow;
    var target = Path.Combine(session.Directory, SafeName(file.FileName, fileId));
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    long size = 0;
    await using (var output = File.Create(target))
    {
      var buffer = new byte[64 * 1024];
      int read;
      while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
      {
        size += read;
        // Never more than was announced: that's the size the user accepted.
        if (size > file.Size)
        {
          break;
        }

        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        hash.AppendData(buffer, 0, read);
      }
    }

    if (size != file.Size)
    {
      File.Delete(target);
      return new HttpResponse(400);
    }

    if (file.Sha256 is { } expected && !expected.Equals(Convert.ToHexString(hash.GetHashAndReset()), StringComparison.OrdinalIgnoreCase))
    {
      File.Delete(target);
      return new HttpResponse(422);
    }

    session.Done[fileId] = target;
    if (session.Done.Count == session.Files.Count && _sessions.TryRemove(session.Id, out _))
    {
      FilesReceived?.Invoke(new ReceivedFiles(session.Sender, session.Files.Keys.Select(k => session.Done[k]).ToList()));
    }

    return new HttpResponse(200);
  }

  private HttpResponse Cancel(HttpRequest request)
  {
    if (request.Query.TryGetValue("sessionId", out var id) && _sessions.TryRemove(id, out var session))
    {
      TryDelete(session.Directory);
    }

    return new HttpResponse(200);
  }

  // ---- sending ------------------------------------------------------------------------------------

  /// <summary>Sends <paramref name="files"/> to <paramref name="peer"/>. Waits while the person there decides.</summary>
  public async Task<SendResult> SendAsync(LocalSendPeer peer, IReadOnlyList<OutgoingFile> files, CancellationToken cancellationToken)
  {
    var entries = new Dictionary<string, (OutgoingFile File, FileDto Dto)>();
    foreach (var file in files)
    {
      var id = Guid.NewGuid().ToString("N");
      entries[id] = (file, new FileDto(id, file.FileName, new FileInfo(file.Path).Length, file.FileType, await Sha256Async(file.Path, cancellationToken)));
    }

    PrepareUploadResponse? answer;
    try
    {
      var prepare = new PrepareUploadRequest(Info(), entries.ToDictionary(e => e.Key, e => e.Value.Dto));
      using var response = await SendHttp.PostAsJsonAsync($"{peer.BaseUrl}/prepare-upload", prepare, LocalSendProtocol.Json, cancellationToken);
      switch ((int)response.StatusCode)
      {
        case 200:
          answer = await response.Content.ReadFromJsonAsync<PrepareUploadResponse>(LocalSendProtocol.Json, cancellationToken);
          break;
        case 204:
          return new SendResult(SendOutcome.Sent);
        case 403:
          return new SendResult(SendOutcome.Declined, $"{peer.Info.Alias} declined.");
        case 409 or 429:
          return new SendResult(SendOutcome.Busy, $"{peer.Info.Alias} is busy with another transfer. Try again in a moment.");
        case 401:
          return new SendResult(SendOutcome.Failed, $"{peer.Info.Alias} asks for a PIN, which LaunchHeim can't send. Turn the PIN off in LocalSend.");
        default:
          return new SendResult(SendOutcome.Failed, $"{peer.Info.Alias} answered {(int)response.StatusCode}.");
      }
    }
    catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
    {
      return new SendResult(SendOutcome.Failed, $"Could not reach {peer.Info.Alias}: {ex.Message}");
    }

    if (answer is null)
    {
      return new SendResult(SendOutcome.Failed, $"{peer.Info.Alias} gave no answer.");
    }

    try
    {
      foreach (var (fileId, token) in answer.Files)
      {
        if (!entries.TryGetValue(fileId, out var entry))
        {
          continue;
        }

        await using var stream = File.OpenRead(entry.File.Path);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(entry.File.FileType);
        var url = $"{peer.BaseUrl}/upload?sessionId={Uri.EscapeDataString(answer.SessionId)}&fileId={Uri.EscapeDataString(fileId)}&token={Uri.EscapeDataString(token)}";
        using var response = await SendHttp.PostAsync(url, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
          await CancelAsync(peer, answer.SessionId);
          return new SendResult(SendOutcome.Failed, $"{peer.Info.Alias} refused {entry.File.FileName} ({(int)response.StatusCode}).");
        }
      }
    }
    catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
    {
      await CancelAsync(peer, answer.SessionId);
      return new SendResult(SendOutcome.Failed, $"The transfer to {peer.Info.Alias} broke off: {ex.Message}");
    }

    return new SendResult(SendOutcome.Sent);
  }

  private static async Task CancelAsync(LocalSendPeer peer, string sessionId)
  {
    try
    {
      using var _ = await QuickHttp.PostAsync($"{peer.BaseUrl}/cancel?sessionId={Uri.EscapeDataString(sessionId)}", null);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
    }
  }

  // ---- helpers ------------------------------------------------------------------------------------

  /// <summary>
  /// Up, multicast-capable interfaces' private IPv4 addresses: the LAN, Wi-Fi, a phone's hotspot. Container
  /// and VM bridges (Podman, Docker, libvirt) are left out; no phone is behind them.
  /// </summary>
  internal static IReadOnlyList<IPAddress> LanAddresses()
  {
    try
    {
      return NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up
          && n.SupportsMulticast
          && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
          && !VirtualInterface().IsMatch(n.Name))
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Select(u => u.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && IsPrivate(a))
        .ToList();
    }
    catch (NetworkInformationException)
    {
      return [];
    }
  }

  private static bool IsPrivate(IPAddress address)
  {
    var b = address.GetAddressBytes();
    return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
  }

  [GeneratedRegex("^(docker|br-|veth|virbr|podman|cni|vEthernet|vmnet|vboxnet)", RegexOptions.IgnoreCase)]
  private static partial Regex VirtualInterface();

  /// <summary>The sender picks the name, so only its last part is kept, and only harmless characters.</summary>
  internal static string SafeName(string fileName, string fallback)
  {
    var name = fileName.Replace('\\', '/');
    name = name[(name.LastIndexOf('/') + 1)..];
    name = UnsafeCharacters().Replace(name, "_").Trim('.', ' ');
    var safe = name.Length == 0 ? fallback : name;
    return safe.Length > 120 ? safe[..120] : safe;
  }

  [GeneratedRegex(@"[^A-Za-z0-9._ ()\-]")]
  private static partial Regex UnsafeCharacters();

  public static string NewFingerprint() => Guid.NewGuid().ToString("N");

  private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
  {
    await using var stream = File.OpenRead(path);
    return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
  }

  private static HttpResponse Json<T>(int status, T value) => new(status, JsonSerializer.Serialize(value, LocalSendProtocol.Json));

  private static void TryDelete(string directory)
  {
    try
    {
      Directory.Delete(directory, recursive: true);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
  }

  /// <summary>
  /// LocalSend's encrypted mode uses a self-signed certificate per device, so there is no chain to check.
  /// The fingerprint is meant to pin it, but the protocol doesn't say how it is computed, so it isn't
  /// checked and HTTPS only protects against passive listening. Only used for LocalSend peers on the LAN.
  /// </summary>
  private static readonly SocketsHttpHandler PeerHandler = new()
  {
    ConnectTimeout = TimeSpan.FromSeconds(3),
    SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
  };

  private static readonly HttpClient QuickHttp = new(PeerHandler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(3) };

  // prepare-upload waits for a person, and a pack with local mods can take a while on Wi-Fi.
  private static readonly HttpClient SendHttp = new(PeerHandler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(5) };
}
