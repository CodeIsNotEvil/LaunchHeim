package local.cine.launchheim.ui

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.OpenInNew
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.ThumbUp
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.fromHtml
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import local.cine.launchheim.catalogs.RemoteDetails
import local.cine.launchheim.catalogs.RemoteFile
import local.cine.launchheim.packs.ModSource
import local.cine.launchheim.packs.PackMod

/** A Nexus or CurseForge mod: its files, its description, and Add with the file picked. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun RemoteModScreen(vm: BrowseViewModel, source: ModSource, id: String, onBack: () -> Unit) {
  val context = LocalContext.current
  val target by vm.target.collectAsState()
  val inTarget by vm.targetKeys.collectAsState()
  var details by remember(source, id) { mutableStateOf<RemoteDetails?>(null) }
  var loading by remember(source, id) { mutableStateOf(true) }
  LaunchedEffect(source, id) {
    details = vm.remoteDetails(source, id)
    loading = false
  }

  Scaffold(
    topBar = {
      TopAppBar(
        title = { Text(details?.mod?.name ?: source.label, maxLines = 1, overflow = TextOverflow.Ellipsis) },
        navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "Back") } },
        actions = {
          details?.let { d ->
            IconButton(onClick = { context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(d.mod.websiteUrl))) }) {
              Icon(Icons.AutoMirrored.Filled.OpenInNew, "Open on ${source.label}")
            }
          }
        },
      )
    },
  ) { padding ->
    val d = details
    if (d == null) {
      Box(Modifier.fillMaxSize().padding(padding), contentAlignment = Alignment.Center) {
        if (loading) CircularProgressIndicator() else Text("The mod could not be loaded.", color = MaterialTheme.colorScheme.onSurfaceVariant)
      }
      return@Scaffold
    }

    val mod = d.mod
    var file by remember(d) { mutableStateOf(d.files.firstOrNull { it.recommended } ?: d.files.firstOrNull()) }
    val added = PackMod.makeKey(source, mod.id).lowercase() in inTarget
    // Images and author styling are left out; the text is what helps decide.
    val description = remember(d) {
      AnnotatedString.fromHtml(d.descriptionHtml.replace(Regex("<img[^>]*>", RegexOption.IGNORE_CASE), ""))
    }

    Column(
      Modifier.fillMaxSize().padding(padding).verticalScroll(rememberScrollState()).padding(16.dp),
      verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
      Row(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
        ModIcon(mod.icon, 72.dp)
        Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
          Text(mod.name, style = MaterialTheme.typography.titleLarge)
          Text("by ${mod.author} · ${source.label}", color = MaterialTheme.colorScheme.onSurfaceVariant)
          Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Stat(Icons.Default.Download, Format.count(mod.downloads))
            Stat(Icons.Default.ThumbUp, Format.count(mod.likes))
            if (mod.updated > 0) Text("updated ${Format.ago(mod.updated)}", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
          }
        }
      }
      Text(mod.summary)

      if (d.files.isEmpty()) {
        Text("This mod has no files that can be added.", color = MaterialTheme.colorScheme.error)
      } else {
        FilePicker(d.files, file) { file = it }
        Button(onClick = { vm.addRemote(mod, file) }, enabled = target != null && file != null) {
          Icon(Icons.Default.Add, null)
          Text(if (added) " Set file" else " Add")
        }
      }
      Text(
        target?.let { "Into ${it.name}" + if (added) ", which already has it" else "" } ?: "Create a mod list first, then add mods to it.",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
      )
      // What happens on the PC differs by site, and the phone can't change it: say so before adding.
      Text(
        when (source) {
          ModSource.Nexus -> "LaunchHeim downloads it with the Nexus API key in its settings. Without Premium it lists the mod after the update, and you install it from Browse mods there with one click on the Nexus page. Nexus doesn't list dependencies, so check the description for what it needs."
          else -> "Its required dependencies are added too. A few authors don't allow downloads outside CurseForge; LaunchHeim then opens the page to download it there."
        },
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
      )

      HorizontalDivider()
      Text(description, style = MaterialTheme.typography.bodyMedium)
    }
  }
}

@Composable
private fun FilePicker(files: List<RemoteFile>, selected: RemoteFile?, onPick: (RemoteFile) -> Unit) {
  var open by remember { mutableStateOf(false) }
  Box {
    OutlinedButton(onClick = { open = true }) {
      Text(selected?.let { "${it.name} · ${it.version} (${it.category})" } ?: "Pick a file", maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
    DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
      files.take(50).forEach { file ->
        DropdownMenuItem(
          text = {
            Column {
              Text(file.name, maxLines = 1, overflow = TextOverflow.Ellipsis)
              Text(
                listOfNotNull(file.version, file.category, Format.bytes(file.size).ifEmpty { null }, if (file.recommended) "recommended" else null).joinToString(" · "),
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
              )
            }
          },
          onClick = { open = false; onPick(file) },
        )
      }
    }
  }
}
