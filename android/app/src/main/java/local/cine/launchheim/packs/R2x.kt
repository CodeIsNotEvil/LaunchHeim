package local.cine.launchheim.packs

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonPrimitive

/**
 * r2modman's profile list, `export.r2x`, ported from LaunchHeim's Core/Packs/R2x.cs. It's a fixed YAML
 * shape, so a line reader is enough and no YAML library has to ship:
 *
 * ```
 * profileName: Survival
 * mods:
 *   - name: denikson-BepInExPack_Valheim
 *     version:
 *       major: 5
 *       minor: 4
 *       patch: 2202
 *     enabled: true
 * ```
 */
object R2x {
  const val ENTRY_NAME = "export.r2x"

  data class Mod(val fullName: String, val version: String, val enabled: Boolean)

  data class Profile(val name: String, val mods: List<Mod>)

  /** Lists the Thunderstore mods; the others only fit in `launchheim.json`. */
  fun write(profileName: String, mods: List<PackMod>): String {
    val entries = mods
      .filter { it.source == ModSource.Thunderstore }
      .mapNotNull { mod -> semVer(mod.version)?.let { mod to it } }

    return buildString {
      // A JSON string is a valid YAML double-quoted scalar, escapes included.
      append("profileName: ").append(JsonPrimitive(profileName).toString()).append('\n')
      if (entries.isEmpty()) {
        append("mods: []\n")
        return@buildString
      }

      append("mods:\n")
      for ((mod, version) in entries) {
        append("  - name: ").append(mod.id).append('\n')
        append("    version:\n")
        append("      major: ").append(version[0]).append('\n')
        append("      minor: ").append(version[1]).append('\n')
        append("      patch: ").append(version[2]).append('\n')
        append("    enabled: ").append(if (mod.enabled) "true" else "false").append('\n')
      }
    }
  }

  fun read(text: String): Profile {
    var name = ""
    val mods = mutableListOf<Mod>()
    var fullName: String? = null
    var major = 0
    var minor = 0
    var patch = 0
    var enabled = true

    fun flush() {
      if (!fullName.isNullOrBlank()) {
        mods += Mod(fullName!!, "$major.$minor.$patch", enabled)
      }
      fullName = null
      major = 0
      minor = 0
      patch = 0
      enabled = true
    }

    for (raw in text.split('\n')) {
      val line = raw.trimEnd('\r', ' ', '\t')
      var content = line.trimStart()
      if (content.isEmpty() || content.startsWith('#')) continue

      if (content.startsWith("- ")) {
        flush()
        content = content.substring(2).trimStart()
      }

      val colon = content.indexOf(':')
      if (colon <= 0) continue

      val key = content.substring(0, colon).trim()
      val value = unquote(content.substring(colon + 1).trim())
      when (key) {
        "profileName" -> if (line.length == content.length) name = value
        "name" -> fullName = value
        "major" -> major = value.toIntOrNull() ?: 0
        "minor" -> minor = value.toIntOrNull() ?: 0
        "patch" -> patch = value.toIntOrNull() ?: 0
        "enabled" -> enabled = !value.equals("false", ignoreCase = true)
      }
    }

    flush()
    return Profile(name, mods)
  }

  private fun semVer(version: String): List<Int>? {
    val parts = version.split('.')
    return if (parts.size == 3 && parts.all { p -> p.isNotEmpty() && p.all(Char::isDigit) }) parts.map(String::toInt) else null
  }

  private fun unquote(value: String): String {
    if (value.length >= 2 && value.first() == '"' && value.last() == '"') {
      return runCatching { Json.decodeFromString<String>(value) }.getOrElse { value.substring(1, value.length - 1) }
    }

    return if (value.length >= 2 && value.first() == '\'' && value.last() == '\'') {
      value.substring(1, value.length - 1).replace("''", "'")
    } else {
      value
    }
  }
}
