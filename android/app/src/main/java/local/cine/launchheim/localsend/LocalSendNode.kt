package local.cine.launchheim.localsend

import java.io.File
import java.io.IOException
import java.net.DatagramPacket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.MulticastSocket
import java.net.NetworkInterface
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.cert.X509Certificate
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit
import javax.net.ssl.SSLContext
import javax.net.ssl.X509TrustManager
import kotlin.concurrent.thread
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.asRequestBody
import okhttp3.RequestBody.Companion.toRequestBody

/** A transfer another device wants to make, waiting for the user to accept or decline it. */
class IncomingOffer(val sender: DeviceInfo, val address: String, val files: List<FileDto>) {
  internal val decision = CompletableDeferred<Boolean>()

  fun accept() = decision.complete(true)

  fun decline() = decision.complete(false)
}

/** Files that arrived complete, in the order they were offered. */
data class ReceivedFiles(val sender: DeviceInfo, val files: List<File>)

/** A file to send and the name the receiver sees. */
data class OutgoingFile(val file: File, val fileName: String, val fileType: String = "application/octet-stream")

sealed interface SendResult {
  data object Sent : SendResult
  data object Declined : SendResult
  data object Busy : SendResult
  data class Failed(val message: String) : SendResult
}

/**
 * One LocalSend device: it announces itself, keeps the list of [peers], receives files through
 * [incoming] offers and [received], and sends files with [send]. LaunchHeim's Core/LocalSend/LocalSendNode.cs
 * is the same thing for the desktop.
 *
 * Plain JVM code apart from [setMulticastLock], so it runs in unit tests. Received files land in [inbox].
 *
 * @param accepts Which offered files this app wants at all. The others are left out of the answer to
 *   `/prepare-upload`, which LocalSend's partial accept allows, and a transfer with none of them is
 *   rejected without asking the user.
 */
class LocalSendNode(
  private val identity: () -> Identity,
  private val inbox: File,
  private val accepts: (FileDto) -> Boolean,
  private val setMulticastLock: (Boolean) -> Unit = {},
  private val multicastPort: Int = LocalSend.DEFAULT_PORT,
  private val httpPort: Int = LocalSend.DEFAULT_PORT,
  /** Off in tests, which shouldn't knock on every address of the network they run in. */
  private val legacyDiscovery: Boolean = true,
) {
  /** Who this device is. The fingerprint is random per install and stays the same. */
  data class Identity(val alias: String, val fingerprint: String, val deviceType: String = "mobile")

  private val _peers = MutableStateFlow<List<Peer>>(emptyList())
  private val _incoming = MutableStateFlow<IncomingOffer?>(null)
  private val _received = MutableSharedFlow<ReceivedFiles>(extraBufferCapacity = 8)
  private val _running = MutableStateFlow(false)
  private val sessions = ConcurrentHashMap<String, Session>()

  private var scope: CoroutineScope? = null
  private var server: MiniHttpServer? = null
  private var multicast: MulticastSocket? = null

  val peers: StateFlow<List<Peer>> = _peers.asStateFlow()
  val incoming: StateFlow<IncomingOffer?> = _incoming.asStateFlow()
  val received: SharedFlow<ReceivedFiles> = _received.asSharedFlow()
  val running: StateFlow<Boolean> = _running.asStateFlow()

  /** The HTTP port actually bound, or -1 while stopped. */
  val port: Int get() = server?.port ?: -1

  fun info(announce: Boolean? = null): DeviceInfo {
    val me = identity()
    return DeviceInfo(
      alias = me.alias,
      deviceModel = LocalSend.DEVICE_MODEL,
      deviceType = me.deviceType,
      fingerprint = me.fingerprint,
      port = port.takeIf { it > 0 } ?: httpPort,
      protocol = "http",
      download = false,
      announce = announce,
    )
  }

  @Synchronized
  fun start() {
    if (_running.value) return
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    this.scope = scope

    server = MiniHttpServer(::handle).also { it.start(httpPort) }
    setMulticastLock(true)
    multicast = try {
      openMulticast()
    } catch (_: IOException) {
      // Another app holds the port without sharing it. Discovery still works one way: our announcements
      // go out, and devices answer them over HTTP.
      null
    }
    multicast?.let { socket -> thread(name = "localsend-multicast", isDaemon = true) { listen(socket) } }
    _running.value = true
    scan()
  }

  @Synchronized
  fun stop() {
    if (!_running.value) return
    _running.value = false
    scope?.cancel()
    scope = null
    runCatching { multicast?.close() }
    multicast = null
    setMulticastLock(false)
    server?.stop()
    server = null
    _incoming.value?.decline()
    _incoming.value = null
    sessions.clear()
    _peers.value = emptyList()
  }

  /**
   * Looks for devices again: forgets the list, announces, and if nobody answers the multicast within two
   * seconds, asks every address in the local /24 networks directly (LocalSend's "legacy" discovery, for
   * Wi-Fi that drops multicast).
   */
  fun scan() {
    val scope = scope ?: return
    _peers.value = emptyList()
    scope.launch {
      repeat(3) { attempt ->
        announce()
        delay(if (attempt == 0) 500L else 1500L)
      }
      if (_peers.value.isEmpty() && legacyDiscovery) legacyScan()
    }
  }

  // ---- discovery ----------------------------------------------------------------------------------

  private fun openMulticast(): MulticastSocket {
    val socket = MulticastSocket(null)
    socket.reuseAddress = true
    socket.bind(InetSocketAddress(multicastPort))
    val group = InetSocketAddress(InetAddress.getByName(LocalSend.MULTICAST_GROUP), 0)
    var joined = false
    for (network in lanInterfaces()) {
      joined = runCatching { socket.joinGroup(group, network) }.isSuccess || joined
    }
    if (!joined) socket.joinGroup(group, null)
    return socket
  }

  private fun listen(socket: MulticastSocket) {
    val buffer = ByteArray(64 * 1024)
    while (!socket.isClosed) {
      val packet = DatagramPacket(buffer, buffer.size)
      try {
        socket.receive(packet)
      } catch (_: IOException) {
        break
      }

      val info = runCatching {
        LocalSend.Json.decodeFromString<DeviceInfo>(String(packet.data, 0, packet.length, Charsets.UTF_8))
      }.getOrNull() ?: continue
      if (info.fingerprint == identity().fingerprint) continue

      val address = packet.address.hostAddress ?: continue
      remember(info, address)
      if (info.announce == true) {
        scope?.launch { answer(Peer(info, address, System.currentTimeMillis())) }
      }
    }
  }

  /** Answers an announcement over HTTP, as the protocol asks, or by multicast when that fails. */
  private suspend fun answer(peer: Peer) {
    val registered = runCatching { register(peer) }.getOrDefault(false)
    if (!registered) sendMulticast(info(announce = false))
  }

  private fun register(peer: Peer): Boolean {
    val body = LocalSend.Json.encodeToString(DeviceInfo.serializer(), info()).toRequestBody(JSON)
    val request = Request.Builder().url("${peer.baseUrl}/register").post(body).build()
    quickHttp.newCall(request).execute().use { response ->
      if (!response.isSuccessful) return false
      val answer = runCatching { LocalSend.Json.decodeFromString<DeviceInfo>(response.body.string()) }.getOrNull() ?: return true
      // The answer to /register has no port or protocol, so the ones we asked on are the right ones.
      remember(answer.copy(port = peer.port, protocol = peer.info.protocol), peer.address)
      return true
    }
  }

  private fun announce() = sendMulticast(info(announce = true))

  private fun sendMulticast(info: DeviceInfo) {
    val bytes = LocalSend.Json.encodeToString(DeviceInfo.serializer(), info).toByteArray()
    val group = InetAddress.getByName(LocalSend.MULTICAST_GROUP)
    val interfaces = lanInterfaces().ifEmpty { listOf(null) }
    for (network in interfaces) {
      runCatching {
        MulticastSocket().use { socket ->
          network?.let { socket.networkInterface = it }
          socket.timeToLive = 4
          socket.send(DatagramPacket(bytes, bytes.size, group, multicastPort))
        }
      }
    }
  }

  private suspend fun legacyScan() = withContext(Dispatchers.IO) {
    val own = lanAddresses()
    val targets = own.flatMap { address ->
      val prefix = address.address.copyOf(3)
      (1..254).map { host -> InetAddress.getByAddress(prefix + host.toByte()) }
    }.filter { it !in own }.distinct()

    val limit = Semaphore(48)
    targets.map { target ->
      launch {
        limit.withPermit {
          val peer = Peer(DeviceInfo(port = LocalSend.DEFAULT_PORT, protocol = "http"), target.hostAddress!!, 0)
          // LocalSend's own app usually runs HTTPS, LaunchHeim HTTP; one of the two answers.
          if (!runCatching { register(peer) }.getOrDefault(false)) {
            runCatching { register(peer.copy(info = peer.info.copy(protocol = "https"))) }
          }
        }
      }
    }.forEach { it.join() }
  }

  private fun remember(info: DeviceInfo, address: String) {
    if (info.fingerprint == identity().fingerprint) return
    val peer = Peer(info, address, System.currentTimeMillis())
    _peers.update { list ->
      val rest = list.filterNot { it.info.fingerprint == info.fingerprint || (it.address == address && it.port == peer.port) }
      (rest + peer).sortedWith(compareByDescending<Peer> { it.info.isLaunchHeim }.thenBy(String.CASE_INSENSITIVE_ORDER) { it.info.alias })
    }
  }

  // ---- receiving ----------------------------------------------------------------------------------

  private class Session(
    val id: String,
    val sender: DeviceInfo,
    val address: String,
    val files: Map<String, FileDto>,
    val tokens: Map<String, String>,
    val directory: File,
  ) {
    val done = ConcurrentHashMap<String, File>()
    @Volatile var lastActivity = System.currentTimeMillis()
  }

  private fun handle(request: HttpRequest): HttpResponse {
    val route = request.path.removePrefix(LocalSend.API)
    return when {
      request.path.startsWith(LocalSend.API).not() -> HttpResponse(404)
      route == "/register" && request.method == "POST" -> onRegister(request)
      route == "/info" && request.method == "GET" -> HttpResponse(200, LocalSend.Json.encodeToString(DeviceInfo.serializer(), info()))
      route == "/prepare-upload" && request.method == "POST" -> onPrepareUpload(request)
      route == "/upload" && request.method == "POST" -> onUpload(request)
      route == "/cancel" && request.method == "POST" -> {
        sessions.remove(request.query["sessionId"])?.directory?.deleteRecursively()
        HttpResponse(200)
      }
      else -> HttpResponse(404)
    }
  }

  private fun onRegister(request: HttpRequest): HttpResponse {
    val info = runCatching { LocalSend.Json.decodeFromString<DeviceInfo>(request.readText()) }.getOrNull()
      ?: return HttpResponse(400)
    if (info.fingerprint != identity().fingerprint) {
      remember(info, request.remoteAddress.hostAddress ?: return HttpResponse(400))
    }
    return HttpResponse(200, LocalSend.Json.encodeToString(DeviceInfo.serializer(), info().copy(port = null, protocol = null)))
  }

  private fun onPrepareUpload(request: HttpRequest): HttpResponse {
    val prepare = runCatching { LocalSend.Json.decodeFromString<PrepareUploadRequest>(request.readText()) }.getOrNull()
      ?: return HttpResponse(400)
    val address = request.remoteAddress.hostAddress ?: return HttpResponse(400)

    // One transfer at a time. One that went quiet for ten minutes is given up.
    sessions.values.removeIf { System.currentTimeMillis() - it.lastActivity > TimeUnit.MINUTES.toMillis(10) }
    if (sessions.isNotEmpty() || _incoming.value != null) return HttpResponse(409)

    val wanted = prepare.files.values.filter(accepts)
    if (wanted.isEmpty()) return HttpResponse(403)

    remember(prepare.info, address)
    val offer = IncomingOffer(prepare.info, address, wanted)
    _incoming.value = offer
    // The sender waits for this answer, as it does for a person tapping Accept in LocalSend.
    val accepted = runBlocking { withTimeoutOrNull(TimeUnit.MINUTES.toMillis(2)) { offer.decision.await() } } ?: false
    _incoming.compareAndSet(offer, null)
    if (!accepted) return HttpResponse(403)

    val id = UUID.randomUUID().toString()
    val tokens = wanted.associate { it.id to UUID.randomUUID().toString() }
    val directory = File(inbox, id).apply { mkdirs() }
    sessions[id] = Session(id, prepare.info, address, wanted.associateBy { it.id }, tokens, directory)
    return HttpResponse(200, LocalSend.Json.encodeToString(PrepareUploadResponse.serializer(), PrepareUploadResponse(id, tokens)))
  }

  private fun onUpload(request: HttpRequest): HttpResponse {
    val session = sessions[request.query["sessionId"]] ?: return HttpResponse(403)
    val fileId = request.query["fileId"] ?: return HttpResponse(400)
    val file = session.files[fileId] ?: return HttpResponse(403)
    if (session.tokens[fileId] != request.query["token"] || session.address != request.remoteAddress.hostAddress) {
      return HttpResponse(403)
    }

    session.lastActivity = System.currentTimeMillis()
    val target = File(session.directory, safeName(file.fileName, fileId))
    val digest = MessageDigest.getInstance("SHA-256")
    var size = 0L
    target.outputStream().use { output ->
      val buffer = ByteArray(64 * 1024)
      while (true) {
        val read = request.body.read(buffer)
        if (read < 0) break
        size += read
        // Never more than was announced: the user accepted that size.
        if (size > file.size) {
          target.delete()
          return HttpResponse(400)
        }
        output.write(buffer, 0, read)
        digest.update(buffer, 0, read)
      }
    }

    if (size != file.size) {
      target.delete()
      return HttpResponse(400)
    }
    val hash = digest.digest().joinToString("") { "%02x".format(it) }
    if (file.sha256 != null && !file.sha256.equals(hash, ignoreCase = true)) {
      target.delete()
      return HttpResponse(422)
    }

    session.done[fileId] = target
    if (session.done.size == session.files.size && sessions.remove(session.id) != null) {
      val ordered = session.files.keys.mapNotNull(session.done::get)
      _received.tryEmit(ReceivedFiles(session.sender, ordered))
    }
    return HttpResponse(200)
  }

  // ---- sending ------------------------------------------------------------------------------------

  /** Sends [files] to [peer]. Waits while the person there decides, up to two minutes. */
  suspend fun send(peer: Peer, files: List<OutgoingFile>): SendResult = withContext(Dispatchers.IO) {
    val dtos = files.associate { outgoing ->
      val id = UUID.randomUUID().toString()
      id to (outgoing to FileDto(id, outgoing.fileName, outgoing.file.length(), outgoing.fileType, sha256(outgoing.file)))
    }

    val prepare = PrepareUploadRequest(info(), dtos.mapValues { it.value.second })
    val body = LocalSend.Json.encodeToString(PrepareUploadRequest.serializer(), prepare).toRequestBody(JSON)
    val answer = try {
      sendHttp.newCall(Request.Builder().url("${peer.baseUrl}/prepare-upload").post(body).build()).execute().use { response ->
        when (response.code) {
          200 -> LocalSend.Json.decodeFromString<PrepareUploadResponse>(response.body.string())
          204 -> return@withContext SendResult.Sent
          403 -> return@withContext SendResult.Declined
          409, 429 -> return@withContext SendResult.Busy
          401 -> return@withContext SendResult.Failed("${peer.info.alias} asks for a PIN, which LaunchHeim can't send. Turn the PIN off in LocalSend.")
          else -> return@withContext SendResult.Failed("${peer.info.alias} answered ${response.code}.")
        }
      }
    } catch (e: IOException) {
      return@withContext SendResult.Failed("Could not reach ${peer.info.alias}: ${e.message}")
    }

    try {
      for ((fileId, token) in answer.files) {
        val (outgoing, _) = dtos[fileId] ?: continue
        val url = "${peer.baseUrl}/upload?sessionId=${answer.sessionId}&fileId=$fileId&token=$token"
        val upload = outgoing.file.asRequestBody(outgoing.fileType.toMediaType())
        sendHttp.newCall(Request.Builder().url(url).post(upload).build()).execute().use { response ->
          if (!response.isSuccessful) {
            cancel(peer, answer.sessionId)
            return@withContext SendResult.Failed("${peer.info.alias} refused ${outgoing.fileName} (${response.code}).")
          }
        }
      }
    } catch (e: IOException) {
      cancel(peer, answer.sessionId)
      return@withContext SendResult.Failed("The transfer to ${peer.info.alias} broke off: ${e.message}")
    }
    SendResult.Sent
  }

  private fun cancel(peer: Peer, sessionId: String) {
    runCatching {
      quickHttp.newCall(Request.Builder().url("${peer.baseUrl}/cancel?sessionId=$sessionId").post(ByteArray(0).toRequestBody()).build()).execute().close()
    }
  }

  // ---- helpers ------------------------------------------------------------------------------------

  /** Up, multicast-capable interfaces with a private IPv4 address: Wi-Fi, Ethernet, the phone's hotspot. */
  private fun lanInterfaces(): List<NetworkInterface> =
    runCatching { NetworkInterface.getNetworkInterfaces().toList() }.getOrDefault(emptyList()).filter { network ->
      runCatching { network.isUp && !network.isLoopback && network.supportsMulticast() }.getOrDefault(false) &&
        network.inetAddresses.toList().any { it is Inet4Address && it.isSiteLocalAddress }
    }

  private fun lanAddresses(): List<Inet4Address> =
    lanInterfaces().flatMap { it.inetAddresses.toList() }.filterIsInstance<Inet4Address>().filter { it.isSiteLocalAddress }

  companion object {
    private val JSON = "application/json".toMediaType()

    /**
     * LocalSend's encrypted mode uses a self-signed certificate per device, so there is no chain to check.
     * Its fingerprint is meant to pin it, but the exact hashing isn't specified, so it isn't checked here
     * and HTTPS only protects against passive listening. Only used for LocalSend peers on the LAN.
     */
    private val trustAll = object : X509TrustManager {
      override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) = Unit
      override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) = Unit
      override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
    }

    private val baseHttp: OkHttpClient by lazy {
      val ssl = SSLContext.getInstance("TLS").apply { init(null, arrayOf(trustAll), SecureRandom()) }
      OkHttpClient.Builder()
        .sslSocketFactory(ssl.socketFactory, trustAll)
        .hostnameVerifier { _, _ -> true }
        .build()
    }

    private val quickHttp by lazy {
      baseHttp.newBuilder().connectTimeout(1, TimeUnit.SECONDS).readTimeout(3, TimeUnit.SECONDS).build()
    }

    // prepare-upload waits for a person, so its answer may take a while.
    private val sendHttp by lazy {
      baseHttp.newBuilder().connectTimeout(5, TimeUnit.SECONDS).readTimeout(150, TimeUnit.SECONDS).writeTimeout(60, TimeUnit.SECONDS).build()
    }

    fun sha256(file: File): String {
      val digest = MessageDigest.getInstance("SHA-256")
      file.inputStream().use { input ->
        val buffer = ByteArray(64 * 1024)
        while (true) {
          val read = input.read(buffer)
          if (read < 0) break
          digest.update(buffer, 0, read)
        }
      }
      return digest.digest().joinToString("") { "%02x".format(it) }
    }

    /** The sender picks the name, so only its last part is kept, and only harmless characters. */
    internal fun safeName(fileName: String, fallback: String): String {
      val base = fileName.substringAfterLast('/').substringAfterLast('\\')
        .replace(Regex("[^A-Za-z0-9._ ()\\-]"), "_")
        .trim('.', ' ')
      return base.ifEmpty { fallback }.take(120)
    }

    fun newFingerprint(): String = UUID.randomUUID().toString().replace("-", "")
  }
}
