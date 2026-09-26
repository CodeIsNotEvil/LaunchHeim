using System.IO.Compression;
using System.Net;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Tests;

/// <summary>A scratch folder per test, laid out like the real XDG directories.</summary>
public sealed class TempDirectory : IDisposable
{
  public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "launchheim-tests-" + Guid.NewGuid().ToString("N"));

  public TempDirectory() => Directory.CreateDirectory(Path);

  public AppPaths AppPaths => new(Combine("data"), Combine("config"), Combine("cache"), Combine("run"));

  public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

  /// <summary>Creates files with the given relative paths (forward slashes) and returns the root.</summary>
  public string Tree(string name, params string[] files)
  {
    var root = Combine(name);
    foreach (var file in files)
    {
      var full = System.IO.Path.Combine(root, file);
      Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
      File.WriteAllText(full, file);
    }

    Directory.CreateDirectory(root);
    return root;
  }

  public void Dispose()
  {
    try
    {
      Directory.Delete(Path, recursive: true);
    }
    catch (IOException)
    {
    }
  }
}

public static class Zip
{
  public static byte[] Create(params (string Path, string Content)[] entries)
  {
    using var memory = new MemoryStream();
    using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
    {
      foreach (var (path, content) in entries)
      {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
      }
    }

    return memory.ToArray();
  }
}

/// <summary>Serves canned responses by URL and records what was requested.</summary>
public sealed class FakeHttp : HttpMessageHandler
{
  private readonly Dictionary<string, byte[]> _responses = new(StringComparer.Ordinal);

  public List<string> Requests { get; } = [];

  public FakeHttp Serve(string url, byte[] body)
  {
    _responses[url] = body;
    return this;
  }

  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
  {
    var url = request.RequestUri!.ToString();
    lock (Requests)
    {
      Requests.Add(url);
    }

    return Task.FromResult(_responses.TryGetValue(url, out var body)
      ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
      : new HttpResponseMessage(HttpStatusCode.NotFound));
  }
}
