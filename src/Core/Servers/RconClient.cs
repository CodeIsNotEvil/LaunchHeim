using System.Net.Sockets;
using CINE.LaunchHeim.Shared;

namespace CINE.LaunchHeim.Core.Servers;

public sealed class RconException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A Source RCON connection to a server: LaunchHeim's own server plugin, or another RCON mod.</summary>
/// <remarks>
/// <para>
/// A long answer comes in several packets, and the protocol doesn't say how many. Source clients send an
/// empty response packet after each command and read until the server mirrors it, which LaunchHeim's
/// plugin does (<see cref="RconServer"/>). Not every Valheim RCON mod mirrors it, so an answer also ends
/// when nothing more has come for <see cref="QuietTime"/> after its first packet.
/// </para>
/// <para>
/// One command at a time: the packets of two answers would mix on the one connection.
/// </para>
/// </remarks>
public sealed class RconClient : IAsyncDisposable
{
  public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

  /// <summary>How long a quiet connection counts as "the answer is complete" when the mirror never comes.</summary>
  internal static readonly TimeSpan QuietTime = TimeSpan.FromMilliseconds(400);

  private readonly TcpClient _tcp;
  private readonly NetworkStream _stream;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private int _nextId = 1;

  private RconClient(TcpClient tcp)
  {
    _tcp = tcp;
    _stream = tcp.GetStream();
  }

  /// <summary>Connects and logs in.</summary>
  /// <exception cref="RconException">The server can't be reached, isn't RCON, or refused the password.</exception>
  public static async Task<RconClient> ConnectAsync(string host, int port, string password, CancellationToken cancellationToken = default)
  {
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(DefaultTimeout);
    var tcp = new TcpClient { NoDelay = true };
    try
    {
      await tcp.ConnectAsync(host, port, timeout.Token);
      var client = new RconClient(tcp);
      await client.LoginAsync(password, timeout.Token);
      return client;
    }
    catch (Exception ex) when (ex is SocketException or IOException or InvalidDataException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
    {
      tcp.Dispose();
      throw new RconException(ex is OperationCanceledException
        ? $"The server's console at {host}:{port} didn't answer."
        : $"Could not reach the server's console at {host}:{port}: {ex.Message}", ex);
    }
    catch
    {
      tcp.Dispose();
      throw;
    }
  }

  /// <summary>Runs a command on the server and returns what it printed.</summary>
  public async Task<string> SendAsync(string command, CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken);
    try
    {
      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      timeout.CancelAfter(DefaultTimeout);
      var id = _nextId++;
      var marker = _nextId++;
      await SourceRcon.WriteAsync(_stream, new RconPacket(id, RconPacket.ExecCommand, command), timeout.Token);
      await SourceRcon.WriteAsync(_stream, new RconPacket(marker, RconPacket.Response, ""), timeout.Token);

      var answer = new System.Text.StringBuilder();
      var gotAny = false;
      while (true)
      {
        // Before the first packet the command may take its time; after it, a pause means the end.
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        if (gotAny)
        {
          wait.CancelAfter(QuietTime);
        }

        RconPacket? packet;
        try
        {
          packet = await SourceRcon.ReadAsync(_stream, wait.Token);
        }
        catch (OperationCanceledException) when (gotAny && !timeout.IsCancellationRequested)
        {
          // A cancelled read may have taken part of a packet, so the connection can't be trusted after
          // it. The answer is complete either way; the next command reconnects.
          _tcp.Dispose();
          break;
        }

        if (packet is null)
        {
          throw new RconException("The server closed its console connection.");
        }

        if (packet.Id == marker)
        {
          // Source servers answer the mirror with a second packet ("\0\x01\0\0"); a quick read drops it.
          break;
        }

        if (packet.Id == id)
        {
          answer.Append(packet.Body);
          gotAny = true;
        }
      }

      return answer.ToString().TrimEnd('\n');
    }
    catch (Exception ex) when (ex is SocketException or IOException or InvalidDataException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
    {
      throw new RconException(ex is OperationCanceledException ? "The server's console didn't answer." : "The server's console connection broke: " + ex.Message, ex);
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>False once the connection broke or was closed, so the caller connects again.</summary>
  public bool IsConnected => _tcp.Connected;

  public ValueTask DisposeAsync()
  {
    _tcp.Dispose();
    _gate.Dispose();
    return ValueTask.CompletedTask;
  }

  private async Task LoginAsync(string password, CancellationToken cancellationToken)
  {
    var id = _nextId++;
    await SourceRcon.WriteAsync(_stream, new RconPacket(id, RconPacket.Auth, password), cancellationToken);
    while (true)
    {
      var packet = await SourceRcon.ReadAsync(_stream, cancellationToken) ?? throw new RconException("The server closed the console connection at login.");
      if (packet.Type != RconPacket.AuthResponse)
      {
        // The empty response Source servers send first.
        continue;
      }

      if (packet.Id == RconPacket.AuthFailed)
      {
        throw new RconException("The server refused the console password.");
      }

      return;
    }
  }
}
