package local.cine.launchheim.localsend

import java.io.File
import java.net.InetAddress
import kotlinx.coroutines.flow.filterNotNull
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

/** Two nodes on localhost, talking HTTP like LaunchHeim and the phone do. Multicast isn't involved. */
class LocalSendNodeTest {
  @get:Rule val temp = TemporaryFolder()

  private val nodes = mutableListOf<LocalSendNode>()

  @After
  fun stopAll() = nodes.forEach { it.stop() }

  private fun node(alias: String, port: Int, accepts: (FileDto) -> Boolean = { it.fileName.endsWith(".r2z") }) =
    LocalSendNode(
      identity = { LocalSendNode.Identity(alias, "fp-$alias") },
      inbox = temp.newFolder("inbox-$alias"),
      accepts = accepts,
      multicastPort = 0,
      httpPort = port,
      legacyDiscovery = false,
    ).also { nodes += it; it.start() }

  private fun peerOf(node: LocalSendNode) = Peer(node.info(), InetAddress.getLoopbackAddress().hostAddress!!, 0)

  @Test
  fun sendsAFileTheReceiverAccepts() = runBlocking {
    val receiver = node("pc", 0)
    val sender = node("phone", 0)
    val pack = File(temp.root, "Survival.r2z").apply { writeBytes(ByteArray(200_000) { (it % 251).toByte() }) }

    launch { receiver.incoming.filterNotNull().first().accept() }
    val received = launch {
      val files = withTimeout(10_000) { receiver.received.first() }
      assertEquals("phone", files.sender.alias)
      assertEquals(pack.readBytes().toList(), files.files.single().readBytes().toList())
    }

    assertEquals(SendResult.Sent, sender.send(peerOf(receiver), listOf(OutgoingFile(pack, pack.name))))
    received.join()
  }

  @Test
  fun aDeclinedOfferIsReportedAsDeclined() = runBlocking {
    val receiver = node("pc", 0)
    val sender = node("phone", 0)
    val pack = File(temp.root, "Survival.r2z").apply { writeText("pack") }

    launch { receiver.incoming.filterNotNull().first().decline() }
    assertEquals(SendResult.Declined, sender.send(peerOf(receiver), listOf(OutgoingFile(pack, pack.name))))
  }

  @Test
  fun filesTheAppDoesNotTakeAreRejectedWithoutAsking() = runBlocking {
    val receiver = node("pc", 0)
    val sender = node("phone", 0)
    val photo = File(temp.root, "holiday.jpg").apply { writeText("jpeg") }

    assertEquals(SendResult.Declined, sender.send(peerOf(receiver), listOf(OutgoingFile(photo, photo.name))))
    assertEquals(null, receiver.incoming.value)
  }

  @Test
  fun registerRemembersTheCallerAndAnswersWithItsOwnInfo() {
    val receiver = node("pc", 0)
    val body = """{"alias":"LocalSend phone","version":"2.0","deviceModel":"Samsung","deviceType":"mobile","fingerprint":"abc","port":53317,"protocol":"https"}"""
    val response = OkHttpClient().newCall(
      Request.Builder().url("http://127.0.0.1:${receiver.port}/api/localsend/v2/register").post(body.toRequestBody("application/json".toMediaType())).build(),
    ).execute()

    val answer = LocalSend.Json.decodeFromString<DeviceInfo>(response.body.string())
    assertEquals("pc", answer.alias)
    assertEquals("LaunchHeim", answer.deviceModel)
    val peer = receiver.peers.value.single()
    assertEquals("LocalSend phone", peer.info.alias)
    assertTrue(peer.https)
  }

  @Test
  fun uploadsArePinnedToTheSessionToken() {
    val receiver = node("pc", 0)
    val response = OkHttpClient().newCall(
      Request.Builder().url("http://127.0.0.1:${receiver.port}/api/localsend/v2/upload?sessionId=x&fileId=y&token=z")
        .post("data".toRequestBody()).build(),
    ).execute()
    assertEquals(403, response.code)
  }

  @Test
  fun safeNamesStayInsideTheInbox() {
    assertEquals("evil.r2z", LocalSendNode.safeName("../../evil.r2z", "f"))
    assertEquals("f", LocalSendNode.safeName("..", "f"))
    assertEquals("a_b.r2z", LocalSendNode.safeName("a:b.r2z", "f"))
  }
}
