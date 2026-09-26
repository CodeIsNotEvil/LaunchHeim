import QtQuick 2.15
import QtQuick.Controls 2.15

TextField {
  id: field

  implicitHeight: 38
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
