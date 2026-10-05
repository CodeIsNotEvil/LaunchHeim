using System.Text.Json;
using System.Text.Json.Serialization;

namespace CINE.LaunchHeim.Core.LocalSend;

/// <summary>
/// The LocalSend protocol v2 (https://github.com/localsend/protocol), which LaunchHeim and its Android
/// companion speak to each other and which the LocalSend app understands too.
/// </summary>
/// <remarks>
/// Discovery is a JSON announcement to a multicast group, answered with <c>POST /register</c> (or a
/// multicast reply). A transfer is <c>POST /prepare-upload</c> with the file list, which the receiver
/// accepts or rejects, then one <c>POST /upload</c> per file. The companion's
/// <c>localsend/LocalSendNode.kt</c> is the same thing in Kotlin; keep the two in step.
/// </remarks>
public static class LocalSendProtocol
{
  public const string MulticastGroup = "224.0.0.167";
  public const int DefaultPort = 53317;
  public const string Version = "2.1";
  public const string Api = "/api/localsend/v2";

  /// <summary>
  /// What LaunchHeim and the companion put in <c>deviceModel</c>, so each can tell the other apart from
  /// other LocalSend devices and list it first. LocalSend shows it under the device name.
  /// </summary>
  public const string DeviceModel = "LaunchHeim";

  public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
  {
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
  };
}

/// <summary>A device's description, sent in announcements, <c>/register</c> and <c>/prepare-upload</c>.</summary>
public sealed record DeviceInfo
{
  public string Alias { get; init; } = "";
  public string Version { get; init; } = LocalSendProtocol.Version;
  public string? DeviceModel { get; init; }

  /// <summary>mobile, desktop, web, headless or server.</summary>
  public string? DeviceType { get; init; }

  /// <summary>Random per install when unencrypted; only used to recognise one's own announcements.</summary>
  public string Fingerprint { get; init; } = "";

  public int? Port { get; init; }

  /// <summary>http or https.</summary>
  public string? Protocol { get; init; }

  public bool? Download { get; init; }
  public bool? Announce { get; init; }

  [JsonIgnore]
  public bool IsLaunchHeim => DeviceModel == LocalSendProtocol.DeviceModel;
}

public sealed record FileDto(string Id, string FileName, long Size, string FileType = "application/octet-stream", string? Sha256 = null, string? Preview = null);

public sealed record PrepareUploadRequest(DeviceInfo Info, Dictionary<string, FileDto> Files);

public sealed record PrepareUploadResponse(string SessionId, Dictionary<string, string> Files);

/// <summary>Another device on the network.</summary>
public sealed record LocalSendPeer(DeviceInfo Info, string Address, DateTimeOffset LastSeen)
{
  public int Port => Info.Port ?? LocalSendProtocol.DefaultPort;

  public bool Https => string.Equals(Info.Protocol, "https", StringComparison.OrdinalIgnoreCase);

  /// <summary>Identifies the peer in the UI, which can't hold the record itself.</summary>
  public string Id => $"{Info.Fingerprint}@{Address}:{Port}";

  public string BaseUrl => $"{(Https ? "https" : "http")}://{(Address.Contains(':') ? $"[{Address}]" : Address)}:{Port}{LocalSendProtocol.Api}";
}

/// <summary>A transfer another device wants to make, waiting for the user to accept or decline it.</summary>
public sealed class IncomingOffer(DeviceInfo sender, string address, IReadOnlyList<FileDto> files)
{
  private readonly TaskCompletionSource<bool> _decision = new(TaskCreationOptions.RunContinuationsAsynchronously);

  public DeviceInfo Sender => sender;
  public string Address => address;
  public IReadOnlyList<FileDto> Files => files;

  internal Task<bool> Decision => _decision.Task;

  public void Accept() => _decision.TrySetResult(true);

  public void Decline() => _decision.TrySetResult(false);
}

/// <summary>Files that arrived complete, in the order they were offered.</summary>
public sealed record ReceivedFiles(DeviceInfo Sender, IReadOnlyList<string> Files);

/// <summary>A file to send and the name the receiver sees.</summary>
public sealed record OutgoingFile(string Path, string FileName, string FileType = "application/octet-stream");

public enum SendOutcome
{
  Sent,
  Declined,
  Busy,
  Failed,
}

public sealed record SendResult(SendOutcome Outcome, string Message = "");
