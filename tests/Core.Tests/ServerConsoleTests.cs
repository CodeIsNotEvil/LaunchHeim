using System.Net;
using System.Net.Sockets;
using CINE.LaunchHeim.Core.Servers;
using CINE.LaunchHeim.Shared;

namespace CINE.LaunchHeim.Core.Tests;

public class ServerConsoleTests : IDisposable
{
  private readonly TempDirectory _temp = new();

  public void Dispose() => _temp.Dispose();

  // Port 0: Windows reserves blocks of fixed ports (see the second brain's LocalSend gotcha).
  private static RconServer StartServer(Func<string, Task<string>> execute, string password = "pw")
  {
    var server = new RconServer(IPAddress.Loopback, 0, password, execute);
    server.Start();
    return server;
  }

  [Fact]
  public async Task Client_logs_in_and_gets_the_answer()
  {
    using var server = StartServer(command => Task.FromResult("you said " + command));
    await using var client = await RconClient.ConnectAsync("127.0.0.1", server.Port, "pw");

    Assert.Equal("you said status", await client.SendAsync("status"));
    Assert.Equal("you said players", await client.SendAsync("players"));
  }

  [Fact]
  public async Task Long_answers_arrive_whole_across_packets()
  {
    var lines = string.Join("\n", Enumerable.Range(0, 2000).Select(i => $"line {i} äöü"));
    using var server = StartServer(_ => Task.FromResult(lines));
    await using var client = await RconClient.ConnectAsync("127.0.0.1", server.Port, "pw");

    Assert.True(SourceRcon.Split(lines).Count > 1);
    Assert.Equal(lines, await client.SendAsync("dump"));
  }

  [Fact]
  public async Task A_wrong_password_is_refused()
  {
    using var server = StartServer(_ => Task.FromResult(""));

    var error = await Assert.ThrowsAsync<RconException>(() => RconClient.ConnectAsync("127.0.0.1", server.Port, "nope"));
    Assert.Contains("password", error.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task A_server_without_a_password_lets_nobody_in()
  {
    using var server = StartServer(_ => Task.FromResult(""), password: "");

    await Assert.ThrowsAsync<RconException>(() => RconClient.ConnectAsync("127.0.0.1", server.Port, ""));
  }

  [Fact]
  public async Task Nothing_listening_is_an_rcon_error()
  {
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();

    await Assert.ThrowsAsync<RconException>(() => RconClient.ConnectAsync("127.0.0.1", port, "pw"));
  }

  [Fact]
  public async Task An_answer_ends_after_a_pause_when_the_server_never_mirrors()
  {
    // Like an RCON mod that ignores the empty packet Source clients send after a command.
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var serve = Task.Run(async () =>
    {
      using var tcp = await listener.AcceptTcpClientAsync();
      var stream = tcp.GetStream();
      var login = await SourceRcon.ReadAsync(stream, default);
      await SourceRcon.WriteAsync(stream, new RconPacket(login!.Id, RconPacket.AuthResponse, ""), default);
      var command = await SourceRcon.ReadAsync(stream, default);
      await SourceRcon.WriteAsync(stream, new RconPacket(command!.Id, RconPacket.Response, "ok"), default);
      await Task.Delay(2000);
    });

    await using (var client = await RconClient.ConnectAsync("127.0.0.1", port, "pw"))
    {
      Assert.Equal("ok", await client.SendAsync("save"));
    }

    listener.Stop();
    await serve;
  }

  [Fact]
  public void Packets_round_trip_through_the_wire_format()
  {
    var bytes = SourceRcon.Encode(new RconPacket(7, RconPacket.ExecCommand, "say hi"));

    Assert.Equal(4 + 4 + 4 + 6 + 2, bytes.Length);
    Assert.Equal(bytes.Length - 4, BitConverter.ToInt32(bytes, 0));
    var packet = SourceRcon.ReadAsync(new MemoryStream(bytes), default).Result!;
    Assert.Equal((7, RconPacket.ExecCommand, "say hi"), (packet.Id, packet.Type, packet.Body));
  }

  [Fact]
  public async Task Garbage_is_not_taken_for_a_packet()
  {
    var bytes = BitConverter.GetBytes(int.MaxValue);

    await Assert.ThrowsAsync<InvalidDataException>(() => SourceRcon.ReadAsync(new MemoryStream(bytes), default));
  }

  [Fact]
  public async Task File_channel_hands_commands_to_the_plugin_and_reads_its_answer()
  {
    var root = _temp.Combine("server");
    var files = new LocalServerFiles(root);
    var channel = new ServerCommandChannel(files, TimeSpan.FromMilliseconds(50));

    // What the plugin's FileChannel does, in short.
    using var stop = new CancellationTokenSource();
    var plugin = Task.Run(async () =>
    {
      while (!stop.IsCancellationRequested)
      {
        foreach (var name in await files.ListAsync(ServerCommandChannel.CommandsDirectory, ".cmd", default))
        {
          var line = await files.ReadTextAsync($"{ServerCommandChannel.CommandsDirectory}/{name}", default);
          await files.DeleteAsync($"{ServerCommandChannel.CommandsDirectory}/{name}", default);
          await files.WriteTextAsync($"{ServerCommandChannel.ResponsesDirectory}/{Path.GetFileNameWithoutExtension(name)}.txt", "ran " + line, default);
        }

        await Task.Delay(20);
      }
    });

    Assert.Equal("ran save", await channel.SendAsync("save", TimeSpan.FromSeconds(5)));
    Assert.Empty(await files.ListAsync(ServerCommandChannel.ResponsesDirectory, ".txt", default));
    stop.Cancel();
    await plugin;
  }

  [Fact]
  public async Task File_channel_gives_up_and_takes_its_command_back()
  {
    var files = new LocalServerFiles(_temp.Combine("server"));
    var channel = new ServerCommandChannel(files, TimeSpan.FromMilliseconds(20));

    await Assert.ThrowsAsync<RconException>(() => channel.SendAsync("save", TimeSpan.FromMilliseconds(100)));
    Assert.Empty(await files.ListAsync(ServerCommandChannel.CommandsDirectory, ".cmd", default));
  }
}
