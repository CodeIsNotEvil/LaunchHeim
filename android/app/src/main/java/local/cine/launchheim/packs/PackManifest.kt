package local.cine.launchheim.packs

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

/** Where a mod comes from, written by name like LaunchHeim's JsonStringEnumConverter does. */
@Serializable
enum class ModSource { Thunderstore, Nexus, CurseForge, Local }

/**
 * One mod in `launchheim.json`, the same shape as `PackMod` in LaunchHeim's Core/Packs/PackManifest.cs.
 *
 * The phone never installs anything: it edits this list, and LaunchHeim downloads and installs the mods
 * when the pack comes back. So unlike the desktop's InstalledMod there are no files or install dates.
 */
@Serializable
data class PackMod(
  val source: ModSource = ModSource.Thunderstore,
  /** The Thunderstore full name (`Owner-Name`), or the Nexus or CurseForge mod id. */
  val id: String = "",
  /** The Nexus or CurseForge file id. Thunderstore pins by [version]. */
  val fileId: String? = null,
  val name: String = "",
  val author: String = "",
  val version: String = "",
  val websiteUrl: String? = null,
  val iconUrl: String? = null,
  val enabled: Boolean = true,
  /** Pulled in by another mod rather than picked by the user, so removing that mod removes this one too. */
  val installedAsDependency: Boolean = false,
  /** Local mods only: their files inside the pack. Kept so they survive the round trip. */
  val files: List<String>? = null,
  /** Keys of the mods this one needs, like `thunderstore:ValheimModding-Jotunn`. */
  val dependencies: List<String> = emptyList(),
) {
  val key: String get() = makeKey(source, id)

  val isLoader: Boolean get() = source == ModSource.Thunderstore && id.equals(LOADER_FULL_NAME, ignoreCase = true)

  companion object {
    const val LOADER_FULL_NAME = "denikson-BepInExPack_Valheim"

    /** `InstalledMod.MakeKey`: the source in lower case, the id as it is. */
    fun makeKey(source: ModSource, id: String) = "${source.name.lowercase()}:$id"
  }
}

/** `launchheim.json`, LaunchHeim's own description of a modpack next to r2modman's `export.r2x`. */
@Serializable
data class PackManifest(
  val format: Int = CURRENT_FORMAT,
  val name: String = "",
  /**
   * The desktop instance the pack was exported from. When the phone sends the pack back, LaunchHeim
   * updates that instance instead of importing a copy. Null for packs made on the phone or by r2modman.
   */
  val instanceId: String? = null,
  val exportedBy: String = "",
  val exportedAt: String = "",
  val launchArguments: String = "",
  val mods: List<PackMod> = emptyList(),
) {
  fun find(key: String): PackMod? = mods.firstOrNull { it.key.equals(key, ignoreCase = true) }

  companion object {
    const val CURRENT_FORMAT = 1
  }
}

/**
 * JSON as LaunchHeim writes it (System.Text.Json's web defaults): camelCase names, and unknown fields
 * skipped so a newer desktop's additions don't break an older phone app.
 */
val PackJson = Json {
  ignoreUnknownKeys = true
  encodeDefaults = true
  explicitNulls = false
  prettyPrint = true
  coerceInputValues = true
}
