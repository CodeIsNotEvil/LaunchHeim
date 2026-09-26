using System.Reflection;

namespace CINE.LaunchHeim.Core;

public static class AppInfo
{
  public static string Version { get; } =
    typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.1.0";

  /// <summary>Sent with every request. Thunderstore and Nexus both ask clients to say who they are.</summary>
  public static string UserAgent => $"LaunchHeim/{Version} (+https://github.com/CodeIsNotEvil/MyMonoRepo)";
}
