package local.cine.launchheim.data

import java.io.File
import java.io.InputStream
import java.time.Instant
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import local.cine.launchheim.localsend.FileDto
import local.cine.launchheim.localsend.LocalSendNode
import local.cine.launchheim.localsend.OutgoingFile
import local.cine.launchheim.localsend.Peer
import local.cine.launchheim.localsend.ReceivedFiles
import local.cine.launchheim.localsend.SendResult
import local.cine.launchheim.packs.PackException
import local.cine.launchheim.packs.PackFile
import local.cine.launchheim.packs.PackJson
import local.cine.launchheim.servers.ServerListFile

/**
 * Turns what arrives (packs and the PC's server list) into instances and servers, and instances back
 * into packs to send or share.
 */
class SyncService(
  private val instances: InstanceStore,
  private val servers: ServerStore,
  private val shareDirectory: File,
  private val appVersion: String,
) {
  sealed interface Outcome {
    data class Imported(val instance: PhoneInstance, val replaced: Boolean, val lostChanges: Boolean) : Outcome
    data class Servers(val count: Int) : Outcome
    data class Failed(val fileName: String, val message: String) : Outcome
  }

  suspend fun handle(received: ReceivedFiles): List<Outcome> = withContext(Dispatchers.IO) {
    try {
      received.files.map { file ->
        when {
          file.name.equals(ServerListFile.FILE_NAME, ignoreCase = true) -> importServers(file)
          else -> importPack(file, received.sender.alias)
        }
      }
    } finally {
      received.files.forEach { it.parentFile?.deleteRecursively() }
    }
  }

  private fun importServers(file: File): Outcome = try {
    val list = PackJson.decodeFromString<ServerListFile>(file.readText())
    servers.replaceFromPc(list)
    Outcome.Servers(list.servers.size)
  } catch (e: Exception) {
    Outcome.Failed(file.name, "The server list could not be read: ${e.message}")
  }

  /** Opens a pack from a file or another app. */
  suspend fun importStream(input: InputStream, fileName: String): Outcome = withContext(Dispatchers.IO) {
    val temp = File(shareDirectory, "incoming-${System.nanoTime()}.r2z")
    try {
      shareDirectory.mkdirs()
      input.use { source -> temp.outputStream().use { source.copyTo(it) } }
      importPack(temp, origin = null, fallbackName = fileName.substringBeforeLast('.'))
    } finally {
      temp.delete()
    }
  }

  /**
   * A pack that came from a LaunchHeim instance the phone already has replaces it, matched by the
   * desktop instance's id. A list started on the phone has no such id until the PC has imported it and
   * sent it back, so a pack with an unknown id also replaces a phone-made list of the same name.
   */
  private fun importPack(file: File, origin: String?, fallbackName: String = file.nameWithoutExtension): Outcome {
    val contents = try {
      file.inputStream().buffered().use { PackFile.read(it, fallbackName) }
    } catch (e: PackException) {
      return Outcome.Failed(file.name, e.message ?: "Not a modpack.")
    }

    val manifest = contents.manifest
    val existing = instances.instances.value.firstOrNull { manifest.instanceId != null && it.manifest.instanceId == manifest.instanceId }
      ?: instances.instances.value.firstOrNull { it.manifest.instanceId == null && it.origin == null && manifest.instanceId != null && it.name.equals(manifest.name, true) }

    val instance = PhoneInstance(
      id = existing?.id ?: instances.newId(),
      manifest = manifest,
      origin = origin,
      configFiles = contents.configFiles,
      fromR2modman = contents.fromR2modman,
    )
    val pack = instances.packFile(instance.id)
    pack.parentFile?.mkdirs()
    file.copyTo(pack, overwrite = true)
    instances.save(instance)
    return Outcome.Imported(instance, replaced = existing != null, lostChanges = existing?.changedSinceSync == true)
  }

  /** Writes the instance as a `.r2z` that LaunchHeim and r2modman import, for sending or sharing. */
  suspend fun buildPack(instance: PhoneInstance): File = withContext(Dispatchers.IO) {
    shareDirectory.mkdirs()
    val file = File(shareDirectory, LocalSendNode.safeName(instance.name, "modpack") + PackFile.EXTENSION)
    val manifest = instance.manifest.copy(
      exportedBy = "LaunchHeim Companion $appVersion",
      exportedAt = Instant.now().toString(),
    )
    file.outputStream().use { PackFile.write(manifest, instances.packFile(instance.id).takeIf(File::exists), it) }
    file
  }

  suspend fun send(node: LocalSendNode, peer: Peer, instance: PhoneInstance): SendResult {
    val pack = buildPack(instance)
    val result = node.send(peer, listOf(OutgoingFile(pack, pack.name, "application/zip")))
    if (result == SendResult.Sent) instances.save(instance.copy(changedSinceSync = false))
    return result
  }

  companion object {
    // Packs list mods and carry configs; a few hundred MB would be local mods nobody meant to send.
    private const val MAX_PACK_BYTES = 256L * 1024 * 1024

    /** What the phone takes from LocalSend: modpacks and LaunchHeim's server list, nothing else. */
    fun accepts(file: FileDto): Boolean =
      (file.fileName.endsWith(PackFile.EXTENSION, ignoreCase = true) && file.size <= MAX_PACK_BYTES) ||
        (file.fileName.equals(ServerListFile.FILE_NAME, ignoreCase = true) && file.size <= 1024 * 1024)
  }
}
