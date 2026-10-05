package local.cine.launchheim.thunderstore

import java.io.BufferedInputStream
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.time.Instant
import java.util.concurrent.TimeUnit
import java.util.zip.GZIPInputStream
import java.util.zip.GZIPOutputStream
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.withContext
import kotlinx.serialization.ExperimentalSerializationApi
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.builtins.serializer
import kotlinx.serialization.json.DecodeSequenceMode
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.decodeFromStream
import kotlinx.serialization.json.decodeToSequence
import kotlinx.serialization.json.encodeToStream
import okhttp3.OkHttpClient
import okhttp3.Request

@Serializable
data class ThunderstoreVersion(
  val version: String,
  val dependencies: List<String>,
  val downloads: Long,
  /** Epoch milliseconds. */
  val created: Long,
  val fileSize: Long,
)

@Serializable
data class ThunderstorePackage(
  val fullName: String,
  val owner: String,
  val name: String,
  val packageUrl: String,
  val created: Long,
  val updated: Long,
  val rating: Int,
  val isDeprecated: Boolean,
  val isNsfw: Boolean,
  val isPinned: Boolean,
  val categories: List<String>,
  val description: String,
  val icon: String?,
  val totalDownloads: Long,
  /** Newest first, as the API lists them. */
  val versions: List<ThunderstoreVersion>,
) {
  val latest: ThunderstoreVersion get() = versions[0]

  /** The name as people read it: `Valheim_Plus` becomes `Valheim Plus`. */
  val displayName: String get() = name.replace('_', ' ')

  fun find(version: String): ThunderstoreVersion? = versions.firstOrNull { it.version.equals(version, ignoreCase = true) }
}

class CatalogException(message: String, cause: Throwable? = null) : Exception(message, cause)

/**
 * The whole Valheim package list, cached on the phone and searched in memory, like LaunchHeim's
 * ThunderstoreIndex.cs.
 *
 * Thunderstore has no search API for third-party apps, only the community's full list in gzipped chunks
 * behind `api/v1/package-listing-index` (about 17 MB to download). Each chunk is parsed as a stream and
 * trimmed to the fields the app uses right away, so the 170 MB of JSON never sits in memory at once. The
 * trimmed list is a few MB, cached for [MAX_AGE_MS], and an old copy is used offline.
 */
class ThunderstoreIndex(private val http: OkHttpClient, private val cacheFile: File) {
  private val gate = Mutex()
  private val _packages = MutableStateFlow<Map<String, ThunderstorePackage>>(emptyMap())
  private val _lastUpdated = MutableStateFlow<Long?>(null)

  /** Keyed by full name, case-insensitively (lower-cased keys). */
  val packages: StateFlow<Map<String, ThunderstorePackage>> = _packages.asStateFlow()
  val lastUpdated: StateFlow<Long?> = _lastUpdated.asStateFlow()

  val isLoaded: Boolean get() = _packages.value.isNotEmpty()

  fun find(fullName: String): ThunderstorePackage? = _packages.value[fullName.lowercase()]

  /** Loads the cache if it's fresh enough, otherwise downloads a new list. */
  suspend fun ensureLoaded(forceRefresh: Boolean = false) = withContext(Dispatchers.IO) {
    gate.withLock {
      val now = System.currentTimeMillis()
      if (!forceRefresh && isLoaded && now - (_lastUpdated.value ?: 0) < MAX_AGE_MS) return@withLock

      if (!forceRefresh && !isLoaded && cacheFile.exists() && now - cacheFile.lastModified() < MAX_AGE_MS) {
        if (runCatching { loadCache() }.isSuccess) return@withLock
      }

      try {
        val packages = download()
        saveCache(packages)
        use(packages, now)
      } catch (e: IOException) {
        if (!cacheFile.exists()) throw CatalogException("Could not download the Thunderstore package list. Check your connection.", e)
        // Offline: an old list is far more useful than none.
        if (!isLoaded) loadCache()
      }
    }
  }

  internal fun use(packages: List<ThunderstorePackage>, updated: Long) {
    _packages.value = packages.filter { it.versions.isNotEmpty() }.associateBy { it.fullName.lowercase() }
    _lastUpdated.value = updated
  }

  @OptIn(ExperimentalSerializationApi::class)
  private fun loadCache() {
    val packages = GZIPInputStream(cacheFile.inputStream().buffered()).use {
      CacheJson.decodeFromStream(ListSerializer(ThunderstorePackage.serializer()), it)
    }
    use(packages, cacheFile.lastModified())
  }

  @OptIn(ExperimentalSerializationApi::class)
  private fun saveCache(packages: List<ThunderstorePackage>) {
    cacheFile.parentFile?.mkdirs()
    val temp = File(cacheFile.path + ".tmp")
    GZIPOutputStream(temp.outputStream().buffered()).use {
      CacheJson.encodeToStream(ListSerializer(ThunderstorePackage.serializer()), packages, it)
    }
    if (!temp.renameTo(cacheFile)) throw IOException("Could not save the Thunderstore list.")
  }

  @OptIn(ExperimentalSerializationApi::class)
  private suspend fun download(): List<ThunderstorePackage> = coroutineScope {
    val chunkUrls = get(LISTING_INDEX_URL) { RawJson.decodeFromStream(ListSerializer(String.serializer()), it) }
    val pool = StringPool()

    // A few chunks at a time: fast, without hammering the CDN or the phone's memory.
    val limit = Semaphore(3)
    chunkUrls.map { url ->
      async(Dispatchers.IO) {
        limit.withPermit {
          get(url) { stream ->
            RawJson.decodeToSequence(stream, RawPackage.serializer(), DecodeSequenceMode.ARRAY_WRAPPED)
              .map { compact(it, pool) }
              .toList()
          }
        }
      }
    }.awaitAll().flatten()
  }

  private fun <T> get(url: String, read: (InputStream) -> T): T {
    val request = Request.Builder().url(url).build()
    http.newCall(request).execute().use { response ->
      if (!response.isSuccessful) throw IOException("Thunderstore answered ${response.code}.")
      return read(gunzipIfNeeded(response.body.byteStream()))
    }
  }

  /**
   * The listing files are gzip files, not gzip transfer encoding, so OkHttp hands over the compressed
   * bytes. Should the CDN ever serve them with Content-Encoding instead, OkHttp has already unpacked them.
   */
  private fun gunzipIfNeeded(stream: InputStream): InputStream {
    val buffered = BufferedInputStream(stream)
    buffered.mark(2)
    val magic = buffered.read() == 0x1f && buffered.read() == 0x8b
    buffered.reset()
    return if (magic) GZIPInputStream(buffered, 64 * 1024) else buffered
  }

  /**
   * The list holds millions of dependency strings but only tens of thousands of distinct ones, because
   * every version of every modpack repeats the same entries. One shared instance per distinct string
   * keeps the index small, as the desktop's PooledStringConverter does.
   */
  private class StringPool {
    private val pool = HashMap<String, String>()

    @Synchronized
    fun of(value: String): String = pool.getOrPut(value) { value }
  }

  private fun compact(raw: RawPackage, pool: StringPool): ThunderstorePackage {
    val versions = raw.versions.orEmpty()
    val latest = versions.firstOrNull()
    return ThunderstorePackage(
      fullName = raw.fullName,
      owner = pool.of(raw.owner),
      name = raw.name,
      packageUrl = raw.packageUrl,
      created = parseTime(raw.dateCreated),
      updated = parseTime(raw.dateUpdated),
      rating = raw.ratingScore,
      isDeprecated = raw.isDeprecated,
      isNsfw = raw.hasNsfwContent,
      isPinned = raw.isPinned,
      categories = raw.categories.orEmpty().map(pool::of),
      description = latest?.description.orEmpty(),
      icon = latest?.icon,
      totalDownloads = versions.sumOf { it.downloads },
      versions = versions.map { v ->
        ThunderstoreVersion(pool.of(v.versionNumber), v.dependencies.orEmpty().map(pool::of), v.downloads, parseTime(v.dateCreated), v.fileSize)
      },
    )
  }

  private fun parseTime(value: String?): Long = value?.let { runCatching { Instant.parse(it).toEpochMilli() }.getOrNull() } ?: 0

  @Serializable
  internal data class RawPackage(
    val name: String,
    @SerialName("full_name") val fullName: String,
    val owner: String,
    @SerialName("package_url") val packageUrl: String = "",
    @SerialName("date_created") val dateCreated: String? = null,
    @SerialName("date_updated") val dateUpdated: String? = null,
    @SerialName("rating_score") val ratingScore: Int = 0,
    @SerialName("is_pinned") val isPinned: Boolean = false,
    @SerialName("is_deprecated") val isDeprecated: Boolean = false,
    @SerialName("has_nsfw_content") val hasNsfwContent: Boolean = false,
    val categories: List<String>? = null,
    val versions: List<RawVersion>? = null,
  )

  @Serializable
  internal data class RawVersion(
    @SerialName("version_number") val versionNumber: String,
    val description: String? = null,
    val icon: String? = null,
    val dependencies: List<String>? = null,
    val downloads: Long = 0,
    @SerialName("date_created") val dateCreated: String? = null,
    @SerialName("file_size") val fileSize: Long = 0,
  )

  companion object {
    const val LISTING_INDEX_URL = "https://thunderstore.io/c/valheim/api/v1/package-listing-index/"
    val MAX_AGE_MS = TimeUnit.HOURS.toMillis(6)

    private val RawJson = Json { ignoreUnknownKeys = true; coerceInputValues = true }
    private val CacheJson = Json { ignoreUnknownKeys = true }
  }
}
