package local.cine.launchheim.data

import java.io.File
import java.util.UUID
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.Serializable
import local.cine.launchheim.servers.SavedServer
import local.cine.launchheim.servers.ServerListFile

@Serializable
private data class ServerStoreFile(val servers: List<SavedServer> = emptyList(), val pcSyncedAt: Long? = null)

class ServerStore(private val file: File) {
  private val state = MutableStateFlow(readJson(file, ServerStoreFile.serializer()) ?: ServerStoreFile())
  private val _servers = MutableStateFlow(state.value.servers)

  val servers: StateFlow<List<SavedServer>> = _servers.asStateFlow()

  /** When the PC last sent its server list. */
  val pcSyncedAt: Long? get() = state.value.pcSyncedAt

  /**
   * Replaces the servers that came from the PC with its current list, and keeps the ones added on the
   * phone. Favorites first, as Valheim's Join tab lists them, each address once.
   */
  @Synchronized
  fun replaceFromPc(list: ServerListFile) {
    val fromPc = list.servers
      .filter { it.address.isNotBlank() }
      .sortedByDescending { it.isFavorite }
      .distinctBy { it.address.lowercase() }
      .map { SavedServer(UUID.randomUUID().toString(), it.name.ifBlank { it.address }, it.address, fromPc = true, isFavorite = it.isFavorite) }
    val own = state.value.servers.filterNot { it.fromPc }
    persist(ServerStoreFile(fromPc + own, System.currentTimeMillis()))
  }

  @Synchronized
  fun save(server: SavedServer) {
    val list = state.value.servers
    val updated = if (list.any { it.id == server.id }) list.map { if (it.id == server.id) server else it } else list + server
    persist(state.value.copy(servers = updated))
  }

  @Synchronized
  fun delete(id: String) = persist(state.value.copy(servers = state.value.servers.filterNot { it.id == id }))

  private fun persist(value: ServerStoreFile) {
    state.value = value
    _servers.value = value.servers
    writeJson(file, ServerStoreFile.serializer(), value)
  }
}
