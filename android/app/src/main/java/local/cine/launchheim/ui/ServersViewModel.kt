package local.cine.launchheim.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import java.util.UUID
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import local.cine.launchheim.container
import local.cine.launchheim.servers.SavedServer
import local.cine.launchheim.servers.ServerQuery
import local.cine.launchheim.servers.ServerStatus

/** The server list and who is online on each server. */
class ServersViewModel(app: Application) : AndroidViewModel(app) {
  private val store = app.container.servers

  sealed interface Status {
    data object Checking : Status
    data object Offline : Status
    data class Online(val status: ServerStatus) : Status
  }

  val servers: StateFlow<List<SavedServer>> = store.servers

  private val _statuses = MutableStateFlow<Map<String, Status>>(emptyMap())
  val statuses: StateFlow<Map<String, Status>> = _statuses.asStateFlow()

  private val _refreshing = MutableStateFlow(false)
  val refreshing: StateFlow<Boolean> = _refreshing.asStateFlow()

  val pcSyncedAt: Long? get() = store.pcSyncedAt

  /**
   * Asks every server at once. A server keeps its last answer while it's asked again, so the list
   * doesn't flicker every 30 seconds; only one never asked shows as checking.
   */
  suspend fun refresh() {
    _refreshing.value = true
    val list = servers.value
    _statuses.update { current -> list.associate { it.address to (current[it.address] ?: Status.Checking) } }
    list.distinctBy { it.address }.map { server ->
      viewModelScope.async {
        val status = ServerQuery.query(server.address)?.let { Status.Online(it) } ?: Status.Offline
        _statuses.update { it + (server.address to status) }
      }
    }.awaitAll()
    _refreshing.value = false
  }

  fun refreshNow() {
    viewModelScope.launch { refresh() }
  }

  fun save(id: String?, name: String, address: String) {
    val server = SavedServer(id ?: UUID.randomUUID().toString(), name.trim().ifBlank { address.trim() }, address.trim())
    store.save(server)
    refreshNow()
  }

  fun delete(id: String) = store.delete(id)
}
