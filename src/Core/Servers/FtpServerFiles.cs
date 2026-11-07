using System.Security.Cryptography;
using System.Text;
using FluentFTP;
using FluentFTP.Exceptions;

namespace CINE.LaunchHeim.Core.Servers;

/// <summary>A hosted server's files over FTP or FTPS (FluentFTP).</summary>
/// <remarks>
/// <para>
/// One connection, opened on first use and opened again after it dropped (game hosts close idle FTP
/// sessions after a minute or two). Calls are serialized: FTP does one transfer per connection.
/// </para>
/// <para>
/// FTPS certificates: a certificate the system trusts is accepted. Game hosts often use self-signed ones,
/// so an untrusted certificate is pinned the first time (its SHA-256 goes into
/// <see cref="HostAccess.Fingerprint"/>) and must match from then on, as SSH does with host keys.
/// </para>
/// </remarks>
public sealed class FtpServerFiles : IServerFiles
{
  private readonly HostAccess _access;
  private readonly AsyncFtpClient _client;
  private readonly SemaphoreSlim _gate = new(1, 1);

  public FtpServerFiles(HostAccess access)
  {
    _access = access;
    _client = new AsyncFtpClient(access.Address, access.User.Length > 0 ? access.User : "anonymous", access.Password, access.EffectivePort);
    _client.Config.EncryptionMode = access.Protocol == FileProtocol.Ftps ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None;
    _client.Config.ConnectTimeout = 15000;
    _client.Config.ReadTimeout = 30000;
    _client.Config.DataConnectionReadTimeout = 30000;
    _client.Config.RetryAttempts = 2;
    _client.ValidateCertificate += (_, e) => e.Accept = Accept(e.Certificate, e.PolicyErrors);
  }

  /// <summary>Raised when a certificate was pinned for the first time, so the profile can be saved.</summary>
  public event Action? FingerprintLearned;

  public string Describe => _access.Describe();

  public Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken) => RunAsync(async () =>
  {
    var full = Full(path);
    if (!await _client.FileExists(full, cancellationToken))
    {
      return null;
    }

    var bytes = await _client.DownloadBytes(full, cancellationToken);
    return bytes is null ? null : Encoding.UTF8.GetString(bytes);
  });

  public Task WriteTextAsync(string path, string text, CancellationToken cancellationToken) => RunAsync(async () =>
  {
    var full = Full(path);
    var temp = full + ".tmp";
    await _client.UploadBytes(Encoding.UTF8.GetBytes(text), temp, FtpRemoteExists.Overwrite, createRemoteDir: true, token: cancellationToken);
    await _client.MoveFile(temp, full, FtpRemoteExists.Overwrite, cancellationToken);
    return true;
  });

  public Task UploadAsync(string localFile, string path, bool executable, CancellationToken cancellationToken) => RunAsync(async () =>
  {
    var full = Full(path);
    var temp = full + ".tmp";
    var status = await _client.UploadFile(localFile, temp, FtpRemoteExists.Overwrite, createRemoteDir: true, token: cancellationToken);
    if (status == FtpStatus.Failed)
    {
      throw new IOException($"The host refused {path}.");
    }

    await _client.MoveFile(temp, full, FtpRemoteExists.Overwrite, cancellationToken);
    if (executable)
    {
      try
      {
        await _client.Chmod(full, 755, cancellationToken);
      }
      catch (FtpCommandException)
      {
        // SITE CHMOD is optional, and Windows hosts don't have it. The host's own start runs the server anyway.
      }
    }

    return true;
  });

  public Task<IReadOnlyList<string>> ListAsync(string directory, string extension, CancellationToken cancellationToken) => RunAsync<IReadOnlyList<string>>(async () =>
  {
    var full = Full(directory);
    if (!await _client.DirectoryExists(full, cancellationToken))
    {
      return [];
    }

    var items = await _client.GetListing(full, cancellationToken);
    return items.Where(i => i.Type == FtpObjectType.File && i.Name.EndsWith(extension, StringComparison.Ordinal))
      .Select(i => i.Name)
      .Order(StringComparer.Ordinal)
      .ToList();
  });

  public Task<long?> SizeAsync(string path, CancellationToken cancellationToken) => RunAsync<long?>(async () =>
  {
    // -1 when the file doesn't exist (or the server can't say).
    var size = await _client.GetFileSize(Full(path), -1, cancellationToken);
    return size < 0 ? null : size;
  });

  public Task<byte[]> ReadRangeAsync(string path, long offset, int count, CancellationToken cancellationToken) => RunAsync(async () =>
  {
    // A transfer stopped early leaves the server's closing reply (426 or 226) on the control connection.
    // DownloadBytes with a stop position didn't read it, and the next command took "Transfer complete"
    // for its own answer (found 2026-11-07, with a backup's listing after the console's reads). So the
    // stream is read by hand and the reply fetched after closing it, as FluentFTP asks for OpenRead.
    var buffer = new byte[count];
    var read = 0;
    await using (var stream = await _client.OpenRead(Full(path), FtpDataType.Binary, offset, checkIfFileExists: false, cancellationToken))
    {
      while (read < count)
      {
        var n = await stream.ReadAsync(buffer.AsMemory(read, count - read), cancellationToken);
        if (n == 0)
        {
          break;
        }

        read += n;
      }
    }

    try
    {
      using var replyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      replyTimeout.CancelAfter(TimeSpan.FromSeconds(5));
      await _client.GetReply(replyTimeout.Token);
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
    {
      // No reply, or one that can't be read: start the next command on a fresh connection instead.
      await _client.Disconnect(CancellationToken.None);
    }

    return read == count ? buffer : buffer[..read];
  });

  public Task DeleteAsync(string path, CancellationToken cancellationToken) => RunAsync(async () =>
  {
    var full = Full(path);
    if (await _client.FileExists(full, cancellationToken))
    {
      await _client.DeleteFile(full, cancellationToken);
    }

    return true;
  });

  public Task<IReadOnlyList<ServerFileEntry>> ListEntriesAsync(string directory, CancellationToken cancellationToken) => RunAsync<IReadOnlyList<ServerFileEntry>>(async () =>
  {
    var full = Full(directory);
    if (!await _client.DirectoryExists(full.Length == 0 ? "." : full, cancellationToken))
    {
      return [];
    }

    var items = await _client.GetListing(full.Length == 0 ? "." : full, cancellationToken);
    return items.Where(i => i.Type is FtpObjectType.File or FtpObjectType.Directory && i.Name is not ("." or ".."))
      .Select(i => new ServerFileEntry(i.Name, i.Type == FtpObjectType.Directory, i.Type == FtpObjectType.File ? i.Size : 0))
      .OrderBy(e => e.Name, StringComparer.Ordinal)
      .ToList();
  });

  public Task DownloadAsync(string path, Stream destination, CancellationToken cancellationToken) => RunAsync(async () =>
  {
    if (!await _client.DownloadStream(destination, Full(path), token: cancellationToken))
    {
      throw new IOException($"The host didn't hand over {path}.");
    }

    return true;
  });

  public async ValueTask DisposeAsync()
  {
    try
    {
      if (_client.IsConnected)
      {
        await _client.Disconnect();
      }
    }
    catch (Exception ex) when (ex is IOException or FtpException or TimeoutException)
    {
    }

    await _client.DisposeAsync();
    _gate.Dispose();
  }

  private string Full(string path) => RemotePath.Combine(_access.Directory, path);

  private async Task<T> RunAsync<T>(Func<Task<T>> action)
  {
    await _gate.WaitAsync();
    try
    {
      if (!_client.IsConnected)
      {
        await _client.Connect();
      }

      return await action();
    }
    catch (FtpAuthenticationException ex)
    {
      throw new IOException($"The host refused the login for {_access.User}: {ex.Message}", ex);
    }
    catch (Exception ex) when (ex is not IOException and not OperationCanceledException)
    {
      // Whatever FluentFTP or the network throws becomes one kind of failure the callers handle.
      throw new IOException($"FTP to {_access.Address} failed: {ex.Message}", ex);
    }
    finally
    {
      _gate.Release();
    }
  }

  private bool Accept(System.Security.Cryptography.X509Certificates.X509Certificate certificate, System.Net.Security.SslPolicyErrors errors)
  {
    if (errors == System.Net.Security.SslPolicyErrors.None)
    {
      return true;
    }

    var fingerprint = "sha256:" + Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())).ToLowerInvariant();
    if (_access.Fingerprint is null)
    {
      _access.Fingerprint = fingerprint;
      FingerprintLearned?.Invoke();
      return true;
    }

    return _access.Fingerprint == fingerprint;
  }
}
