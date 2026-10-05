package local.cine.launchheim.servers

import java.io.ByteArrayInputStream
import java.io.DataInputStream
import java.io.EOFException
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.SocketTimeoutException
import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runInterruptible
import kotlinx.coroutines.withTimeoutOrNull

/** Who is on a server right now, as the server reports it. */
data class ServerStatus(val players: Int, val maxPlayers: Int, val playerNames: List<String>)

/**
 * Asks a Valheim dedicated server how many players are on it with Steam's server query (A2S), ported from
 * LaunchHeim's Core/Saves/ServerQuery.cs. The server answers on the query port, the game port + 1.
 *
 * Valheim's own server lists its players to Steam with empty names, so usually only the count is known.
 * Both requests follow the challenge rule Valve added in 2020: the server may answer with a challenge
 * (`0x41`), and the request is sent again with it appended.
 */
object ServerQuery {
  const val DEFAULT_GAME_PORT = 2456
  const val DEFAULT_TIMEOUT_MS = 3000L

  private const val INFO_REQUEST: Byte = 0x54
  private const val INFO_RESPONSE: Byte = 0x49
  private const val PLAYER_REQUEST: Byte = 0x55
  private const val PLAYER_RESPONSE: Byte = 0x44
  private const val CHALLENGE_RESPONSE: Byte = 0x41
  private const val SINGLE_PACKET = -1

  /** The status, or null when the server didn't answer in time or couldn't be found. */
  suspend fun query(address: String, timeoutMs: Long = DEFAULT_TIMEOUT_MS): ServerStatus? {
    val (host, port) = queryEndpoint(address) ?: return null
    return withTimeoutOrNull(timeoutMs) {
      runInterruptible(Dispatchers.IO) {
        try {
          queryBlocking(host, port, timeoutMs.toInt())
        } catch (_: IOException) {
          // Refused, no route, unknown host or timed out: the server counts as off.
          null
        }
      }
    }
  }

  private fun queryBlocking(host: String, port: Int, timeoutMs: Int): ServerStatus? {
    // IPv4 first: most home servers are only reachable that way, even where DNS also has an AAAA record.
    val addresses = InetAddress.getAllByName(host)
    val ip = addresses.firstOrNull { it is Inet4Address } ?: addresses.firstOrNull() ?: return null

    DatagramSocket().use { udp ->
      udp.soTimeout = timeoutMs
      udp.connect(InetSocketAddress(ip, port))

      val info = request(udp, infoPacket(ByteArray(0)), INFO_RESPONSE, ::infoPacket) ?: return null
      val (players, maxPlayers) = parseInfo(info) ?: return null

      // The count alone is worth showing, so a server that ignores A2S_PLAYER still gets one.
      val names = if (players > 0) {
        try {
          request(udp, playerPacket(byteArrayOf(-1, -1, -1, -1)), PLAYER_RESPONSE, ::playerPacket)?.let(::parsePlayers).orEmpty()
        } catch (_: SocketTimeoutException) {
          emptyList()
        }
      } else {
        emptyList()
      }

      return ServerStatus(players, maxPlayers, names)
    }
  }

  /** The host and query port for a server's `host:port`, `[v6]:port` or bare host. */
  fun queryEndpoint(raw: String): Pair<String, Int>? {
    val address = raw.trim()
    val host: String
    var gamePort = DEFAULT_GAME_PORT

    if (address.startsWith('[')) {
      val end = address.indexOf(']')
      if (end < 0) return null
      host = address.substring(1, end)
      val rest = address.substring(end + 1)
      if (rest.startsWith(':')) gamePort = rest.substring(1).toIntOrNull() ?: return null
    } else {
      val colon = address.lastIndexOf(':')
      // More than one colon without brackets is a bare IPv6 address, which has no port.
      if (colon >= 0 && address.indexOf(':') == colon) {
        host = address.substring(0, colon)
        gamePort = address.substring(colon + 1).toIntOrNull() ?: return null
      } else {
        host = address
      }
    }

    return if (host.isNotEmpty() && gamePort in 1 until 65535) host to gamePort + 1 else null
  }

  /** Sends a request and returns the answer's payload after its type byte, answering one challenge on the way. */
  private fun request(udp: DatagramSocket, request: ByteArray, expected: Byte, withChallenge: (ByteArray) -> ByteArray): ByteArray? {
    udp.send(DatagramPacket(request, request.size))
    val buffer = ByteArray(1400)
    for (attempt in 0 until 2) {
      val packet = DatagramPacket(buffer, buffer.size)
      udp.receive(packet)
      val data = packet.data.copyOf(packet.length)
      if (data.size < 5 || ByteBuffer.wrap(data).order(ByteOrder.LITTLE_ENDIAN).int != SINGLE_PACKET) return null
      if (data[4] == expected) return data.copyOfRange(5, data.size)
      if (data[4] != CHALLENGE_RESPONSE || data.size < 9 || attempt > 0) return null
      val retry = withChallenge(data.copyOfRange(5, 9))
      udp.send(DatagramPacket(retry, retry.size))
    }
    return null
  }

  internal fun infoPacket(challenge: ByteArray): ByteArray =
    byteArrayOf(-1, -1, -1, -1, INFO_REQUEST) + "Source Engine Query\u0000".toByteArray(Charsets.US_ASCII) + challenge

  internal fun playerPacket(challenge: ByteArray): ByteArray = byteArrayOf(-1, -1, -1, -1, PLAYER_REQUEST) + challenge

  /** Reads the player counts from an A2S_INFO payload (after the `0x49`). */
  internal fun parseInfo(payload: ByteArray): Pair<Int, Int>? = try {
    val reader = DataInputStream(ByteArrayInputStream(payload))
    reader.readByte() // protocol
    readCString(reader) // name
    readCString(reader) // map
    readCString(reader) // folder
    readCString(reader) // game
    reader.readShort() // app id
    reader.readUnsignedByte() to reader.readUnsignedByte()
  } catch (_: EOFException) {
    null
  }

  /** The non-empty names in an A2S_PLAYER payload (after the `0x44`). */
  internal fun parsePlayers(payload: ByteArray): List<String> {
    val names = mutableListOf<String>()
    try {
      val reader = DataInputStream(ByteArrayInputStream(payload))
      val count = reader.readUnsignedByte()
      repeat(count) {
        reader.readByte() // index
        val name = readCString(reader)
        reader.readInt() // score
        reader.readInt() // seconds connected, a float
        if (name.isNotBlank()) names += name
      }
    } catch (_: EOFException) {
      // A truncated list still has the names read so far.
    }
    return names
  }

  private fun readCString(reader: DataInputStream): String {
    val bytes = java.io.ByteArrayOutputStream()
    while (true) {
      val b = reader.readByte()
      if (b == 0.toByte()) break
      bytes.write(b.toInt())
    }
    return bytes.toString("UTF-8")
  }
}
