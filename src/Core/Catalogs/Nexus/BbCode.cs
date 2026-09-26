using System.Net;
using System.Text.RegularExpressions;

namespace CINE.LaunchHeim.Core.Catalogs.Nexus;

/// <summary>
/// Turns Nexus descriptions (BBCode mixed with the odd <c>&lt;br /&gt;</c>) into the HTML subset Qt's
/// rich text understands.
/// </summary>
/// <remarks>
/// It only has to look right, not be a complete BBCode implementation. Unknown tags are dropped rather
/// than shown, and no author HTML survives except line breaks, since Qt would otherwise render it.
/// </remarks>
public static partial class BbCode
{
  public static string ToHtml(string bbcode)
  {
    var text = BreakTag().Replace(bbcode, "\n");
    text = WebUtility.HtmlEncode(WebUtility.HtmlDecode(text));

    text = Simple(text, "b", "<b>", "</b>");
    text = Simple(text, "i", "<i>", "</i>");
    text = Simple(text, "u", "<u>", "</u>");
    text = Simple(text, "s", "<s>", "</s>");
    text = Simple(text, "center", "<div align=\"center\">", "</div>");
    text = Simple(text, "right", "<div align=\"right\">", "</div>");
    text = Simple(text, "quote", "<blockquote>", "</blockquote>");
    text = Simple(text, "code", "<pre>", "</pre>");
    text = Simple(text, "heading", "<h3>", "</h3>");
    text = Simple(text, "line", "<hr/>", "");

    text = UrlWithTarget().Replace(text, m => $"<a href=\"{SafeUrl(m.Groups[1].Value)}\">{m.Groups[2].Value}</a>");
    text = UrlPlain().Replace(text, m => $"<a href=\"{SafeUrl(m.Groups[1].Value)}\">{m.Groups[1].Value}</a>");
    text = Image().Replace(text, m => $"<img src=\"{SafeUrl(m.Groups[1].Value)}\" width=\"560\"/>");
    text = Size().Replace(text, m => $"<font size=\"{Math.Clamp(int.Parse(m.Groups[1].Value), 1, 6)}\">{m.Groups[2].Value}</font>");
    text = Color().Replace(text, m => $"<font color=\"{m.Groups[1].Value}\">{m.Groups[2].Value}</font>");
    text = ListBlock().Replace(text, m => "<ul>" + ListItem().Replace(m.Groups[1].Value, "<li>") + "</ul>");

    // Whatever is left ([spoiler], [font=...], [youtube]...) only adds noise.
    text = AnyTag().Replace(text, "");
    return text.Replace("\n", "<br/>");
  }

  private static string Simple(string text, string tag, string open, string close) =>
    Regex.Replace(text, $@"\[{tag}\](.*?)\[/{tag}\]", $"{open}$1{close}", RegexOptions.IgnoreCase | RegexOptions.Singleline);

  // Only web links survive; javascript: and friends become harmless.
  private static string SafeUrl(string url)
  {
    var decoded = WebUtility.HtmlDecode(url).Trim();
    return decoded.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || decoded.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
      ? WebUtility.HtmlEncode(decoded)
      : "#";
  }

  [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
  private static partial Regex BreakTag();

  [GeneratedRegex(@"\[url=([^\]]+)\](.*?)\[/url\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
  private static partial Regex UrlWithTarget();

  [GeneratedRegex(@"\[url\](.*?)\[/url\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
  private static partial Regex UrlPlain();

  [GeneratedRegex(@"\[img[^\]]*\](.*?)\[/img\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
  private static partial Regex Image();

  [GeneratedRegex(@"\[size=(\d+)\](.*?)\[/size\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
  private static partial Regex Size();

  [GeneratedRegex(@"\[color=(#[0-9a-fA-F]{3,8}|[a-zA-Z]+)\](.*?)\[/color\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
  private static partial Regex Color();

  [GeneratedRegex(@"\[list(?:=[^\]]*)?\](.*?)\[/list\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
  private static partial Regex ListBlock();

  [GeneratedRegex(@"\[\*\]")]
  private static partial Regex ListItem();

  [GeneratedRegex(@"\[/?[a-zA-Z*]+(?:=[^\]]*)?\]")]
  private static partial Regex AnyTag();
}
