using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CINE.LaunchHeim.Desktop.Theming;

/// <summary>Windows' light or dark app mode and accent color, mapped onto the Breeze palettes.</summary>
/// <remarks>
/// The QML is styled around a Plasma color scheme, so Windows gets Breeze Light or Breeze Dark (whichever
/// matches "Choose your app mode") with the user's accent color and Segoe UI. It is read once at start;
/// Windows announces changes only through window messages, which Qml.Net does not pass on.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class WindowsColorScheme
{
  public static readonly KdeColorScheme BreezeLight = new(
    "#eff0f1", "#e3e5e7", "#ffffff", "#f7f7f7", "#fcfcfc", "#dee0e2",
    "#232629", "#707d8a", "#3daee9", "#ffffff", "#27ae60", "#da4453", "#f67400", "#2980b9",
    "Segoe UI", 9);

  public static KdeColorScheme Load()
  {
    try
    {
      using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
      var light = personalize?.GetValue("AppsUseLightTheme") is int value && value != 0;
      var scheme = light ? BreezeLight : KdeColorScheme.BreezeDark with { FontFamily = "Segoe UI", FontPointSize = 9 };

      using var dwm = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
      return dwm?.GetValue("AccentColor") is int abgr ? scheme with { Accent = FromAbgr(abgr) } : scheme;
    }
    catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
    {
      return KdeColorScheme.BreezeDark;
    }
  }

  // DWM stores the accent as 0xAABBGGRR.
  public static string FromAbgr(int abgr) =>
    $"#{abgr & 0xff:x2}{(abgr >> 8) & 0xff:x2}{(abgr >> 16) & 0xff:x2}";
}
