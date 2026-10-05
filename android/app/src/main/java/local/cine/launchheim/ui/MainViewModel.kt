package local.cine.launchheim.ui

import android.app.Application
import android.net.Uri
import android.provider.OpenableColumns
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import java.io.File
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import local.cine.launchheim.Container
import local.cine.launchheim.container
import local.cine.launchheim.data.PhoneInstance
import local.cine.launchheim.data.Settings
import local.cine.launchheim.data.SyncService
import local.cine.launchheim.localsend.IncomingOffer
import local.cine.launchheim.localsend.Peer
import local.cine.launchheim.localsend.SendResult
import local.cine.launchheim.packs.ModListEditor
import local.cine.launchheim.packs.PackManifest
import local.cine.launchheim.packs.ModSource
import local.cine.launchheim.packs.PackMod

/** Instances, sending and settings: everything outside Browse and Servers. */
class MainViewModel(app: Application) : AndroidViewModel(app) {
  val container: Container = app.container
  private val editor = ModListEditor(container.index::find)

  val instances: StateFlow<List<PhoneInstance>> = container.instances.instances
  val settings: StateFlow<Settings> = container.settings.settings
  val peers: StateFlow<List<Peer>> = container.node.peers
  val nodeRunning: StateFlow<Boolean> = container.node.running
  val incoming: StateFlow<IncomingOffer?> = container.node.incoming
  val indexUpdated: StateFlow<Long?> = container.index.lastUpdated

  /** Newer Thunderstore versions per instance id and mod key, once the index is loaded. */
  val updates: StateFlow<Map<String, Map<String, String>>> =
    combine(instances, container.index.packages) { list, _ -> list.associate { it.id to editor.updates(it.manifest) } }
      .stateIn(viewModelScope, SharingStarted.Eagerly, emptyMap())

  /** The device a pack is being sent to, while it is. */
  private val _sendingTo = MutableStateFlow<String?>(null)
  val sendingTo: StateFlow<String?> = _sendingTo.asStateFlow()

  private val _indexBusy = MutableStateFlow(false)
  val indexBusy: StateFlow<Boolean> = _indexBusy.asStateFlow()

  init {
    // Loads the cached list in the background, so update badges are there without opening Browse.
    viewModelScope.launch { runCatching { container.index.ensureLoaded() } }
  }

  /**
   * A pack only has icons for mods installed by LaunchHeim 0.4 or later, and none for r2modman's. The
   * Thunderstore list has them all.
   */
  fun iconOf(mod: PackMod): String? =
    mod.iconUrl ?: if (mod.source == ModSource.Thunderstore) container.index.find(mod.id)?.icon else null

  fun alias(): String = settings.value.alias.ifBlank { Container.defaultAlias(getApplication()) }

  fun create(name: String): String {
    val instance = PhoneInstance(container.instances.newId(), PackManifest(name = name.trim()), changedSinceSync = true)
    container.instances.save(instance)
    // Every list needs BepInEx. Added once the index is there, which is usually right away.
    viewModelScope.launch {
      runCatching { container.index.ensureLoaded() }
      container.instances.edit(instance.id) { editor.add(it, PackMod.LOADER_FULL_NAME).manifest }
    }
    return instance.id
  }

  fun rename(id: String, name: String) {
    if (name.isBlank()) return
    container.instances.edit(id) { it.copy(name = name.trim()) }
  }

  fun delete(id: String) = container.instances.delete(id)

  /**
   * A copy that LaunchHeim imports as a new instance rather than an update of the original: without the
   * desktop instance's id. The configs it came with go along.
   */
  fun duplicate(id: String, name: String): String? {
    val source = container.instances.get(id) ?: return null
    val copy = PhoneInstance(
      id = container.instances.newId(),
      manifest = source.manifest.copy(name = name.trim().ifBlank { source.name + " (copy)" }, instanceId = null),
      changedSinceSync = true,
      configFiles = source.configFiles,
    )
    container.instances.packFile(source.id).takeIf { it.exists() }?.let { pack ->
      pack.copyTo(container.instances.packFile(copy.id).apply { parentFile?.mkdirs() }, overwrite = true)
    }
    container.instances.save(copy)
    return copy.id
  }

  fun remove(id: String, key: String) {
    var removed = 0
    container.instances.edit(id) { manifest -> ModListEditor.remove(manifest, key).also { removed = it.second.size }.first }
    if (removed > 1) container.notify("Removed ${removed - 1} dependenc${if (removed == 2) "y" else "ies"} nothing else needs")
  }

  fun setEnabled(id: String, key: String, enabled: Boolean) {
    container.instances.edit(id) { ModListEditor.setEnabled(it, key, enabled) }
  }

  fun update(id: String, key: String) {
    container.instances.edit(id) { editor.update(it, key).manifest }
  }

  fun updateAll(id: String) {
    val keys = updates.value[id]?.keys.orEmpty()
    container.instances.edit(id) { manifest -> keys.fold(manifest) { current, key -> editor.update(current, key).manifest } }
    container.notify("Updated ${Format.mods(keys.size)}")
  }

  fun scan() = container.node.scan()

  fun send(instance: PhoneInstance, peer: Peer) {
    if (_sendingTo.value != null) return
    _sendingTo.value = peer.info.alias
    viewModelScope.launch {
      val result = try {
        container.sync.send(container.node, peer, instance)
      } finally {
        _sendingTo.value = null
      }
      container.notify(
        when (result) {
          SendResult.Sent -> "Sent ${instance.name} to ${peer.info.alias}"
          SendResult.Declined -> "${peer.info.alias} declined ${instance.name}"
          SendResult.Busy -> "${peer.info.alias} is busy with another transfer. Try again in a moment."
          is SendResult.Failed -> result.message
        },
      )
    }
  }

  /** Writes the pack for Android's share sheet. */
  suspend fun packForSharing(instance: PhoneInstance): File = container.sync.buildPack(instance)

  /** Opens a pack handed over by a file manager or another app. Returns the new instance's id. */
  suspend fun open(uri: Uri): String? {
    val resolver = getApplication<Application>().contentResolver
    val name = runCatching {
      resolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { if (it.moveToFirst()) it.getString(0) else null }
    }.getOrNull() ?: uri.lastPathSegment ?: "Modpack"
    val input = runCatching { resolver.openInputStream(uri) }.getOrNull()
    if (input == null) {
      container.notify("Could not open $name")
      return null
    }
    val outcome = container.sync.importStream(input, name)
    container.notify(Container.describe(outcome, null))
    return (outcome as? SyncService.Outcome.Imported)?.instance?.id
  }

  fun refreshIndex() {
    if (_indexBusy.value) return
    _indexBusy.value = true
    viewModelScope.launch {
      runCatching { container.index.ensureLoaded(forceRefresh = true) }
        .onSuccess { container.notify("The Thunderstore list is up to date") }
        .onFailure { container.notify(it.message ?: "Could not refresh the Thunderstore list") }
      _indexBusy.value = false
    }
  }

  fun updateSettings(change: (Settings) -> Settings) = container.settings.update(change)

  /** A new name only reaches others with the next announcement, so it's sent right away. */
  fun setAlias(alias: String) {
    updateSettings { it.copy(alias = alias.trim()) }
    if (nodeRunning.value) scan()
  }
}
