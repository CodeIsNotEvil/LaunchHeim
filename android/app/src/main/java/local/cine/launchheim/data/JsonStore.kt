package local.cine.launchheim.data

import java.io.File
import kotlinx.serialization.KSerializer
import kotlinx.serialization.json.Json

internal val StoreJson = Json {
  ignoreUnknownKeys = true
  encodeDefaults = true
  prettyPrint = true
}

/** Reads a JSON file, or null when it's missing or damaged (a damaged file must not stop the app). */
internal fun <T> readJson(file: File, serializer: KSerializer<T>): T? =
  if (!file.exists()) null else runCatching { StoreJson.decodeFromString(serializer, file.readText()) }.getOrNull()

/** Writes to a temporary file and renames it over the target, so a crash never leaves half a file. */
internal fun <T> writeJson(file: File, serializer: KSerializer<T>, value: T) {
  file.parentFile?.mkdirs()
  val temp = File(file.path + ".tmp")
  temp.writeText(StoreJson.encodeToString(serializer, value))
  if (!temp.renameTo(file)) {
    file.delete()
    temp.renameTo(file)
  }
}
