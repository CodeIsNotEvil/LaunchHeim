package local.cine.launchheim.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.FlowPreview
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.debounce
import kotlinx.coroutines.flow.flowOn
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.distinctUntilChanged
import local.cine.launchheim.catalogs.RemoteCatalog
import local.cine.launchheim.catalogs.RemoteDetails
import local.cine.launchheim.catalogs.RemoteFile
import local.cine.launchheim.catalogs.RemoteMod
import local.cine.launchheim.container
import local.cine.launchheim.packs.ModSource
import local.cine.launchheim.packs.PackMod
import local.cine.launchheim.thunderstore.CatalogException
import local.cine.launchheim.data.PhoneInstance
import local.cine.launchheim.packs.ModListEditor
import local.cine.launchheim.thunderstore.ModSort
import local.cine.launchheim.thunderstore.ThunderstorePackage
import local.cine.launchheim.thunderstore.ThunderstoreSearch
import okhttp3.Request

/** Searching Thunderstore and adding mods to a mod list. */
@OptIn(FlowPreview::class)
class BrowseViewModel(app: Application) : AndroidViewModel(app) {
  private val container = app.container
  private val index = container.index
  private val editor = ModListEditor(index::find)

  sealed interface IndexState {
    data object Loading : IndexState
    data object Ready : IndexState
    data class Failed(val message: String) : IndexState
  }

  private val _state = MutableStateFlow<IndexState>(if (index.isLoaded) IndexState.Ready else IndexState.Loading)
  val state: StateFlow<IndexState> = _state.asStateFlow()

  val query = MutableStateFlow("")
  val sort = MutableStateFlow(ModSort.Downloads)

  /** The mod list that Add puts mods into. */
  private val _targetId = MutableStateFlow<String?>(null)
  val target: StateFlow<PhoneInstance?> = combine(_targetId, container.instances.instances) { id, list ->
    list.firstOrNull { it.id == id } ?: list.firstOrNull()
  }.stateIn(viewModelScope, SharingStarted.Eagerly, null)

  val instances = container.instances.instances
  val packages = index.packages

  val results: StateFlow<List<ThunderstorePackage>> =
    combine(query.debounce(200), sort, index.packages, container.settings.settings) { text, sort, packages, settings ->
      val effectiveSort = if (text.isNotBlank() && sort == ModSort.Downloads) ModSort.Relevance else sort
      ThunderstoreSearch.search(packages.values, text, effectiveSort, settings.showNsfw)
    }.flowOn(Dispatchers.Default).stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

  /** Keys of the target's mods, to show which results are already in it. */
  val targetKeys: StateFlow<Set<String>> = target.map { t -> t?.manifest?.mods?.map { it.key.lowercase() }?.toSet().orEmpty() }
    .stateIn(viewModelScope, SharingStarted.Eagerly, emptySet())

  /** Which site Browse shows. Thunderstore is searched in memory, the others on their servers. */
  val source = MutableStateFlow(ModSource.Thunderstore)

  data class RemoteState(
    val items: List<RemoteMod> = emptyList(),
    val total: Int = 0,
    val loading: Boolean = false,
    val error: String? = null,
  ) {
    val canLoadMore: Boolean get() = !loading && error == null && items.size < total
  }

  private val _remote = MutableStateFlow(RemoteState())
  val remote: StateFlow<RemoteState> = _remote.asStateFlow()
  private var remoteJob: Job? = null

  fun catalog(source: ModSource): RemoteCatalog? = when (source) {
    ModSource.Nexus -> container.nexus
    ModSource.CurseForge -> container.curseForge
    else -> null
  }

  init {
    load(force = false)
    // A new search on Nexus or CurseForge whenever what's asked changes; typing waits a moment, since
    // every search is a request to their servers.
    viewModelScope.launch {
      combine(source, query.debounce(400), sort, container.settings.settings) { source, text, sort, settings ->
        listOf(source, text, sort, settings.showNsfw, settings.curseForgeApiKey)
      }.distinctUntilChanged().collectLatest {
        if (catalog(source.value) != null) loadRemote(reset = true)
      }
    }
  }

  /** The next page of Nexus or CurseForge results, or the first one after [reset]. */
  fun loadRemote(reset: Boolean = false) {
    val catalog = catalog(source.value) ?: return
    if (!reset && !_remote.value.canLoadMore) return
    remoteJob?.cancel()
    val before = if (reset) RemoteState() else _remote.value
    _remote.value = before.copy(loading = true, error = null)
    remoteJob = viewModelScope.launch {
      val text = query.value
      val sort = sort.value.let { if (text.isNotBlank() && it == ModSort.Downloads) ModSort.Relevance else it }
      _remote.value = try {
        catalog.setupHint?.let { throw CatalogException(it) }
        val page = catalog.search(text, sort, before.items.size / PAGE_SIZE, PAGE_SIZE, container.settings.settings.value.showNsfw)
        RemoteState(before.items + page.items, page.total)
      } catch (e: CatalogException) {
        before.copy(loading = false, error = e.message)
      }
    }
  }

  suspend fun remoteDetails(source: ModSource, id: String): RemoteDetails? =
    runCatching { catalog(source)?.details(id) }.onFailure { container.notify(it.message ?: "Could not load the mod.") }.getOrNull()

  /**
   * Adds a Nexus or CurseForge mod at [file], or at the file LaunchHeim would pick. CurseForge's
   * required dependencies are looked up and added too, so the list is what LaunchHeim installs.
   */
  fun addRemote(mod: RemoteMod, file: RemoteFile? = null) {
    val targetId = target.value?.id ?: run {
      container.notify("Create a mod list first, then add mods to it.")
      return
    }
    val catalog = catalog(mod.source) ?: return
    viewModelScope.launch {
      try {
        val chosen = file ?: catalog.newestFile(mod.id)?.second ?: throw CatalogException("${mod.name} has no files to add.")
        val existing = container.instances.get(targetId)?.manifest ?: return@launch
        val dependencies = resolveDependencies(catalog, chosen.dependencies, existing, HashSet())
        var added: ModListEditor.Result? = null
        val instance = container.instances.edit(targetId) { manifest ->
          editor.addFile(manifest, packMod(mod, chosen), dependencies).also { added = it }.manifest
        } ?: return@launch
        val extra = (added?.added?.size ?: 1) - 1
        container.notify(
          "Added ${mod.name} ${chosen.version} to ${instance.name}" + if (extra > 0) ", with $extra dependenc${if (extra == 1) "y" else "ies"}" else "",
        )
      } catch (e: CatalogException) {
        container.notify(e.message ?: "Could not add ${mod.name}.")
      }
    }
  }

  private suspend fun resolveDependencies(catalog: RemoteCatalog, ids: List<String>, manifest: local.cine.launchheim.packs.PackManifest, seen: MutableSet<String>): List<PackMod> {
    val result = mutableListOf<PackMod>()
    for (id in ids) {
      val key = PackMod.makeKey(catalog.source, id)
      if (!seen.add(key) || manifest.find(key) != null) continue
      val (mod, file) = catalog.newestFile(id) ?: continue
      result += packMod(mod, file).copy(installedAsDependency = true)
      // Long chains are rare; fifty mods is plenty and stops a loop in the site's data.
      if (seen.size < 50) result += resolveDependencies(catalog, file.dependencies, manifest, seen)
    }
    return result
  }

  private fun packMod(mod: RemoteMod, file: RemoteFile) = PackMod(
    source = mod.source,
    id = mod.id,
    fileId = file.id,
    name = mod.name,
    author = mod.author,
    version = file.version,
    websiteUrl = mod.websiteUrl,
    iconUrl = mod.icon,
    dependencies = file.dependencies.map { PackMod.makeKey(mod.source, it) },
  )

  fun load(force: Boolean) {
    if (!index.isLoaded) _state.value = IndexState.Loading
    viewModelScope.launch {
      _state.value = runCatching { index.ensureLoaded(force) }.fold(
        onSuccess = { IndexState.Ready },
        onFailure = { if (index.isLoaded) IndexState.Ready else IndexState.Failed(it.message ?: "Could not load Thunderstore.") },
      )
    }
  }

  fun setTarget(id: String?) {
    if (id != null) _targetId.value = id
  }

  fun find(fullName: String): ThunderstorePackage? = index.find(fullName)

  /** Adds the mod to the target list, creating a first list if there is none yet. */
  fun add(fullName: String, version: String? = null) {
    val targetId = target.value?.id ?: run {
      container.notify("Create a mod list first, then add mods to it.")
      return
    }
    var result: ModListEditor.Result? = null
    val instance = container.instances.edit(targetId) { manifest -> editor.add(manifest, fullName, version).also { result = it }.manifest }
    val added = result ?: return
    added.warnings.forEach(container::notify)
    val main = added.added.lastOrNull { it.id.equals(fullName, true) }
    val extra = added.added.count { !it.id.equals(fullName, true) }
    if (main != null && instance != null) {
      container.notify(
        "Added ${main.name} ${main.version} to ${instance.name}" + if (extra > 0) ", with ${extra} dependenc${if (extra == 1) "y" else "ies"}" else "",
      )
    }
  }

  @Serializable
  private data class ReadmeResponse(val markdown: String? = null)

  /** The README of a version, or null when Thunderstore can't be reached. */
  suspend fun readme(pkg: ThunderstorePackage, version: String): String? = withContext(Dispatchers.IO) {
    runCatching {
      val url = "https://thunderstore.io/api/experimental/package/${pkg.owner}/${pkg.name}/$version/readme/"
      container.http.newCall(Request.Builder().url(url).build()).execute().use { response ->
        if (!response.isSuccessful) return@use null
        ReadmeJson.decodeFromString<ReadmeResponse>(response.body.string()).markdown
      }
    }.getOrNull()
  }

  private companion object {
    const val PAGE_SIZE = 30
    val ReadmeJson = Json { ignoreUnknownKeys = true }
  }
}
