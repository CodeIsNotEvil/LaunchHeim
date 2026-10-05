package local.cine.launchheim.ui

import java.text.DateFormat
import java.util.Date
import java.util.Locale

/** The desktop's Format helpers (ViewModels/ViewModel.cs), so numbers read the same on both. */
object Format {
  fun count(value: Long): String = when {
    value >= 10_000_000 -> String.format(Locale.ROOT, "%.0fM", value / 1_000_000.0)
    value >= 1_000_000 -> String.format(Locale.ROOT, "%.1fM", value / 1_000_000.0).replace(".0M", "M")
    value >= 10_000 -> String.format(Locale.ROOT, "%.0fk", value / 1_000.0)
    value >= 1_000 -> String.format(Locale.ROOT, "%.1fk", value / 1_000.0).replace(".0k", "k")
    else -> value.toString()
  }

  fun bytes(bytes: Long): String = when {
    bytes <= 0 -> ""
    bytes >= 1024 * 1024 -> String.format(Locale.ROOT, "%.1f MB", bytes / 1024.0 / 1024.0)
    bytes >= 1024 -> String.format(Locale.ROOT, "%.0f KB", bytes / 1024.0)
    else -> "$bytes B"
  }

  fun ago(epochMs: Long?): String {
    if (epochMs == null || epochMs <= 0) return "never"
    val minutes = (System.currentTimeMillis() - epochMs) / 60_000
    return when {
      minutes < 1 -> "just now"
      minutes < 60 -> "$minutes min ago"
      minutes < 60 * 24 -> "${minutes / 60} h ago"
      minutes < 60 * 48 -> "yesterday"
      minutes < 60 * 24 * 30 -> "${minutes / 60 / 24} days ago"
      minutes < 60 * 24 * 365 -> "${minutes / 60 / 24 / 30} months ago"
      else -> date(epochMs)
    }
  }

  fun date(epochMs: Long): String = DateFormat.getDateInstance(DateFormat.MEDIUM).format(Date(epochMs))

  fun mods(count: Int) = if (count == 1) "1 mod" else "$count mods"
}
