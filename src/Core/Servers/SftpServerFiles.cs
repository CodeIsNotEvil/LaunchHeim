using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace CINE.LaunchHeim.Core.Servers;

/// <summary>A hosted server's files over SFTP (SSH.NET): a VPS, or a host that offers SFTP.</summary>
/// <remarks>
/// <para>
/// One connection, opened on first use and again after it dropped; calls are serialized like FTP's so
/// both behave the same. Password or private key login, whichever the profile has.
/// </para>
/// <para>
/// The host key is trusted on first use: its SHA-256 fingerprint goes into <see cref="HostAccess.Fingerprint"/>
/// and a different key later is refused, which is what SSH's <c>known_hosts</c> does. Clearing the
/// fingerprint in the server's settings accepts a new one (after a host reinstalled its server, say).
/// </para>
/// </remarks>
public sealed class SftpServerFiles : IServerFiles
{
  private readonly HostAccess _access;
  private readonly SftpClient _client;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private bool _keyRefused;

  public SftpServerFiles(HostAccess access)
  {
    _access = access;
    var user = access.User;
    List<AuthenticationMethod> methods = [];
    if (!string.IsNullOrEmpty(access.KeyFile))
    {
      // An encrypted key takes the password field as its passphrase.
      methods.Add(new PrivateKeyAuthenticationMethod(user, string.IsNullOrEmpty(access.Password)
        ? new PrivateKeyFile(access.KeyFile)
        : new PrivateKeyFile(access.KeyFile, access.Password)));
    }

    if (access.Password.Length > 0)
    {
      methods.Add(new PasswordAuthenticationMethod(user, access.Password));
      methods.Add(KeyboardInteractive(user, access.Password));
    }

    if (methods.Count == 0)
    {
      methods.Add(new NoneAuthenticationMethod(user));
    }

    var info = new ConnectionInfo(access.Address, access.EffectivePort, user, [.. methods])
    {
      Timeout = TimeSpan.FromSeconds(15),
    };
    _client = new SftpClient(info) { OperationTimeout = TimeSpan.FromSeconds(60) };
    _client.HostKeyReceived += (_, e) => e.CanTrust = Accept(e);
  }

  /// <summary>Raised when the host key was pinned for the first time, so the profile can be saved.</summary>
  public event Action? FingerprintLearned;

  public string Describe => _access.Describe();

  public Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken) => RunAsync<string?>(async () =>
  {
    var full = Full(path);
    if (!await _client.ExistsAsync(full, cancellationToken))
    {
      return null;
    }

    using var buffer = new MemoryStream();
    await _client.DownloadFileAsync(full, buffer, cancellationToken);
    return Encoding.UTF8.GetString(buffer.ToArray());
  }, cancellationToken);

  public Task WriteTextAsync(string path, string text, CancellationToken cancellationToken) => RunAsync(async () =>
  {
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
    await PutAsync(stream, Full(path), executable: false, cancellationToken);
    return true;
  }, cancellationToken);

  public Task UploadAsync(string localFile, string path, bool executable, CancellationToken cancellationToken) => RunAsync(async () =>
  {
    await using var stream = File.OpenRead(localFile);
    await PutAsync(stream, Full(path), executable, cancellationToken);
    return true;
  }, cancellationToken);

  public Task<IReadOnlyList<string>> ListAsync(string directory, string extension, CancellationToken cancellationToken) => RunAsync<IReadOnlyList<string>>(async () =>
  {
    var full = Full(directory);
    if (!await _client.ExistsAsync(full.Length == 0 ? "." : full, cancellationToken))
    {
      return [];
    }

    var names = new List<string>();
    await foreach (var item in _client.ListDirectoryAsync(full.Length == 0 ? "." : full, cancellationToken))
    {
      if (item.IsRegularFile && item.Name.EndsWith(extension, StringComparison.Ordinal))
      {
        names.Add(item.Name);
      }
    }

    return names.Order(StringComparer.Ordinal).ToList();
  }, cancellationToken);

  public Task<long?> SizeAsync(string path, CancellationToken cancellationToken) => RunAsync<long?>(async () =>
  {
    var full = Full(path);
    if (!await _client.ExistsAsync(full, cancellationToken))
    {
      return null;
    }

    return (await _client.GetAttributesAsync(full, cancellationToken)).Size;
  }, cancellationToken);

  public Task<byte[]> ReadRangeAsync(string path, long offset, int count, CancellationToken cancellationToken) => RunAsync(async () =>
  {
    await using var stream = await _client.OpenAsync(Full(path), FileMode.Open, FileAccess.Read, cancellationToken);
    if (offset >= stream.Length)
    {
      return [];
    }

    stream.Position = offset;
    var buffer = new byte[(int)Math.Min(count, stream.Length - offset)];
    await stream.ReadExactlyAsync(buffer, cancellationToken);
    return buffer;
  }, cancellationToken);

  public Task DeleteAsync(string path, CancellationToken cancellationToken) => RunAsync(async () =>
  {
    var full = Full(path);
    if (await _client.ExistsAsync(full, cancellationToken))
    {
      await _client.DeleteFileAsync(full, cancellationToken);
    }

    return true;
  }, cancellationToken);

  public ValueTask DisposeAsync()
  {
    try
    {
      if (_client.IsConnected)
      {
        _client.Disconnect();
      }
    }
    catch (Exception ex) when (ex is SshException or IOException or ObjectDisposedException)
    {
    }

    _client.Dispose();
    _gate.Dispose();
    return ValueTask.CompletedTask;
  }

  private string Full(string path) => RemotePath.Combine(_access.Directory, path);

  /// <summary>Uploads under a temporary name and renames it over the target, creating folders first.</summary>
  private async Task PutAsync(Stream content, string full, bool executable, CancellationToken cancellationToken)
  {
    await EnsureDirectoryAsync(RemotePath.Parent(full), cancellationToken);
    var temp = full + ".tmp";
    await _client.UploadFileAsync(content, temp, true, null, cancellationToken);
    if (executable)
    {
      _client.ChangePermissions(temp, 755);
    }

    // POSIX rename replaces atomically where the server supports it (OpenSSH does); otherwise delete first.
    try
    {
      _client.RenameFile(temp, full, isPosix: true);
    }
    catch (SshException)
    {
      if (await _client.ExistsAsync(full, cancellationToken))
      {
        await _client.DeleteFileAsync(full, cancellationToken);
      }

      await _client.RenameFileAsync(temp, full, cancellationToken);
    }
  }

  private async Task EnsureDirectoryAsync(string? directory, CancellationToken cancellationToken)
  {
    if (string.IsNullOrEmpty(directory) || directory == "/" || await _client.ExistsAsync(directory, cancellationToken))
    {
      return;
    }

    await EnsureDirectoryAsync(RemotePath.Parent(directory), cancellationToken);
    await _client.CreateDirectoryAsync(directory, cancellationToken);
  }

  private async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
  {
    await _gate.WaitAsync(cancellationToken);
    try
    {
      if (!_client.IsConnected)
      {
        await _client.ConnectAsync(cancellationToken);
      }

      return await action();
    }
    catch (SshConnectionException) when (_keyRefused)
    {
      throw new IOException($"{_access.Address} showed a different SSH host key than the first time. If the host really changed it, clear the saved key in the server's connection settings.");
    }
    catch (SshAuthenticationException ex)
    {
      throw new IOException($"The host refused the login for {_access.User}: {ex.Message}", ex);
    }
    catch (Exception ex) when (ex is SshException or System.Net.Sockets.SocketException or TimeoutException or SftpPathNotFoundException or SftpPermissionDeniedException)
    {
      throw new IOException($"SFTP to {_access.Address} failed: {ex.Message}", ex);
    }
    finally
    {
      _gate.Release();
    }
  }

  private bool Accept(HostKeyEventArgs e)
  {
    var fingerprint = "SHA256:" + e.FingerPrintSHA256;
    if (_access.Fingerprint is null)
    {
      _access.Fingerprint = fingerprint;
      FingerprintLearned?.Invoke();
      return true;
    }

    _keyRefused = _access.Fingerprint != fingerprint;
    return !_keyRefused;
  }

  // Some hosts only allow keyboard-interactive logins, which ask for the password as a prompt.
  private static KeyboardInteractiveAuthenticationMethod KeyboardInteractive(string user, string password)
  {
    var method = new KeyboardInteractiveAuthenticationMethod(user);
    method.AuthenticationPrompt += (_, e) =>
    {
      foreach (var prompt in e.Prompts)
      {
        prompt.Response = password;
      }
    };
    return method;
  }
}

/// <summary>Opens a hosted server's files with the protocol its profile names.</summary>
public static class ServerConnections
{
  /// <param name="learned">Called when a host key or certificate was pinned for the first time, to save the profile.</param>
  public static IServerFiles Open(HostAccess access, Action? learned = null)
  {
    if (access.Protocol == FileProtocol.Sftp)
    {
      var sftp = new SftpServerFiles(access);
      if (learned is not null)
      {
        sftp.FingerprintLearned += learned;
      }

      return sftp;
    }

    var ftp = new FtpServerFiles(access);
    if (learned is not null)
    {
      ftp.FingerprintLearned += learned;
    }

    return ftp;
  }
}
