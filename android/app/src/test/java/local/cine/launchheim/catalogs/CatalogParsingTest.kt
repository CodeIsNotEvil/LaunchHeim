package local.cine.launchheim.catalogs

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import local.cine.launchheim.packs.ModListEditor
import local.cine.launchheim.packs.ModSource
import local.cine.launchheim.packs.PackManifest
import local.cine.launchheim.packs.PackMod
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class CatalogParsingTest {
  @Test
  fun nexusFilesPutTheMainFileFirstAndDropOldOnes() {
    val files = Json.parseToJsonElement(
      """[
        {"fileId": 11, "name": "Optional", "version": "1.0", "category": "OPTIONAL", "sizeInBytes": "100", "date": 1700000000, "primary": 0},
        {"fileId": 12, "name": "Old", "version": "0.9", "category": "OLD_VERSION", "sizeInBytes": "100", "date": 1600000000, "primary": 0},
        {"fileId": 13, "name": "Main", "version": "1.1", "category": "MAIN", "sizeInBytes": "2048", "date": 1710000000, "primary": 0}
      ]""",
    ).jsonArray

    val parsed = NexusCatalog.parseFiles(files)

    assertEquals(listOf("13", "11"), parsed.map { it.id })
    assertTrue(parsed[0].recommended)
    assertEquals("Main", parsed[0].category)
    assertEquals(1_710_000_000_000, parsed[0].date)
  }

  @Test
  fun curseForgeFilesKeepOnlyRequiredDependencies() {
    val file = Json.parseToJsonElement(
      """{"id": 5, "displayName": "Cool Mod 1.2.3", "fileName": "cool.zip", "fileDate": "2026-01-02T03:04:05Z", "fileLength": 10, "releaseType": 1,
          "dependencies": [{"modId": 7, "relationType": 3}, {"modId": 8, "relationType": 2}]}""",
    ).jsonObject

    val parsed = CurseForgeCatalog.toFile(file, recommended = true)

    assertEquals("1.2.3", parsed.version)
    assertEquals(listOf("7"), parsed.dependencies)
    assertEquals("Release", parsed.category)
  }

  @Test
  fun bbCodeKeepsLinksAndBoldButNoScriptsOrImages() {
    val html = BbCode.toHtml("[b]Hi[/b]<br />[url=javascript:alert(1)]x[/url] [url=https://a.b]link[/url] [img]https://a.b/c.png[/img] <script>")
    assertEquals("<b>Hi</b><br><a href=\"#\">x</a> <a href=\"https://a.b\">link</a>  &lt;script&gt;", html)
  }

  @Test
  fun runsOfEmptyLinesShrinkToOne() {
    assertEquals("a<br><br>b", BbCode.toHtml("a<br /><br />\n \n<br/>b"))
  }

  @Test
  fun aFileModIsPickedAndItsDependenciesComeAlongOnce() {
    val editor = ModListEditor { null }
    val start = PackManifest(mods = listOf(PackMod(id = PackMod.LOADER_FULL_NAME, version = "5.4.2202"), PackMod(source = ModSource.CurseForge, id = "7", fileId = "1", version = "1")))
    val mod = PackMod(source = ModSource.CurseForge, id = "5", fileId = "50", name = "Cool", version = "1.2.3", dependencies = listOf("curseforge:7", "curseforge:9"))
    val dependencies = listOf(
      PackMod(source = ModSource.CurseForge, id = "7", fileId = "70", version = "2"),
      PackMod(source = ModSource.CurseForge, id = "9", fileId = "90", version = "1"),
    )

    val result = editor.addFile(start, mod, dependencies).manifest

    assertEquals("50", result.find("curseforge:5")!!.fileId)
    assertFalse(result.find("curseforge:5")!!.installedAsDependency)
    // The dependency the list already had stays at its file; the missing one is added as a dependency.
    assertEquals("1", result.find("curseforge:7")!!.fileId)
    assertTrue(result.find("curseforge:9")!!.installedAsDependency)
    assertEquals(4, result.mods.size)
  }
}
