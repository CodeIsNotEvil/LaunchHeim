package local.cine.launchheim.packs

import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.File
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

class PackFileTest {
  @get:Rule val temp = TemporaryFolder()

  // What LaunchHeim 0.4's PackService writes (System.Text.Json web defaults, enums as names).
  private val desktopManifest = """
    {
      "format": 1,
      "name": "Survival",
      "instanceId": "survival-1a2b",
      "exportedBy": "LaunchHeim 0.5.0",
      "exportedAt": "2026-10-05T12:00:00+00:00",
      "launchArguments": "-console",
      "mods": [
        { "source": "Thunderstore", "id": "denikson-BepInExPack_Valheim", "fileId": null, "name": "BepInExPack Valheim", "author": "denikson",
          "version": "5.4.2202", "websiteUrl": null, "iconUrl": null, "enabled": true, "installedAsDependency": false, "files": null, "dependencies": [] },
        { "source": "Nexus", "id": "4", "fileId": "1234", "name": "Valheim Plus", "author": "someone", "version": "0.9.9",
          "enabled": false, "installedAsDependency": false, "dependencies": [], "somethingNew": 42 }
      ]
    }
  """.trimIndent()

  private fun zip(vararg entries: Pair<String, String>): ByteArray {
    val bytes = ByteArrayOutputStream()
    ZipOutputStream(bytes).use { zip ->
      for ((name, text) in entries) {
        zip.putNextEntry(ZipEntry(name))
        zip.write(text.toByteArray())
        zip.closeEntry()
      }
    }
    return bytes.toByteArray()
  }

  @Test
  fun readsLaunchHeimPacksIgnoringUnknownFields() {
    val contents = PackFile.read(ByteArrayInputStream(zip("launchheim.json" to desktopManifest, "BepInEx/config/a.cfg" to "x=1")), "fallback")

    assertFalse(contents.fromR2modman)
    assertEquals(1, contents.configFiles)
    assertEquals("survival-1a2b", contents.manifest.instanceId)
    assertEquals(ModSource.Nexus, contents.manifest.mods[1].source)
    assertEquals("1234", contents.manifest.mods[1].fileId)
    assertFalse(contents.manifest.mods[1].enabled)
  }

  @Test
  fun readsR2modmanPacks() {
    val r2x = """
      profileName: Friends
      mods:
        - name: ValheimModding-Jotunn
          version:
            major: 2
            minor: 20
            patch: 1
          enabled: false
    """.trimIndent()
    val contents = PackFile.read(ByteArrayInputStream(zip("export.r2x" to r2x)), "fallback")

    assertTrue(contents.fromR2modman)
    assertEquals("Friends", contents.manifest.name)
    val mod = contents.manifest.mods.single()
    assertEquals("thunderstore:ValheimModding-Jotunn", mod.key)
    assertEquals("2.20.1", mod.version)
    assertEquals("Jotunn", mod.name)
    assertFalse(mod.enabled)
  }

  @Test(expected = PackException::class)
  fun rejectsZipsThatAreNoPack() {
    PackFile.read(ByteArrayInputStream(zip("readme.txt" to "hi")), "x")
  }

  @Test(expected = PackException::class)
  fun rejectsPacksFromANewerFormat() {
    PackFile.read(ByteArrayInputStream(zip("launchheim.json" to """{"format": 2, "name": "x"}""")), "x")
  }

  @Test
  fun writingKeepsTheOriginalConfigsAndReplacesTheLists() {
    val original = temp.newFile("original.r2z")
    original.writeBytes(zip("launchheim.json" to desktopManifest, "export.r2x" to "profileName: old", "BepInEx/config/a.cfg" to "x=1"))
    val manifest = PackFile.read(original).manifest.copy(name = "Renamed")

    val written = ByteArrayOutputStream()
    PackFile.write(manifest, original, written)

    val entries = entries(written.toByteArray())
    assertEquals(setOf("launchheim.json", "export.r2x", "BepInEx/config/a.cfg"), entries.keys)
    assertEquals("x=1", entries["BepInEx/config/a.cfg"])
    assertTrue(entries["export.r2x"]!!.startsWith("profileName: \"Renamed\""))
    val back = PackFile.read(ByteArrayInputStream(written.toByteArray()), "x").manifest
    assertEquals(manifest, back)
  }

  @Test
  fun aListStartedOnThePhoneGetsAnExportTimeTheDesktopCanParse() {
    val written = ByteArrayOutputStream()
    PackFile.write(PackManifest(name = "New"), null, written)

    val exportedAt = PackFile.read(ByteArrayInputStream(written.toByteArray()), "x").manifest.exportedAt
    assertTrue(runCatching { java.time.Instant.parse(exportedAt) }.isSuccess)
  }

  @Test
  fun r2xRoundTripsThunderstoreModsOnly() {
    val mods = listOf(
      PackMod(id = "denikson-BepInExPack_Valheim", version = "5.4.2202"),
      PackMod(source = ModSource.Nexus, id = "4", version = "0.9.9"),
      PackMod(id = "Someone-Odd", version = "1.0", enabled = false),
      PackMod(id = "ValheimModding-Jotunn", version = "2.20.1", enabled = false),
    )
    val profile = R2x.read(R2x.write("Say \"hi\"", mods))

    assertEquals("Say \"hi\"", profile.name)
    // Nexus mods and versions that aren't three numbers can't be expressed in r2modman's format.
    assertEquals(listOf(R2x.Mod("denikson-BepInExPack_Valheim", "5.4.2202", true), R2x.Mod("ValheimModding-Jotunn", "2.20.1", false)), profile.mods)
  }

  private fun entries(bytes: ByteArray): Map<String, String> {
    val result = LinkedHashMap<String, String>()
    ZipInputStream(ByteArrayInputStream(bytes)).use { zip ->
      while (true) {
        val entry = zip.nextEntry ?: break
        result[entry.name] = zip.readBytes().toString(Charsets.UTF_8)
      }
    }
    return result
  }

}
