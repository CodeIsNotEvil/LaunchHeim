package local.cine.launchheim.thunderstore

enum class ModSort(val label: String) {
  Relevance("Relevance"),
  Popular("Most liked"),
  Downloads("Downloads"),
  Updated("Last updated"),
  Newest("Newest"),
  Name("Name"),
}

/** Search and ordering, the same as ThunderstoreCatalog.Search on the desktop so both find the same mods. */
object ThunderstoreSearch {
  fun search(packages: Collection<ThunderstorePackage>, text: String, sort: ModSort, includeNsfw: Boolean): List<ThunderstorePackage> {
    val terms = text.split(' ').map(String::trim).filter(String::isNotEmpty)
    val candidates = packages.asSequence().filter { includeNsfw || !it.isNsfw }
    val scored = if (terms.isEmpty()) {
      candidates.map { it to 0 }
    } else {
      candidates.map { it to score(it, terms) }.filter { it.second > 0 }
    }

    val sorted = when {
      sort == ModSort.Relevance && terms.isNotEmpty() ->
        scored.sortedWith(compareByDescending<Pair<ThunderstorePackage, Int>> { it.second }.thenByDescending { it.first.totalDownloads })
      sort == ModSort.Popular -> scored.sortedByDescending { it.first.rating }
      sort == ModSort.Updated -> scored.sortedByDescending { it.first.updated }
      sort == ModSort.Newest -> scored.sortedByDescending { it.first.created }
      sort == ModSort.Name -> scored.sortedWith(compareBy(String.CASE_INSENSITIVE_ORDER) { it.first.name })
      else -> scored.sortedByDescending { it.first.totalDownloads }
    }

    // Like the website: deprecated packages sink to the bottom, and without a search the pinned
    // essentials (BepInExPack, Jötunn) come first. sortedWith is stable, so the order above holds within.
    return sorted
      .sortedWith(compareBy<Pair<ThunderstorePackage, Int>> { it.first.isDeprecated }.thenByDescending { terms.isEmpty() && it.first.isPinned })
      .map { it.first }
      .toList()
  }

  private fun score(p: ThunderstorePackage, terms: List<String>): Int {
    val name = p.name.replace('_', ' ')
    var score = 0
    for (term in terms) {
      val termScore = when {
        name.equals(term, true) || p.name.equals(term, true) -> 100
        name.startsWith(term, true) -> 50
        name.contains(term, true) || p.name.contains(term, true) -> 30
        p.owner.contains(term, true) -> 15
        p.description.contains(term, true) || p.categories.any { it.contains(term, true) } -> 5
        else -> 0
      }
      // Every term has to match somewhere, like any search box people are used to.
      if (termScore == 0) return 0
      score += termScore
    }
    return score
  }
}
