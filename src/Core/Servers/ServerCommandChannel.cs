namespace CINE.LaunchHeim.Core.Servers;

/// <summary>Runs console commands through files, for servers whose only way in is file access.</summary>
/// <remarks>
/// <para>
/// Hosts like G-Portal hand out FTP and nothing else: no shell, no extra port for RCON. LaunchHeim's server
/// plugin therefore also watches a folder (decision 0020). A command is a file <c>commands/&lt;id&gt;.cmd</c>
/// holding one command line; the plugin runs it, deletes it and writes the answer to
/// <c>responses/&lt;id&gt;.txt</c>, which is read and deleted here. Both sides write through a temporary
/// name and rename, so neither ever reads half a file.
/// </para>
/// <para>
/// The plugin looks every <see cref="PluginPollInterval"/>, so an answer takes a few seconds: slower than
/// RCON, but it works through anything that can move files.
/// </para>
/// </remarks>
public sealed class ServerCommandChannel(IServerFiles files, TimeSpan? pollInterval = null)
{
  /// <summary>The plugin's folder, relative to the server's BepInEx root. The plugin has the same constant.</summary>
  public const string Directory = "BepInEx/config/LaunchHeim.Server";

  public const string CommandsDirectory = Directory + "/commands";
  public const string ResponsesDirectory = Directory + "/responses";

  /// <summary>How often the plugin looks for commands. The plugin has the same constant.</summary>
  public static readonly TimeSpan PluginPollInterval = TimeSpan.FromSeconds(2);

  public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

  private readonly TimeSpan _poll = pollInterval ?? TimeSpan.FromSeconds(1);

  public async Task<string> SendAsync(string command, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
  {
    // Sortable, so the plugin runs commands in the order they were sent.
    var id = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..30];
    var response = $"{ResponsesDirectory}/{id}.txt";
    await files.WriteTextAsync($"{CommandsDirectory}/{id}.cmd", command, cancellationToken);

    var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
    while (DateTime.UtcNow < deadline)
    {
      await Task.Delay(_poll, cancellationToken);
      if (await files.ReadTextAsync(response, cancellationToken) is { } answer)
      {
        await files.DeleteAsync(response, cancellationToken);
        return answer.TrimEnd('\n');
      }
    }

    // Left behind, the command would run whenever the plugin comes back, which nobody expects by then.
    await files.DeleteAsync($"{CommandsDirectory}/{id}.cmd", CancellationToken.None);
    throw new RconException("The server didn't answer. Is it running, with LaunchHeim's server plugin?");
  }
}
