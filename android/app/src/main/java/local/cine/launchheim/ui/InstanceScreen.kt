package local.cine.launchheim.ui

import android.content.Intent
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.Share
import androidx.compose.material.icons.filled.Upgrade
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.core.content.FileProvider
import kotlinx.coroutines.launch
import local.cine.launchheim.packs.ModListEditor
import local.cine.launchheim.packs.ModSource
import local.cine.launchheim.packs.PackMod
import local.cine.launchheim.ui.theme.Positive

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun InstanceScreen(vm: MainViewModel, id: String, onBack: () -> Unit, onBrowse: (String) -> Unit, onMod: (PackMod) -> Unit, onOpenList: (String) -> Unit) {
  val instances by vm.instances.collectAsState()
  val instance = instances.firstOrNull { it.id == id }
  val updates by vm.updates.collectAsState()
  val peers by vm.peers.collectAsState()
  val running by vm.nodeRunning.collectAsState()
  val sendingTo by vm.sendingTo.collectAsState()
  val context = LocalContext.current
  val scope = rememberCoroutineScope()

  var menu by remember { mutableStateOf(false) }
  var renaming by remember { mutableStateOf(false) }
  var deleting by remember { mutableStateOf(false) }
  var duplicating by remember { mutableStateOf(false) }
  var picking by remember { mutableStateOf(false) }
  var removing by remember { mutableStateOf<PackMod?>(null) }

  if (instance == null) {
    // Deleted: go back rather than show an empty page.
    LaunchedEffect(Unit) { onBack() }
    return
  }
  val modUpdates = updates[id].orEmpty()
  val mods = instance.manifest.mods.sortedWith(
    compareByDescending<PackMod> { it.isLoader }.thenBy { it.installedAsDependency }.thenBy(String.CASE_INSENSITIVE_ORDER) { it.name },
  )

  Scaffold(
    topBar = {
      TopAppBar(
        title = { Text(instance.name, maxLines = 1, overflow = TextOverflow.Ellipsis) },
        navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "Back") } },
        actions = {
          if (sendingTo != null) {
            CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
          } else {
            IconButton(onClick = { picking = true }) { Icon(Icons.AutoMirrored.Filled.Send, "Send to PC") }
          }
          IconButton(onClick = { menu = true }) { Icon(Icons.Default.MoreVert, "More") }
          DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
            DropdownMenuItem(text = { Text("Share as file…") }, leadingIcon = { Icon(Icons.Default.Share, null) }, onClick = {
              menu = false
              scope.launch {
                val file = vm.packForSharing(instance)
                val uri = FileProvider.getUriForFile(context, "${context.packageName}.files", file)
                val send = Intent(Intent.ACTION_SEND).setType("application/zip").putExtra(Intent.EXTRA_STREAM, uri)
                  .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                context.startActivity(Intent.createChooser(send, "Share ${instance.name}"))
              }
            })
            DropdownMenuItem(text = { Text("Rename…") }, onClick = { menu = false; renaming = true })
            DropdownMenuItem(text = { Text("Duplicate as a new list…") }, onClick = { menu = false; duplicating = true })
            DropdownMenuItem(text = { Text("Delete…") }, leadingIcon = { Icon(Icons.Default.Delete, null) }, onClick = { menu = false; deleting = true })
          }
        },
      )
    },
  ) { padding ->
    LazyColumn(Modifier.fillMaxSize().padding(padding), contentPadding = PaddingValues(bottom = 32.dp)) {
      item {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
          Text(
            buildString {
              append(instance.origin?.let { "From $it" } ?: "Made on this phone")
              if (instance.manifest.instanceId != null) append(" · linked to its instance on the PC")
              if (instance.configFiles > 0) append(" · ${instance.configFiles} config file(s) kept")
            },
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
          )
          if (sendingTo != null) {
            Text("Waiting for $sendingTo to accept…", color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.bodyMedium)
          } else if (instance.changedSinceSync) {
            Text(
              "Changed since it was last sent. Send it to the PC and LaunchHeim installs the changes into " +
                if (instance.manifest.instanceId != null) "the instance." else "a new instance.",
              style = MaterialTheme.typography.bodyMedium,
            )
          }
          Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(onClick = { onBrowse(id) }) { Icon(Icons.Default.Add, null); Text(" Add mods") }
            if (modUpdates.isNotEmpty()) {
              OutlinedButton(onClick = { vm.updateAll(id) }) { Icon(Icons.Default.Upgrade, null); Text(" Update ${modUpdates.size}") }
            }
          }
        }
        HorizontalDivider()
      }

      items(mods, key = { it.key }) { mod ->
        ModListRow(
          mod = mod,
          icon = vm.iconOf(mod),
          update = modUpdates[mod.key],
          onToggle = { vm.setEnabled(id, mod.key, it) },
          onUpdate = { vm.update(id, mod.key) },
          onRemove = { removing = mod },
          onOpen = { onMod(mod) },
        )
      }
    }
  }

  if (picking) {
    DeviceSheet(
      title = "Send ${instance.name}",
      peers = peers,
      running = running,
      onScan = vm::scan,
      onPick = { peer -> picking = false; vm.send(instance, peer) },
      onDismiss = { picking = false },
    )
  }
  if (renaming) {
    FieldsDialog("Rename", listOf("Name" to instance.name), "Rename", onConfirm = { vm.rename(id, it[0]) }, onDismiss = { renaming = false })
  }
  if (duplicating) {
    // For a new instance on the PC with this list as its start: the copy isn't linked to the original's
    // instance, so LaunchHeim imports it instead of offering to update that one.
    FieldsDialog(
      "Duplicate as a new list",
      listOf("Name" to instance.name + " (copy)"),
      "Duplicate",
      onConfirm = { vm.duplicate(id, it[0])?.let(onOpenList) },
      onDismiss = { duplicating = false },
    )
  }
  if (deleting) {
    ConfirmDialog(
      "Delete ${instance.name}?",
      "The list is removed from this phone. The instance on your PC is not touched.",
      "Delete",
      onConfirm = { vm.delete(id); onBack() },
      onDismiss = { deleting = false },
    )
  }
  removing?.let { mod ->
    val dependents = ModListEditor.dependents(instance.manifest, mod.key)
    ConfirmDialog(
      "Remove ${mod.name}?",
      if (dependents.isEmpty()) "Dependencies that only ${mod.name} needed are removed with it."
      else "${dependents.joinToString { it.name }} need${if (dependents.size == 1) "s" else ""} it and won't work without it.",
      "Remove",
      onConfirm = { vm.remove(id, mod.key) },
      onDismiss = { removing = null },
    )
  }
}

@Composable
private fun ModListRow(mod: PackMod, icon: String?, update: String?, onToggle: (Boolean) -> Unit, onUpdate: () -> Unit, onRemove: () -> Unit, onOpen: () -> Unit) {
  ListItem(
    modifier = Modifier.clickable(onClick = onOpen),
    leadingContent = { ModIcon(icon, 40.dp) },
    headlineContent = { Text(mod.name.ifBlank { mod.id }, maxLines = 1, overflow = TextOverflow.Ellipsis) },
    supportingContent = {
      Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
          Text(mod.version.ifBlank { "?" }, maxLines = 1)
          when {
            mod.isLoader -> Tag("Mod loader", MaterialTheme.colorScheme.primary)
            mod.installedAsDependency -> Tag("Dependency")
          }
          if (mod.source != ModSource.Thunderstore) Tag(mod.source.name)
        }
        if (update != null) {
          Text(
            "Update to $update",
            color = Positive,
            style = MaterialTheme.typography.labelLarge,
            modifier = Modifier.clickable(onClick = onUpdate).padding(vertical = 2.dp),
          )
        }
      }
    },
    trailingContent = {
      Row(verticalAlignment = Alignment.CenterVertically) {
        Switch(checked = mod.enabled, onCheckedChange = onToggle)
        // Every mod needs BepInEx, so it can't be removed here; LaunchHeim would add it again anyway.
        if (mod.isLoader) {
          Spacer(Modifier.size(48.dp))
        } else {
          IconButton(onClick = onRemove) { Icon(Icons.Default.Delete, "Remove ${mod.name}") }
        }
      }
    },
  )
}
