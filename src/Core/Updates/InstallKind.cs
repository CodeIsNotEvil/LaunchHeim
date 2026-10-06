namespace CINE.LaunchHeim.Core.Updates;

/// <summary>How this copy of LaunchHeim was installed, which decides how it is updated.</summary>
public enum InstallKind
{
  /// <summary>Not known (a development build, a custom install.sh prefix, os-release unreadable): only the download page is offered.</summary>
  Unknown,

  /// <summary>The portable Windows zip.</summary>
  Windows,

  /// <summary>The pacman package (Arch, CachyOS, Manjaro, EndeavourOS...).</summary>
  Arch,

  /// <summary>The .deb (Debian, Ubuntu and their derivatives).</summary>
  Debian,

  /// <summary>The .rpm (Fedora, RHEL and their rebuilds).</summary>
  Fedora,

  /// <summary>install.sh into ~/.local/opt/LaunchHeim, updated by running it again in the checkout.</summary>
  InstallScript,
}

public static class InstallDetection
{
  /// <summary>Works out the <see cref="InstallKind"/> from facts the caller gathers, so it can be tested.</summary>
  /// <param name="distroPackage">Whether a package manager installed this copy (Desktop's DistroPackage switch).</param>
  /// <param name="osRelease">The text of <c>/etc/os-release</c>, or null when it can't be read (a sandbox, say).</param>
  /// <param name="appDirectory">The folder the app runs from.</param>
  /// <param name="home">The user's home folder.</param>
  public static InstallKind Detect(bool isWindows, bool distroPackage, string? osRelease, string appDirectory, string home)
  {
    if (isWindows)
    {
      return InstallKind.Windows;
    }

    if (!distroPackage)
    {
      // install.sh's default prefix. A custom LAUNCHHEIM_PREFIX can't be told from a build in a
      // checkout, and running install.sh there would be wrong advice, so it stays Unknown.
      var script = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(home, ".local", "opt", "LaunchHeim")));
      return Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory)).Equals(script, StringComparison.Ordinal)
        ? InstallKind.InstallScript
        : InstallKind.Unknown;
    }

    if (osRelease is null)
    {
      return InstallKind.Unknown;
    }

    var fields = ParseOsRelease(osRelease);
    var ids = new[] { fields.GetValueOrDefault("ID", "") }
      .Concat(fields.GetValueOrDefault("ID_LIKE", "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
      .Select(id => id.ToLowerInvariant())
      .ToList();

    // The distribution itself first: Ubuntu says ID_LIKE=debian, CachyOS ID_LIKE=arch.
    foreach (var id in ids)
    {
      switch (id)
      {
        case "arch" or "archarm" or "cachyos" or "manjaro" or "endeavouros" or "garuda" or "artix":
          return InstallKind.Arch;
        case "debian" or "ubuntu" or "linuxmint" or "pop" or "raspbian":
          return InstallKind.Debian;
        case "fedora" or "rhel" or "centos" or "rocky" or "almalinux" or "nobara":
          return InstallKind.Fedora;
      }
    }

    return InstallKind.Unknown;
  }

  /// <summary>The KEY=value lines of os-release, with quotes removed.</summary>
  internal static Dictionary<string, string> ParseOsRelease(string text)
  {
    var fields = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var raw in text.Split('\n'))
    {
      var line = raw.Trim();
      var equals = line.IndexOf('=');
      if (line.StartsWith('#') || equals <= 0)
      {
        continue;
      }

      var value = line[(equals + 1)..].Trim();
      if (value.Length >= 2 && (value[0] is '"' or '\'') && value[^1] == value[0])
      {
        value = value[1..^1];
      }

      fields[line[..equals]] = value;
    }

    return fields;
  }

  /// <summary>What people call the system, for "Update from a terminal on …".</summary>
  public static string DisplayName(InstallKind kind) => kind switch
  {
    InstallKind.Windows => "Windows (PowerShell)",
    InstallKind.Arch => "Arch Linux, CachyOS",
    InstallKind.Debian => "Debian, Ubuntu",
    InstallKind.Fedora => "Fedora, RHEL",
    InstallKind.InstallScript => "your install.sh install",
    _ => "",
  };
}
