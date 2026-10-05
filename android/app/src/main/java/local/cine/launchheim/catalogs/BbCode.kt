package local.cine.launchheim.catalogs

/**
 * Nexus descriptions (BBCode with the odd `<br />`) as the HTML subset Compose's
 * `AnnotatedString.fromHtml` renders, ported from Core/Catalogs/Nexus/BbCode.cs.
 *
 * It only has to look right. Unknown tags are dropped, images too (a phone screen is better spent on
 * text), and no author HTML survives except line breaks.
 */
object BbCode {
  private val opts = setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL)
  private val breakTag = Regex("<br\\s*/?>", RegexOption.IGNORE_CASE)
  private val urlWithTarget = Regex("\\[url=([^\\]]+)\\](.*?)\\[/url\\]", opts)
  private val urlPlain = Regex("\\[url\\](.*?)\\[/url\\]", opts)
  private val image = Regex("\\[img[^\\]]*\\](.*?)\\[/img\\]", opts)
  private val listBlock = Regex("\\[list(?:=[^\\]]*)?\\](.*?)\\[/list\\]", opts)
  private val listItem = Regex("\\[\\*\\]")
  private val anyTag = Regex("\\[/?[a-zA-Z*]+(?:=[^\\]]*)?\\]")

  fun toHtml(bbcode: String): String {
    var text = breakTag.replace(bbcode, "\n")
    text = escape(unescape(text))
    for ((tag, open, close) in listOf(
      Triple("b", "<b>", "</b>"), Triple("i", "<i>", "</i>"), Triple("u", "<u>", "</u>"), Triple("s", "<s>", "</s>"),
      Triple("quote", "<blockquote>", "</blockquote>"), Triple("code", "<tt>", "</tt>"), Triple("heading", "<h3>", "</h3>"),
    )) {
      text = Regex("\\[$tag\\](.*?)\\[/$tag\\]", opts).replace(text) { open + it.groupValues[1] + close }
    }
    text = image.replace(text, "")
    text = urlWithTarget.replace(text) { "<a href=\"${safeUrl(it.groupValues[1])}\">${it.groupValues[2]}</a>" }
    text = urlPlain.replace(text) { "<a href=\"${safeUrl(it.groupValues[1])}\">${it.groupValues[1]}</a>" }
    text = listBlock.replace(text) { "\n" + listItem.replace(it.groupValues[1], "\n• ") + "\n" }
    // Whatever is left ([size], [color], [spoiler], [youtube]...) only adds noise on a phone.
    text = anyTag.replace(text, "")
    // Authors space their pages with runs of empty lines, which on a phone is a screen of nothing.
    return text.trim().replace(Regex("\n[ \t]*(\n[ \t]*)+"), "\n\n").replace("\n", "<br>")
  }

  // Only web links survive; javascript: and friends become harmless.
  private fun safeUrl(url: String): String {
    val decoded = unescape(url).trim()
    return if (decoded.startsWith("http://", true) || decoded.startsWith("https://", true)) escape(decoded) else "#"
  }

  private fun escape(text: String) = text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("\"", "&quot;")

  private fun unescape(text: String) =
    text.replace("&lt;", "<").replace("&gt;", ">").replace("&quot;", "\"").replace("&#39;", "'").replace("&amp;", "&")
}
