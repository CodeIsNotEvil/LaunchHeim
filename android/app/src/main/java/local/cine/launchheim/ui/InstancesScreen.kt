package local.cine.launchheim.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.FileOpen
import androidx.compose.material.icons.filled.Inventory2
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import local.cine.launchheim.data.PhoneInstance

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun InstancesScreen(vm: MainViewModel, onOpen: (String) -> Unit, onOpenFile: () -> Unit) {
  val instances by vm.instances.collectAsState()
  val updates by vm.updates.collectAsState()
  // Icons come from the Thunderstore list for packs that don't carry them, so the cards follow it too.
  val index by vm.container.index.packages.collectAsState()
  var creating by remember { mutableStateOf(false) }

  Scaffold(
    topBar = {
      TopAppBar(
        title = { Text("Mod lists") },
        actions = { IconButton(onClick = onOpenFile) { Icon(Icons.Default.FileOpen, "Open a modpack file") } },
      )
    },
    floatingActionButton = {
      ExtendedFloatingActionButton(onClick = { creating = true }, icon = { Icon(Icons.Default.Add, null) }, text = { Text("New list") })
    },
  ) { padding ->
    if (instances.isEmpty()) {
      EmptyState(
        Icons.Default.Inventory2,
        "No mod lists yet",
        "Send an instance from LaunchHeim on the PC (the phone button on its page), open a .r2z modpack, or start a new list and add mods from Browse.",
        Modifier.padding(padding).padding(top = 48.dp),
      )
    } else {
      LazyColumn(
        Modifier.fillMaxSize().padding(padding),
        contentPadding = PaddingValues(16.dp, 8.dp, 16.dp, 96.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
      ) {
        items(instances, key = { it.id }) { instance ->
          val mods = instance.manifest.mods
          val icon = remember(instance, index) { (mods.firstOrNull { !it.isLoader && !it.installedAsDependency } ?: mods.firstOrNull())?.let(vm::iconOf) }
          InstanceCard(instance, updates[instance.id]?.size ?: 0, icon) { onOpen(instance.id) }
        }
      }
    }
  }

  if (creating) {
    FieldsDialog("New mod list", listOf("Name" to ""), "Create", onConfirm = { onOpen(vm.create(it[0])) }, onDismiss = { creating = false })
  }
}

@Composable
private fun InstanceCard(instance: PhoneInstance, updateCount: Int, icon: String?, onClick: () -> Unit) {
  val mods = instance.manifest.mods
  Card(
    Modifier.fillMaxWidth().clickable(onClick = onClick),
    colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainer),
  ) {
    Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
      // The first picked mod's icon says more about a list than its name.
      ModIcon(icon, 52.dp)
      Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Text(instance.name, style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
        Text(
          "${Format.mods(mods.count { !it.isLoader })} · " + (instance.origin?.let { "from $it" } ?: "made on this phone"),
          style = MaterialTheme.typography.bodySmall,
          color = MaterialTheme.colorScheme.onSurfaceVariant,
          maxLines = 1,
          overflow = TextOverflow.Ellipsis,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
          if (instance.changedSinceSync) Tag("Not sent", MaterialTheme.colorScheme.primary)
          if (updateCount > 0) Tag("$updateCount update${if (updateCount == 1) "" else "s"}", local.cine.launchheim.ui.theme.Positive)
          if (instance.fromR2modman) Tag("r2modman")
        }
      }
    }
  }
}
