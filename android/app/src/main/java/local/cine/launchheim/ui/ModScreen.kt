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
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.automirrored.filled.OpenInNew
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
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import local.cine.launchheim.thunderstore.DependencyString

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ModScreen(vm: BrowseViewModel, fullName: String, onBack: () -> Unit, onMod: (String) -> Unit) {
  val packages by vm.packages.collectAsState()
  val pkg = remember(packages, fullName) { vm.find(fullName) }
  val target by vm.target.collectAsState()
  val inTarget by vm.targetKeys.collectAsState()
  val context = LocalContext.current

  Scaffold(
    topBar = {
      TopAppBar(
        title = { Text(pkg?.displayName ?: fullName, maxLines = 1, overflow = TextOverflow.Ellipsis) },
        navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "Back") } },
        actions = {
          if (pkg != null) {
            IconButton(onClick = { context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(pkg.packageUrl))) }) {
              Icon(Icons.AutoMirrored.Filled.OpenInNew, "Open on Thunderstore")
            }
          }
        },
      )
    },
  ) { padding ->
    if (pkg == null) {
      Box(Modifier.fillMaxSize().padding(padding), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
      return@Scaffold
    }

    var version by remember(pkg) { mutableStateOf(pkg.latest.version) }
    var readme by remember(pkg, version) { mutableStateOf<String?>(null) }
    var loadingReadme by remember(pkg, version) { mutableStateOf(true) }
    LaunchedEffect(pkg, version) {
      readme = vm.readme(pkg, version)
      loadingReadme = false
    }
    val chosen = pkg.find(version) ?: pkg.latest
    val added = "thunderstore:${pkg.fullName}".lowercase() in inTarget

    Column(Modifier.fillMaxSize().padding(padding).verticalScroll(rememberScrollState()).padding(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
      Row(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
        ModIcon(pkg.icon, 72.dp)
        Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
          Text(pkg.displayName, style = MaterialTheme.typography.titleLarge)
          Text("by ${pkg.owner}", color = MaterialTheme.colorScheme.onSurfaceVariant)
          Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Stat(Icons.Default.Download, Format.count(pkg.totalDownloads))
            Stat(Icons.Default.ThumbUp, Format.count(pkg.rating.toLong()))
            Text("updated ${Format.ago(pkg.updated)}", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
          }
        }
      }

      if (pkg.isDeprecated) Tag("Deprecated: the author no longer maintains it", MaterialTheme.colorScheme.error)
      Text(pkg.description)
      if (pkg.categories.isNotEmpty()) {
        Text(pkg.categories.joinToString(" · "), style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
      }

      Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
        VersionPicker(pkg.versions.map { it.version }, version) { version = it }
        Button(onClick = { vm.add(pkg.fullName, version) }, enabled = target != null) {
          Icon(Icons.Default.Add, null)
          Text(if (added) " Use this version" else " Add")
        }
      }
      Text(
        target?.let { "Into ${it.name}" + if (added) ", which already has it" else "" } ?: "Create a mod list first, then add mods to it.",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
      )

      val dependencies = chosen.dependencies.mapNotNull(DependencyString::parse)
      if (dependencies.isNotEmpty()) {
        HorizontalDivider()
        Text("Needs", style = MaterialTheme.typography.titleSmall)
        dependencies.forEach { dependency ->
          OutlinedButton(onClick = { onMod(dependency.fullName) }) {
            Text("${dependency.fullName.substringAfter('-').replace('_', ' ')} ${dependency.version}+", maxLines = 1, overflow = TextOverflow.Ellipsis)
          }
        }
      }

      HorizontalDivider()
      when {
        loadingReadme -> CircularProgressIndicator(Modifier.size(24.dp), strokeWidth = 2.dp)
        readme.isNullOrBlank() -> Text("The README could not be loaded.", color = MaterialTheme.colorScheme.onSurfaceVariant)
        else -> MarkdownText(readme!!)
      }
    }
  }
}

@Composable
private fun VersionPicker(versions: List<String>, selected: String, onPick: (String) -> Unit) {
  var open by remember { mutableStateOf(false) }
  Box {
    OutlinedButton(onClick = { open = true }) { Text(if (selected == versions.firstOrNull()) "$selected (latest)" else selected) }
    DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
      versions.take(50).forEach { v -> DropdownMenuItem(text = { Text(v) }, onClick = { open = false; onPick(v) }) }
    }
  }
}
