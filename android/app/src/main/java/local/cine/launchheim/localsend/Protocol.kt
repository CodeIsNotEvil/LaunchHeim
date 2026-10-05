package local.cine.launchheim.localsend

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

/**
 * The LocalSend protocol v2 (https://github.com/localsend/protocol), which LaunchHeim and this app speak
 * to each other and which the LocalSend app understands too.
 *
 * Discovery is a JSON announcement to the multicast group, answered with `POST /register` (or a
 * multicast reply). A transfer is `POST /prepare-upload` with the file list, which the receiver accepts or
 * rejects, then one `POST /upload` per file.
 */
object LocalSend {
  const val MULTICAST_GROUP = "224.0.0.167"
  const val DEFAULT_PORT = 53317
  const val PROTOCOL_VERSION = "2.1"
  const val API = "/api/localsend/v2"

  /**
   * What LaunchHeim and this app put in `deviceModel`, so each can tell the other apart from other
   * LocalSend devices and list it first. LocalSend shows it under the device name.
   */
  const val DEVICE_MODEL = "LaunchHeim"

  val Json = Json {
    ignoreUnknownKeys = true
    explicitNulls = false
    encodeDefaults = true
  }
}

/** A device's description, sent in announcements, `/register` and `/prepare-upload`. */
@Serializable
data class DeviceInfo(
  val alias: String = "",
  val version: String = LocalSend.PROTOCOL_VERSION,
  val deviceModel: String? = null,
  /** mobile, desktop, web, headless or server. */
  val deviceType: String? = null,
  /** Random per install when unencrypted; only used to recognise one's own announcements. */
  val fingerprint: String = "",
  val port: Int? = null,
  /** http or https. */
  val protocol: String? = null,
  val download: Boolean? = null,
  val announce: Boolean? = null,
) {
  val isLaunchHeim: Boolean get() = deviceModel == LocalSend.DEVICE_MODEL
}

@Serializable
data class FileDto(
  val id: String,
  val fileName: String,
  val size: Long,
  val fileType: String = "application/octet-stream",
  val sha256: String? = null,
  val preview: String? = null,
)

@Serializable
data class PrepareUploadRequest(val info: DeviceInfo, val files: Map<String, FileDto>)

@Serializable
data class PrepareUploadResponse(val sessionId: String, val files: Map<String, String> = emptyMap())

/** Another device on the network. */
data class Peer(val info: DeviceInfo, val address: String, val lastSeen: Long) {
  val port: Int get() = info.port ?: LocalSend.DEFAULT_PORT
  val https: Boolean get() = info.protocol.equals("https", ignoreCase = true)
  val baseUrl: String get() = "${if (https) "https" else "http"}://${hostForUrl(address)}:$port${LocalSend.API}"

  private fun hostForUrl(address: String) = if (address.contains(':')) "[$address]" else address
}
