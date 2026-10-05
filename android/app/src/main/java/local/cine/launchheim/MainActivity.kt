package local.cine.launchheim

import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Dns
import androidx.compose.material.icons.filled.Inventory2
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.NavGraph.Companion.findStartDestination
import androidx.navigation.NavHostController
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch
import local.cine.launchheim.localsend.IncomingOffer
import local.cine.launchheim.servers.ServerListFile
import local.cine.launchheim.ui.BrowseScreen
import local.cine.launchheim.ui.BrowseViewModel
import local.cine.launchheim.ui.InstanceScreen
import local.cine.launchheim.ui.InstancesScreen
import local.cine.launchheim.ui.LicensesScreen
import local.cine.launchheim.ui.MainViewModel
import local.cine.launchheim.ui.ModScreen
import local.cine.launchheim.ui.RemoteModScreen
import local.cine.launchheim.packs.ModSource
import local.cine.launchheim.ui.ServersScreen
import local.cine.launchheim.ui.ServersViewModel
import local.cine.launchheim.ui.SettingsScreen
import local.cine.launchheim.ui.theme.LaunchHeimTheme

class MainActivity : ComponentActivity() {
  /** A pack handed over by another app, waiting for the UI to open it. */
  private val pendingUri = MutableStateFlow<Uri?>(null)

  override fun onCreate(savedInstanceState: Bundle?) {
    enableEdgeToEdge()
    super.onCreate(savedInstanceState)
    if (savedInstanceState == null) take(intent)
    setContent { LaunchHeimTheme { App(pendingUri) } }
  }

  override fun onNewIntent(intent: Intent) {
    super.onNewIntent(intent)
    take(intent)
  }

  private fun take(intent: Intent?) {
    pendingUri.value = when (intent?.action) {
      Intent.ACTION_VIEW -> intent.data
      Intent.ACTION_SEND -> if (Build.VERSION.SDK_INT >= 33) {
        intent.getParcelableExtra(Intent.EXTRA_STREAM, Uri::class.java)
      } else {
        @Suppress("DEPRECATION")
        intent.getParcelableExtra(Intent.EXTRA_STREAM)
      }
      else -> null
    } ?: pendingUri.value
  }
}

private data class Tab(val route: String, val label: String, val icon: ImageVector)

private val tabs = listOf(
  Tab("instances", "Mod lists", Icons.Default.Inventory2),
  Tab("browse", "Browse", Icons.Default.Search),
  Tab("servers", "Servers", Icons.Default.Dns),
  Tab("settings", "Settings", Icons.Default.Settings),
)

@Composable
private fun App(pendingUri: MutableStateFlow<Uri?>) {
  val nav = rememberNavController()
  val main: MainViewModel = viewModel()
  val browse: BrowseViewModel = viewModel()
  val servers: ServersViewModel = viewModel()
  val snackbar = remember { SnackbarHostState() }
  val scope = rememberCoroutineScope()
  val incoming by main.incoming.collectAsState()
  val uri by pendingUri.collectAsState()

  LaunchedEffect(Unit) { main.container.messages.collect { snackbar.showSnackbar(it) } }
  LaunchedEffect(uri) {
    val handed = uri ?: return@LaunchedEffect
    pendingUri.value = null
    main.open(handed)?.let { id -> nav.navigate("instance/$id") }
  }

  val openFile = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { picked ->
    if (picked != null) scope.launch { main.open(picked)?.let { id -> nav.navigate("instance/$id") } }
  }

  val backStack by nav.currentBackStackEntryAsState()
  val route = backStack?.destination?.route
  val topLevel = tabs.any { route?.startsWith(it.route) == true && !route.startsWith("instance/") }

  Scaffold(
    snackbarHost = { SnackbarHost(snackbar) },
    bottomBar = {
      if (topLevel) {
        NavigationBar {
          tabs.forEach { tab ->
            NavigationBarItem(
              selected = route?.startsWith(tab.route) == true,
              onClick = { nav.switchTo(tab.route) },
              icon = { Icon(tab.icon, null) },
              label = { Text(tab.label) },
            )
          }
        }
      }
    },
  ) { padding ->
    // Each screen has its own top bar, so only the bottom bar's space is taken here.
    Column(Modifier.padding(bottom = padding.calculateBottomPadding())) {
      NavHost(nav, startDestination = "instances") {
        composable("instances") {
          InstancesScreen(main, onOpen = { nav.navigate("instance/$it") }, onOpenFile = { openFile.launch(arrayOf("*/*")) })
        }
        composable("instance/{id}", listOf(navArgument("id") { type = NavType.StringType })) { entry ->
          InstanceScreen(
            main,
            entry.arguments?.getString("id").orEmpty(),
            onBack = { nav.popBackStack() },
            onBrowse = { id -> nav.switchTo("browse?target=$id") },
            onOpenList = { copy -> nav.navigate("instance/$copy") { popUpTo("instances") } },
            onMod = { mod ->
              when (mod.source) {
                ModSource.Thunderstore -> nav.navigate("mod/${mod.id}")
                ModSource.Nexus, ModSource.CurseForge -> nav.navigate("remote/${mod.source.name}/${mod.id}")
                ModSource.Local -> Unit
              }
            },
          )
        }
        composable("browse?target={target}", listOf(navArgument("target") { type = NavType.StringType; nullable = true })) { entry ->
          BrowseScreen(
            browse,
            entry.arguments?.getString("target"),
            onMod = { nav.navigate("mod/$it") },
            onRemoteMod = { source, id -> nav.navigate("remote/${source.name}/$id") },
          )
        }
        composable("mod/{name}", listOf(navArgument("name") { type = NavType.StringType })) { entry ->
          ModScreen(browse, entry.arguments?.getString("name").orEmpty(), onBack = { nav.popBackStack() }, onMod = { nav.navigate("mod/$it") })
        }
        composable("remote/{source}/{id}", listOf(navArgument("source") { type = NavType.StringType }, navArgument("id") { type = NavType.StringType })) { entry ->
          val source = runCatching { ModSource.valueOf(entry.arguments?.getString("source").orEmpty()) }.getOrDefault(ModSource.Nexus)
          RemoteModScreen(browse, source, entry.arguments?.getString("id").orEmpty(), onBack = { nav.popBackStack() })
        }
        composable("servers") { ServersScreen(servers) }
        composable("settings") { SettingsScreen(main, onLicenses = { nav.navigate("licenses") }) }
        composable("licenses") { LicensesScreen(onBack = { nav.popBackStack() }) }
      }
    }
  }

  incoming?.let { IncomingDialog(it) }
}

/** Bottom-bar navigation: one copy of each tab, with its state kept when switching back. */
private fun NavHostController.switchTo(route: String) = navigate(route) {
  popUpTo(graph.findStartDestination().id) { saveState = true }
  launchSingleTop = true
  restoreState = true
}

/** Another device wants to send files; LocalSend waits for this answer. */
@Composable
private fun IncomingDialog(offer: IncomingOffer) {
  AlertDialog(
    onDismissRequest = { offer.decline() },
    title = { Text("${offer.sender.alias} wants to send") },
    text = {
      Text(
        offer.files.joinToString("\n") { "• " + if (it.fileName == ServerListFile.FILE_NAME) "The server list" else it.fileName.removeSuffix(".r2z") } +
          "\n\nMod lists that came from the same LaunchHeim instance are replaced.",
      )
    },
    confirmButton = { TextButton(onClick = { offer.accept() }) { Text("Accept") } },
    dismissButton = { TextButton(onClick = { offer.decline() }) { Text("Decline") } },
  )
}
