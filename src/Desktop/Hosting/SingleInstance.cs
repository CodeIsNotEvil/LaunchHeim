using System.Net.Sockets;
using System.Text;

namespace CINE.LaunchHeim.Desktop.Hosting;

/// <summary>Keeps one LaunchHeim running and hands it the command lines of later starts.</summary>
/// <remarks>
/// A browser opening an <c>nxm://</c> link starts a second LaunchHeim. That process only forwards the
/// link over a Unix socket in <c>$XDG_RUNTIME_DIR</c> (private to the user) and exits, so the download
/// lands in the window the user is already looking at.
/// </remarks>
public sealed class SingleInstance : IDisposable
{
  private readonly Socket _socket;
  private readonly string _path;
  private readonly CancellationTokenSource _stop = new();

  private SingleInstance(Socket socket, string path)
  {
    _socket = socket;
    _path = path;
  }

  public static bool TryForward(string path, string message)
  {
    if (!File.Exists(path))
    {
      return false;
    }

    try
    {
      using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
      socket.Connect(new UnixDomainSocketEndPoint(path));
      socket.Send(Encoding.UTF8.GetBytes(message + "\n"));
      return true;
    }
    catch (SocketException)
    {
      // Nobody listening: a crashed instance left the file behind.
      return false;
    }
  }

  public static SingleInstance Listen(string path, Action<string> onMessage)
  {
    if (File.Exists(path))
    {
      File.Delete(path);
    }

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    socket.Bind(new UnixDomainSocketEndPoint(path));
    socket.Listen(4);

    var instance = new SingleInstance(socket, path);
    _ = Task.Run(() => instance.AcceptLoopAsync(onMessage));
    return instance;
  }

  private async Task AcceptLoopAsync(Action<string> onMessage)
  {
    while (!_stop.IsCancellationRequested)
    {
      try
      {
        using var client = await _socket.AcceptAsync(_stop.Token);
        await using var stream = new NetworkStream(client);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(_stop.Token) is { } line)
        {
          onMessage(line);
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex) when (ex is SocketException or IOException)
      {
        // One broken client must not stop the listener.
      }
    }
  }

  public void Dispose()
  {
    _stop.Cancel();
    _socket.Dispose();
    try
    {
      File.Delete(_path);
    }
    catch (IOException)
    {
    }
  }
}
