using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Desktop.Hosting;

/// <summary>Downloads remote images for QML and hands back local file URLs.</summary>
/// <remarks>
/// Qt 5.15 speaks HTTPS only through OpenSSL 1.1, which current distributions no longer ship, so an
/// <c>Image { source: "https://..." }</c> silently stays empty. Fetching through .NET avoids that and
/// doubles as an offline cache for mod icons.
/// </remarks>
public sealed partial class ImageCache(HttpClient http, AppPaths paths)
{
  private const long MaxBytes = 8 * 1024 * 1024;
  private readonly ConcurrentDictionary<string, Task<string?>> _inflight = new();
  private readonly SemaphoreSlim _parallel = new(6, 6);

  private string Directory => Path.Combine(paths.CacheDirectory, "images");

  /// <returns>A file:// URL, or null if the image could not be fetched.</returns>
  public Task<string?> GetAsync(string? url)
  {
    if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
    {
      return Task.FromResult<string?>(null);
    }

    return _inflight.GetOrAdd(url, _ => Task.Run(() => FetchAsync(uri)));
  }

  private async Task<string?> FetchAsync(Uri uri)
  {
    var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
    if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".bmp"))
    {
      extension = ".img";
    }

    var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..32];
    var file = Path.Combine(Directory, hash + extension);
    if (File.Exists(file))
    {
      return new Uri(file).AbsoluteUri;
    }

    await _parallel.WaitAsync();
    try
    {
      using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
      if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
      {
        return null;
      }

      System.IO.Directory.CreateDirectory(Directory);
      var temp = file + ".part";
      await using (var input = await response.Content.ReadAsStreamAsync())
      await using (var output = File.Create(temp))
      {
        await input.CopyToAsync(output);
      }

      File.Move(temp, file, overwrite: true);
      return new Uri(file).AbsoluteUri;
    }
    catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
    {
      return null;
    }
    finally
    {
      _parallel.Release();
    }
  }

  /// <summary>
  /// Rewrites every <c>&lt;img&gt;</c> in a description to a cached local copy, scaled down to fit
  /// <paramref name="maxWidth"/>. Images that fail to download are dropped rather than left broken.
  /// </summary>
  public async Task<string> LocalizeImagesAsync(string html, int maxWidth)
  {
    var matches = ImageTag().Matches(html).Cast<Match>().Take(40).ToList();
    var urls = matches.Select(m => System.Net.WebUtility.HtmlDecode(m.Groups["src"].Value)).Distinct().ToList();
    var local = new Dictionary<string, string?>();
    foreach (var (url, path) in urls.Zip(await Task.WhenAll(urls.Select(GetAsync))))
    {
      local[url] = path;
    }

    return ImageTag().Replace(html, m =>
    {
      var src = System.Net.WebUtility.HtmlDecode(m.Groups["src"].Value);
      if (local.GetValueOrDefault(src) is not { } file)
      {
        return "";
      }

      // Qt's rich text never scales images to the text width, so a 1920px banner would overflow.
      var size = ImageSize.Read(new Uri(file).LocalPath);
      var attributes = size is { } s && s.Width > maxWidth
        ? $" width=\"{maxWidth}\" height=\"{s.Height * maxWidth / s.Width}\""
        : size is { } fits ? $" width=\"{fits.Width}\" height=\"{fits.Height}\"" : $" width=\"{maxWidth}\"";
      return $"<img src=\"{file}\"{attributes}/>";
    });
  }

  public long SizeOnDisk() =>
    System.IO.Directory.Exists(Directory) ? new DirectoryInfo(Directory).EnumerateFiles().Sum(f => f.Length) : 0;

  [GeneratedRegex("""<img\b[^>]*?\bsrc\s*=\s*["'](?<src>[^"']+)["'][^>]*>""", RegexOptions.IgnoreCase)]
  private static partial Regex ImageTag();
}

/// <summary>Reads image dimensions from file headers, so no image library is needed.</summary>
public static class ImageSize
{
  public static (int Width, int Height)? Read(string path)
  {
    try
    {
      using var stream = File.OpenRead(path);
      Span<byte> header = stackalloc byte[32];
      var read = stream.Read(header);
      if (read < 24)
      {
        return null;
      }

      // PNG: width and height are big-endian ints in the IHDR chunk.
      if (header[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
      {
        return (BigEndian(header[16..20]), BigEndian(header[20..24]));
      }

      // GIF: little-endian shorts after the signature.
      if (header[..3].SequenceEqual("GIF"u8))
      {
        return (header[6] | header[7] << 8, header[8] | header[9] << 8);
      }

      // WebP lossy (VP8 ) and lossless (VP8L).
      if (header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
      {
        stream.Position = 0;
        var webp = new byte[30];
        if (stream.Read(webp) < 30)
        {
          return null;
        }

        if (webp.AsSpan(12, 4).SequenceEqual("VP8 "u8))
        {
          return ((webp[26] | webp[27] << 8) & 0x3FFF, (webp[28] | webp[29] << 8) & 0x3FFF);
        }

        if (webp.AsSpan(12, 4).SequenceEqual("VP8L"u8))
        {
          var bits = webp[21] | webp[22] << 8 | webp[23] << 16 | webp[24] << 24;
          return ((bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1);
        }

        return null;
      }

      // JPEG: walk the segments to the first start-of-frame marker.
      if (header[0] == 0xFF && header[1] == 0xD8)
      {
        stream.Position = 2;
        var segment = new byte[9];
        while (stream.Read(segment, 0, 4) == 4)
        {
          if (segment[0] != 0xFF)
          {
            return null;
          }

          var marker = segment[1];
          var length = segment[2] << 8 | segment[3];
          if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
          {
            if (stream.Read(segment, 0, 5) != 5)
            {
              return null;
            }

            return (segment[3] << 8 | segment[4], segment[1] << 8 | segment[2]);
          }

          stream.Position += length - 2;
        }
      }
    }
    catch (IOException)
    {
    }

    return null;
  }

  private static int BigEndian(ReadOnlySpan<byte> bytes) => bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3];
}
