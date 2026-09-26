pragma Singleton
import QtQuick 2.15
import LaunchHeim 1.0

// The Plasma color scheme (read from kdeglobals by ThemeViewModel) plus the few colors derived from
// it. The C# side re-reads kdeglobals when it changes, so switching color schemes in System Settings
// repaints the window live.
QtObject {
  readonly property var source: App.theme

  readonly property color window: source.window
  readonly property color windowAlt: source.windowAlternate
  readonly property color view: source.view
  readonly property color viewAlt: source.viewAlternate
  readonly property color button: source.button
  readonly property color text: source.text
  readonly property color textMuted: source.textMuted
  readonly property color accent: source.accent
  readonly property color accentText: source.accentText
  readonly property color positive: source.positive
  readonly property color negative: source.negative
  readonly property color neutral: source.neutral
  readonly property color link: source.link
  readonly property bool dark: source.isDark
  readonly property string fontFamily: source.fontFamily
  readonly property real fontPointSize: source.fontPointSize

  // Cards sit slightly off the window color, like Kirigami cards do.
  readonly property color sidebar: dark ? Qt.darker(window, 1.12) : Qt.darker(window, 1.04)
  readonly property color card: dark ? Qt.lighter(window, 1.22) : Qt.lighter(window, 1.04)
  readonly property color cardHover: dark ? Qt.lighter(window, 1.4) : Qt.darker(window, 1.02)
  readonly property color divider: Qt.rgba(text.r, text.g, text.b, 0.10)
  readonly property color subtle: Qt.rgba(text.r, text.g, text.b, 0.06)
  readonly property color hover: Qt.rgba(text.r, text.g, text.b, 0.09)
  readonly property color accentSoft: Qt.rgba(accent.r, accent.g, accent.b, 0.18)
  readonly property color shadow: Qt.rgba(0, 0, 0, dark ? 0.35 : 0.12)

  readonly property int radius: 10
  readonly property int smallRadius: 6
  readonly property int gap: 12
  readonly property real small: fontPointSize * 0.9
  readonly property real title: fontPointSize * 2.1
  readonly property real heading: fontPointSize * 1.35

  function sourceColor(source) {
    switch (source) {
    case "thunderstore": return "#23c4a0"
    case "nexus": return "#da8e35"
    case "curseforge": return "#f16436"
    default: return textMuted
    }
  }

  function sourceName(source) {
    switch (source) {
    case "thunderstore": return "Thunderstore"
    case "nexus": return "Nexus Mods"
    case "curseforge": return "CurseForge"
    default: return "Local"
    }
  }

  // Accepts colors and "#rrggbb" strings from C# alike; Qt.darker(x, 1) turns either into a color.
  function alpha(c, a) {
    var color = Qt.darker(c, 1.0)
    return Qt.rgba(color.r, color.g, color.b, a)
  }
}
