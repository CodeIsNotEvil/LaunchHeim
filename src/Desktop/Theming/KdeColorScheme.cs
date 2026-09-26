namespace CINE.LaunchHeim.Desktop.Theming;

/// <summary>The colors of the active Plasma color scheme, read from <c>kdeglobals</c>.</summary>
/// <remarks>
/// Qml.Net runs on Qt 5, and Plasma 6 no longer installs a Qt 5 platform theme, so Qt's own palette
/// would not follow the user's color scheme. Reading kdeglobals directly gets the same colors Breeze
/// uses, including a custom accent color, and it is re-read when the file changes, so switching
/// between light and dark in System Settings recolors LaunchHeim immediately.
/// </remarks>
public sealed record KdeColorScheme(
  string Window,
  string WindowAlternate,
  string View,
  string ViewAlternate,
  string Button,
  string Header,
  string Text,
  string TextInactive,
  string Accent,
  string AccentText,
  string Positive,
  string Negative,
  string Neutral,
  string Link,
  string FontFamily,
  double FontPointSize)
{
  /// <summary>Breeze Dark, used when there is no kdeglobals (not on Plasma) or a key is missing.</summary>
  public static readonly KdeColorScheme BreezeDark = new(
    "#202326", "#292c30", "#141618", "#1d1f22", "#292c30", "#2b2f33",
    "#fcfcfc", "#a1a9b1", "#3daee9", "#fcfcfc", "#27ae60", "#da4453", "#f67400", "#1d99f3",
    "Noto Sans", 10);

  public bool IsDark => Luminance(Window) < 0.5;

  public static string ConfigPath
  {
    get
    {
      var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
      if (string.IsNullOrEmpty(config))
      {
        config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
      }

      return Path.Combine(config, "kdeglobals");
    }
  }

  public static KdeColorScheme Load(string path)
  {
    if (!File.Exists(path))
    {
      return BreezeDark;
    }

    try
    {
      return Parse(File.ReadAllLines(path));
    }
    catch (IOException)
    {
      return BreezeDark;
    }
  }

  public static KdeColorScheme Parse(IEnumerable<string> lines)
  {
    var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
    Dictionary<string, string>? current = null;
    foreach (var raw in lines)
    {
      var line = raw.Trim();
      if (line.StartsWith('[') && line.EndsWith(']'))
      {
        // Nested groups look like [Colors:Header][Inactive]; only the plain groups matter here.
        var name = line[1..^1];
        current = name.Contains("][", StringComparison.Ordinal) ? null : sections.TryGetValue(name, out var s) ? s : sections[name] = new();
        continue;
      }

      var equals = line.IndexOf('=');
      if (current is not null && equals > 0)
      {
        current[line[..equals].Trim()] = line[(equals + 1)..].Trim();
      }
    }

    var d = BreezeDark;
    string Color(string section, string key, string fallback) =>
      sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value) && ToHex(value) is { } hex ? hex : fallback;

    var accent = Color("General", "AccentColor", Color("Colors:Selection", "BackgroundNormal", d.Accent));
    var font = sections.TryGetValue("General", out var general) && general.TryGetValue("font", out var f) ? f.Split(',') : [];
    var pointSize = font.Length > 1 && double.TryParse(font[1], System.Globalization.CultureInfo.InvariantCulture, out var size) && size > 0 ? size : d.FontPointSize;

    return new KdeColorScheme(
      Color("Colors:Window", "BackgroundNormal", d.Window),
      Color("Colors:Window", "BackgroundAlternate", d.WindowAlternate),
      Color("Colors:View", "BackgroundNormal", d.View),
      Color("Colors:View", "BackgroundAlternate", d.ViewAlternate),
      Color("Colors:Button", "BackgroundNormal", d.Button),
      Color("Colors:Header", "BackgroundNormal", Color("Colors:Window", "BackgroundNormal", d.Header)),
      Color("Colors:Window", "ForegroundNormal", d.Text),
      Color("Colors:Window", "ForegroundInactive", d.TextInactive),
      accent,
      Color("Colors:Selection", "ForegroundNormal", d.AccentText),
      Color("Colors:View", "ForegroundPositive", d.Positive),
      Color("Colors:View", "ForegroundNegative", d.Negative),
      Color("Colors:View", "ForegroundNeutral", d.Neutral),
      Color("Colors:View", "ForegroundLink", d.Link),
      font.Length > 0 && font[0].Length > 0 ? font[0] : d.FontFamily,
      pointSize);
  }

  /// <summary>kdeglobals stores colors as <c>r,g,b</c> (sometimes with alpha) or as <c>#rrggbb</c>.</summary>
  internal static string? ToHex(string value)
  {
    if (value.StartsWith('#') && value.Length is 7 or 9)
    {
      return value;
    }

    var parts = value.Split(',');
    if (parts.Length is < 3 or > 4 || !parts.Take(3).All(p => byte.TryParse(p.Trim(), out _)))
    {
      return null;
    }

    return "#" + string.Concat(parts.Take(3).Select(p => byte.Parse(p.Trim()).ToString("x2")));
  }

  private static double Luminance(string hex)
  {
    var r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0;
    var g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0;
    var b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0;
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  }
}
