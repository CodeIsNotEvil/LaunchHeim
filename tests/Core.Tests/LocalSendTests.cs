using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CINE.LaunchHeim.Core.LocalSend;

namespace CINE.LaunchHeim.Core.Tests;

/// <summary>Two nodes on localhost, talking HTTP as LaunchHeim and the companion app do. No multicast.</summary>
public sealed class LocalSendTests : IDisposable
{
  private readonly TempDirectory _temp = new();
  private readonly List<LocalSendNode> _nodes = [];

  public void Dispose()
  {
    _nodes.ForEach(n => n.Dispose());
    _temp.Dispose();
  }

  private LocalSendNode Node(string alias, int port, Func<FileDto, bool>? accepts = null)
  {
    var node = new LocalSendNode(
      () => new LocalSendNode.Identity(alias, "fp-" + alias),
      _temp.Combine("inbox-" + alias),
      accepts ?? (f => f.FileName.EndsWith(".r2z", StringComparison.Ordinal)),
      multicastPort: 0,
      httpPort: port,
      legacyDiscovery: false);
    _nodes.Add(node);
    node.Start();
    return node;
  }

  private static LocalSendPeer PeerOf(LocalSendNode node) => new(node.Info(), IPAddress.Loopback.ToString(), DateTimeOffset.UtcNow);

  [Fact]
  public async Task A_file_the_receiver_accepts_arrives_whole()
  {
    var receiver = Node("pc", 53510);
    var sender = Node("phone", 53520);
    var pack = _temp.Combine("Survival.r2z");
    File.WriteAllBytes(pack, Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251)).ToArray());

    var offered = new TaskCompletionSource<IncomingOffer>();
    var received = new TaskCompletionSource<ReceivedFiles>();
    receiver.OfferReceived += offer => { offered.TrySetResult(offer); offer.Accept(); };
    receiver.FilesReceived += files => received.TrySetResult(files);

    var result = await sender.SendAsync(PeerOf(receiver), [new OutgoingFile(pack, "Survival.r2z")], CancellationToken.None);

    Assert.Equal(SendOutcome.Sent, result.Outcome);
    Assert.Equal("phone", (await offered.Task).Sender.Alias);
    var files = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Assert.Equal(File.ReadAllBytes(pack), File.ReadAllBytes(files.Files.Single()));
  }

  [Fact]
  public async Task A_declined_offer_is_reported()
  {
    var receiver = Node("pc", 53530);
    var sender = Node("phone", 53540);
    var pack = _temp.Combine("Survival.r2z");
    File.WriteAllText(pack, "pack");
    receiver.OfferReceived += offer => offer.Decline();

    var result = await sender.SendAsync(PeerOf(receiver), [new OutgoingFile(pack, "Survival.r2z")], CancellationToken.None);

    Assert.Equal(SendOutcome.Declined, result.Outcome);
  }

  [Fact]
  public async Task Files_LaunchHeim_does_not_take_are_refused_without_asking()
  {
    var receiver = Node("pc", 53550);
    var sender = Node("phone", 53560);
    var photo = _temp.Combine("holiday.jpg");
    File.WriteAllText(photo, "jpeg");
    var asked = false;
    receiver.OfferReceived += _ => asked = true;

    var result = await sender.SendAsync(PeerOf(receiver), [new OutgoingFile(photo, "holiday.jpg")], CancellationToken.None);

    Assert.Equal(SendOutcome.Declined, result.Outcome);
    Assert.False(asked);
  }

  [Fact]
  public async Task Register_remembers_the_caller_and_answers_with_LaunchHeims_info()
  {
    var receiver = Node("pc", 53570);
    using var http = new HttpClient();
    var phone = new DeviceInfo { Alias = "Pixel", DeviceModel = "Samsung", DeviceType = "mobile", Fingerprint = "abc", Port = 53317, Protocol = "https" };

    using var response = await http.PostAsJsonAsync($"http://127.0.0.1:{receiver.Port}/api/localsend/v2/register", phone, LocalSendProtocol.Json);
    var answer = await response.Content.ReadFromJsonAsync<DeviceInfo>(LocalSendProtocol.Json);

    Assert.Equal("pc", answer!.Alias);
    Assert.True(answer.IsLaunchHeim);
    var peer = Assert.Single(receiver.Peers);
    Assert.Equal("Pixel", peer.Info.Alias);
    Assert.True(peer.Https);
  }

  [Fact]
  public async Task An_upload_needs_the_sessions_token()
  {
    var receiver = Node("pc", 53580);
    using var http = new HttpClient();
    using var response = await http.PostAsync($"http://127.0.0.1:{receiver.Port}/api/localsend/v2/upload?sessionId=x&fileId=y&token=z", new StringContent("data"));
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public void The_kotlin_side_reads_what_this_side_writes()
  {
    // Field names and casing as the companion's kotlinx.serialization classes expect them.
    var json = JsonSerializer.Serialize(new PrepareUploadResponse("s", new() { ["f"] = "t" }), LocalSendProtocol.Json);
    Assert.Equal("""{"sessionId":"s","files":{"f":"t"}}""", json);
  }

  [Theory]
  [InlineData("../../evil.r2z", "evil.r2z")]
  [InlineData("..\\..\\evil.r2z", "evil.r2z")]
  [InlineData("..", "fallback")]
  [InlineData("a:b.r2z", "a_b.r2z")]
  public void Received_file_names_stay_inside_the_inbox(string name, string expected) =>
    Assert.Equal(expected, LocalSendNode.SafeName(name, "fallback"));
}
