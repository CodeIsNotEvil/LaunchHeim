import QtQuick 2.15
import QtQuick.Controls 2.15

// A Breeze-like switch. Material's knob is drawn with a shader effect, which the software renderer
// used for screenshots (and some VMs) skips, and its proportions look out of place on a desktop.
Switch {
  id: control

  hoverEnabled: true
  implicitWidth: indicator.width + (text.length > 0 ? spacing + contentItem.implicitWidth : 0)
  implicitHeight: 28
  spacing: 10

  indicator: Rectangle {
    width: 40
    height: 22
    radius: 11
    x: control.leftPadding
    anchors.verticalCenter: parent.verticalCenter
    color: control.checked ? Theme.accent : Theme.hover
    border.width: control.checked ? 0 : 1
    border.color: Theme.divider

    Behavior on color { ColorAnimation { duration: 120 } }

    Rectangle {
      width: 16
      height: 16
      radius: 8
      y: 3
      x: control.checked ? parent.width - width - 3 : 3
      color: control.checked ? Theme.accentText : Theme.text
      opacity: control.checked ? 1 : 0.7

      Behavior on x { NumberAnimation { duration: 120; easing.type: Easing.OutCubic } }
    }
  }

  contentItem: Text {
    text: control.text
    visible: text.length > 0
    color: Theme.text
    font: control.font
    leftPadding: control.indicator.width + control.spacing
    verticalAlignment: Text.AlignVCenter
  }
}
