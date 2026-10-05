package local.cine.launchheim.ui

import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp

/** THIRD-PARTY-NOTICES.txt, which the build copies into the APK's assets. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LicensesScreen(onBack: () -> Unit) {
  val context = LocalContext.current
  val text = remember {
    runCatching { context.assets.open("THIRD-PARTY-NOTICES.txt").bufferedReader().use { it.readText() } }.getOrDefault("The notices are missing from this build.")
  }
  Scaffold(
    topBar = {
      TopAppBar(
        title = { Text("Third-party licenses") },
        navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "Back") } },
      )
    },
  ) { padding ->
    Text(
      text,
      style = MaterialTheme.typography.bodySmall,
      modifier = Modifier.fillMaxSize().padding(padding).verticalScroll(rememberScrollState()).padding(16.dp),
    )
  }
}
