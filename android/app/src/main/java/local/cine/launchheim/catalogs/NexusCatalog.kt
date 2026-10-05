package local.cine.launchheim.catalogs

import java.io.IOException
import java.time.Instant
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull
import kotlinx.serialization.json.put
import local.cine.launchheim.packs.ModSource
import local.cine.launchheim.thunderstore.CatalogException
import local.cine.launchheim.thunderstore.ModSort
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody

/**
 * Nexus Mods through its public GraphQL API, which answers anonymous searches, so the phone needs no
 * account. Downloading is LaunchHeim's business: it needs the API key there, and free accounts confirm
 * each download on the website.
 */
class NexusCatalog(private val http: OkHttpClient) : RemoteCatalog {
  override val source = ModSource.Nexus
  override val setupHint: String? = null

  override suspend fun search(text: String, sort: ModSort, page: Int, pageSize: Int, includeNsfw: Boolean): RemotePage {
    val query = text.trim()
    val filter = buildJsonObject {
      put("gameDomainName", filterOf(JsonPrimitive(GAME_DOMAIN), "EQUALS"))
      put("status", filterOf(JsonPrimitive("published"), "EQUALS"))
      if (!includeNsfw) put("adultContent", filterOf(JsonPrimitive(false), "EQUALS"))
      if (query.isNotEmpty()) put("name", filterOf(JsonPrimitive(query), "WILDCARD"))
    }
    val field = when {
      sort == ModSort.Relevance && query.isNotEmpty() -> "relevance"
      sort == ModSort.Popular -> "endorsements"
      sort == ModSort.Updated -> "updatedAt"
      sort == ModSort.Newest -> "createdAt"
      sort == ModSort.Name -> "name"
      else -> "downloads"
    }
    val variables = buildJsonObject {
      put("filter", filter)
      put("sort", buildJsonArray { add(buildJsonObject { put(field, buildJsonObject { put("direction", if (sort == ModSort.Name) "ASC" else "DESC") }) }) })
      put("offset", page * pageSize)
      put("count", pageSize)
    }
    val data = graphQl(SEARCH, variables)
    val mods = data["mods"]!!.jsonObject
    return RemotePage(mods["nodes"]!!.jsonArray.map { toMod(it.jsonObject) }, mods["totalCount"]?.jsonPrimitive?.longOrNull?.toInt() ?: 0)
  }

  override suspend fun details(id: String): RemoteDetails {
    val data = graphQl(DETAILS, buildJsonObject { put("modId", id); put("gameId", GAME_ID.toString()) })
    val mod = (data["mod"] as? JsonObject) ?: throw CatalogException("Nexus mod $id was not found.")
    val description = mod.string("description").orEmpty()
    return RemoteDetails(toMod(mod), BbCode.toHtml(description), parseFiles(data["modFiles"]?.jsonArray ?: JsonArray(emptyList())))
  }

  private suspend fun graphQl(query: String, variables: JsonObject): JsonObject = withContext(Dispatchers.IO) {
    val body = buildJsonObject { put("query", query); put("variables", variables) }.toString().toRequestBody(JSON)
    val request = Request.Builder().url(GRAPHQL_URL).post(body).header("Application-Name", "LaunchHeim Companion").build()
    val json = try {
      http.newCall(request).execute().use { response ->
        if (!response.isSuccessful) throw CatalogException("Nexus answered ${response.code}.")
        Json.parseToJsonElement(response.body.string()).jsonObject
      }
    } catch (e: IOException) {
      throw CatalogException("Nexus Mods can't be reached. Check your connection.", e)
    }
    (json["errors"] as? JsonArray)?.firstOrNull()?.let { throw CatalogException("Nexus: " + (it.jsonObject.string("message") ?: "unknown error")) }
    json["data"] as? JsonObject ?: throw CatalogException("Nexus returned no data.")
  }

  companion object {
    const val GAME_DOMAIN = "valheim"
    const val GAME_ID = 3667
    private const val GRAPHQL_URL = "https://api.nexusmods.com/v2/graphql"
    private val JSON = "application/json".toMediaType()

    private const val FIELDS = "modId name summary author version downloads endorsements thumbnailUrl pictureUrl updatedAt createdAt modCategory { name }"
    private const val SEARCH = """
      query(${'$'}filter: ModsFilter, ${'$'}sort: [ModsSort!], ${'$'}offset: Int, ${'$'}count: Int) {
        mods(filter: ${'$'}filter, sort: ${'$'}sort, offset: ${'$'}offset, count: ${'$'}count) { totalCount nodes { $FIELDS } }
      }"""
    private const val DETAILS = """
      query(${'$'}modId: ID!, ${'$'}gameId: ID!) {
        mod(modId: ${'$'}modId, gameId: ${'$'}gameId) { $FIELDS description }
        modFiles(modId: ${'$'}modId, gameId: ${'$'}gameId) { fileId name version category sizeInBytes date description primary }
      }"""

    private fun filterOf(value: JsonPrimitive, op: String) = buildJsonArray { add(buildJsonObject { put("value", value); put("op", op) }) }

    /**
     * Main files first, then updates, optional files and the rest; old, archived and removed files are
     * left out because adding them is almost never what anyone wants. As in NexusCatalog.ParseFiles.
     */
    internal fun parseFiles(files: JsonArray): List<RemoteFile> {
      data class Raw(val file: RemoteFile, val category: String, val primary: Boolean)
      val parsed = files.map { it.jsonObject }.map { f ->
        val category = f.string("category").orEmpty()
        Raw(
          RemoteFile(
            id = f["fileId"]!!.jsonPrimitive.content,
            name = f.string("name").orEmpty(),
            version = f.string("version").orEmpty(),
            date = (f["date"]?.jsonPrimitive?.longOrNull ?: 0) * 1000,
            size = f["sizeInBytes"]?.jsonPrimitive?.contentOrNull?.toLongOrNull() ?: 0,
            category = when (category) { "MAIN" -> "Main"; "OPTIONAL" -> "Optional"; "UPDATE" -> "Update"; else -> "Misc" },
            recommended = false,
          ),
          category,
          f["primary"]?.jsonPrimitive?.contentOrNull == "1",
        )
      }.filter { it.category in setOf("MAIN", "OPTIONAL", "UPDATE", "MISCELLANEOUS") }
        .sortedWith(compareBy<Raw> { when (it.category) { "MAIN" -> 0; "UPDATE" -> 1; "OPTIONAL" -> 2; else -> 3 } }.thenByDescending { it.file.date })

      val recommended = parsed.firstOrNull { it.primary } ?: parsed.firstOrNull { it.category == "MAIN" }
      return parsed.map { if (it === recommended) it.file.copy(recommended = true) else it.file }
    }

    internal fun toMod(node: JsonObject): RemoteMod {
      val id = node["modId"]!!.jsonPrimitive.content
      return RemoteMod(
        source = ModSource.Nexus,
        id = id,
        name = unescapeHtml(node.string("name").orEmpty()),
        author = node.string("author").orEmpty(),
        summary = unescapeHtml(node.string("summary").orEmpty()),
        icon = node.string("thumbnailUrl") ?: node.string("pictureUrl"),
        downloads = node["downloads"]?.jsonPrimitive?.longOrNull ?: 0,
        likes = node["endorsements"]?.jsonPrimitive?.longOrNull ?: 0,
        updated = node.string("updatedAt")?.let { runCatching { Instant.parse(it).toEpochMilli() }.getOrNull() } ?: 0,
        latestVersion = node.string("version"),
        websiteUrl = "https://www.nexusmods.com/$GAME_DOMAIN/mods/$id",
        categories = listOfNotNull((node["modCategory"] as? JsonObject)?.string("name")),
      )
    }

    // Nexus sends names and summaries HTML-escaped.
    private fun unescapeHtml(text: String) =
      text.replace("&#39;", "'").replace("&quot;", "\"").replace("&lt;", "<").replace("&gt;", ">").replace("&amp;", "&")
  }
}

internal fun JsonObject.string(name: String): String? = (this[name] as? JsonPrimitive)?.takeIf { it !is JsonNull }?.contentOrNull
