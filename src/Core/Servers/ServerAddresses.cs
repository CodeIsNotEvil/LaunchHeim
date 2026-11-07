using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace CINE.LaunchHeim.Core.Servers;

/// <summary>Where an address reaches this PC from.</summary>
public enum AddressScope
{
  /// <summary>A home or office network (10/8, 172.16/12, 192.168/16): players on the same network.</summary>
  LocalNetwork,

  /// <summary>A VPN like Tailscale or ZeroTier's carrier-grade range (100.64/10): players on the same VPN.</summary>
  Vpn,

  /// <summary>An address the internet routes to this PC directly, without a router in between.</summary>
  Internet,
}

/// <param name="Interface">The network adapter's name, so two addresses on one PC can be told apart.</param>
public sealed record LocalAddress(IPAddress Address, AddressScope Scope, string Interface);

/// <summary>The addresses other players type into Valheim's "Join IP" to reach a server on this PC.</summary>
/// <remarks>
/// <para>
/// The local ones come from this PC's network adapters. Only IPv4: Valheim's "Join IP" takes
/// <c>host:port</c>, which an IPv6 address can't be written as. Adapters of containers and virtual
/// machines (Docker, libvirt, VirtualBox, VMware) are left out, since nobody joins through them.
/// </para>
/// <para>
/// The internet address isn't known on this PC when a router sits in front of it, which is nearly always
/// the case. Only an outside service sees it, so it's asked only when the player clicks for it
/// (<see cref="FindPublicAsync"/>): the request tells that service this PC's address, which it sees
/// anyway. Players from the internet also need the router to forward the server's UDP ports.
/// </para>
/// </remarks>
public static partial class ServerAddresses
{
  /// <summary>Answers with nothing but the caller's address, as plain text. Free, no account, no tracking by design.</summary>
  public const string PublicAddressService = "https://api.ipify.org/";

  private static readonly string[] VirtualAdapters = ["docker", "br-", "veth", "virbr", "vboxnet", "vmnet", "podman", "cni", "flannel", "lxc", "lxd"];

  public static IReadOnlyList<LocalAddress> Local()
  {
    var addresses = new List<LocalAddress>();
    NetworkInterface[] adapters;
    try
    {
      adapters = NetworkInterface.GetAllNetworkInterfaces();
    }
    catch (NetworkInformationException)
    {
      return addresses;
    }

    foreach (var adapter in adapters)
    {
      if (adapter.OperationalStatus != OperationalStatus.Up
        || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback
        || IsVirtual(adapter.Name))
      {
        continue;
      }

      foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
      {
        if (Classify(unicast.Address) is { } scope)
        {
          addresses.Add(new LocalAddress(unicast.Address, scope, adapter.Name));
        }
      }
    }

    // The home network first: it's what most players need.
    return addresses.OrderBy(a => a.Scope).ThenBy(a => a.Address.ToString(), StringComparer.Ordinal).ToList();
  }

  /// <summary>What an address is good for, or null for one players can't use (IPv6, loopback, link-local).</summary>
  public static AddressScope? Classify(IPAddress address)
  {
    if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
    {
      return null;
    }

    var b = address.GetAddressBytes();
    return b switch
    {
      [169, 254, ..] => null,
      [10, ..] or [192, 168, ..] => AddressScope.LocalNetwork,
      [172, >= 16 and <= 31, ..] => AddressScope.LocalNetwork,
      [100, >= 64 and <= 127, ..] => AddressScope.Vpn,
      _ => AddressScope.Internet,
    };
  }

  internal static bool IsVirtual(string adapterName) =>
    VirtualAdapters.Any(prefix => adapterName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

  /// <summary>This network's address on the internet, as an outside service sees it.</summary>
  /// <exception cref="HttpRequestException">The service couldn't be reached or answered something else.</exception>
  public static async Task<IPAddress> FindPublicAsync(HttpClient http, CancellationToken cancellationToken)
  {
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(10));
    var text = (await http.GetStringAsync(PublicAddressService, timeout.Token)).Trim();
    return IPAddress.TryParse(text, out var address) && address.AddressFamily == AddressFamily.InterNetwork
      ? address
      : throw new HttpRequestException("The address service answered something that isn't an address.");
  }

  /// <summary>
  /// The join code of a crossplay server, which Xbox and Game Pass players type instead of an address.
  /// Valheim logs it once PlayFab took the session: <c>Session "&lt;name&gt;" registered with join code 123456</c>.
  /// </summary>
  public static string? JoinCode(string logFile)
  {
    try
    {
      using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
      using var reader = new StreamReader(stream);
      string? code = null;
      while (reader.ReadLine() is { } line)
      {
        // The last one counts: Valheim makes a new code when the old one wasn't unique.
        if (JoinCodePattern().Match(line) is { Success: true } match)
        {
          code = match.Groups["code"].Value;
        }
      }

      return code;
    }
    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
    {
      return null;
    }
  }

  [GeneratedRegex("registered with join code (?<code>[0-9A-Za-z]+)")]
  private static partial Regex JoinCodePattern();
}
