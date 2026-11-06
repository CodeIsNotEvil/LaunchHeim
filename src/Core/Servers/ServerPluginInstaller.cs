using System.Globalization;
using CINE.LaunchHeim.Core.Game;

namespace CINE.LaunchHeim.Core.Servers;

/// <summary>Puts LaunchHeim's server plugin (<c>src/ServerPlugin</c>) into a server's BepInEx and writes its settings.</summary>
/// <remarks>
/// The plugin is what gives Valheim's server a console (decision 0020): RCON and the file channel. Like
/// the game plugin it is copied in on every start, so it always matches this LaunchHeim, into a folder of
/// its own that nothing else uses. Its config is written each time too: the RCON port and password live
/// in the server's profile, and the plugin only reads them.
/// </remarks>
public static class ServerPluginInstaller
{
  public const string PluginFileName = "LaunchHeim.ServerPlugin.dll";

  /// <summary>The plugin's GUID, which is also its config file's name. The plugin has the same constant.</summary>
  public const string PluginGuid = "io.github.codeisnotevil.launchheim.server";

  public static readonly string PluginDirectory = Path.Combine("BepInEx", "plugins", "LaunchHeim.Server");

  public static readonly string ConfigPath = Path.Combine("BepInEx", "config", PluginGuid + ".cfg");

  /// <summary>The copy built with LaunchHeim, next to the game plugin (see the Desktop csproj).</summary>
  public static string BundledPlugin => Path.Combine(AppContext.BaseDirectory, "bepinex", PluginFileName);

  /// <param name="listenAddress">
  /// Where RCON listens. A local server only needs this machine (127.0.0.1); a server elsewhere needs
  /// 0.0.0.0, and then a strong password, since RCON sends it unencrypted.
  /// </param>
  public static void Install(string serverDirectory, ServerProfile server, string listenAddress = "127.0.0.1", string? bundledPlugin = null)
  {
    var plugin = bundledPlugin ?? BundledPlugin;
    if (!File.Exists(plugin))
    {
      throw new LaunchException($"LaunchHeim's server plugin is missing ({plugin}). Reinstall LaunchHeim.");
    }

    var directory = Path.Combine(serverDirectory, PluginDirectory);
    Directory.CreateDirectory(directory);
    var target = Path.Combine(directory, PluginFileName);
    if (!File.Exists(target) || !File.ReadAllBytes(target).AsSpan().SequenceEqual(File.ReadAllBytes(plugin)))
    {
      File.Copy(plugin, target, overwrite: true);
    }

    var config = Path.Combine(serverDirectory, ConfigPath);
    Directory.CreateDirectory(Path.GetDirectoryName(config)!);
    File.WriteAllText(config, Config(server, listenAddress));
    if (!OperatingSystem.IsWindows())
    {
      // It holds the RCON password.
      File.SetUnixFileMode(config, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
  }

  /// <summary>Takes the plugin out again, for a server whose console was switched off.</summary>
  public static void Remove(string serverDirectory)
  {
    var directory = Path.Combine(serverDirectory, PluginDirectory);
    if (Directory.Exists(directory))
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  /// <summary>The plugin's BepInEx config. Section and key names match the plugin's <c>Config.Bind</c> calls.</summary>
  public static string Config(ServerProfile server, string listenAddress) => string.Create(CultureInfo.InvariantCulture, $"""
    ## Written by LaunchHeim on every start of the server; change these in LaunchHeim instead.

    [RCON]

    ## Source RCON, for LaunchHeim's console and any other RCON tool.
    Enabled = true

    ## 127.0.0.1 accepts only this machine, 0.0.0.0 every network.
    Address = {listenAddress}

    Port = {server.EffectiveRconPort}

    Password = {server.RconPassword}

    [FileChannel]

    ## Run commands written as files into BepInEx/config/LaunchHeim.Server/commands.
    Enabled = true

    """).ReplaceLineEndings("\n");
}
