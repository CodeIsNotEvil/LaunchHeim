using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CINE.LaunchHeim.Core.Saves;

/// <summary>Who is on a server right now, as the server reports it.</summary>
/// <param name="PlayerNames">The names the server gave, possibly fewer than <paramref name="Players"/> or none.</param>
public sealed record ServerStatus(int Players, int MaxPlayers, IReadOnlyList<string> PlayerNames);

/// <summary>Asks a Valheim dedicated server how many players are on it, and who, with Steam's server query (A2S).</summary>
/// <remarks>
/// <para>
/// A dedicated server registers with Steam as a game server, and Steam's library answers A2S on the
/// query port, which Valheim puts right after the game port (2457 for the default 2456). That is what
/// the in-game server browser and sites like BattleMetrics read. A2S_INFO carries the player count,
/// A2S_PLAYER the names.
/// </para>
/// <para>
/// Names are only there if the server hands them to Steam, and Valheim's own server doesn't: checked
/// against a live server (2026-10-04), A2S_PLAYER lists every player with an empty name, a score of 0
/// and the time connected. The count is right, and names show up only from a server that registers
/// them some other way, such as through a mod. A server started with <c>-crossplay</c> goes through
/// PlayFab instead and may not answer at all, which looks like offline.
/// </para>
/// <para>
/// Both requests follow the challenge rule Valve added in 2020: the server may answer with a challenge
/// (<c>0x41</c>), and the request is sent again with it appended. Answers split over several packets
/// (<c>0xFFFFFFFE</c>) only happen with dozens of players, far above Valheim's ten, and are treated as no names.
/// </para>
/// </remarks>
public static class ServerQuery
{
  public const int DefaultGamePort = 2456;
  public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

  private const byte InfoRequest = 0x54;
  private const byte InfoResponse = 0x49;
  private const byte PlayerRequest = 0x55;
  private const byte PlayerResponse = 0x44;
  private const byte ChallengeResponse = 0x41;
  private const int SinglePacket = -1;

  /// <returns>The status, or null when the server didn't answer in time or couldn't be found.</returns>
  public static async Task<ServerStatus?> QueryAsync(string address, TimeSpan timeout, CancellationToken cancellationToken = default)
  {
    if (QueryEndpoint(address) is not { } endpoint)
    {
      return null;
    }

    var (host, port) = endpoint;

    using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutSource.CancelAfter(timeout);
    var token = timeoutSource.Token;
    try
    {
      var ip = IPAddress.TryParse(host, out var literal) ? literal : await ResolveAsync(host, token);
      if (ip is null)
      {
        return null;
      }

      using var udp = new UdpClient(ip.AddressFamily);
      udp.Connect(ip, port);

      var info = await RequestAsync(udp, InfoPacket([]), InfoResponse, challenge => InfoPacket(challenge), token);
      if (info is null || ParseInfo(info) is not { } counts)
      {
        return null;
      }

      var (players, maxPlayers) = counts;

      // The count alone is worth showing, so a server that ignores A2S_PLAYER still gets one.
      IReadOnlyList<string> names = [];
      if (players > 0)
      {
        try
        {
          var list = await RequestAsync(udp, PlayerPacket([0xFF, 0xFF, 0xFF, 0xFF]), PlayerResponse, PlayerPacket, token);
          names = list is null ? [] : ParsePlayers(list);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
      }

      return new ServerStatus(players, maxPlayers, names);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      return null;
    }
    catch (SocketException)
    {
      // Refused (nothing on the port, reported back over ICMP) or no route: the server is off.
      return null;
    }
  }

  /// <summary>The host and query port for a server's <c>host:port</c>, <c>[v6]:port</c> or bare host.</summary>
  internal static (string Host, int Port)? QueryEndpoint(string address)
  {
    address = address.Trim();
    string host;
    var gamePort = DefaultGamePort;

    if (address.StartsWith('['))
    {
      var end = address.IndexOf(']');
      if (end < 0)
      {
        return null;
      }

      host = address[1..end];
      var rest = address[(end + 1)..];
      if (rest.StartsWith(':') && !int.TryParse(rest[1..], out gamePort))
      {
        return null;
      }
    }
    else
    {
      var colon = address.LastIndexOf(':');
      // More than one colon without brackets is a bare IPv6 address, which has no port.
      if (colon >= 0 && address.IndexOf(':') == colon)
      {
        host = address[..colon];
        if (!int.TryParse(address[(colon + 1)..], out gamePort))
        {
          return null;
        }
      }
      else
      {
        host = address;
      }
    }

    return host.Length > 0 && gamePort is > 0 and < 65535 ? (host, gamePort + 1) : null;
  }

  // IPv4 first: most home servers are only reachable that way, even where DNS also has an AAAA record.
  private static async Task<IPAddress?> ResolveAsync(string host, CancellationToken token)
  {
    var addresses = await Dns.GetHostAddressesAsync(host, token);
    return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
  }

  /// <summary>Sends a request and returns the answer's payload after its type byte, answering one challenge on the way.</summary>
  private static async Task<byte[]?> RequestAsync(
    UdpClient udp, byte[] request, byte expected, Func<byte[], byte[]> withChallenge, CancellationToken token)
  {
    await udp.SendAsync(request, token);
    for (var attempt = 0; attempt < 2; attempt++)
    {
      var packet = (await udp.ReceiveAsync(token)).Buffer;
      if (packet.Length < 5 || BinaryPrimitives.ReadInt32LittleEndian(packet) != SinglePacket)
      {
        return null;
      }

      if (packet[4] == expected)
      {
        return packet[5..];
      }

      if (packet[4] != ChallengeResponse || packet.Length < 9 || attempt > 0)
      {
        return null;
      }

      await udp.SendAsync(withChallenge(packet[5..9]), token);
    }

    return null;
  }

  internal static byte[] InfoPacket(byte[] challenge) =>
    [0xFF, 0xFF, 0xFF, 0xFF, InfoRequest, .. "Source Engine Query\0"u8, .. challenge];

  internal static byte[] PlayerPacket(byte[] challenge) => [0xFF, 0xFF, 0xFF, 0xFF, PlayerRequest, .. challenge];

  /// <summary>Reads the player counts from an A2S_INFO payload (after the <c>0x49</c>).</summary>
  internal static (int Players, int MaxPlayers)? ParseInfo(byte[] payload)
  {
    try
    {
      using var reader = new BinaryReader(new MemoryStream(payload));
      reader.ReadByte(); // protocol
      ReadCString(reader); // name
      ReadCString(reader); // map
      ReadCString(reader); // folder
      ReadCString(reader); // game
      reader.ReadInt16(); // app id
      return (reader.ReadByte(), reader.ReadByte());
    }
    catch (EndOfStreamException)
    {
      return null;
    }
  }

  /// <summary>The non-empty names in an A2S_PLAYER payload (after the <c>0x44</c>).</summary>
  internal static IReadOnlyList<string> ParsePlayers(byte[] payload)
  {
    var names = new List<string>();
    try
    {
      using var reader = new BinaryReader(new MemoryStream(payload));
      var count = reader.ReadByte();
      for (var i = 0; i < count; i++)
      {
        reader.ReadByte(); // index
        var name = ReadCString(reader);
        reader.ReadInt32(); // score
        reader.ReadSingle(); // seconds connected
        if (!string.IsNullOrWhiteSpace(name))
        {
          names.Add(name);
        }
      }
    }
    catch (EndOfStreamException)
    {
      // A truncated list still has the names read so far.
    }

    return names;
  }

  private static string ReadCString(BinaryReader reader)
  {
    var bytes = new List<byte>();
    for (var b = reader.ReadByte(); b != 0; b = reader.ReadByte())
    {
      bytes.Add(b);
    }

    return Encoding.UTF8.GetString(bytes.ToArray());
  }
}
