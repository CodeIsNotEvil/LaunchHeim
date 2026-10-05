package local.cine.launchheim.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Computer
import androidx.compose.material.icons.filled.Extension
import androidx.compose.material.icons.filled.Smartphone
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.ListItem
import androidx.compose.material3.ListItemDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import coil3.compose.AsyncImage
import local.cine.launchheim.localsend.Peer

/** A mod's icon, or a puzzle piece while it loads or when it has none. */
@Composable
fun ModIcon(url: String?, size: Dp = 44.dp) {
  Box(
    Modifier.size(size).clip(RoundedCornerShape(10.dp)).background(MaterialTheme.colorScheme.surfaceContainerHighest),
    contentAlignment = Alignment.Center,
  ) {
    Icon(Icons.Default.Extension, null, tint = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.size(size / 2))
    if (url != null) AsyncImage(url, null, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
  }
}

/** A small coloured label, like the desktop's Badge. */
@Composable
fun Tag(text: String, color: Color = MaterialTheme.colorScheme.onSurfaceVariant) {
  Surface(shape = RoundedCornerShape(6.dp), color = color.copy(alpha = 0.14f)) {
    Text(text, color = color, style = MaterialTheme.typography.labelSmall, maxLines = 1, modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp))
  }
}

@Composable
fun EmptyState(icon: ImageVector, title: String, text: String, modifier: Modifier = Modifier, action: @Composable () -> Unit = {}) {
  Column(
    modifier.fillMaxWidth().padding(32.dp),
    horizontalAlignment = Alignment.CenterHorizontally,
    verticalArrangement = Arrangement.spacedBy(12.dp),
  ) {
    Box(
      Modifier.size(64.dp).clip(RoundedCornerShape(18.dp)).background(MaterialTheme.colorScheme.primary.copy(alpha = 0.14f)),
      contentAlignment = Alignment.Center,
    ) {
      Icon(icon, null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(32.dp))
    }
    Text(title, style = MaterialTheme.typography.titleMedium, textAlign = TextAlign.Center)
    Text(text, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, textAlign = TextAlign.Center)
    action()
  }
}

@Composable
fun SectionLabel(text: String, modifier: Modifier = Modifier) {
  Text(
    text.uppercase(),
    style = MaterialTheme.typography.labelMedium,
    color = MaterialTheme.colorScheme.primary,
    modifier = modifier.padding(start = 16.dp, end = 16.dp, top = 20.dp, bottom = 6.dp),
  )
}

@Composable
fun ConfirmDialog(title: String, text: String, confirm: String, onConfirm: () -> Unit, onDismiss: () -> Unit) {
  AlertDialog(
    onDismissRequest = onDismiss,
    title = { Text(title) },
    text = { Text(text) },
    confirmButton = { TextButton(onClick = { onConfirm(); onDismiss() }) { Text(confirm) } },
    dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
  )
}

/** A dialog with text fields, for names and server addresses. */
@Composable
fun FieldsDialog(
  title: String,
  fields: List<Pair<String, String>>,
  confirm: String,
  onConfirm: (List<String>) -> Unit,
  onDismiss: () -> Unit,
  isValid: (List<String>) -> Boolean = { values -> values.first().isNotBlank() },
) {
  var values by remember { mutableStateOf(fields.map { it.second }) }
  val focus = remember { FocusRequester() }
  LaunchedEffect(Unit) { focus.requestFocus() }
  AlertDialog(
    onDismissRequest = onDismiss,
    title = { Text(title) },
    text = {
      Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        fields.forEachIndexed { i, (label, _) ->
          OutlinedTextField(
            value = values[i],
            onValueChange = { v -> values = values.toMutableList().also { it[i] = v } },
            label = { Text(label) },
            singleLine = true,
            keyboardOptions = KeyboardOptions(imeAction = if (i == fields.lastIndex) ImeAction.Done else ImeAction.Next),
            modifier = Modifier.fillMaxWidth().let { if (i == 0) it.focusRequester(focus) else it },
          )
        }
      }
    },
    confirmButton = {
      TextButton(enabled = isValid(values), onClick = { onConfirm(values); onDismiss() }) { Text(confirm) }
    },
    dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
  )
}

/**
 * Picks the device to send to. LaunchHeim PCs come first; other LocalSend devices (the LocalSend app on
 * a laptop, say) are listed too, since a pack is just a file to them.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DeviceSheet(
  title: String,
  peers: List<Peer>,
  running: Boolean,
  onScan: () -> Unit,
  onPick: (Peer) -> Unit,
  onDismiss: () -> Unit,
) {
  LaunchedEffect(Unit) { onScan() }
  ModalBottomSheet(onDismissRequest = onDismiss) {
    Column(Modifier.padding(bottom = 24.dp)) {
      Row(Modifier.fillMaxWidth().padding(horizontal = 24.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(title, style = MaterialTheme.typography.titleLarge, modifier = Modifier.weight(1f))
        TextButton(onClick = onScan, enabled = running) { Text("Search again") }
      }
      Spacer(Modifier.height(8.dp))
      when {
        !running -> Text(
          "Phone sync is off. Turn it on in Settings to find your PC.",
          color = MaterialTheme.colorScheme.onSurfaceVariant,
          modifier = Modifier.padding(24.dp),
        )
        peers.isEmpty() -> Row(Modifier.padding(24.dp), verticalAlignment = Alignment.CenterVertically) {
          CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
          Spacer(Modifier.size(16.dp))
          Text(
            "Looking for LaunchHeim on your network. On the PC, turn on Settings → Phone sync, and keep both on the same Wi-Fi.",
            color = MaterialTheme.colorScheme.onSurfaceVariant,
          )
        }
        else -> LazyColumn {
          items(peers, key = { it.info.fingerprint + it.address }) { peer ->
            ListItem(
              headlineContent = { Text(peer.info.alias, maxLines = 1, overflow = TextOverflow.Ellipsis) },
              supportingContent = { Text(if (peer.info.isLaunchHeim) "LaunchHeim · ${peer.address}" else "${peer.info.deviceModel ?: "LocalSend"} · ${peer.address}") },
              leadingContent = {
                Icon(if (peer.info.deviceType == "mobile") Icons.Default.Smartphone else Icons.Default.Computer, null,
                  tint = if (peer.info.isLaunchHeim) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant)
              },
              colors = ListItemDefaults.colors(containerColor = Color.Transparent),
              modifier = Modifier.clickable { onPick(peer) },
            )
          }
        }
      }
    }
  }
}
