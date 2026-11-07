using System.Net;
using CINE.LaunchHeim.Core.Servers;

namespace CINE.LaunchHeim.Core.Tests;

public class ServerAddressesTests : IDisposable
{
  private readonly TempDirectory _temp = new();

  public void Dispose() => _temp.Dispose();

  [Theory]
  [InlineData("192.168.178.26", AddressScope.LocalNetwork)]
  [InlineData("10.0.0.5", AddressScope.LocalNetwork)]
  [InlineData("172.20.1.1", AddressScope.LocalNetwork)]
  [InlineData("100.101.102.103", AddressScope.Vpn)]
  [InlineData("172.32.0.1", AddressScope.Internet)]
  [InlineData("84.150.12.7", AddressScope.Internet)]
  public void Addresses_are_sorted_by_who_can_reach_them(string address, AddressScope scope)
  {
    Assert.Equal(scope, ServerAddresses.Classify(IPAddress.Parse(address)));
  }

  [Theory]
  [InlineData("127.0.0.1")]
  [InlineData("169.254.3.4")]
  [InlineData("2001:16b8:c71a:3200::1")]
  public void Addresses_players_cant_type_are_left_out(string address)
  {
    Assert.Null(ServerAddresses.Classify(IPAddress.Parse(address)));
  }

  [Theory]
  [InlineData("docker0", true)]
  [InlineData("virbr0", true)]
  [InlineData("br-3f2a", true)]
  [InlineData("eno1", false)]
  [InlineData("wlan0", false)]
  [InlineData("tailscale0", false)]
  public void Container_and_vm_adapters_are_left_out(string name, bool isVirtual)
  {
    Assert.Equal(isVirtual, ServerAddresses.IsVirtual(name));
  }

  [Fact]
  public async Task The_internet_address_comes_from_the_service()
  {
    using var http = new HttpClient(new FakeHttp().Serve(ServerAddresses.PublicAddressService, "84.150.12.7\n"u8.ToArray()));

    Assert.Equal(IPAddress.Parse("84.150.12.7"), await ServerAddresses.FindPublicAsync(http, default));
  }

  [Fact]
  public async Task Anything_but_an_address_from_the_service_is_an_error()
  {
    using var http = new HttpClient(new FakeHttp().Serve(ServerAddresses.PublicAddressService, "<html>busy</html>"u8.ToArray()));

    await Assert.ThrowsAsync<HttpRequestException>(() => ServerAddresses.FindPublicAsync(http, default));
  }

  [Fact]
  public void The_last_join_code_in_the_log_counts()
  {
    var log = _temp.Combine("server.log");
    Assert.Null(ServerAddresses.JoinCode(log));

    File.WriteAllLines(log,
    [
      "11/07/2026 01:02:03: Session \"Friday\" registered with join code 123456",
      "11/07/2026 01:02:09: Session \"Friday\" registered with join code 654321",
    ]);
    Assert.Equal("654321", ServerAddresses.JoinCode(log));
  }

  [Fact]
  public void This_pcs_addresses_can_be_listed()
  {
    // Whatever the machine has; only that nothing unusable slips through.
    Assert.All(ServerAddresses.Local(), a => Assert.NotNull(ServerAddresses.Classify(a.Address)));
  }
}
