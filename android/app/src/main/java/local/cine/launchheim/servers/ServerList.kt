package local.cine.launchheim.servers

import kotlinx.serialization.Serializable

/**
 * `launchheim-servers.json`: the dedicated servers from Valheim's Favorites and Recent lists on the PC,
 * which LaunchHeim sends along when it syncs to the phone. Written by Core/Sync/ServerListFile.cs.
 */
@Serializable
data class ServerListFile(
  val format: Int = 1,
  val exportedBy: String = "",
  val exportedAt: String = "",
  val servers: List<Entry> = emptyList(),
) {
  @Serializable
  data class Entry(val name: String = "", val address: String = "", val isFavorite: Boolean = false, val isRecent: Boolean = false)

  companion object {
    const val FILE_NAME = "launchheim-servers.json"
  }
}

/** A server in the phone's list: from the PC (replaced on every sync) or added on the phone. */
@Serializable
data class SavedServer(
  val id: String,
  val name: String,
  val address: String,
  val fromPc: Boolean = false,
  val isFavorite: Boolean = false,
)
