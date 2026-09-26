using System.Net.Http.Headers;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Downloads;

public readonly record struct DownloadProgress(long Received, long? Total)
{
  public double? Fraction => Total is > 0 ? Received / (double)Total.Value : null;
}

/// <summary>Downloads mod files into the cache, keyed by source and file so a re-install is free.</summary>
public sealed class ModDownloader(HttpClient http, AppPaths paths)
{
  public async Task<string> DownloadAsync(
    Uri url,
    string cacheKey,
    string fileName,
    IReadOnlyDictionary<string, string>? headers,
    IProgress<DownloadProgress>? progress,
    CancellationToken cancellationToken)
  {
    var directory = Path.Combine(paths.DownloadsDirectory, Sanitize(cacheKey));
    var target = Path.Combine(directory, Sanitize(fileName));

    // Every source here publishes immutable files per version or file id, so a cached copy is valid.
    if (File.Exists(target))
    {
      return target;
    }

    Directory.CreateDirectory(directory);
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    foreach (var (name, value) in headers ?? new Dictionary<string, string>())
    {
      request.Headers.TryAddWithoutValidation(name, value);
    }

    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    response.EnsureSuccessStatusCode();

    // Thunderstore's download URL ends in a slash; the real name is only in the response.
    if (ContentDispositionName(response.Content.Headers.ContentDisposition) is { } served
      && fileName.EndsWith(".download", StringComparison.Ordinal))
    {
      target = Path.Combine(directory, Sanitize(served));
    }

    var total = response.Content.Headers.ContentLength;
    var temp = target + ".part";
    await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
    await using (var output = File.Create(temp))
    {
      var buffer = new byte[81920];
      long received = 0;
      var lastReport = 0L;
      int read;
      while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
      {
        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        received += read;

        // Reporting every buffer would flood the UI thread with property updates.
        if (received - lastReport > 256 * 1024 || received == total)
        {
          progress?.Report(new DownloadProgress(received, total));
          lastReport = received;
        }
      }
    }

    File.Move(temp, target, overwrite: true);
    return target;
  }

  private static string? ContentDispositionName(ContentDispositionHeaderValue? header) =>
    (header?.FileNameStar ?? header?.FileName)?.Trim('"') is { Length: > 0 } name ? name : null;

  internal static string Sanitize(string name)
  {
    var invalid = Path.GetInvalidFileNameChars();
    var safe = new string(name.Select(c => invalid.Contains(c) || c is ':' or '\\' ? '_' : c).ToArray());
    return safe.Trim('.', ' ') is { Length: > 0 } trimmed ? trimmed : "download";
  }
}
