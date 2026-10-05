using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;

namespace CINE.LaunchHeim.Core.LocalSend;

/// <summary>One HTTP request, with the body left as a stream so an upload goes straight to a file.</summary>
public sealed record HttpRequest(
  string Method,
  string Path,
  IReadOnlyDictionary<string, string> Query,
  IReadOnlyDictionary<string, string> Headers,
  IPAddress RemoteAddress,
  Stream Body)
{
  public async Task<string> ReadTextAsync(CancellationToken cancellationToken, int limit = 1024 * 1024)
  {
    using var memory = new MemoryStream();
    var buffer = new byte[8192];
    int read;
    while ((read = await Body.ReadAsync(buffer, cancellationToken)) > 0)
    {
      memory.Write(buffer, 0, read);
      if (memory.Length > limit)
      {
        throw new IOException("Request body too large");
      }
    }

    return Encoding.UTF8.GetString(memory.ToArray());
  }
}

public sealed record HttpResponse(int Status, string Body = "");

/// <summary>
/// The few routes LocalSend needs, over plain HTTP/1.1 on a <see cref="TcpListener"/>: one request per
/// connection, a Content-Length or chunked body.
/// </summary>
/// <remarks>
/// <para>
/// Not <c>HttpListener</c>: on Windows it goes through http.sys, which only lets an administrator listen on
/// anything but localhost. Not Kestrel either, which would add the ASP.NET Core runtime to every package.
/// </para>
/// <para>
/// Only HTTP, not LocalSend's default HTTPS: that needs a certificate per device, and LocalSend clients
/// use whatever protocol a device announces, so they reach LaunchHeim over HTTP. The companion app's
/// <c>MiniHttpServer.kt</c> works the same way.
/// </para>
/// </remarks>
public sealed class MiniHttpServer(Func<HttpRequest, CancellationToken, Task<HttpResponse>> handler) : IDisposable
{
  private TcpListener? _listener;
  private CancellationTokenSource? _stop;

  public int Port => _listener is { } listener ? ((IPEndPoint)listener.LocalEndpoint).Port : -1;

  /// <summary>Binds the first free port from <paramref name="preferred"/> on, because the LocalSend app may hold the default one.</summary>
  public void Start(int preferred, int attempts = 10)
  {
    SocketException? last = null;
    for (var port = preferred; port < preferred + attempts; port++)
    {
      try
      {
        var listener = StartListener(port);
        _listener = listener;
        _stop = new CancellationTokenSource();
        _ = AcceptLoopAsync(listener, _stop.Token);
        return;
      }
      catch (SocketException ex)
      {
        last = ex;
      }
    }

    throw last ?? new SocketException((int)SocketError.AddressAlreadyInUse);
  }

  // Both IPv4 and IPv6 where the system has IPv6, since phones may well reach the PC over either.
  private static TcpListener StartListener(int port)
  {
    if (Socket.OSSupportsIPv6)
    {
      var dual = new TcpListener(IPAddress.IPv6Any, port);
      try
      {
        dual.Server.DualMode = true;
        dual.Start();
        return dual;
      }
      catch (SocketException ex) when (ex.SocketErrorCode != SocketError.AddressAlreadyInUse)
      {
        // IPv6 switched off in the kernel: IPv4 alone still reaches every phone on the LAN.
        dual.Stop();
      }
    }

    var v4 = new TcpListener(IPAddress.Any, port);
    v4.Start();
    return v4;
  }

  public void Dispose()
  {
    _stop?.Cancel();
    _listener?.Stop();
    _listener = null;
  }

  private async Task AcceptLoopAsync(TcpListener listener, CancellationToken stop)
  {
    while (!stop.IsCancellationRequested)
    {
      TcpClient client;
      try
      {
        client = await listener.AcceptTcpClientAsync(stop);
      }
      catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
      {
        return;
      }

      _ = ServeAsync(client, stop);
    }
  }

  private async Task ServeAsync(TcpClient client, CancellationToken stop)
  {
    using (client)
    {
      try
      {
        var stream = client.GetStream();
        // Bounds every read, the upload's included, so a peer that stalls can't hold a connection forever.
        stream.ReadTimeout = 30_000;
        var input = new BufferedStream(stream);

        var requestLine = ReadLine(input);
        var parts = requestLine?.Split(' ');
        if (parts is not { Length: >= 2 })
        {
          return;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (ReadLine(input) is { Length: > 0 } line)
        {
          var colon = line.IndexOf(':');
          if (colon > 0)
          {
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
          }
        }

        var target = parts[1];
        var question = target.IndexOf('?');
        var path = question < 0 ? target : target[..question];
        var parsed = HttpUtility.ParseQueryString(question < 0 ? "" : target[(question + 1)..]);
        var query = parsed.AllKeys.OfType<string>().ToDictionary(k => k, k => parsed[k] ?? "", StringComparer.Ordinal);

        Stream body = headers.TryGetValue("Transfer-Encoding", out var encoding) && encoding.Contains("chunked", StringComparison.OrdinalIgnoreCase)
          ? new ChunkedReadStream(input)
          : new LimitedReadStream(input, headers.TryGetValue("Content-Length", out var length) && long.TryParse(length, out var size) ? size : 0);

        var remote = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
        if (remote.IsIPv4MappedToIPv6)
        {
          remote = remote.MapToIPv4();
        }

        HttpResponse response;
        try
        {
          response = await handler(new HttpRequest(parts[0].ToUpperInvariant(), path, query, headers, remote, body), stop);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          response = new HttpResponse(500);
        }

        await WriteAsync(stream, response, stop);
      }
      catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
      {
        // The peer went away mid-request, or LaunchHeim is stopping; nothing to answer.
      }
    }
  }

  private static async Task WriteAsync(Stream output, HttpResponse response, CancellationToken cancellationToken)
  {
    var body = Encoding.UTF8.GetBytes(response.Body);
    var head = new StringBuilder()
      .Append("HTTP/1.1 ").Append(response.Status).Append(' ').Append(Reason(response.Status)).Append("\r\n");
    if (body.Length > 0)
    {
      head.Append("Content-Type: application/json; charset=utf-8\r\n");
    }

    head.Append("Content-Length: ").Append(body.Length).Append("\r\nConnection: close\r\n\r\n");
    await output.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), cancellationToken);
    await output.WriteAsync(body, cancellationToken);
    await output.FlushAsync(cancellationToken);
  }

  private static string Reason(int status) => status switch
  {
    200 => "OK",
    204 => "No Content",
    400 => "Bad Request",
    403 => "Forbidden",
    404 => "Not Found",
    409 => "Conflict",
    422 => "Unprocessable Entity",
    _ => "Error",
  };

  /// <summary>A CRLF-terminated line of at most 8 KB, or null at the end of the stream.</summary>
  internal static string? ReadLine(Stream input)
  {
    var bytes = new List<byte>();
    while (true)
    {
      var b = input.ReadByte();
      if (b < 0)
      {
        return bytes.Count == 0 ? null : Encoding.Latin1.GetString(bytes.ToArray());
      }

      if (b == '\n')
      {
        return Encoding.Latin1.GetString(bytes.ToArray()).TrimEnd('\r');
      }

      bytes.Add((byte)b);
      if (bytes.Count > 8192)
      {
        throw new IOException("Header line too long");
      }
    }
  }
}

/// <summary>Reads exactly the bytes a Content-Length announced, then reports the end.</summary>
internal sealed class LimitedReadStream(Stream inner, long length) : Stream
{
  private long _remaining = length;

  public override int Read(byte[] buffer, int offset, int count)
  {
    if (_remaining <= 0)
    {
      return 0;
    }

    var read = inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
    _remaining -= read;
    return read;
  }

  public override bool CanRead => true;
  public override bool CanSeek => false;
  public override bool CanWrite => false;
  public override long Length => throw new NotSupportedException();
  public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
  public override void Flush() { }
  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
  public override void SetLength(long value) => throw new NotSupportedException();
  public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Decodes a <c>Transfer-Encoding: chunked</c> body.</summary>
internal sealed class ChunkedReadStream(Stream inner) : Stream
{
  private long _left;
  private bool _done;

  private bool NextChunk()
  {
    if (_done)
    {
      return false;
    }

    if (_left == 0)
    {
      var line = MiniHttpServer.ReadLine(inner);
      // The CRLF after the previous chunk's data shows up as an empty line first.
      if (line is "")
      {
        line = MiniHttpServer.ReadLine(inner);
      }

      var size = line?.Split(';')[0].Trim();
      if (string.IsNullOrEmpty(size) || !long.TryParse(size, System.Globalization.NumberStyles.HexNumber, null, out _left) || _left == 0)
      {
        _done = true;
        return false;
      }
    }

    return true;
  }

  public override int Read(byte[] buffer, int offset, int count)
  {
    if (!NextChunk())
    {
      return 0;
    }

    var read = inner.Read(buffer, offset, (int)Math.Min(count, _left));
    _left -= read;
    return read;
  }

  public override bool CanRead => true;
  public override bool CanSeek => false;
  public override bool CanWrite => false;
  public override long Length => throw new NotSupportedException();
  public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
  public override void Flush() { }
  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
  public override void SetLength(long value) => throw new NotSupportedException();
  public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
