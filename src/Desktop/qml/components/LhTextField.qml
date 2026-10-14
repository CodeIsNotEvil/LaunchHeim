import QtQuick 2.15
import QtQuick.Controls 2.15

TextField {
  id: field

  implicitHeight: 38
  // Material pads 8 above and 16 below to leave room for its underline. The box here has none, so the
  // text (and the placeholder, which follows these) sat 4 px high; equal padding centres it.
  topPadding: 8
  bottomPadding: 8
  leftPadding: 12
  rightPadding: 12
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
}
