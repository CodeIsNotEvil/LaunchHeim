package local.cine.launchheim.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Computer
import androidx.compose.material.icons.filled.Smartphone
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(vm: MainViewModel, onLicenses: () -> Unit) {
  val settings by vm.settings.collectAsState()
  val running by vm.nodeRunning.collectAsState()
  val peers by vm.peers.collectAsState()
  val indexUpdated by vm.indexUpdated.collectAsState()
  val indexBusy by vm.indexBusy.collectAsState()
  var renaming by remember { mutableStateOf(false) }
  var editingKey by remember { mutableStateOf(false) }

  Scaffold(topBar = { TopAppBar(title = { Text("Settings") }) }) { padding ->
    Column(Modifier.fillMaxSize().padding(padding).verticalScroll(rememberScrollState())) {
      SectionLabel("Phone sync")
      Text(
        "LaunchHeim and this app find each other on your Wi-Fi with LocalSend's protocol, so mod lists " +
          "travel straight between them. The LocalSend app sees this phone too. Only while the app is open.",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        modifier = Modifier.padding(horizontal = 16.dp),
      )
      ListItem(
        headlineContent = { Text("Visible on the network") },
        supportingContent = { Text(if (running) "Port ${vm.container.node.port}" else "Off: the PC can't send to this phone") },
        trailingContent = { Switch(checked = settings.localSendEnabled, onCheckedChange = { on -> vm.updateSettings { it.copy(localSendEnabled = on) } }) },
      )
      ListItem(
        headlineContent = { Text("Device name") },
        supportingContent = { Text(vm.alias()) },
        modifier = Modifier.clickable { renaming = true },
      )
      ListItem(
        headlineContent = { Text("Nearby devices") },
        supportingContent = { Text(if (!running) "—" else if (peers.isEmpty()) "None found yet" else "${peers.size} found") },
        trailingContent = { TextButton(onClick = vm::scan, enabled = running) { Text("Search") } },
      )
      peers.forEach { peer ->
        ListItem(
          leadingContent = { Icon(if (peer.info.deviceType == "mobile") Icons.Default.Smartphone else Icons.Default.Computer, null) },
          headlineContent = { Text(peer.info.alias) },
          supportingContent = { Text((if (peer.info.isLaunchHeim) "LaunchHeim" else peer.info.deviceModel ?: "LocalSend") + " · ${peer.address}:${peer.port}") },
          modifier = Modifier.padding(start = 16.dp),
        )
      }

      HorizontalDivider(Modifier.padding(top = 8.dp))
      SectionLabel("Mods")
      ListItem(
        headlineContent = { Text("Show mods marked as adult content") },
        trailingContent = { Switch(checked = settings.showNsfw, onCheckedChange = { on -> vm.updateSettings { it.copy(showNsfw = on) } }) },
      )
      ListItem(
        headlineContent = { Text("CurseForge API key") },
        supportingContent = {
          Text(
            if (settings.curseForgeApiKey.isBlank()) "Not set. CurseForge answers no app without one; create a free key at console.curseforge.com."
            else "Set. Nexus Mods needs none for browsing.",
          )
        },
        modifier = Modifier.clickable { editingKey = true },
      )
      ListItem(
        headlineContent = { Text("Thunderstore list") },
        supportingContent = { Text(if (indexBusy) "Downloading…" else "Updated ${Format.ago(indexUpdated)}. Refreshed every six hours.") },
        trailingContent = { TextButton(onClick = vm::refreshIndex, enabled = !indexBusy) { Text("Refresh") } },
      )

      HorizontalDivider(Modifier.padding(top = 8.dp))
      SectionLabel("About")
      Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Text("LaunchHeim Companion ${vm.container.version}")
        Text("MIT, Copyright (c) 2026 CodeIsNotEvil", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text(
          "Mods and their data come from Thunderstore. Not affiliated with Iron Gate or Thunderstore.",
          style = MaterialTheme.typography.bodySmall,
          color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
      }
      ListItem(headlineContent = { Text("Third-party licenses") }, modifier = Modifier.clickable(onClick = onLicenses))
    }
  }

  if (editingKey) {
    FieldsDialog(
      "CurseForge API key",
      listOf("Key from console.curseforge.com" to settings.curseForgeApiKey),
      "Save",
      onConfirm = { values -> vm.updateSettings { it.copy(curseForgeApiKey = values[0].trim()) } },
      onDismiss = { editingKey = false },
      isValid = { true },
    )
  }
  if (renaming) {
    FieldsDialog(
      "Device name",
      listOf("Shown on your PC and in LocalSend" to vm.alias()),
      "Save",
      onConfirm = { vm.setAlias(it[0]) },
      onDismiss = { renaming = false },
      isValid = { true },
    )
  }
}
