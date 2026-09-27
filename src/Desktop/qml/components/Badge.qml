import QtQuick 2.15

Rectangle {
  property alias text: label.text
  property color tint: Theme.accent
  property bool solid: false

  implicitWidth: label.implicitWidth + 14
  implicitHeight: label.implicitHeight + 6
  radius: height / 2
  color: solid ? tint : Theme.alpha(tint, 0.18)

  Text {
    id: label
    anchors.centerIn: parent
    color: parent.solid ? "white" : parent.tint
    // Plain Text does not inherit the window font the way controls do.
    font.family: Theme.fontFamily
    font.pointSize: Theme.small
    font.bold: true
  }
}
