package local.cine.launchheim.thunderstore

/** Thunderstore's `Owner-Name-1.2.3` dependency strings, as in LaunchHeim's ThunderstoreIndex.cs. */
data class DependencyString(val fullName: String, val version: String) {
  companion object {
    /**
     * Team and package names may only contain letters, digits and underscores, so the last hyphen always
     * separates the version and the first separates the owner.
     */
    fun parse(value: String): DependencyString? {
      val lastDash = value.lastIndexOf('-')
      if (lastDash <= 0 || lastDash == value.length - 1 || value.indexOf('-') == lastDash) return null
      return DependencyString(value.substring(0, lastDash), value.substring(lastDash + 1))
    }
  }
}

object ModVersion {
  /**
   * Compares dotted versions part by part, treating missing parts as zero, so `2.0` equals `2.0.0`.
   * Anything non-numeric falls back to text.
   */
  fun compare(a: String?, b: String?): Int {
    val left = parts(a)
    val right = parts(b)
    if (left == null || right == null) return (a ?: "").compareTo(b ?: "", ignoreCase = true)

    for (i in 0 until maxOf(left.size, right.size)) {
      val difference = (left.getOrElse(i) { 0 }).compareTo(right.getOrElse(i) { 0 })
      if (difference != 0) return difference
    }
    return 0
  }

  // Nexus and CurseForge authors write "v1.2" as often as "1.2".
  private fun parts(version: String?): List<Long>? {
    val parts = version?.trim()?.trimStart('v', 'V')?.split('.') ?: return null
    return parts.map { it.toLongOrNull() ?: return null }
  }
}
