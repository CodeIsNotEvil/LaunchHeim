package local.cine.launchheim.data

import java.io.File
import java.util.UUID
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.Serializable
import local.cine.launchheim.packs.PackManifest

/**
 * A mod list on the phone: a LaunchHeim instance received from the PC, a pack opened from a file, or a
 * list started here. The pack it came in is kept next to it, so its configs go back unchanged.
 */
@Serializable
data class PhoneInstance(
  /** The phone's own id. The desktop instance's id, if any, is [PackManifest.instanceId]. */
  val id: String,
  val manifest: PackManifest,
  /** The device it was last received from, or null for a list started on the phone. */
  val origin: String? = null,
  val updatedAt: Long = System.currentTimeMillis(),
  /** Edited since it was received or last sent, so sending it back is worth a reminder. */
  val changedSinceSync: Boolean = false,
  val configFiles: Int = 0,
  val fromR2modman: Boolean = false,
) {
  val name: String get() = manifest.name
}

/** One folder per instance in the app's files: `instance.json` and the original `pack.r2z`. */
class InstanceStore(private val root: File) {
  private val _instances = MutableStateFlow(load())

  val instances: StateFlow<List<PhoneInstance>> = _instances.asStateFlow()

  fun get(id: String): PhoneInstance? = _instances.value.firstOrNull { it.id == id }

  fun packFile(id: String): File = File(File(root, id), PACK_FILE)

  fun newId(): String = UUID.randomUUID().toString()

  @Synchronized
  fun save(instance: PhoneInstance) {
    writeJson(File(File(root, instance.id), MANIFEST_FILE), PhoneInstance.serializer(), instance)
    _instances.value = (_instances.value.filterNot { it.id == instance.id } + instance).sortedByDescending { it.updatedAt }
  }

  /** Changes an instance's mod list and marks it as not sent yet. */
  @Synchronized
  fun edit(id: String, change: (PackManifest) -> PackManifest): PhoneInstance? {
    val instance = get(id) ?: return null
    val manifest = change(instance.manifest)
    if (manifest == instance.manifest) return instance
    return instance.copy(manifest = manifest, updatedAt = System.currentTimeMillis(), changedSinceSync = true).also(::save)
  }

  @Synchronized
  fun delete(id: String) {
    File(root, id).deleteRecursively()
    _instances.value = _instances.value.filterNot { it.id == id }
  }

  private fun load(): List<PhoneInstance> =
    root.listFiles().orEmpty()
      .mapNotNull { readJson(File(it, MANIFEST_FILE), PhoneInstance.serializer()) }
      .sortedByDescending { it.updatedAt }

  companion object {
    const val MANIFEST_FILE = "instance.json"
    const val PACK_FILE = "pack.r2z"
  }
}
