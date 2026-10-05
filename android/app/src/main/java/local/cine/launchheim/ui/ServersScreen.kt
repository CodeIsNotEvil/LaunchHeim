package local.cine.launchheim.ui

import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Dns
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.Star
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.repeatOnLifecycle
import kotlinx.coroutines.delay
import local.cine.launchheim.servers.SavedServer
import local.cine.launchheim.ui.theme.Positive

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ServersScreen(vm: ServersViewModel) {
  val servers by vm.servers.collectAsState()
  val statuses by vm.statuses.collectAsState()
  val refreshing by vm.refreshing.collectAsState()
  var editing by remember { mutableStateOf<SavedServer?>(null) }
  var adding by remember { mutableStateOf(false) }
  var deleting by remember { mutableStateOf<SavedServer?>(null) }

  // Every 30 seconds while the page is on screen, as the desktop's Play page does.
  val lifecycle = LocalLifecycleOwner.current.lifecycle
  LaunchedEffect(lifecycle, servers.map { it.address }) {
    lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) {
      while (true) {
        vm.refresh()
        delay(30_000)
      }
    }
  }

  Scaffold(
    topBar = { TopAppBar(title = { Text("Servers") }) },
    floatingActionButton = { FloatingActionButton(onClick = { adding = true }) { Icon(Icons.Default.Add, "Add a server") } },
  ) { padding ->
    PullToRefreshBox(isRefreshing = refreshing, onRefresh = vm::refreshNow, modifier = Modifier.fillMaxSize().padding(padding)) {
      if (servers.isEmpty()) {
        EmptyState(
          Icons.Default.Dns,
          "No servers yet",
          "LaunchHeim sends the dedicated servers from Valheim's Favorites and Recent lists with every sync. You can also add one here by its address.",
          Modifier.padding(top = 48.dp),
        )
      } else {
        LazyColumn(contentPadding = PaddingValues(bottom = 96.dp)) {
          val (fromPc, own) = servers.partition { it.fromPc }
          if (fromPc.isNotEmpty()) {
            item { SectionLabel("From your PC" + (vm.pcSyncedAt?.let { " · ${Format.ago(it)}" } ?: "")) }
            items(fromPc, key = { it.id }) { server -> ServerRow(server, statuses[server.address], null) }
          }
          if (own.isNotEmpty()) {
            item { SectionLabel("Added on this phone") }
            items(own, key = { it.id }) { server ->
              ServerRow(server, statuses[server.address]) { action -> if (action == "edit") editing = server else deleting = server }
            }
          }
        }
      }
    }
  }

  if (adding) {
    FieldsDialog(
      "Add a server",
      listOf("Name" to "", "Address (host:port)" to ""),
      "Add",
      onConfirm = { vm.save(null, it[0], it[1]) },
      onDismiss = { adding = false },
      isValid = { it[1].isNotBlank() },
    )
  }
  editing?.let { server ->
    FieldsDialog(
      "Edit server",
      listOf("Name" to server.name, "Address (host:port)" to server.address),
      "Save",
      onConfirm = { vm.save(server.id, it[0], it[1]) },
      onDismiss = { editing = null },
      isValid = { it[1].isNotBlank() },
    )
  }
  deleting?.let { server ->
    ConfirmDialog("Remove ${server.name}?", "It is only removed from this phone.", "Remove", onConfirm = { vm.delete(server.id) }, onDismiss = { deleting = null })
  }
}

@Composable
private fun ServerRow(server: SavedServer, status: ServersViewModel.Status?, onAction: ((String) -> Unit)?) {
  var menu by remember { mutableStateOf(false) }
  ListItem(
    leadingContent = {
      Icon(if (server.isFavorite) Icons.Default.Star else Icons.Default.Dns, null,
        tint = if (server.isFavorite) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant)
    },
    headlineContent = { Text(server.name, maxLines = 1, overflow = TextOverflow.Ellipsis) },
    supportingContent = {
      val names = (status as? ServersViewModel.Status.Online)?.status?.playerNames.orEmpty()
      Text(if (names.isEmpty()) server.address else "${server.address} · ${names.joinToString()}", maxLines = 2, overflow = TextOverflow.Ellipsis)
    },
    trailingContent = {
      Row(verticalAlignment = Alignment.CenterVertically) {
        when (status) {
          null, ServersViewModel.Status.Checking -> CircularProgressIndicator(Modifier.size(16.dp), strokeWidth = 2.dp)
          ServersViewModel.Status.Offline -> Tag("Offline")
          is ServersViewModel.Status.Online -> Tag("${status.status.players}/${status.status.maxPlayers} online", if (status.status.players > 0) Positive else MaterialTheme.colorScheme.onSurfaceVariant)
        }
        if (onAction != null) {
          IconButton(onClick = { menu = true }) { Icon(Icons.Default.MoreVert, "More") }
          DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
            DropdownMenuItem(text = { Text("Edit…") }, onClick = { menu = false; onAction("edit") })
            DropdownMenuItem(text = { Text("Remove") }, onClick = { menu = false; onAction("delete") })
          }
        }
      }
    },
  )
}
