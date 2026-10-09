using CINE.LaunchHeim.Core.LocalSend;

namespace CINE.LaunchHeim.Core.Tests;

public class FirewallTests : IDisposable
{
  private readonly TempDirectory _temp = new();

  public void Dispose() => _temp.Dispose();

  private string Ufw(bool enabled, string rules = "", bool profile = false)
  {
    var directory = _temp.Combine("ufw");
    Directory.CreateDirectory(Path.Combine(directory, "applications.d"));
    File.WriteAllText(Path.Combine(directory, "ufw.conf"), $"# comment\nENABLED={(enabled ? "yes" : "no")}\nLOGLEVEL=low\n");
    File.WriteAllText(Path.Combine(directory, "user.rules"), "*filter\n" + rules + "COMMIT\n");
    if (profile)
    {
      File.WriteAllText(Path.Combine(directory, "applications.d", "launchheim"), "[LaunchHeim]\n");
    }

    return directory;
  }

  [Fact]
  public void No_ufw_or_ufw_off_needs_nothing()
  {
    Assert.Null(Firewall.UfwCommand(_temp.Combine("missing")));
    Assert.Null(Firewall.UfwCommand(Ufw(enabled: false)));
  }

  /// <summary>What CachyOS ships: ufw on, incoming dropped, rules for Samba and nothing else.</summary>
  [Fact]
  public void Ufw_without_a_rule_for_the_port_gets_the_ports_named()
  {
    var command = Firewall.UfwCommand(Ufw(enabled: true, "-A ufw-user-input -p tcp -m multiport --dports 139,445 -j ACCEPT\n"));

    Assert.Equal("sudo ufw allow 53317/udp && sudo ufw allow 53317:53326/tcp", command);
  }

  [Fact]
  public void The_packaged_profile_is_used_when_installed()
  {
    Assert.Equal("sudo ufw allow LaunchHeim", Firewall.UfwCommand(Ufw(enabled: true, profile: true)));
  }

  [Fact]
  public void Any_rule_for_the_port_counts_as_allowed()
  {
    // `ufw allow from 192.168.178.0/24 to any port 53317:53327 proto tcp`, as written to user.rules.
    var rules = "-A ufw-user-input -p tcp -m multiport --dports 53317:53327 -s 192.168.178.0/24 -j ACCEPT\n";

    Assert.Null(Firewall.UfwCommand(Ufw(enabled: true, rules)));
  }

  [Fact]
  public void The_windows_script_quotes_the_program_path()
  {
    var script = Firewall.WindowsScript(@"C:\Users\O'Brien\AppData\Local\Programs\LaunchHeim\LaunchHeim.exe");

    Assert.Contains(@"$program = 'C:\Users\O''Brien\AppData\Local\Programs\LaunchHeim\LaunchHeim.exe'", script);
    Assert.Contains("Remove-NetFirewallRule", script);
    Assert.Contains("-Profile Any", script);
  }
}
