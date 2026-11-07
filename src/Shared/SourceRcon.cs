// One file, compiled into two projects: LaunchHeim.Core (the client, .NET 10) and LaunchHeim.ServerPlugin
// (the server inside Valheim, .NET Framework 4.7.2 on Unity's Mono). Linking it instead of copying keeps
// both ends of the protocol in step. Hence the plain C#: no records, no spans, no implicit usings.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CINE.LaunchHeim.Shared;

/// <summary>One packet of Valve's Source RCON protocol.</summary>
/// <remarks>
/// Valheim's server has no remote console of its own. The mods that add one (Tristan-ValheimRcon,
/// AviiNL-rcon) all speak Source RCON, and so do the RCON tools people already have, so LaunchHeim's
/// server plugin speaks it too (decision 0020). The format: a little-endian int32 size, then the
/// request id, the type, an ASCII body and two NUL bytes. https://developer.valvesoftware.com/wiki/Source_RCON_Protocol
/// </remarks>
internal sealed class RconPacket(int id, int type, string body)
{
  public const int Response = 0;

  /// <summary>Both a command (client to server) and the answer to a login (server to client): the protocol reuses 2.</summary>
  public const int ExecCommand = 2;

  public const int AuthResponse = 2;
  public const int Auth = 3;

  /// <summary>The id a server answers a login with when the password was wrong.</summary>
  public const int AuthFailed = -1;

  public int Id { get; } = id;
  public int Type { get; } = type;
  public string Body { get; } = body;
}

internal static class SourceRcon
{
  /// <summary>
  /// The most a packet may carry. Source servers split longer answers at 4096 bytes and some clients
  /// refuse more, so the plugin splits its answers to stay below this.
  /// </summary>
  public const int MaxBodyBytes = 4000;

  /// <summary>Anything bigger is not RCON, or a broken stream; reading it would only allocate garbage.</summary>
  private const int MaxPacketSize = 64 * 1024;

  // UTF-8, not ASCII: player names are Unicode. Valve's clients only ever send ASCII commands, so they
  // read the same bytes either way.
  private static readonly Encoding Text = new UTF8Encoding(false);

  public static byte[] Encode(RconPacket packet)
  {
    var body = Text.GetBytes(packet.Body);
    var size = 4 + 4 + body.Length + 2;
    var buffer = new byte[4 + size];
    WriteInt(buffer, 0, size);
    WriteInt(buffer, 4, packet.Id);
    WriteInt(buffer, 8, packet.Type);
    Buffer.BlockCopy(body, 0, buffer, 12, body.Length);
    return buffer;
  }

  public static async Task WriteAsync(Stream stream, RconPacket packet, CancellationToken cancellationToken)
  {
    var bytes = Encode(packet);
    await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
  }

  /// <summary>The next packet, or null when the other side closed the connection.</summary>
  /// <exception cref="InvalidDataException">The stream doesn't carry RCON packets.</exception>
  public static async Task<RconPacket?> ReadAsync(Stream stream, CancellationToken cancellationToken)
  {
    var header = new byte[4];
    if (!await FillAsync(stream, header, cancellationToken).ConfigureAwait(false))
    {
      return null;
    }

    var size = BitConverter.ToInt32(header, 0);
    if (!BitConverter.IsLittleEndian)
    {
      size = ReverseInt(size);
    }

    if (size < 10 || size > MaxPacketSize)
    {
      throw new InvalidDataException($"Not an RCON packet (size {size}).");
    }

    var data = new byte[size];
    if (!await FillAsync(stream, data, cancellationToken).ConfigureAwait(false))
    {
      return null;
    }

    // The body ends at the first NUL. Some servers send a single NUL instead of two, which this accepts.
    var end = Array.IndexOf(data, (byte)0, 8);
    var bodyLength = (end < 0 ? data.Length : end) - 8;
    return new RconPacket(ReadInt(data, 0), ReadInt(data, 4), Text.GetString(data, 8, bodyLength));
  }

  /// <summary>Cuts an answer into bodies that each fit a packet, at line ends where it can.</summary>
  public static IList<string> Split(string text)
  {
    var parts = new List<string>();
    if (Text.GetByteCount(text) <= MaxBodyBytes)
    {
      parts.Add(text);
      return parts;
    }

    var current = new StringBuilder();
    var currentBytes = 0;
    foreach (var line in text.Split('\n'))
    {
      var piece = line + "\n";
      var pieceBytes = Text.GetByteCount(piece);
      if (currentBytes + pieceBytes > MaxBodyBytes && current.Length > 0)
      {
        parts.Add(current.ToString());
        current.Clear();
        currentBytes = 0;
      }

      // A single line longer than a packet is cut by characters; nothing in Valheim prints one.
      while (pieceBytes > MaxBodyBytes)
      {
        var take = MaxBodyBytes / 4;
        parts.Add(piece.Substring(0, take));
        piece = piece.Substring(take);
        pieceBytes = Text.GetByteCount(piece);
      }

      current.Append(piece);
      currentBytes += pieceBytes;
    }

    if (current.Length > 0)
    {
      parts.Add(current.ToString().TrimEnd('\n'));
    }

    return parts;
  }

  private static async Task<bool> FillAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
  {
    var read = 0;
    while (read < buffer.Length)
    {
      var n = await stream.ReadAsync(buffer, read, buffer.Length - read, cancellationToken).ConfigureAwait(false);
      if (n == 0)
      {
        return false;
      }

      read += n;
    }

    return true;
  }

  private static void WriteInt(byte[] buffer, int offset, int value)
  {
    var bytes = BitConverter.GetBytes(BitConverter.IsLittleEndian ? value : ReverseInt(value));
    Buffer.BlockCopy(bytes, 0, buffer, offset, 4);
  }

  private static int ReadInt(byte[] buffer, int offset)
  {
    var value = BitConverter.ToInt32(buffer, offset);
    return BitConverter.IsLittleEndian ? value : ReverseInt(value);
  }

  private static int ReverseInt(int value) =>
    (int)(((uint)value >> 24) | (((uint)value >> 8) & 0xFF00) | (((uint)value << 8) & 0xFF0000) | ((uint)value << 24));
}

/// <summary>
/// The server side of Source RCON: what LaunchHeim's server plugin runs inside Valheim, and what the
/// tests run LaunchHeim's client against.
/// </summary>
/// <remarks>
/// <para>
/// Every connection has to log in with the password before anything else; a wrong one closes it. Each
/// command goes to <c>execute</c>, and its answer comes back split into packets with the command's id.
/// </para>
/// <para>
/// An empty <see cref="RconPacket.Response"/> packet from the client is mirrored back unchanged. That is
/// how Source clients learn where a multi-packet answer ends: they send one right after the command and
/// stop reading when it comes back. Commands on one connection run one after the other, so the mirror
/// always follows the whole answer.
/// </para>
/// </remarks>
/// <param name="execute">Runs one command line and returns what to print. It may be called from any thread.</param>
/// <param name="log">Receives one line per login or failure, for the server's log.</param>
internal sealed class RconServer(IPAddress address, int port, string password, Func<string, Task<string>> execute, Action<string>? log = null) : IDisposable
{
  /// <summary>Long enough for a slow command; short enough that a forgotten connection doesn't stay forever.</summary>
  private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

  private readonly TcpListener _listener = new(address, port);
  private readonly CancellationTokenSource _stop = new();

  /// <summary>The port actually listened on, which differs from the one asked for when that was 0 (tests).</summary>
  public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

  public void Start()
  {
    _listener.Start();
    Task.Run(AcceptLoopAsync);
  }

  public void Dispose()
  {
    _stop.Cancel();
    _listener.Stop();
  }

  private async Task AcceptLoopAsync()
  {
    while (!_stop.IsCancellationRequested)
    {
      TcpClient client;
      try
      {
        client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
      }
      catch (Exception) when (_stop.IsCancellationRequested)
      {
        return;
      }
      catch (SocketException)
      {
        continue;
      }

      _ = Task.Run(() => ServeAsync(client));
    }
  }

  private async Task ServeAsync(TcpClient client)
  {
    var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
    using (client)
    using (var idle = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
    {
      var stream = client.GetStream();
      var authenticated = false;
      try
      {
        while (true)
        {
          idle.CancelAfter(IdleTimeout);
          var packet = await SourceRcon.ReadAsync(stream, idle.Token).ConfigureAwait(false);
          if (packet is null)
          {
            return;
          }

          if (!authenticated)
          {
            if (packet.Type != RconPacket.Auth)
            {
              return;
            }

            authenticated = password.Length > 0 && FixedTimeEquals(packet.Body, password);
            // Source servers send an empty response before the login's answer, and some clients wait for it.
            await SourceRcon.WriteAsync(stream, new RconPacket(packet.Id, RconPacket.Response, ""), idle.Token).ConfigureAwait(false);
            await SourceRcon.WriteAsync(stream, new RconPacket(authenticated ? packet.Id : RconPacket.AuthFailed, RconPacket.AuthResponse, ""), idle.Token).ConfigureAwait(false);
            log?.Invoke(authenticated ? $"RCON login from {remote}" : $"RCON login with a wrong password from {remote}");
            if (!authenticated)
            {
              return;
            }

            continue;
          }

          if (packet.Type == RconPacket.Response)
          {
            await SourceRcon.WriteAsync(stream, packet, idle.Token).ConfigureAwait(false);
            continue;
          }

          if (packet.Type != RconPacket.ExecCommand)
          {
            continue;
          }

          string answer;
          try
          {
            answer = await execute(packet.Body).ConfigureAwait(false);
          }
          catch (Exception ex)
          {
            answer = "Error: " + ex.Message;
          }

          foreach (var part in SourceRcon.Split(answer ?? ""))
          {
            await SourceRcon.WriteAsync(stream, new RconPacket(packet.Id, RconPacket.Response, part), idle.Token).ConfigureAwait(false);
          }
        }
      }
      catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException or SocketException)
      {
        // The client went away, sent garbage or idled out. Nothing to tell it.
      }
    }
  }

  // Compares without stopping at the first difference, so the time taken says nothing about the password.
  private static bool FixedTimeEquals(string a, string b)
  {
    var diff = a.Length ^ b.Length;
    for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
    {
      diff |= a[i] ^ b[i];
    }

    return diff == 0;
  }
}
