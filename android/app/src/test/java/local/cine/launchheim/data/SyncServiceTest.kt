package local.cine.launchheim.data

import java.io.File
import kotlinx.coroutines.runBlocking
import local.cine.launchheim.localsend.DeviceInfo
import local.cine.launchheim.localsend.ReceivedFiles
import local.cine.launchheim.packs.PackFile
import local.cine.launchheim.packs.PackManifest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

class SyncServiceTest {
  @get:Rule val temp = TemporaryFolder()

  private val instances by lazy { InstanceStore(temp.newFolder("instances")) }
  private val sync by lazy { SyncService(instances, ServerStore(File(temp.root, "servers.json")), temp.newFolder("share"), "0.0.0") }

  private fun made(name: String, instanceId: String? = null): PhoneInstance =
    PhoneInstance(id = instances.newId(), manifest = PackManifest(name = name, instanceId = instanceId)).also(instances::save)

  /** What LaunchHeim sends: a pack in a folder of its own, which [SyncService.handle] deletes afterwards. */
  private fun arrive(manifest: PackManifest): ReceivedFiles {
    val file = File(temp.newFolder(), "pack.r2z")
    file.outputStream().use { PackFile.write(manifest, null, it) }
    return ReceivedFiles(DeviceInfo(alias = "PC"), listOf(file))
  }

  @Test
  fun `a list made on the phone goes out with the phone's own id`() = runBlocking {
    val list = made("Phone list")

    val manifest = PackFile.read(sync.buildPack(list)).manifest

    assertEquals(list.id, manifest.instanceId)
    // Building a pack doesn't mark the list as linked; only a successful send does.
    assertNull(instances.get(list.id)!!.manifest.instanceId)
  }

  /** LaunchHeim kept the phone's id for the instance it imported and sends it back with every pack. */
  @Test
  fun `a pack with the phone's own id updates that list`() = runBlocking {
    val list = made("Phone list")

    val outcome = sync.handle(arrive(PackManifest(name = "Renamed on the PC", instanceId = list.id))).single()

    outcome as SyncService.Outcome.Imported
    assertTrue(outcome.replaced)
    assertEquals(list.id, outcome.instance.id)
    assertEquals(listOf("Renamed on the PC"), instances.instances.value.map { it.name })
  }

  @Test
  fun `a pack of a desktop instance updates its list and nothing else`() = runBlocking {
    val linked = made("Survival", instanceId = "survival")
    made("Other")

    val outcome = sync.handle(arrive(PackManifest(name = "Survival", instanceId = "survival"))).single()

    outcome as SyncService.Outcome.Imported
    assertTrue(outcome.replaced)
    assertEquals(linked.id, outcome.instance.id)
    assertEquals(2, instances.instances.value.size)
  }
}
