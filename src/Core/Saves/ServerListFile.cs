using System.Text;

namespace CINE.LaunchHeim.Core.Saves;

internal sealed record ServerListEntry(string Name, string Address);

/// <summary>Valheim's server list files, <c>serverlist_local/favorite</c> and <c>recent</c> (and their cloud copies).</summary>
/// <remarks>
/// The layout follows <c>LocalServerList.LoadUniqueServerListEntriesIntoList</c>: an int32 length, then a
/// ZPackage holding a uint32 version, an int32 count and per entry the backend type, the server's name and
/// the backend's data. ZPackage strings are .NET <see cref="BinaryReader"/> strings (7-bit length, UTF-8),
/// so <see cref="BinaryReader"/> reads them as they are.
/// </remarks>
internal static class ServerListFile
{
  /// <summary>The dedicated servers in the file, in its order. A damaged or unknown file gives what could be read.</summary>
  public static IReadOnlyList<ServerListEntry> ReadDedicated(byte[] file)
  {
    var result = new List<ServerListEntry>();
    try
    {
      using var outer = new BinaryReader(new MemoryStream(file));
      var length = outer.ReadInt32();
      using var reader = new BinaryReader(new MemoryStream(outer.ReadBytes(length)), Encoding.UTF8);

      // Version 0 stored IPv4 addresses as numbers. Valheim only writes version 2 now, so old lists are skipped.
      var version = reader.ReadUInt32();
      if (version is not (1 or 2))
      {
        return result;
      }

      var count = reader.ReadInt32();
      for (var i = 0; i < count; i++)
      {
        var type = reader.ReadString();
        var name = reader.ReadString();
        switch (type)
        {
          case "Dedicated":
            var host = reader.ReadString();
            var port = (ushort)reader.ReadUInt32();
            result.Add(new ServerListEntry(name, FormatAddress(host, port)));
            break;
          case "Steam user":
            reader.ReadUInt64();
            SkipPlatformId(reader, version);
            break;
          case "PlayFab user":
            reader.ReadString();
            SkipPlatformId(reader, version);
            break;
          default:
            // An unknown backend's data can't be skipped, so nothing after it can be read either.
            return result;
        }
      }
    }
    catch (EndOfStreamException)
    {
    }
    catch (IOException)
    {
    }

    return result;
  }

  private static void SkipPlatformId(BinaryReader reader, uint version)
  {
    if (version == 2)
    {
      reader.ReadString();
    }
  }

  // IPv6 needs brackets so the port can be told apart, which is how ServerJoinDataDedicated writes it too.
  private static string FormatAddress(string host, ushort port) =>
    host.Contains(':') && !host.StartsWith('[') ? $"[{host}]:{port}" : $"{host}:{port}";
}
