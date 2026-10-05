package local.cine.launchheim.packs

import local.cine.launchheim.thunderstore.ThunderstorePackage
import local.cine.launchheim.thunderstore.ThunderstoreVersion
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ModListEditorTest {
  private fun pkg(fullName: String, vararg versions: Pair<String, List<String>>) = ThunderstorePackage(
    fullName = fullName,
    owner = fullName.substringBefore('-'),
    name = fullName.substringAfter('-'),
    packageUrl = "https://thunderstore.io/c/valheim/p/${fullName.replace('-', '/')}/",
    created = 0, updated = 0, rating = 0, isDeprecated = false, isNsfw = false, isPinned = false,
    categories = emptyList(), description = "", icon = null, totalDownloads = 0,
    versions = versions.map { (v, deps) -> ThunderstoreVersion(v, deps, 0, 0, 0) },
  )

  private val catalog = listOf(
    pkg("denikson-BepInExPack_Valheim", "5.4.2202" to emptyList()),
    pkg("ValheimModding-Jotunn", "2.21.0" to listOf("denikson-BepInExPack_Valheim-5.4.2200"), "2.20.0" to emptyList()),
    pkg("Azumatt-Lib", "1.1.0" to emptyList(), "1.0.0" to emptyList()),
    pkg("Someone-Mod", "3.0.0" to listOf("ValheimModding-Jotunn-2.20.0", "Azumatt-Lib-1.1.0", "denikson-BepInExPack_Valheim-5.4.2202"), "2.0.0" to listOf("ValheimModding-Jotunn-2.20.0")),
    pkg("Other-Mod", "1.0.0" to listOf("Azumatt-Lib-1.0.0")),
  ).associateBy { it.fullName.lowercase() }

  private val editor = ModListEditor { catalog[it.lowercase()] }

  @Test
  fun addingPullsInDependenciesAtTheNewestVersionAndBepInEx() {
    val result = editor.add(PackManifest(name = "Test"), "Someone-Mod")
    val mods = result.manifest.mods.associateBy { it.id }

    assertEquals("3.0.0", mods.getValue("Someone-Mod").version)
    assertFalse(mods.getValue("Someone-Mod").installedAsDependency)
    // Jotunn 2.20.0 is asked for, but the newest satisfies it and every other mod that needs Jotunn.
    assertEquals("2.21.0", mods.getValue("ValheimModding-Jotunn").version)
    assertTrue(mods.getValue("ValheimModding-Jotunn").installedAsDependency)
    assertTrue(mods.getValue("Azumatt-Lib").installedAsDependency)
    // BepInEx is never a removable dependency.
    assertFalse(mods.getValue("denikson-BepInExPack_Valheim").installedAsDependency)
    assertEquals(
      listOf("thunderstore:ValheimModding-Jotunn", "thunderstore:Azumatt-Lib", "thunderstore:denikson-BepInExPack_Valheim"),
      mods.getValue("Someone-Mod").dependencies,
    )
  }

  @Test
  fun aDependencyThatIsNewEnoughIsKept() {
    val start = PackManifest(mods = listOf(PackMod(id = "Azumatt-Lib", version = "1.0.0", installedAsDependency = false)))
    val result = editor.add(start, "Other-Mod")

    assertEquals("1.0.0", result.manifest.find("thunderstore:Azumatt-Lib")!!.version)
  }

  @Test
  fun pickingAModThatWasADependencyMakesItPicked() {
    val withDependency = editor.add(PackManifest(), "Someone-Mod").manifest
    val result = editor.add(withDependency, "Azumatt-Lib", "1.0.0")

    val lib = result.manifest.find("thunderstore:azumatt-lib")!!
    assertEquals("1.0.0", lib.version)
    assertFalse(lib.installedAsDependency)
  }

  @Test
  fun removingTakesOrphanedDependenciesButNotSharedOnesOrBepInEx() {
    val manifest = editor.add(editor.add(PackManifest(), "Someone-Mod").manifest, "Other-Mod").manifest

    val (afterFirst, removedFirst) = ModListEditor.remove(manifest, "thunderstore:Someone-Mod")
    // Jotunn was only there for Someone-Mod; the Lib is still needed by Other-Mod.
    assertEquals(listOf("Someone-Mod", "ValheimModding-Jotunn"), removedFirst.map { it.id })
    assertTrue(afterFirst.find("thunderstore:Azumatt-Lib") != null)

    val (afterSecond, removedSecond) = ModListEditor.remove(afterFirst, "thunderstore:Other-Mod")
    assertEquals(listOf("Other-Mod", "Azumatt-Lib"), removedSecond.map { it.id })
    assertEquals(listOf("denikson-BepInExPack_Valheim"), afterSecond.mods.map { it.id })
  }

  @Test
  fun enablingAModEnablesWhatItNeeds() {
    var manifest = editor.add(PackManifest(), "Someone-Mod").manifest
    manifest = ModListEditor.setEnabled(manifest, "thunderstore:Azumatt-Lib", false)
    manifest = ModListEditor.setEnabled(manifest, "thunderstore:Someone-Mod", false)
    assertFalse(manifest.find("thunderstore:Azumatt-Lib")!!.enabled)

    manifest = ModListEditor.setEnabled(manifest, "thunderstore:Someone-Mod", true)
    assertTrue(manifest.find("thunderstore:Azumatt-Lib")!!.enabled)
  }

  @Test
  fun updatesListNewerVersionsAndUpdateKeepsTheDependencyFlag() {
    val manifest = PackManifest(
      mods = listOf(
        PackMod(id = "denikson-BepInExPack_Valheim", version = "5.4.2202"),
        PackMod(id = "Azumatt-Lib", version = "1.0.0", installedAsDependency = true),
        PackMod(source = ModSource.Nexus, id = "4", version = "0.1"),
      ),
    )
    assertEquals(mapOf("thunderstore:Azumatt-Lib" to "1.1.0"), editor.updates(manifest))

    val updated = editor.update(manifest, "thunderstore:Azumatt-Lib").manifest.find("thunderstore:Azumatt-Lib")!!
    assertEquals("1.1.0", updated.version)
    assertTrue(updated.installedAsDependency)
  }

  @Test
  fun anUnknownModIsAWarningNotACrash() {
    val result = editor.add(PackManifest(), "Nobody-Nothing")
    assertEquals(listOf("Nobody-Nothing is not on Thunderstore."), result.warnings)
    assertNull(result.manifest.find("thunderstore:Nobody-Nothing"))
  }
}
