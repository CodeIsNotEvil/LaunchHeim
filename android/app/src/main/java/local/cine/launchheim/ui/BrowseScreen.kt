package local.cine.launchheim.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Clear
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.automirrored.filled.Sort
import androidx.compose.material.icons.filled.ThumbUp
import androidx.compose.material3.AssistChip
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.FilledTonalIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import local.cine.launchheim.packs.ModSource
import local.cine.launchheim.packs.PackMod
import local.cine.launchheim.thunderstore.ModSort
import local.cine.launchheim.thunderstore.ThunderstorePackage

@Composable
fun BrowseScreen(vm: BrowseViewModel, targetId: String?, onMod: (String) -> Unit, onRemoteMod: (ModSource, String) -> Unit) {
  LaunchedEffect(targetId) { vm.setTarget(targetId) }
  val state by vm.state.collectAsState()
  val query by vm.query.collectAsState()
  val sort by vm.sort.collectAsState()
  val results by vm.results.collectAsState()
  val target by vm.target.collectAsState()
  val instances by vm.instances.collectAsState()
  val inTarget by vm.targetKeys.collectAsState()
  val addedNames = remember(inTarget) { inTarget.map { it.substringAfter(':') }.toSet() }
  val source by vm.source.collectAsState()
  val remote by vm.remote.collectAsState()
  val list = rememberLazyListState()
  LaunchedEffect(query, sort, source) { list.scrollToItem(0) }

  Scaffold(
    topBar = {
      // A plain Column, not a TopAppBar, so it has to keep clear of the status bar itself.
      Column(Modifier.statusBarsPadding().padding(start = 16.dp, end = 16.dp, top = 12.dp)) {
        OutlinedTextField(
          value = query,
          onValueChange = { vm.query.value = it },
          placeholder = { Text("Search ${source.label}") },
          leadingIcon = { Icon(Icons.Default.Search, null) },
          trailingIcon = { if (query.isNotEmpty()) IconButton(onClick = { vm.query.value = "" }) { Icon(Icons.Default.Clear, "Clear") } },
          singleLine = true,
          modifier = Modifier.fillMaxWidth(),
        )
        Row(
          Modifier.horizontalScroll(rememberScrollState()),
          verticalAlignment = Alignment.CenterVertically,
          horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
          SourceChip(source) { vm.source.value = it }
          TargetChip(target?.name, instances.map { it.id to it.name }, vm::setTarget)
          SortChip(sort) { vm.sort.value = it }
        }
      }
    },
  ) { padding ->
    Box(Modifier.fillMaxSize().padding(padding)) {
      if (source != ModSource.Thunderstore) {
        RemoteResults(vm, remote, target?.name, inTarget, list, onRemoteMod)
        return@Box
      }
      when (val s = state) {
        BrowseViewModel.IndexState.Loading -> Column(Modifier.align(Alignment.Center).padding(32.dp), horizontalAlignment = Alignment.CenterHorizontally) {
          CircularProgressIndicator()
          Spacer(Modifier.size(16.dp))
          Text(
            "Downloading the Thunderstore mod list (about 17 MB, once every six hours)…",
            color = MaterialTheme.colorScheme.onSurfaceVariant,
          )
        }
        is BrowseViewModel.IndexState.Failed -> EmptyState(Icons.Default.CloudOff, "Thunderstore can't be reached", s.message, Modifier.align(Alignment.Center)) {
          Button(onClick = { vm.load(force = false) }) { Text("Try again") }
        }
        BrowseViewModel.IndexState.Ready -> LazyColumn(state = list, contentPadding = PaddingValues(bottom = 24.dp)) {
          item {
            Text(
              "${results.size} mods" + (target?.let { " · adding to ${it.name}" } ?: " · create a mod list to add them"),
              style = MaterialTheme.typography.bodySmall,
              color = MaterialTheme.colorScheme.onSurfaceVariant,
              modifier = Modifier.padding(horizontal = 16.dp, vertical = 6.dp),
            )
          }
          items(results, key = { it.fullName }) { pkg ->
            ResultRow(pkg, added = pkg.fullName.lowercase() in addedNames, canAdd = target != null, onAdd = { vm.add(pkg.fullName) }, onOpen = { onMod(pkg.fullName) })
          }
        }
      }
    }
  }
}

@Composable
private fun ResultRow(pkg: ThunderstorePackage, added: Boolean, canAdd: Boolean, onAdd: () -> Unit, onOpen: () -> Unit) {
  Row(
    Modifier.fillMaxWidth().clickable(onClick = onOpen).padding(horizontal = 16.dp, vertical = 10.dp),
    horizontalArrangement = Arrangement.spacedBy(12.dp),
    verticalAlignment = Alignment.CenterVertically,
  ) {
    ModIcon(pkg.icon, 48.dp)
    Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
      Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(pkg.displayName, style = MaterialTheme.typography.titleSmall, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
        if (pkg.isDeprecated) Tag("Deprecated", MaterialTheme.colorScheme.error)
      }
      Text(pkg.description, style = MaterialTheme.typography.bodySmall, maxLines = 2, overflow = TextOverflow.Ellipsis, color = MaterialTheme.colorScheme.onSurfaceVariant)
      Row(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(pkg.owner, style = MaterialTheme.typography.labelSmall, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
        Stat(Icons.Default.Download, Format.count(pkg.totalDownloads))
        Stat(Icons.Default.ThumbUp, Format.count(pkg.rating.toLong()))
      }
    }
    FilledTonalIconButton(onClick = onAdd, enabled = canAdd && !added) {
      Icon(if (added) Icons.Default.Check else Icons.Default.Add, if (added) "In the list" else "Add ${pkg.displayName}")
    }
  }
}

@Composable
fun Stat(icon: androidx.compose.ui.graphics.vector.ImageVector, text: String) {
  Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(3.dp)) {
    Icon(icon, null, Modifier.size(12.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
    Text(text, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
  }
}

@Composable
private fun TargetChip(name: String?, instances: List<Pair<String, String>>, onPick: (String) -> Unit) {
  var open by remember { mutableStateOf(false) }
  Box {
    AssistChip(onClick = { open = true }, label = { Text(name?.let { "Into: $it" } ?: "No mod list", maxLines = 1, overflow = TextOverflow.Ellipsis) }, enabled = instances.isNotEmpty())
    DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
      instances.forEach { (id, label) -> DropdownMenuItem(text = { Text(label) }, onClick = { open = false; onPick(id) }) }
    }
  }
}

@Composable
private fun SortChip(sort: ModSort, onPick: (ModSort) -> Unit) {
  var open by remember { mutableStateOf(false) }
  Box {
    AssistChip(onClick = { open = true }, label = { Text(sort.label) }, leadingIcon = { Icon(Icons.AutoMirrored.Filled.Sort, null, Modifier.size(16.dp)) })
    DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
      ModSort.entries.forEach { option -> DropdownMenuItem(text = { Text(option.label) }, onClick = { open = false; onPick(option) }) }
    }
  }
}

/** Nexus or CurseForge results, a page at a time as the list is scrolled. */
@Composable
private fun RemoteResults(
  vm: BrowseViewModel,
  remote: BrowseViewModel.RemoteState,
  targetName: String?,
  /** Lower-cased mod keys of the target list. */
  added: Set<String>,
  list: androidx.compose.foundation.lazy.LazyListState,
  onOpen: (ModSource, String) -> Unit,
) {
  val nearEnd by remember { derivedStateOf { list.layoutInfo.visibleItemsInfo.lastOrNull()?.index?.let { it >= list.layoutInfo.totalItemsCount - 5 } == true } }
  LaunchedEffect(nearEnd, remote.items.size) { if (nearEnd) vm.loadRemote() }

  if (remote.error != null && remote.items.isEmpty()) {
    EmptyState(Icons.Default.CloudOff, "Nothing to show", remote.error, Modifier.padding(top = 32.dp)) {
      Button(onClick = { vm.loadRemote(reset = true) }) { Text("Try again") }
    }
    return
  }

  LazyColumn(state = list, contentPadding = PaddingValues(bottom = 24.dp)) {
    item {
      Text(
        "${remote.total} mods" + (targetName?.let { " · adding to $it" } ?: " · create a mod list to add them"),
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        modifier = Modifier.padding(horizontal = 16.dp, vertical = 6.dp),
      )
    }
    items(remote.items, key = { it.source.name + it.id }) { mod ->
      Row(
        Modifier.fillMaxWidth().clickable { onOpen(mod.source, mod.id) }.padding(horizontal = 16.dp, vertical = 10.dp),
        horizontalArrangement = Arrangement.spacedBy(12.dp),
        verticalAlignment = Alignment.CenterVertically,
      ) {
        ModIcon(mod.icon, 48.dp)
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
          Text(mod.name, style = MaterialTheme.typography.titleSmall, maxLines = 1, overflow = TextOverflow.Ellipsis)
          Text(mod.summary, style = MaterialTheme.typography.bodySmall, maxLines = 2, overflow = TextOverflow.Ellipsis, color = MaterialTheme.colorScheme.onSurfaceVariant)
          Row(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(mod.author, style = MaterialTheme.typography.labelSmall, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
            Stat(Icons.Default.Download, Format.count(mod.downloads))
            Stat(Icons.Default.ThumbUp, Format.count(mod.likes))
          }
        }
        val inList = PackMod.makeKey(mod.source, mod.id).lowercase() in added
        FilledTonalIconButton(onClick = { vm.addRemote(mod) }, enabled = targetName != null && !inList) {
          Icon(if (inList) Icons.Default.Check else Icons.Default.Add, if (inList) "In the list" else "Add ${mod.name}")
        }
      }
    }
    if (remote.loading) {
      item { Box(Modifier.fillMaxWidth().padding(16.dp), contentAlignment = Alignment.Center) { CircularProgressIndicator(Modifier.size(24.dp)) } }
    } else if (remote.error != null) {
      item { Text(remote.error, color = MaterialTheme.colorScheme.error, modifier = Modifier.padding(16.dp)) }
    }
  }
}

@Composable
private fun SourceChip(source: ModSource, onPick: (ModSource) -> Unit) {
  var open by remember { mutableStateOf(false) }
  Box {
    AssistChip(onClick = { open = true }, label = { Text(source.label) })
    DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
      listOf(ModSource.Thunderstore, ModSource.Nexus, ModSource.CurseForge).forEach { option ->
        DropdownMenuItem(text = { Text(option.label) }, onClick = { open = false; onPick(option) })
      }
    }
  }
}

/** The site's name as people write it. */
val ModSource.label: String
  get() = when (this) {
    ModSource.Thunderstore -> "Thunderstore"
    ModSource.Nexus -> "Nexus Mods"
    ModSource.CurseForge -> "CurseForge"
    ModSource.Local -> "Local"
  }
