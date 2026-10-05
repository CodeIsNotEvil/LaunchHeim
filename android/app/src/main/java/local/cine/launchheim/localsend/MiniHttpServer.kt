package local.cine.launchheim.localsend

import java.io.BufferedInputStream
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.URLDecoder
import kotlin.concurrent.thread

/** One HTTP request, with the body left as a stream so an upload goes straight to a file. */
class HttpRequest(
  val method: String,
  val path: String,
  val query: Map<String, String>,
  val headers: Map<String, String>,
  val remoteAddress: InetAddress,
  val body: InputStream,
) {
  fun readText(limit: Int = 1024 * 1024): String {
    val bytes = java.io.ByteArrayOutputStream()
    val buffer = ByteArray(8192)
    while (true) {
      val read = body.read(buffer)
      if (read < 0) break
      bytes.write(buffer, 0, read)
      if (bytes.size() > limit) throw IOException("Request body too large")
    }
    return bytes.toString("UTF-8")
  }
}

class HttpResponse(val status: Int, val body: String = "", val contentType: String = "application/json")

/**
 * The few routes LocalSend needs, over plain HTTP/1.1 on a [ServerSocket]: one request per connection,
 * a `Content-Length` or chunked body. A server library would be far more than that, and the desktop side
 * (Core/LocalSend/MiniHttpServer.cs) works the same way.
 *
 * Only HTTP, not LocalSend's default HTTPS: that would need a self-signed certificate made on the phone,
 * and LocalSend clients use whatever protocol a device announces, so they reach this one over HTTP.
 */
class MiniHttpServer(private val handler: (HttpRequest) -> HttpResponse) {
  private var socket: ServerSocket? = null

  val port: Int get() = socket?.localPort ?: -1

  /** Binds the first free port from [preferred] on, because the LocalSend app may hold the default one. */
  fun start(preferred: Int, attempts: Int = 10) {
    var lastError: IOException? = null
    for (port in preferred until preferred + attempts) {
      try {
        val server = ServerSocket(port)
        socket = server
        thread(name = "localsend-http", isDaemon = true) { acceptLoop(server) }
        return
      } catch (e: IOException) {
        lastError = e
      }
    }
    throw lastError ?: IOException("No free port")
  }

  fun stop() {
    runCatching { socket?.close() }
    socket = null
  }

  private fun acceptLoop(server: ServerSocket) {
    while (!server.isClosed) {
      val client = try {
        server.accept()
      } catch (_: IOException) {
        break
      }
      thread(name = "localsend-client", isDaemon = true) { client.use(::serve) }
    }
  }

  private fun serve(client: Socket) {
    try {
      // Bounds every read, the upload's included, so a peer that stalls can't hold a thread forever.
      client.soTimeout = 30_000
      val input = BufferedInputStream(client.getInputStream())
      val requestLine = readLine(input) ?: return
      val parts = requestLine.split(' ')
      if (parts.size < 2) return

      val headers = HashMap<String, String>()
      while (true) {
        val line = readLine(input) ?: return
        if (line.isEmpty()) break
        val colon = line.indexOf(':')
        if (colon > 0) headers[line.substring(0, colon).trim().lowercase()] = line.substring(colon + 1).trim()
      }

      val target = parts[1]
      val path = target.substringBefore('?')
      val query = target.substringAfter('?', "").split('&').filter { it.contains('=') }.associate {
        URLDecoder.decode(it.substringBefore('='), "UTF-8") to URLDecoder.decode(it.substringAfter('='), "UTF-8")
      }

      val body: InputStream = when {
        headers["transfer-encoding"]?.contains("chunked", ignoreCase = true) == true -> ChunkedInputStream(input)
        else -> LimitedInputStream(input, headers["content-length"]?.toLongOrNull() ?: 0)
      }

      val response = try {
        handler(HttpRequest(parts[0].uppercase(), path, query, headers, client.inetAddress, body))
      } catch (e: Exception) {
        HttpResponse(500, """{"message":"${e.message?.replace("\"", "'") ?: "error"}"}""")
      }
      write(client.getOutputStream(), response)
    } catch (_: IOException) {
      // The peer went away mid-request; nothing to answer.
    }
  }

  private fun write(output: OutputStream, response: HttpResponse) {
    val body = response.body.toByteArray()
    val head = buildString {
      append("HTTP/1.1 ").append(response.status).append(' ').append(reason(response.status)).append("\r\n")
      if (body.isNotEmpty()) append("Content-Type: ").append(response.contentType).append("; charset=utf-8\r\n")
      append("Content-Length: ").append(body.size).append("\r\n")
      append("Connection: close\r\n\r\n")
    }
    output.write(head.toByteArray(Charsets.US_ASCII))
    output.write(body)
    output.flush()
  }

  private fun reason(status: Int) = when (status) {
    200 -> "OK"
    204 -> "No Content"
    400 -> "Bad Request"
    401 -> "Unauthorized"
    403 -> "Forbidden"
    404 -> "Not Found"
    409 -> "Conflict"
    422 -> "Unprocessable Entity"
    429 -> "Too Many Requests"
    else -> "Error"
  }

  companion object {
    /** A CRLF-terminated line of at most 8 KB, or null at the end of the stream. */
    internal fun readLine(input: InputStream): String? {
      val bytes = java.io.ByteArrayOutputStream()
      while (true) {
        val b = input.read()
        if (b < 0) return if (bytes.size() == 0) null else bytes.toString("ISO-8859-1")
        if (b == '\n'.code) return bytes.toString("ISO-8859-1").trimEnd('\r')
        bytes.write(b)
        if (bytes.size() > 8192) throw IOException("Header line too long")
      }
    }
  }
}

/** Reads exactly [remaining] bytes of a body and then reports the end. */
internal class LimitedInputStream(private val input: InputStream, private var remaining: Long) : InputStream() {
  override fun read(): Int {
    if (remaining <= 0) return -1
    val b = input.read()
    if (b >= 0) remaining--
    return b
  }

  override fun read(b: ByteArray, off: Int, len: Int): Int {
    if (remaining <= 0) return -1
    val read = input.read(b, off, minOf(len.toLong(), remaining).toInt())
    if (read > 0) remaining -= read
    return read
  }
}

/** Decodes a `Transfer-Encoding: chunked` body. */
internal class ChunkedInputStream(private val input: InputStream) : InputStream() {
  private var left = 0L
  private var done = false

  private fun nextChunk(): Boolean {
    if (done) return false
    if (left == 0L) {
      val size = MiniHttpServer.readLine(input)?.substringBefore(';')?.trim() ?: return false.also { done = true }
      // The CRLF after the previous chunk's data shows up as an empty line first.
      val actual = if (size.isEmpty()) MiniHttpServer.readLine(input)?.substringBefore(';')?.trim() ?: "0" else size
      left = actual.toLongOrNull(16) ?: throw IOException("Bad chunk size")
      if (left == 0L) {
        done = true
        return false
      }
    }
    return true
  }

  override fun read(): Int {
    if (!nextChunk()) return -1
    val b = input.read()
    if (b >= 0) left--
    return b
  }

  override fun read(b: ByteArray, off: Int, len: Int): Int {
    if (!nextChunk()) return -1
    val read = input.read(b, off, minOf(len.toLong(), left).toInt())
    if (read > 0) left -= read
    return read
  }
}
