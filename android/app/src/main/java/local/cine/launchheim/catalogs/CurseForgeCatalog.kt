package local.cine.launchheim.catalogs

import java.io.IOException
import java.time.Instant
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull
import local.cine.launchheim.packs.ModSource
import local.cine.launchheim.thunderstore.CatalogException
import local.cine.launchheim.thunderstore.ModSort
import okhttp3.OkHttpClient
import okhttp3.Request

/**
 * CurseForge through the "CurseForge for Studios" API, which answers nothing without an API key, so the
 * phone needs one of its own in Settings (the desktop's stays on the desktop).
 *
 * Valheim's game id is looked up by slug, as on the desktop, since it isn't documented.
 */
class CurseForgeCatalog(private val http: OkHttpClient, private val apiKey: () -> String) : RemoteCatalog {
  private val gate = Mutex()
  private var gameId: Int? = null

  override val source = ModSource.CurseForge

  override val setupHint: String?
    get() = if (apiKey().isBlank()) "CurseForge only answers apps with an API key. Create a free one at console.curseforge.com/#/api-keys and paste it in Settings." else null

  override suspend fun search(text: String, sort: ModSort, page: Int, pageSize: Int, includeNsfw: Boolean): RemotePage {
    val game = gameId()
    // ModsSearchSortField: 1 Featured, 2 Popularity, 3 LastUpdated, 4 Name, 6 TotalDownloads, 11 ReleasedDate.
    val field = when {
      sort == ModSort.Relevance && text.isNotBlank() -> 1
      sort == ModSort.Popular -> 2
      sort == ModSort.Updated -> 3
      sort == ModSort.Name -> 4
      sort == ModSort.Newest -> 11
      else -> 6
    }
    val order = if (sort == ModSort.Name) "asc" else "desc"
    val json = get(
      "$API/mods/search?gameId=$game&searchFilter=${java.net.URLEncoder.encode(text.trim(), "UTF-8")}" +
        "&sortField=$field&sortOrder=$order&index=${page * pageSize}&pageSize=$pageSize",
    )
    val items = json["data"]!!.jsonArray.map { toMod(it.jsonObject) }
    // The API refuses index + pageSize beyond 10,000.
    val total = minOf((json["pagination"] as? JsonObject)?.get("totalCount")?.jsonPrimitive?.intOrNull ?: items.size, 10_000)
    return RemotePage(items, total)
  }

  override suspend fun details(id: String): RemoteDetails {
    val mod = toMod(get("$API/mods/$id")["data"]!!.jsonObject)
    val files = get("$API/mods/$id/files?pageSize=50")["data"]!!.jsonArray.map { it.jsonObject }
      .filter { it["isAvailable"]?.jsonPrimitive?.content != "false" }
      .sortedByDescending { it.string("fileDate") }
    // The newest release, not beta, is what LaunchHeim installs when no file is given.
    val recommended = files.firstOrNull { it["releaseType"]?.jsonPrimitive?.intOrNull == 1 } ?: files.firstOrNull()
    val description = runCatching { get("$API/mods/$id/description").string("data") }.getOrNull() ?: mod.summary
    return RemoteDetails(mod, description, files.map { toFile(it, recommended = it === recommended) })
  }

  private suspend fun gameId(): Int = gate.withLock {
    gameId?.let { return it }
    var index = 0
    while (index < 1000) {
      val games = get("$API/games?index=$index&pageSize=50")["data"]!!.jsonArray
      games.map { it.jsonObject }.firstOrNull { it.string("slug").equals("valheim", true) }?.let { valheim ->
        return valheim["id"]!!.jsonPrimitive.intOrNull!!.also { gameId = it }
      }
      if (games.size < 50) break
      index += 50
    }
    // The key is fine when this is reached: CurseForge answered the list of games, without Valheim (a
    // working key got 38 games on 2026-10-09). The desktop's CurseForgeCatalog.cs says the same.
    throw CatalogException("CurseForge accepts the key but doesn't offer Valheim mods to other apps, so they can't be searched or installed from LaunchHeim.")
  }

  private suspend fun get(url: String): JsonObject = withContext(Dispatchers.IO) {
    val key = apiKey().trim()
    if (key.isEmpty()) throw CatalogException(setupHint!!)
    val request = Request.Builder().url(url).header("x-api-key", key).build()
    try {
      http.newCall(request).execute().use { response ->
        when (response.code) {
          401, 403 -> throw CatalogException("CurseForge rejected the API key. Check it in Settings.")
          404 -> throw CatalogException("CurseForge could not find that mod or file.")
        }
        if (!response.isSuccessful) throw CatalogException("CurseForge answered ${response.code}.")
        Json.parseToJsonElement(response.body.string()).jsonObject
      }
    } catch (e: IOException) {
      throw CatalogException("CurseForge can't be reached. Check your connection.", e)
    }
  }

  companion object {
    private const val API = "https://api.curseforge.com/v1"
    private const val REQUIRED_DEPENDENCY = 3

    // CurseForge has no version field; display names usually carry it ("MyMod 1.2.3").
    private fun versionOf(file: JsonObject): String {
      val name = file.string("displayName") ?: file.string("fileName").orEmpty()
      return Regex("\\d+(\\.\\d+)+").find(name)?.value ?: name
    }

    internal fun toFile(f: JsonObject, recommended: Boolean) = RemoteFile(
      id = f["id"]!!.jsonPrimitive.content,
      name = f.string("displayName") ?: f.string("fileName").orEmpty(),
      version = versionOf(f),
      date = f.string("fileDate")?.let { runCatching { Instant.parse(it).toEpochMilli() }.getOrNull() } ?: 0,
      size = f["fileLength"]?.jsonPrimitive?.longOrNull ?: 0,
      category = when (f["releaseType"]?.jsonPrimitive?.intOrNull) { 2 -> "Beta"; 3 -> "Alpha"; else -> "Release" },
      recommended = recommended,
      dependencies = (f["dependencies"] as? JsonArray).orEmpty().map { it.jsonObject }
        .filter { it["relationType"]?.jsonPrimitive?.intOrNull == REQUIRED_DEPENDENCY }
        .mapNotNull { it["modId"]?.jsonPrimitive?.content },
    )

    internal fun toMod(mod: JsonObject): RemoteMod {
      val latest = (mod["latestFiles"] as? JsonArray).orEmpty().map { it.jsonObject }.maxByOrNull { it.string("fileDate").orEmpty() }
      return RemoteMod(
        source = ModSource.CurseForge,
        id = mod["id"]!!.jsonPrimitive.content,
        name = mod.string("name").orEmpty(),
        author = (mod["authors"] as? JsonArray).orEmpty().mapNotNull { it.jsonObject.string("name") }.joinToString(", "),
        summary = mod.string("summary").orEmpty(),
        icon = (mod["logo"] as? JsonObject)?.string("thumbnailUrl"),
        downloads = (mod["downloadCount"]?.jsonPrimitive?.doubleOrNull ?: 0.0).toLong(),
        likes = mod["thumbsUpCount"]?.jsonPrimitive?.longOrNull ?: 0,
        updated = mod.string("dateModified")?.let { runCatching { Instant.parse(it).toEpochMilli() }.getOrNull() } ?: 0,
        latestVersion = latest?.let(::versionOf),
        websiteUrl = (mod["links"] as? JsonObject)?.string("websiteUrl") ?: "https://www.curseforge.com/valheim/mods/${mod.string("slug")}",
        categories = (mod["categories"] as? JsonArray).orEmpty().mapNotNull { it.jsonObject.string("name") },
      )
    }
  }
}
