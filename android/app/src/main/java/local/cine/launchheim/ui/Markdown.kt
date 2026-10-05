package local.cine.launchheim.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp

/**
 * Enough Markdown for mod READMEs on a phone: headings, lists, code blocks, bold and inline code. Images
 * and HTML are dropped and links keep their text, because a README is read here to decide whether to
 * add a mod; the full page is one tap away on Thunderstore.
 */
@Composable
fun MarkdownText(markdown: String, modifier: Modifier = Modifier) {
  Column(modifier, verticalArrangement = Arrangement.spacedBy(8.dp)) {
    for (block in blocks(markdown)) {
      when (block) {
        is Block.Heading -> Text(
          inline(block.text),
          style = when (block.level) {
            1 -> MaterialTheme.typography.titleLarge
            2 -> MaterialTheme.typography.titleMedium
            else -> MaterialTheme.typography.titleSmall
          },
          modifier = Modifier.padding(top = 6.dp),
        )
        is Block.Bullet -> Row(Modifier.padding(start = (block.depth * 12).dp)) {
          Text("•  ", color = MaterialTheme.colorScheme.primary)
          Text(inline(block.text), style = MaterialTheme.typography.bodyMedium)
        }
        is Block.Code -> Text(
          block.text,
          style = MaterialTheme.typography.bodySmall,
          modifier = Modifier.fillMaxWidth()
            .background(MaterialTheme.colorScheme.surfaceContainerHighest, RoundedCornerShape(8.dp))
            .padding(10.dp),
        )
        is Block.Paragraph -> Text(inline(block.text), style = MaterialTheme.typography.bodyMedium)
      }
    }
  }
}

private sealed interface Block {
  data class Heading(val level: Int, val text: String) : Block
  data class Bullet(val depth: Int, val text: String) : Block
  data class Code(val text: String) : Block
  data class Paragraph(val text: String) : Block
}

private val Html = Regex("<[^>]+>")
private val Image = Regex("!\\[[^\\]]*]\\([^)]*\\)")
private val Link = Regex("\\[([^\\]]*)]\\([^)]*\\)")
private val BulletLine = Regex("^(\\s*)([-*+]|\\d+\\.)\\s+(.*)$")
private val Rule = Regex("^\\s*([-*_])(\\s*\\1){2,}\\s*$")

private fun blocks(markdown: String): List<Block> {
  val result = mutableListOf<Block>()
  val paragraph = StringBuilder()
  var code: StringBuilder? = null

  fun flush() {
    if (paragraph.isNotBlank()) result += Block.Paragraph(paragraph.toString().trim())
    paragraph.clear()
  }

  for (raw in markdown.replace("\r", "").lines()) {
    if (raw.trimStart().startsWith("```")) {
      if (code == null) {
        flush()
        code = StringBuilder()
      } else {
        result += Block.Code(code.toString().trimEnd())
        code = null
      }
      continue
    }
    code?.let { it.append(raw).append('\n'); continue }

    val line = Link.replace(Image.replace(Html.replace(raw, ""), ""), "$1")
    val trimmed = line.trim()
    val bullet = BulletLine.find(line)
    when {
      trimmed.isEmpty() || Rule.matches(trimmed) -> flush()
      trimmed.startsWith("#") -> {
        flush()
        val level = trimmed.takeWhile { it == '#' }.length
        result += Block.Heading(level, trimmed.drop(level).trim().trimEnd('#').trim())
      }
      bullet != null -> {
        flush()
        result += Block.Bullet(bullet.groupValues[1].length / 2, bullet.groupValues[3])
      }
      trimmed.startsWith(">") -> paragraph.append(trimmed.removePrefix(">").trim()).append(' ')
      else -> paragraph.append(trimmed).append(' ')
    }
  }
  code?.let { result += Block.Code(it.toString()) }
  flush()
  return result
}

/** `**bold**`, `__bold__` and `` `code` ``; single `*` and `_` are left alone, since mod names use them. */
private fun inline(text: String): AnnotatedString = buildAnnotatedString {
  var i = 0
  while (i < text.length) {
    val bold = text.startsWith("**", i) || text.startsWith("__", i)
    if (bold) {
      val marker = text.substring(i, i + 2)
      val end = text.indexOf(marker, i + 2)
      if (end > i + 2) {
        withStyle(SpanStyle(fontWeight = FontWeight.Bold)) { append(text.substring(i + 2, end)) }
        i = end + 2
        continue
      }
    }
    if (text[i] == '`') {
      val end = text.indexOf('`', i + 1)
      if (end > i) {
        withStyle(SpanStyle(background = Color.Gray.copy(alpha = 0.2f))) { append(text.substring(i + 1, end)) }
        i = end + 1
        continue
      }
    }
    append(text[i])
    i++
  }
}
