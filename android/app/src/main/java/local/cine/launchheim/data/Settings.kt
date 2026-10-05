package local.cine.launchheim.data

import java.io.File
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.serialization.Serializable

@Serializable
data class Settings(
  /** The name other devices show for this phone. Empty means the phone's model name. */
  val alias: String = "",
  /** LocalSend's device fingerprint: random, made once, so others don't list this phone twice. */
  val fingerprint: String = "",
  /** Visible to LaunchHeim and LocalSend while the app is open. */
  val localSendEnabled: Boolean = true,
  val showNsfw: Boolean = false,
  /** For browsing CurseForge, which answers nothing without one. Only in the app's private storage. */
  val curseForgeApiKey: String = "",
)

class SettingsStore(private val file: File, defaultFingerprint: () -> String) {
  private val _settings = MutableStateFlow(
    (readJson(file, Settings.serializer()) ?: Settings()).let { if (it.fingerprint.isEmpty()) it.copy(fingerprint = defaultFingerprint()) else it },
  )

  val settings: StateFlow<Settings> = _settings.asStateFlow()

  init {
    writeJson(file, Settings.serializer(), _settings.value)
  }

  fun update(change: (Settings) -> Settings) {
    _settings.update(change)
    writeJson(file, Settings.serializer(), _settings.value)
  }
}
