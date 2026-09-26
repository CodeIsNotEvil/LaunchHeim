import QtQuick 2.15
import QtQuick.Controls 2.15

// Rounded, mixed-case buttons in the Breeze spirit instead of Material's all-caps ones.
// kind: primary (accent), secondary (tinted), ghost (text only), danger.
Button {
  id: control

  property string kind: "secondary"
  property string iconName: ""
  property bool large: false

  readonly property color baseColor: kind === "primary" ? Theme.accent
    : kind === "danger" ? Theme.negative
    : kind === "ghost" ? "transparent" : Theme.hover
  readonly property color foreground: kind === "primary" ? Theme.accentText
    : kind === "danger" ? "white"
    : Theme.text

  implicitHeight: large ? 42 : 34
  implicitWidth: Math.max(implicitHeight, contentItem.implicitWidth + leftPadding + rightPadding)
  leftPadding: text.length > 0 ? (large ? 20 : 14) : 8
  rightPadding: leftPadding
  font.capitalization: Font.MixedCase
  font.bold: kind === "primary"
  font.pointSize: large ? Theme.fontPointSize * 1.1 : Theme.fontPointSize
  hoverEnabled: true
  opacity: enabled ? 1 : 0.45

  contentItem: Row {
    spacing: 8
    anchors.centerIn: parent

    Icon {
      iconName: control.iconName
      visible: iconName.length > 0
      size: control.large ? 18 : 16
      color: control.foreground
      anchors.verticalCenter: parent.verticalCenter
    }

    Text {
      text: control.text
      visible: text.length > 0
      font: control.font
      color: control.foreground
      anchors.verticalCenter: parent.verticalCenter
    }
  }

  background: Rectangle {
    radius: Theme.smallRadius + 2
    color: {
      if (control.kind === "ghost")
        return control.down ? Theme.hover : control.hovered ? Theme.subtle : "transparent"
      if (control.down)
        return Qt.darker(control.baseColor, 1.25)
      if (control.hovered)
        return control.kind === "secondary" ? Qt.lighter(control.baseColor, 1.6) : Qt.lighter(control.baseColor, 1.12)
      return control.baseColor
    }
    border.width: control.visualFocus ? 2 : 0
    border.color: Theme.accent

    Behavior on color { ColorAnimation { duration: 120 } }
  }
}
