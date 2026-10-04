using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace CINE.LaunchHeim.Core.Saves;

/// <summary>Valheim's own PlayerPrefs keys that LaunchHeim sets before a launch.</summary>
/// <remarks>
/// The main menu reads them to decide what is selected (<c>FejdStartup.Start</c> and
/// <c>ShowStartGame</c>), and the game writes them back whenever the player picks something.
/// </remarks>
public static class ValheimPrefs
{
  /// <summary>The selected character: its file name without <c>.fch</c>.</summary>
  public const string Character = "profile";

  /// <summary>The selected world's name.</summary>
  public const string World = "world";
}

/// <summary>Reads and writes string values in a Unity game's PlayerPrefs while the game is not running.</summary>
/// <remarks>
/// Unity keeps PlayerPrefs in memory and writes them all on quit, so a running game would overwrite
/// these changes. LaunchHeim only writes them right before it starts the game itself.
/// </remarks>
public abstract class UnityPrefs
{
  public abstract string? GetString(string key);

  public abstract void SetString(string key, string value);

  /// <summary>The registry value Unity stores <paramref name="key"/> under on Windows: <c>&lt;key&gt;_h&lt;hash&gt;</c>.</summary>
  /// <remarks>The hash is djb2 with xor over the key's bytes. Here so tests on Linux can check it.</remarks>
  internal static string RegistryValueName(string key)
  {
    var hash = 5381u;
    foreach (var b in Encoding.UTF8.GetBytes(key))
    {
      hash = unchecked(hash * 33) ^ b;
    }

    return $"{key}_h{hash}";
  }

  /// <summary>Valheim's prefs: the <c>prefs</c> file in its data folder on Linux, the registry on Windows.</summary>
  public static UnityPrefs ForValheim(string dataDirectory) =>
    OperatingSystem.IsWindows()
      ? new RegistryUnityPrefs(@"Software\IronGate\Valheim")
      : new FileUnityPrefs(Path.Combine(dataDirectory, "prefs"));
}

/// <summary>Unity's Linux PlayerPrefs: an XML file with one <c>pref</c> element per key, strings in base64.</summary>
/// <remarks>
/// The file is edited as text rather than through an XML library, so every other line stays byte for
/// byte as Unity wrote it. Only <c>type="string"</c> entries are touched.
/// </remarks>
public sealed partial class FileUnityPrefs(string path) : UnityPrefs
{
  private const string Root = "unity_prefs";

  public override string? GetString(string key)
  {
    if (!File.Exists(path))
    {
      return null;
    }

    var match = PrefPattern(key).Match(File.ReadAllText(path));
    if (!match.Success)
    {
      return null;
    }

    try
    {
      return Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups["value"].Value.Trim()));
    }
    catch (FormatException)
    {
      return null;
    }
  }

  public override void SetString(string key, string value)
  {
    var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    var text = File.Exists(path) ? File.ReadAllText(path) : "";
    var existing = PrefPattern(key).Match(text).Groups["value"];
    var close = text.LastIndexOf($"</{Root}>", StringComparison.Ordinal);
    if (existing.Success)
    {
      text = string.Concat(text.AsSpan(0, existing.Index), encoded, text.AsSpan(existing.Index + existing.Length));
    }
    else if (close >= 0)
    {
      text = text.Insert(close, $"\t{Element(key, encoded)}\n");
    }
    else
    {
      // The game has never run, or the file is broken. Unity reads a fresh file like this one.
      text = $"<{Root} version_major=\"1\" version_minor=\"1\">\n\t{Element(key, encoded)}\n</{Root}>\n";
    }

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var temp = path + ".launchheim.tmp";
    File.WriteAllText(temp, text);
    File.Move(temp, path, overwrite: true);
  }

  private static string Element(string key, string encoded) =>
    $"<pref name=\"{System.Security.SecurityElement.Escape(key)}\" type=\"string\">{encoded}</pref>";

  private static Regex PrefPattern(string key) =>
    new($"<pref name=\"{Regex.Escape(System.Security.SecurityElement.Escape(key))}\" type=\"string\">(?<value>[^<]*)</pref>");
}

/// <summary>Unity's Windows PlayerPrefs: values under <c>HKCU\Software\&lt;company&gt;\&lt;product&gt;</c>.</summary>
/// <remarks>
/// Unity names each value <c>&lt;key&gt;_h&lt;hash&gt;</c> (<see cref="UnityPrefs.RegistryValueName"/>, for
/// example <c>UnityGraphicsQuality_h1669003810</c>) and stores strings as binary UTF-8 ending in a zero byte.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class RegistryUnityPrefs(string subKey) : UnityPrefs
{
  public override string? GetString(string key)
  {
    using var registry = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey);
    return registry?.GetValue(RegistryValueName(key)) is byte[] bytes ? Encoding.UTF8.GetString(bytes).TrimEnd('\0') : null;
  }

  public override void SetString(string key, string value)
  {
    using var registry = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subKey);
    registry.SetValue(RegistryValueName(key), Encoding.UTF8.GetBytes(value + "\0"), Microsoft.Win32.RegistryValueKind.Binary);
  }
}
