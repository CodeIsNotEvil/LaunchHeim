package local.cine.launchheim.packs

import local.cine.launchheim.thunderstore.DependencyString
import local.cine.launchheim.thunderstore.ModVersion
import local.cine.launchheim.thunderstore.ThunderstorePackage

/**
 * Changes a mod list the way LaunchHeim's ModService changes an installed instance, so what the phone
 * sends back is what the desktop would have ended up with: dependencies come along, get the newest
 * version, and leave again with the last mod that needed them.
 *
 * Everything here is a pure function from one manifest to the next; nothing is downloaded.
 */
class ModListEditor(private val lookup: (String) -> ThunderstorePackage?) {

  data class Result(val manifest: PackManifest, val added: List<PackMod>, val warnings: List<String>)

  /**
   * Adds a Thunderstore mod at [version] (null for the newest), plus whatever it needs that the list
   * lacks or has in a version older than asked for. A mod that's already there is moved to [version]
   * and counts as picked from then on.
   */
  fun add(manifest: PackManifest, fullName: String, version: String? = null): Result {
    val mods = manifest.mods.toMutableList()
    val added = mutableListOf<PackMod>()
    val warnings = mutableListOf<String>()
    val visited = HashSet<String>()

    fun place(mod: PackMod) {
      val index = mods.indexOfFirst { it.key.equals(mod.key, ignoreCase = true) }
      if (index >= 0) mods[index] = mod else mods += mod
      added += mod
    }

    fun addRecursive(name: String, pinned: String?, asDependency: Boolean) {
      val key = PackMod.makeKey(ModSource.Thunderstore, name)
      if (!visited.add(key.lowercase())) return

      val pkg = lookup(name)
      if (pkg == null) {
        warnings += "$name is not on Thunderstore."
        return
      }

      val chosen = pinned?.let(pkg::find) ?: pkg.latest
      val dependencies = chosen.dependencies.mapNotNull(DependencyString::parse)
      for (dependency in dependencies) {
        val existing = mods.firstOrNull { it.key.equals(PackMod.makeKey(ModSource.Thunderstore, dependency.fullName), ignoreCase = true) }
        // A dependency the list already has in a new enough version is kept, as on the desktop.
        if (existing != null && ModVersion.compare(existing.version, dependency.version) >= 0) continue
        addRecursive(dependency.fullName, null, asDependency = true)
      }

      val previous = mods.firstOrNull { it.key.equals(key, ignoreCase = true) }
      place(
        PackMod(
          source = ModSource.Thunderstore,
          id = pkg.fullName,
          name = pkg.displayName,
          author = pkg.owner,
          version = chosen.version,
          websiteUrl = pkg.packageUrl,
          iconUrl = pkg.icon,
          enabled = previous?.enabled ?: true,
          // Updating a dependency must not promote it, and picking a mod by hand always makes it picked.
          // BepInEx is what the instance is for, so it is never a removable leftover either.
          installedAsDependency = asDependency && (previous?.installedAsDependency ?: true) && !pkg.fullName.equals(PackMod.LOADER_FULL_NAME, true),
          dependencies = dependencies.map { PackMod.makeKey(ModSource.Thunderstore, it.fullName) },
        ),
      )
    }

    addRecursive(fullName, version, asDependency = false)
    val withLoader = ensureLoader(manifest.copy(mods = mods), added, warnings)
    return Result(withLoader, added.distinctBy { it.key.lowercase() }, warnings)
  }

  /**
   * Adds a Nexus or CurseForge mod at a file, and the dependencies given that the list doesn't have
   * yet. Those sites have no dependency strings to resolve here; the caller looks them up (CurseForge)
   * or there are none (Nexus, where LaunchHeim can't know them either).
   */
  fun addFile(manifest: PackManifest, mod: PackMod, dependencies: List<PackMod> = emptyList()): Result {
    val mods = manifest.mods.toMutableList()
    val added = mutableListOf<PackMod>()
    for (dependency in dependencies) {
      if (mods.none { it.key.equals(dependency.key, true) }) {
        mods += dependency.copy(installedAsDependency = true)
        added += dependency
      }
    }
    val index = mods.indexOfFirst { it.key.equals(mod.key, true) }
    val picked = mod.copy(installedAsDependency = false, enabled = mods.getOrNull(index)?.enabled ?: mod.enabled)
    if (index >= 0) mods[index] = picked else mods += picked
    added += picked

    val warnings = mutableListOf<String>()
    return Result(ensureLoader(manifest.copy(mods = mods), added, warnings), added, warnings)
  }

  /** Moves a Thunderstore mod to its newest version, pulling in new dependencies like [add]. */
  fun update(manifest: PackManifest, key: String): Result {
    val mod = manifest.find(key) ?: return Result(manifest, emptyList(), emptyList())
    val result = add(manifest, mod.id, null)
    // add() marks the target as picked; an update keeps what it was.
    val mods = result.manifest.mods.map { if (it.key.equals(key, true)) it.copy(installedAsDependency = mod.installedAsDependency) else it }
    return result.copy(manifest = result.manifest.copy(mods = mods))
  }

  /** The newest version for each Thunderstore mod that has a newer one, keyed by mod key. */
  fun updates(manifest: PackManifest): Map<String, String> =
    manifest.mods
      .filter { it.source == ModSource.Thunderstore }
      .mapNotNull { mod ->
        val latest = lookup(mod.id)?.latest?.version ?: return@mapNotNull null
        if (ModVersion.compare(latest, mod.version) > 0) mod.key to latest else null
      }
      .toMap()

  private fun ensureLoader(manifest: PackManifest, added: MutableList<PackMod>, warnings: MutableList<String>): PackManifest {
    if (manifest.mods.any { it.isLoader }) return manifest
    val result = add(manifest, PackMod.LOADER_FULL_NAME)
    added += result.added
    warnings += result.warnings
    return result.manifest
  }

  companion object {
    /** Mods that list [key] as a dependency and would break without it. */
    fun dependents(manifest: PackManifest, key: String): List<PackMod> =
      manifest.mods.filter { mod -> mod.dependencies.any { it.equals(key, ignoreCase = true) } }

    /**
     * Removes the mod and the dependencies that were only there for it, the way ModService.RemoveAsync
     * cleans up. Mods the user picked are never removed as a side effect, and neither is BepInEx.
     */
    fun remove(manifest: PackManifest, key: String): Pair<PackManifest, List<PackMod>> {
      var current = manifest
      val removed = mutableListOf<PackMod>()
      val queue = ArrayDeque(listOf(key))
      while (queue.isNotEmpty()) {
        val mod = current.find(queue.removeFirst()) ?: continue
        current = current.copy(mods = current.mods - mod)
        removed += mod
        for (dependency in mod.dependencies) {
          val orphan = current.find(dependency) ?: continue
          if (orphan.installedAsDependency && !orphan.isLoader && dependents(current, orphan.key).isEmpty()) {
            queue += orphan.key
          }
        }
      }
      return current to removed
    }

    /** Turning a mod on also turns on what it needs; turning it off leaves its dependencies alone. */
    fun setEnabled(manifest: PackManifest, key: String, enabled: Boolean): PackManifest {
      var mods = manifest.mods
      val queue = ArrayDeque(listOf(key))
      val seen = HashSet<String>()
      while (queue.isNotEmpty()) {
        val next = queue.removeFirst()
        if (!seen.add(next.lowercase())) continue
        val mod = mods.firstOrNull { it.key.equals(next, true) } ?: continue
        mods = mods.map { if (it === mod) it.copy(enabled = enabled) else it }
        if (enabled) queue += mod.dependencies
      }
      return manifest.copy(mods = mods)
    }
  }
}
