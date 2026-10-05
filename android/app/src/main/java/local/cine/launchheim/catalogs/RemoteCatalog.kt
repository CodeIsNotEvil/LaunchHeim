package local.cine.launchheim.catalogs

import local.cine.launchheim.packs.ModSource
import local.cine.launchheim.thunderstore.ModSort

/** A mod on Nexus or CurseForge, as a search result or the head of its details. */
data class RemoteMod(
  val source: ModSource,
  val id: String,
  val name: String,
  val author: String,
  val summary: String,
  val icon: String?,
  val downloads: Long,
  val likes: Long,
  /** Epoch milliseconds, 0 when unknown. */
  val updated: Long,
  val latestVersion: String?,
  val websiteUrl: String,
  val categories: List<String>,
)

/** One downloadable file. Nexus and CurseForge pin a mod by file, not by version. */
data class RemoteFile(
  val id: String,
  val name: String,
  val version: String,
  val date: Long,
  val size: Long,
  val category: String,
  val recommended: Boolean,
  /** CurseForge's required dependencies, as mod ids. Nexus lists none. */
  val dependencies: List<String> = emptyList(),
)

data class RemoteDetails(val mod: RemoteMod, val descriptionHtml: String, val files: List<RemoteFile>)

data class RemotePage(val items: List<RemoteMod>, val total: Int)

/**
 * The sites that are searched on their servers, unlike Thunderstore's list in memory. Ports of
 * LaunchHeim's Core/Catalogs/Nexus/NexusCatalog.cs and CurseForge/CurseForgeCatalog.cs, browsing only:
 * the phone adds a mod with its file id, and LaunchHeim downloads it.
 */
interface RemoteCatalog {
  val source: ModSource

  /** Null when the catalog can be used, otherwise what has to be set up first. */
  val setupHint: String?

  suspend fun search(text: String, sort: ModSort, page: Int, pageSize: Int, includeNsfw: Boolean): RemotePage

  suspend fun details(id: String): RemoteDetails

  /** The file LaunchHeim would pick: the recommended one, else the newest. */
  suspend fun newestFile(id: String): Pair<RemoteMod, RemoteFile>? =
    details(id).let { d -> (d.files.firstOrNull { it.recommended } ?: d.files.firstOrNull())?.let { d.mod to it } }
}
