package local.cine.launchheim

import android.app.Application
import android.content.Context
import android.net.wifi.WifiManager
import android.os.Build
import android.provider.Settings as AndroidSettings
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.ProcessLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import java.io.File
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import local.cine.launchheim.data.InstanceStore
import local.cine.launchheim.data.ServerStore
import local.cine.launchheim.data.SettingsStore
import local.cine.launchheim.data.SyncService
import local.cine.launchheim.localsend.LocalSendNode
import local.cine.launchheim.thunderstore.ThunderstoreIndex
import okhttp3.OkHttpClient

/** Everything the screens share, made once per process. */
class Container(context: Context) {
  val appScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

  val version: String = context.packageManager.getPackageInfo(context.packageName, 0).versionName ?: ""

  val http: OkHttpClient = OkHttpClient.Builder()
    .connectTimeout(15, TimeUnit.SECONDS)
    .readTimeout(60, TimeUnit.SECONDS)
    .addInterceptor { chain ->
      chain.proceed(chain.request().newBuilder().header("User-Agent", "LaunchHeimCompanion/$version (+https://github.com/CodeIsNotEvil/MyMonoRepo)").build())
    }
    .build()

  val settings = SettingsStore(File(context.filesDir, "settings.json"), LocalSendNode::newFingerprint)
  val instances = InstanceStore(File(context.filesDir, "instances"))
  val servers = ServerStore(File(context.filesDir, "servers.json"))
  val index = ThunderstoreIndex(http, File(context.cacheDir, "thunderstore-valheim.json.gz"))
  val sync = SyncService(instances, servers, File(context.cacheDir, "shared"), version)

  /** Short notices for the snackbar, from any screen or from a transfer that just finished. */
  private val _messages = MutableSharedFlow<String>(extraBufferCapacity = 16)
  val messages: SharedFlow<String> = _messages.asSharedFlow()

  fun notify(message: String) {
    _messages.tryEmit(message)
  }

  private val multicastLock: WifiManager.MulticastLock =
    (context.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager)
      .createMulticastLock("LaunchHeim LocalSend")
      .apply { setReferenceCounted(false) }

  val node = LocalSendNode(
    identity = {
      val alias = settings.settings.value.alias.ifBlank { defaultAlias(context) }
      LocalSendNode.Identity(alias, settings.settings.value.fingerprint)
    },
    inbox = File(context.cacheDir, "inbox"),
    accepts = SyncService::accepts,
    setMulticastLock = { on -> if (on) multicastLock.acquire() else if (multicastLock.isHeld) multicastLock.release() },
  )

  init {
    File(context.cacheDir, "inbox").deleteRecursively()

    // LocalSend runs while the app is on screen and switched on in Settings. A phone in a pocket shouldn't
    // keep a port open or hold the multicast lock, which costs battery; and Android would stop a
    // background server anyway.
    appScope.launch {
      ProcessLifecycleOwner.get().lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
        try {
          settings.settings.map { it.localSendEnabled }.distinctUntilChanged().collect { enabled ->
            withContext(Dispatchers.IO) {
              if (enabled) {
                runCatching { node.start() }.onFailure { notify("LocalSend could not start: ${it.message}") }
              } else {
                node.stop()
              }
            }
          }
        } finally {
          withContext(NonCancellable + Dispatchers.IO) { node.stop() }
        }
      }
    }
    appScope.launch {
      node.received.collect { received ->
        for (outcome in sync.handle(received)) {
          notify(describe(outcome, received.sender.alias))
        }
      }
    }
  }

  companion object {
    fun describe(outcome: SyncService.Outcome, from: String?): String = when (outcome) {
      is SyncService.Outcome.Imported -> buildString {
        append(if (outcome.replaced) "Updated " else "Added ").append(outcome.instance.name)
        if (from != null) append(" from ").append(from)
        if (outcome.lostChanges) append(". Your unsent changes to it were replaced")
      }
      is SyncService.Outcome.Servers -> "Got ${outcome.count} server(s)${from?.let { " from $it" } ?: ""}"
      is SyncService.Outcome.Failed -> "${outcome.fileName}: ${outcome.message}"
    }

    /** The phone's name as the user set it, else its model ("Pixel 8"). */
    fun defaultAlias(context: Context): String =
      runCatching { AndroidSettings.Global.getString(context.contentResolver, AndroidSettings.Global.DEVICE_NAME) }.getOrNull()
        ?.takeIf { it.isNotBlank() }
        ?: Build.MODEL
  }
}

class CompanionApp : Application() {
  lateinit var container: Container
    private set

  override fun onCreate() {
    super.onCreate()
    container = Container(this)
  }
}

val Context.container: Container get() = (applicationContext as CompanionApp).container
