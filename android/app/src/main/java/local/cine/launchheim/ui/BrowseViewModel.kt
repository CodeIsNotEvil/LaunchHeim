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
import local.cine.launchheim.container
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

  init {
    load(force = false)
  }

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
    val ReadmeJson = Json { ignoreUnknownKeys = true }
  }
}
