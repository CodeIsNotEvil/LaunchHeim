package local.cine.launchheim.packs

import java.io.File
import java.io.InputStream
import java.io.OutputStream
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream

/** A pack that couldn't be read: not a zip, or neither `launchheim.json` nor `export.r2x` inside. */
class PackException(message: String, cause: Throwable? = null) : Exception(message, cause)

/**
 * Reads and writes `.r2z` modpacks, the zip LaunchHeim's PackService and r2modman both use:
 * `launchheim.json`, `export.r2x`, the configs under `BepInEx/config/` and the files of local mods.
 */
object PackFile {
  const val MANIFEST_ENTRY = "launchheim.json"
  const val EXTENSION = ".r2z"

  // The two lists are small. Anything larger is not a pack, and reading it whole would only cost memory.
  private const val MAX_LIST_BYTES = 4 * 1024 * 1024

  data class Contents(val manifest: PackManifest, val fromR2modman: Boolean, val configFiles: Int)

  /**
   * Reads the mod list. A pack without `launchheim.json` is r2modman's, and only has Thunderstore mods,
   * all of them treated as picked by the user because r2modman doesn't say which ones were dependencies.
   */
  fun read(input: InputStream, fallbackName: String): Contents {
    var manifestText: String? = null
    var r2xText: String? = null
    var configFiles = 0

    try {
      ZipInputStream(input).use { zip ->
        while (true) {
          val entry = zip.nextEntry ?: break
          val name = entry.name.replace('\\', '/')
          when {
            entry.isDirectory -> Unit
            name.equals(MANIFEST_ENTRY, ignoreCase = true) -> manifestText = readText(zip, name)
            name.equals(R2x.ENTRY_NAME, ignoreCase = true) -> r2xText = readText(zip, name)
            name.startsWith("BepInEx/config/", ignoreCase = true) || name.startsWith("config/", ignoreCase = true) -> configFiles++
          }
        }
      }
    } catch (e: java.util.zip.ZipException) {
      throw PackException("This file is not a modpack: it isn't a zip.", e)
    }

    manifestText?.let { text ->
      val manifest = try {
        PackJson.decodeFromString<PackManifest>(text)
      } catch (e: Exception) {
        throw PackException("The pack's $MANIFEST_ENTRY is damaged: ${e.message}", e)
      }
      if (manifest.format > PackManifest.CURRENT_FORMAT) {
        throw PackException("This pack was made by a newer LaunchHeim. Update the app to open it.")
      }
      return Contents(manifest.copy(name = manifest.name.ifBlank { fallbackName }), fromR2modman = false, configFiles)
    }

    val r2x = r2xText ?: throw PackException("This file is not a modpack: it has neither $MANIFEST_ENTRY nor r2modman's ${R2x.ENTRY_NAME}.")
    val profile = R2x.read(r2x)
    val mods = profile.mods.map { mod ->
      PackMod(
        source = ModSource.Thunderstore,
        id = mod.fullName,
        name = mod.fullName.substringAfter('-').replace('_', ' '),
        author = mod.fullName.substringBefore('-'),
        version = mod.version,
        enabled = mod.enabled,
      )
    }
    return Contents(PackManifest(name = profile.name.ifBlank { fallbackName }, mods = mods), fromR2modman = true, configFiles)
  }

  fun read(file: File): Contents = file.inputStream().buffered().use { read(it, file.nameWithoutExtension) }

  /**
   * Writes [manifest] as a pack. Everything else in [original] (configs, local mod files) is copied over
   * unchanged, so a pack that came from the desktop goes back with its configs. The phone can't edit
   * them, and LaunchHeim doesn't copy a returning pack's configs over the instance's anyway.
   */
  fun write(manifest: PackManifest, original: File?, output: OutputStream) {
    ZipOutputStream(output.buffered()).use { zip ->
      zip.putNextEntry(ZipEntry(MANIFEST_ENTRY))
      zip.write(PackJson.encodeToString(PackManifest.serializer(), manifest).toByteArray())
      zip.closeEntry()

      zip.putNextEntry(ZipEntry(R2x.ENTRY_NAME))
      zip.write(R2x.write(manifest.name, manifest.mods).toByteArray())
      zip.closeEntry()

      if (original == null || !original.exists()) return@use

      ZipInputStream(original.inputStream().buffered()).use { source ->
        while (true) {
          val entry = source.nextEntry ?: break
          val name = entry.name.replace('\\', '/')
          if (entry.isDirectory || name.equals(MANIFEST_ENTRY, ignoreCase = true) || name.equals(R2x.ENTRY_NAME, ignoreCase = true)) {
            continue
          }

          zip.putNextEntry(ZipEntry(name).apply { if (entry.time >= 0) time = entry.time })
          source.copyTo(zip)
          zip.closeEntry()
        }
      }
    }
  }

  private fun readText(zip: ZipInputStream, name: String): String {
    val bytes = zip.readNBytesCompat(MAX_LIST_BYTES + 1)
    if (bytes.size > MAX_LIST_BYTES) throw PackException("$name is too large for a mod list.")
    // Thunderstore tooling often saves JSON with a BOM.
    return String(bytes, Charsets.UTF_8).removePrefix("﻿")
  }

  // InputStream.readNBytes is Android 13+.
  private fun InputStream.readNBytesCompat(limit: Int): ByteArray {
    val out = java.io.ByteArrayOutputStream()
    val buffer = ByteArray(16 * 1024)
    while (out.size() < limit) {
      val read = read(buffer, 0, minOf(buffer.size, limit - out.size()))
      if (read < 0) break
      out.write(buffer, 0, read)
    }
    return out.toByteArray()
  }
}
