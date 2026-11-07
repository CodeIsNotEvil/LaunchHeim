using System.Text;
using CINE.LaunchHeim.Core.Logging;
using CINE.LaunchHeim.Core.Servers;
using Xunit.Abstractions;

namespace CINE.LaunchHeim.Core.Tests;

/// <summary>
/// The same checks for every way to a server's files. Local always runs; FTP and SFTP run against real
/// servers when <c>LAUNCHHEIM_TEST_FTP</c> / <c>LAUNCHHEIM_TEST_SFTP</c> name one as
/// <c>host:port:user:password[:folder]</c> (the podman containers in the second brain's server-manager
/// note), and pass without checking anything otherwise, so CI needs no servers.
/// </summary>
public class ServerFilesTests(ITestOutputHelper output) : IDisposable
{
  private readonly TempDirectory _temp = new();

  public void Dispose() => _temp.Dispose();

  [Fact]
  public Task Local_files_keep_the_contract() => CheckAsync(new LocalServerFiles(_temp.Combine("server")));

  [Fact]
  public Task Ftp_files_keep_the_contract() => FromEnvironmentAsync("LAUNCHHEIM_TEST_FTP", FileProtocol.Ftp);

  [Fact]
  public Task Sftp_files_keep_the_contract() => FromEnvironmentAsync("LAUNCHHEIM_TEST_SFTP", FileProtocol.Sftp);

  [Fact]
  public void Remote_paths_join_with_slashes()
  {
    Assert.Equal("BepInEx/config/a.cfg", RemotePath.Combine("", "BepInEx/config/a.cfg"));
    Assert.Equal("/home/foo/valheim/BepInEx", RemotePath.Combine("/home/foo/valheim/", "/BepInEx"));
    Assert.Equal("valheim/a", RemotePath.Combine("valheim", "a"));
    Assert.Equal("/home/foo", RemotePath.Parent("/home/foo/valheim"));
    Assert.Null(RemotePath.Parent("valheim"));
  }

  [Fact]
  public void Missing_settings_are_named_by_field_and_nothing_is_tried()
  {
    // The settings that crashed LaunchHeim on 2026-11-07: SFTP without a user name.
    var access = new HostAccess { Protocol = FileProtocol.Sftp, Address = "vps.example" };

    var error = Assert.Throws<ServerSetupException>(() => ServerConnections.Open(access));
    Assert.Equal(["user", "password"], error.Problems.Select(p => p.Field));
    Assert.IsAssignableFrom<IOException>(error);
  }

  [Theory]
  [InlineData("", "address")]
  [InlineData("sftp://vps.example", "address")]
  [InlineData("vps.example:22", "address")]
  [InlineData("vps.example/valheim", "address")]
  public void The_host_address_is_only_a_name(string address, string field)
  {
    var access = new HostAccess { Protocol = FileProtocol.Ftp, Address = address };

    Assert.Contains(access.Validate(), p => p.Field == field);
  }

  [Theory]
  [InlineData("203.0.113.7:2456", true)]
  [InlineData("valheim.example", true)]
  [InlineData("203.0.113.7:99999", false)]
  [InlineData("203.0.113.7:abc", false)]
  [InlineData("my server:2456", false)]
  public void The_join_address_is_host_and_port(string gameAddress, bool valid)
  {
    var access = new HostAccess { Protocol = FileProtocol.Ftp, Address = "ftp.example", GameAddress = gameAddress };

    Assert.Equal(valid, access.Validate().All(p => p.Field != "gameAddress"));
  }

  [Fact]
  public void Ftp_may_log_in_without_a_user_but_sftp_needs_one_and_a_secret()
  {
    Assert.Empty(new HostAccess { Protocol = FileProtocol.Ftp, Address = "ftp.example" }.Validate());
    Assert.Empty(new HostAccess { Protocol = FileProtocol.Sftp, Address = "vps.example", User = "foo", Password = "pw" }.Validate());
    Assert.Equal(["keyFile"], new HostAccess { Protocol = FileProtocol.Sftp, Address = "vps.example", User = "foo", KeyFile = "/nowhere/id_ed25519" }.Validate().Select(p => p.Field));
  }

  [Fact]
  public void Local_settings_name_their_fields()
  {
    var server = new ServerProfile { Name = "", World = "", Public = true, Password = "abc", Binary = ServerBinary.Custom };

    Assert.Equal(["name", "world", "password", "customDirectory"], server.Validate().Select(p => p.Field));
  }

  [Fact]
  public void Host_access_describes_itself_without_the_password()
  {
    var access = new HostAccess { Protocol = FileProtocol.Sftp, Address = "vps.example", User = "foo", Password = "secret", Directory = "/srv/valheim" };

    Assert.Equal("sftp://foo@vps.example:22/srv/valheim", access.Describe());
    Assert.Equal(21, new HostAccess { Protocol = FileProtocol.Ftps }.EffectivePort);
  }

  private async Task FromEnvironmentAsync(string variable, FileProtocol protocol)
  {
    if (Environment.GetEnvironmentVariable(variable) is not { Length: > 0 } value)
    {
      output.WriteLine($"{variable} isn't set; nothing to check against.");
      return;
    }

    var parts = value.Split(':');
    var access = new HostAccess
    {
      Protocol = protocol,
      Address = parts[0],
      Port = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
      User = parts[2],
      Password = parts[3],
      Directory = parts.Length > 4 ? parts[4] : "",
    };
    var learned = false;
    await using var files = ServerConnections.Open(access, () => learned = true);
    await CheckAsync(files);
    if (protocol == FileProtocol.Sftp)
    {
      Assert.True(learned);
      Assert.StartsWith("SHA256:", access.Fingerprint, StringComparison.Ordinal);

      // Another key than the pinned one is refused.
      access.Fingerprint = "SHA256:someone-else";
      await using var impostor = ServerConnections.Open(access);
      await Assert.ThrowsAsync<IOException>(() => impostor.SizeAsync("anything", default));
    }
  }

  private async Task CheckAsync(IServerFiles files)
  {
    var root = $"lh-test-{Guid.NewGuid():N}"[..16];

    Assert.Null(await files.ReadTextAsync($"{root}/missing.txt", default));
    Assert.Null(await files.SizeAsync($"{root}/missing.txt", default));
    Assert.Empty(await files.ListAsync($"{root}/nowhere", ".cmd", default));

    await files.WriteTextAsync($"{root}/a/b/hello.cmd", "save", default);
    Assert.Equal("save", await files.ReadTextAsync($"{root}/a/b/hello.cmd", default));
    await files.WriteTextAsync($"{root}/a/b/hello.cmd", "players", default);
    Assert.Equal("players", await files.ReadTextAsync($"{root}/a/b/hello.cmd", default));
    Assert.Equal(["hello.cmd"], await files.ListAsync($"{root}/a/b", ".cmd", default));

    var local = _temp.Combine("upload.bin");
    var content = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, 2000).Select(i => $"line {i}\n")));
    await File.WriteAllBytesAsync(local, content);
    await files.UploadAsync(local, $"{root}/x/server.sh", executable: true, default);
    Assert.Equal(content.Length, await files.SizeAsync($"{root}/x/server.sh", default));
    Assert.Equal(content.AsSpan(100, 50).ToArray(), await files.ReadRangeAsync($"{root}/x/server.sh", 100, 50, default));
    Assert.Equal(content[^10..], await files.ReadRangeAsync($"{root}/x/server.sh", content.Length - 10, 4096, default));
    Assert.Empty(await files.ReadRangeAsync($"{root}/x/server.sh", content.Length, 10, default));

    // The console follows a log through the same files.
    var follower = new LogFollower(new ServerFilesLogSource(files, $"{root}/x/server.sh"));
    Assert.Equal(2000, follower.Read().Lines.Count);
    Assert.Empty(follower.Read().Lines);

    // Deploying twice uploads once, and a file taken out is removed.
    var a = _temp.Combine("mod-a.dll");
    var b = _temp.Combine("mod-b.dll");
    await File.WriteAllTextAsync(a, "a");
    await File.WriteAllTextAsync(b, "b");
    var deployFiles = new RemoteRoot(files, root);
    var first = await ServerDeploy.RunAsync(deployFiles, "mods", [new("plugins/a.dll", a), new("plugins/b.dll", b)], false, false, null, default);
    var second = await ServerDeploy.RunAsync(deployFiles, "mods", [new("plugins/a.dll", a), new("plugins/b.dll", b)], false, false, null, default);
    var third = await ServerDeploy.RunAsync(deployFiles, "mods", [new("plugins/a.dll", a)], false, false, null, default);
    Assert.Equal((2, 0, 0), (first.Uploaded, first.Unchanged, first.Removed));
    Assert.Equal((0, 2, 0), (second.Uploaded, second.Unchanged, second.Removed));
    Assert.Equal((0, 1, 1), (third.Uploaded, third.Unchanged, third.Removed));
    Assert.Null(await files.SizeAsync($"{root}/plugins/b.dll", default));

    await files.DeleteAsync($"{root}/a/b/hello.cmd", default);
    await files.DeleteAsync($"{root}/a/b/hello.cmd", default);
    Assert.Empty(await files.ListAsync($"{root}/a/b", ".cmd", default));
    output.WriteLine($"Checked {files.Describe}/{root}");
  }

  /// <summary>Puts a deploy into a test's own folder, so runs don't see each other's records.</summary>
  private sealed class RemoteRoot(IServerFiles files, string root) : IServerFiles
  {
    public string Describe => files.Describe;
    public Task<string?> ReadTextAsync(string path, CancellationToken c) => files.ReadTextAsync($"{root}/{path}", c);
    public Task WriteTextAsync(string path, string text, CancellationToken c) => files.WriteTextAsync($"{root}/{path}", text, c);
    public Task UploadAsync(string local, string path, bool executable, CancellationToken c) => files.UploadAsync(local, $"{root}/{path}", executable, c);
    public Task<IReadOnlyList<string>> ListAsync(string directory, string extension, CancellationToken c) => files.ListAsync($"{root}/{directory}", extension, c);
    public Task<long?> SizeAsync(string path, CancellationToken c) => files.SizeAsync($"{root}/{path}", c);
    public Task<byte[]> ReadRangeAsync(string path, long offset, int count, CancellationToken c) => files.ReadRangeAsync($"{root}/{path}", offset, count, c);
    public Task DeleteAsync(string path, CancellationToken c) => files.DeleteAsync($"{root}/{path}", c);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }
}
