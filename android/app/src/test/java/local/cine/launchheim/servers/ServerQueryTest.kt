package local.cine.launchheim.servers

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ServerQueryTest {
  @Test
  fun queryPortIsTheGamePortPlusOne() {
    assertEquals("example.com" to 2457, ServerQuery.queryEndpoint("example.com"))
    assertEquals("10.0.0.5" to 2459, ServerQuery.queryEndpoint(" 10.0.0.5:2458 "))
    assertEquals("::1" to 2457, ServerQuery.queryEndpoint("[::1]:2456"))
    assertEquals("fe80::1" to 2457, ServerQuery.queryEndpoint("fe80::1"))
    assertNull(ServerQuery.queryEndpoint("host:notaport"))
    assertNull(ServerQuery.queryEndpoint(""))
  }

  @Test
  fun parsesPlayerCountsFromA2sInfo() {
    // protocol, name, map, folder, game, app id (LE), players, max players, ...
    val payload = byteArrayOf(17) + "Walheim\u0000".toByteArray() + "Map\u0000".toByteArray() + "valheim\u0000".toByteArray() +
      "Valheim\u0000".toByteArray() + byteArrayOf(0, 0, 3, 10, 0)
    assertEquals(3 to 10, ServerQuery.parseInfo(payload))
    assertNull(ServerQuery.parseInfo(byteArrayOf(17, 65)))
  }

  @Test
  fun skipsTheEmptyNamesValheimReports() {
    fun player(name: String) = byteArrayOf(0) + "$name\u0000".toByteArray() + ByteArray(8)
    val payload = byteArrayOf(3) + player("") + player("Ragnar") + player("")
    assertEquals(listOf("Ragnar"), ServerQuery.parsePlayers(payload))
  }
}
