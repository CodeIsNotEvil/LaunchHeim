import QtQuick 2.15
import QtQuick.Controls 2.15

TextField {
  id: field

  // Material pads 8 above and 16 below to leave room for its underline. The box here has none, so the
  // text (and the placeholder, which follows these) sat 4 px high; equal padding centres it.
  topPadding: 8
  bottomPadding: 8
  leftPadding: 38
  rightPadding: text.length > 0 ? 36 : 12
  implicitHeight: 38
  selectByMouse: true
  color: Theme.text
  placeholderTextColor: Theme.textMuted
  verticalAlignment: TextInput.AlignVCenter

  background: Rectangle {
    radius: Theme.smallRadius + 2
    color: Theme.view
    border.width: field.activeFocus ? 2 : 1
    border.color: field.activeFocus ? Theme.accent : Theme.divider
  }

  Icon {
    iconName: "search"
    size: 16
    color: Theme.textMuted
    anchors.left: parent.left
    anchors.leftMargin: 12
    anchors.verticalCenter: parent.verticalCenter
  }

  IconButton {
    iconName: "close"
    visible: field.text.length > 0
    implicitHeight: 26
    anchors.right: parent.right
    anchors.rightMargin: 6
    anchors.verticalCenter: parent.verticalCenter
    onClicked: { field.clear(); field.accepted() }
  }
}
