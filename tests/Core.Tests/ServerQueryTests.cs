using System.Net;
using System.Net.Sockets;
using CINE.LaunchHeim.Core.Saves;

namespace CINE.LaunchHeim.Core.Tests;

public class ServerQueryTests
{
  // What a live Valheim dedicated server answered on 2026-10-04 (after the 0xFFFFFFFF header and type byte).
  private static readonly byte[] ValheimInfo =
  [
    0x11, .. "Shitbox\0Shitbox\0valheim\0\0"u8, 0x00, 0x00, 0x02, 0x0A, 0x00, (byte)'d', (byte)'l', 0x01, 0x00,
    .. "1.0.0.0\0"u8,
  ];

  // Two players with empty names: Valheim doesn't give Steam its players' names.
  private static readonly byte[] ValheimPlayers =
  [
    0x02,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x96, 0x23, 0xAD, 0x45,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xB8, 0x81, 0xA7, 0x45,
  ];

  [Theory]
  [InlineData("176.57.174.9:28500", "176.57.174.9", 28501)]
  [InlineData("valheim.example.org:2456", "valheim.example.org", 2457)]
  [InlineData("valheim.example.org", "valheim.example.org", 2457)]
  [InlineData("[2001:db8::1]:2456", "2001:db8::1", 2457)]
  [InlineData("[2001:db8::1]", "2001:db8::1", 2457)]
  [InlineData("2001:db8::1", "2001:db8::1", 2457)]
  public void The_query_port_follows_the_game_port(string address, string host, int port) =>
    Assert.Equal((host, port), ServerQuery.QueryEndpoint(address));

  [Theory]
  [InlineData("")]
  [InlineData("host:notaport")]
  [InlineData("host:65535")]
  [InlineData("[2001:db8::1")]
  public void Unusable_addresses_give_nothing(string address) => Assert.Null(ServerQuery.QueryEndpoint(address));

  [Fact]
  public void The_counts_come_from_a2s_info() => Assert.Equal((2, 10), ServerQuery.ParseInfo(ValheimInfo));

  [Fact]
  public void Empty_names_are_left_out() => Assert.Empty(ServerQuery.ParsePlayers(ValheimPlayers));

  [Fact]
  public void Names_a_server_shares_are_read()
  {
    byte[] payload =
    [
      0x02,
      0x00, .. "Lukaz\0"u8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x3F,
      0x01, .. "Bolo Hård\0"u8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x3F,
    ];

    Assert.Equal(["Lukaz", "Bolo Hård"], ServerQuery.ParsePlayers(payload));
  }

  [Fact]
  public void A_truncated_info_gives_nothing() => Assert.Null(ServerQuery.ParseInfo(ValheimInfo[..10]));

  [Fact]
  public async Task Both_queries_answer_the_servers_challenge()
  {
    using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    var queryPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
    byte[] challenge = [0x12, 0x34, 0x56, 0x78];
    var requests = new List<byte[]>();

    var serve = Task.Run(async () =>
    {
      // Each request is challenged first and answered only once it carries the challenge.
      for (var i = 0; i < 4; i++)
      {
        var request = await server.ReceiveAsync();
        requests.Add(request.Buffer);
        var answered = request.Buffer.AsSpan()[^4..].SequenceEqual(challenge);
        byte[] reply = !answered
          ? [0xFF, 0xFF, 0xFF, 0xFF, 0x41, .. challenge]
          : request.Buffer[4] == 0x54 ? [0xFF, 0xFF, 0xFF, 0xFF, 0x49, .. ValheimInfo] : [0xFF, 0xFF, 0xFF, 0xFF, 0x44, .. ValheimPlayers];
        await server.SendAsync(reply, request.RemoteEndPoint);
      }
    });

    var status = await ServerQuery.QueryAsync($"127.0.0.1:{queryPort - 1}", TimeSpan.FromSeconds(5));
    await serve;

    Assert.NotNull(status);
    Assert.Equal((2, 10), (status.Players, status.MaxPlayers));
    Assert.Empty(status.PlayerNames);
    Assert.Equal(ServerQuery.InfoPacket([]), requests[0]);
    Assert.Equal(ServerQuery.InfoPacket(challenge), requests[1]);
    Assert.Equal(ServerQuery.PlayerPacket([0xFF, 0xFF, 0xFF, 0xFF]), requests[2]);
    Assert.Equal(ServerQuery.PlayerPacket(challenge), requests[3]);
  }

  [Fact]
  public async Task A_server_that_does_not_answer_is_offline()
  {
    // Bound but silent, so the query waits for the timeout instead of getting a refusal.
    using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    var queryPort = ((IPEndPoint)silent.Client.LocalEndPoint!).Port;

    Assert.Null(await ServerQuery.QueryAsync($"127.0.0.1:{queryPort - 1}", TimeSpan.FromMilliseconds(200)));
  }
}
